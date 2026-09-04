using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using OverTranslate.Models;
using OverTranslate.Services;
using OverTranslate.Services.Providers;
using Xunit;

namespace OverTranslate.Tests;

public class OpenAiCompatibleProviderTests
{
    [Theory]
    [InlineData("https://api.openai.com/v1", "https://api.openai.com/v1/chat/completions")]
    [InlineData("http://localhost:11434/v1/", "http://localhost:11434/v1/chat/completions")]
    [InlineData("http://localhost:1234", "http://localhost:1234/v1/chat/completions")]
    [InlineData("https://example.test/custom/chat/completions", "https://example.test/custom/chat/completions")]
    public void BuildEndpoint_AcceptsBaseOrFullChatCompletionsUrl(string input, string expected)
    {
        Assert.Equal(expected, OpenAiCompatibleProvider.BuildEndpoint(input).AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildEndpoint_FallsBackToTheDefaultServerWhenTheBoxIsEmpty(string input)
    {
        Assert.Equal(
            "http://localhost:11434/v1/chat/completions",
            OpenAiCompatibleProvider.BuildEndpoint(input).AbsoluteUri);
    }

    [Theory]
    [InlineData("localhost:1234")]
    [InlineData("ftp://example.test/v1")]
    public void BuildEndpoint_RejectsInvalidUrl(string input)
    {
        Assert.Throws<InvalidOperationException>(() => OpenAiCompatibleProvider.BuildEndpoint(input));
    }

    /// <summary>
    /// Runs a test with the interface in a given language, and puts it back afterwards.
    /// </summary>
    /// <remarks>
    /// The interface language lives in the one shared settings instance, so a test that set it and
    /// walked away would decide the answer for whichever test ran next.
    /// </remarks>
    private static void WithInterfaceLanguage(string language, Action assert)
    {
        var settings = SettingsService.Instance.Current;
        var original = settings.UiLanguage;
        try
        {
            settings.UiLanguage = language;
            assert();
        }
        finally
        {
            settings.UiLanguage = original;
        }
    }

    [Theory]
    [InlineData("ZH-HANT", "繁體中文")]
    [InlineData("ZH-HANS", "簡體中文")]
    public void BuildPrompt_IsWrittenInTheInterfaceLanguage(string targetCode, string targetName)
    {
        WithInterfaceLanguage(LocalizationService.TraditionalChinese, () =>
        {
            var prompt = OpenAiCompatibleProvider.BuildPrompt("AUTO", targetCode);

            // Names alone, no tags: the shipped default is tuned for the model this provider ships
            // against, and the tags are there for a template someone writes themselves.
            Assert.Contains($"從(各種語言)翻譯成({targetName})", prompt);
            Assert.Contains("只回傳譯文", prompt);
            Assert.Contains("不要解釋、道歉、提問", prompt);
            Assert.DoesNotContain("JSON", prompt);
            Assert.True(prompt.Length <= 180);
        });
    }

    // 简体界面拿简体句子：翻译专用的小模型会模仿指令自身的简繁体，繁体指令连简体目标都
    // 会被带偏成繁体输出——这正是本分隔存在的原因。
    [Theory]
    [InlineData("ZH-HANS", "简体中文")]
    [InlineData("ZH-HANT", "繁体中文")]
    public void BuildPrompt_IsWrittenInTheInterfaceLanguage_Simplified(string targetCode, string targetName)
    {
        WithInterfaceLanguage(LocalizationService.SimplifiedChinese, () =>
        {
            var prompt = OpenAiCompatibleProvider.BuildPrompt("AUTO", targetCode);

            Assert.Contains($"从(各种语言)翻译成({targetName})", prompt);
            Assert.Contains("只返回译文", prompt);
            Assert.Contains("不要解释、致歉、提问", prompt);
            Assert.DoesNotContain("回傳", prompt);
            Assert.True(prompt.Length <= 190);
        });
    }

    // The target language decides what the model is asked to produce; the interface language decides
    // what the sentence asking for it is written in. Translating into Chinese from an English
    // interface has to produce an English instruction naming Chinese.
    [Fact]
    public void BuildPrompt_FollowsTheInterfaceRatherThanTheTarget()
    {
        WithInterfaceLanguage(LocalizationService.English, () =>
        {
            var prompt = OpenAiCompatibleProvider.BuildPrompt("JA", "ZH-HANT");

            Assert.Contains("from (Japanese) to (Traditional Chinese)", prompt);
            Assert.DoesNotContain("繁體中文", prompt);
        });

        WithInterfaceLanguage(LocalizationService.TraditionalChinese, () =>
        {
            var prompt = OpenAiCompatibleProvider.BuildPrompt("JA", "EN-US");

            Assert.Contains("從(日語)翻譯成(英語)", prompt);
            Assert.DoesNotContain("Translate", prompt);
        });
    }

    // 自動 keeps the same "from … to …" skeleton as a chosen source language rather than
    // restructuring the sentence: a translation-only model stopped half-way through a Japanese line
    // when handed the restructured wording, and finished it under this one.
    [Theory]
    [InlineData("ZH-HANT")]
    [InlineData("EN-US")]
    public void BuildPrompt_NamesTheSourceEvenWhenItIsAutomatic(string targetCode)
    {
        WithInterfaceLanguage(LocalizationService.TraditionalChinese, () =>
        {
            var automatic = OpenAiCompatibleProvider.BuildPrompt("AUTO", targetCode);
            var chosen = OpenAiCompatibleProvider.BuildPrompt("JA", targetCode);

            // Compared as "same opening, same closing instruction" rather than by splitting on the
            // first bracket, which stopped isolating the skeleton once the language tag brought a
            // bracket of its own.
            const string tail = "。只回傳譯文；不要解釋、道歉、提問或加入任何其他文字；輸入沒有有效文字時只回傳空字串。";

            Assert.StartsWith("從", automatic);
            Assert.StartsWith("從", chosen);
            Assert.EndsWith(tail, automatic);
            Assert.EndsWith(tail, chosen);
        });
    }

    [Theory]
    [InlineData("EN", "JA", "English", "Japanese")]
    [InlineData("JA", "KO", "Japanese", "Korean")]
    public void BuildPrompt_NamesBothLanguagesInAnEnglishInterface(
        string sourceCode,
        string targetCode,
        string sourceName,
        string targetName)
    {
        WithInterfaceLanguage(LocalizationService.English, () =>
        {
            var prompt = OpenAiCompatibleProvider.BuildPrompt(sourceCode, targetCode);

            Assert.Contains($"from ({sourceName}) to ({targetName})", prompt);
            Assert.Contains("Return only the translation", prompt);
            Assert.Contains("do not explain, apologize, ask questions", prompt);
            Assert.DoesNotContain("只回傳", prompt);
        });
    }

    [Fact]
    public void BuildPrompt_UsesEnglishForAutomaticSourceInAnEnglishInterface()
    {
        WithInterfaceLanguage(LocalizationService.English, () =>
        {
            var prompt = OpenAiCompatibleProvider.BuildPrompt("AUTO", "EN-US");

            Assert.Contains("from (any language) to (English)", prompt);
            Assert.Contains("Return only the translation", prompt);
            Assert.Contains("do not explain, apologize, ask questions", prompt);
        });
    }

    // The custom prompt belongs to the case it was written for: editing one must not change what
    // the other case sends, which is the whole reason two of them are stored.
    [Fact]
    public void BuildPrompt_PrefersTheCustomPromptForTheCaseInHand()
    {
        WithInterfaceLanguage(LocalizationService.TraditionalChinese, () =>
        {
            var automatic = OpenAiCompatibleProvider.BuildPrompt(
                "AUTO", "ZH-HANT", customAuto: "自動用：翻成{target}", customExplicit: "指定用：{source}→{target}");
            var chosen = OpenAiCompatibleProvider.BuildPrompt(
                "JA", "ZH-HANT", customAuto: "自動用：翻成{target}", customExplicit: "指定用：{source}→{target}");

            // The name placeholders still mean the name alone, which is what they meant before the
            // tags existed — a template written back then reads the way it was written. A template
            // that wants the tag asks for it with {source_code} / {target_code}.
            Assert.Equal("自動用：翻成繁體中文", automatic);
            Assert.Equal("指定用：日語→繁體中文", chosen);
        });
    }

    // The point of splitting the tag out of the name: a template can place it wherever its own model
    // expects it, including TranslateGemma's documented wording, which this application does not ship
    // as its default because restructuring the sentence once cost a model half a Japanese line.
    [Fact]
    public void BuildPrompt_LetsATemplatePlaceTheLanguageTagItself()
    {
        // The English interface, because the NAME follows the interface language while the tag does
        // not — see BuildPrompt_FollowsTheInterfaceRatherThanTheTarget.
        WithInterfaceLanguage(LocalizationService.English, () =>
        {
            var prompt = OpenAiCompatibleProvider.BuildPrompt(
                "JA", "EN-US",
                customExplicit: "You are a professional {source} ({source_code}) to {target} ({target_code}) translator.");

            Assert.Equal(
                "You are a professional Japanese (ja) to English (en) translator.",
                prompt);
        });
    }

    // {source} / {target} were the names before the tags gained placeholders of their own. A template
    // written back then is sitting in someone's settings file, and dropping the pair would send the
    // model a literal "{source}" instead of a language.
    [Fact]
    public void BuildPrompt_StillFillsThePlaceholderNamesItUsedToAdvertise()
    {
        WithInterfaceLanguage(LocalizationService.English, () =>
        {
            var prompt = OpenAiCompatibleProvider.BuildPrompt(
                "JA", "EN-US", customExplicit: "from {source} to {target}");

            Assert.Equal("from Japanese to English", prompt);
        });
    }

    [Fact]
    public void BuildPrompt_LetsATemplateUseTheTagWithoutTheName()
    {
        var prompt = OpenAiCompatibleProvider.BuildPrompt(
            "JA", "ZH-HANT", customExplicit: "{source_code}->{target_code}");

        Assert.Equal("ja->zh-Hant", prompt);
    }

    // 自動 has no language to name, so it has no tag either. A template that asks for one anyway is
    // left with whatever brackets it wrote around it rather than a leaked placeholder.
    [Fact]
    public void BuildPrompt_EmptiesTheSourceTagWhenTheSourceIsAutomatic()
    {
        var prompt = OpenAiCompatibleProvider.BuildPrompt(
            "AUTO", "ZH-HANT", customAuto: "[{source_code}]{target_code}");

        Assert.Equal("[]zh-Hant", prompt);
        Assert.DoesNotContain("{source_code}", prompt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildPrompt_FallsBackToTheBuiltInWhenTheCustomOneIsBlank(string custom)
    {
        var prompt = OpenAiCompatibleProvider.BuildPrompt("JA", "ZH-HANT", customExplicit: custom);

        Assert.Equal(OpenAiCompatibleProvider.BuildPrompt("JA", "ZH-HANT"), prompt);
    }

    // A template written for a chosen source language, left in place after switching to 自動, would
    // otherwise send the model a literal "{source}".
    [Fact]
    public void BuildPrompt_FillsTheSourcePlaceholderEvenWhenTheSourceIsAutomatic()
    {
        WithInterfaceLanguage(LocalizationService.TraditionalChinese, () =>
        {
            var prompt = OpenAiCompatibleProvider.BuildPrompt(
                "AUTO", "ZH-HANT", customAuto: "從({source})翻譯成({target})");

            Assert.Equal("從(各種語言)翻譯成(繁體中文)", prompt);
            Assert.DoesNotContain("{source}", prompt);
        });
    }

    [Theory]
    [InlineData("<think>internal reasoning</think>\n正確譯文", "正確譯文")]
    [InlineData("<THINK mode=\"deep\">hidden</THINK>Visible", "Visible")]
    [InlineData("保留正常的譯文", "保留正常的譯文")]
    public void StripThinking_RemovesCommonThinkingBlocks(string response, string expected)
    {
        Assert.Equal(expected, OpenAiCompatibleProvider.StripThinking(response));
    }

    [Fact]
    public async Task TranslateAsync_SendsOneRequestPerBlockAndPreservesOrderAndBounds()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var provider = new OpenAiCompatibleProvider(
            http,
            () => new OpenAiCompatibleOptions(
                "http://localhost:1234/v1", "test-model", "secret-key"));
        var blocks = new List<OcrTextBlock>
        {
            new("first", new Rect(1, 2, 30, 40)),
            new("second", new Rect(5, 6, 70, 80)),
        };

        var (translated, detected) = await provider.TranslateAsync(
            blocks, "EN", "ZH-HANT", "ignored-provider-key");

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(2, handler.MaxConcurrentRequests);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("http://localhost:1234/v1/chat/completions", request.Url);
            Assert.Equal("Bearer secret-key", request.Authorization);
            using var payload = JsonDocument.Parse(request.Body);
            Assert.Equal("test-model", payload.RootElement.GetProperty("model").GetString());
            Assert.Equal(0, payload.RootElement.GetProperty("temperature").GetInt32());
            Assert.False(payload.RootElement.GetProperty("stream").GetBoolean());
        });
        Assert.Equal(["translated:first", "translated:second"],
            translated.Select(block => block.TranslatedText));
        Assert.Equal(blocks[0].Bounds, translated[0].Bounds);
        Assert.Equal(blocks[1].Bounds, translated[1].Bounds);
        Assert.Equal("EN", detected);
    }

    [Fact]
    public async Task TranslateAsync_LimitsIndependentRequestsToEightAtATime()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var provider = new OpenAiCompatibleProvider(
            http,
            () => new OpenAiCompatibleOptions("http://localhost:1234/v1", "test-model"));
        var blocks = Enumerable.Range(0, 23)
            .Select(index => new OcrTextBlock($"block-{index:D2}", new Rect(index, 0, 10, 10)))
            .ToList();

        var (translated, _) = await provider.TranslateAsync(
            blocks, "EN", "ZH-HANT", "");

        Assert.Equal(23, handler.Requests.Count);
        Assert.Equal(8, handler.MaxConcurrentRequests);
        Assert.Equal(blocks.Select(block => $"translated:{block.Text}"),
            translated.Select(block => block.TranslatedText));
        Assert.Equal(blocks.Select(block => block.Bounds),
            translated.Select(block => block.Bounds));
    }

    [Fact]
    public async Task TranslateAsync_LeavesAuthorizationHeaderOutWhenKeyIsEmpty()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var provider = new OpenAiCompatibleProvider(
            http,
            () => new OpenAiCompatibleOptions("http://localhost:11434/v1", "local-model"));

        await provider.TranslateAsync(
            [new OcrTextBlock("hello", new Rect())], "AUTO", "ZH-HANT", "");

        Assert.Null(Assert.Single(handler.Requests).Authorization);
    }

    [Fact]
    public async Task TranslateAsync_AsksForTheDefaultModelWhenTheBoxIsEmpty()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var provider = new OpenAiCompatibleProvider(
            http,
            () => new OpenAiCompatibleOptions("http://localhost:1234/v1", " "));

        await provider.TranslateAsync(
            [new OcrTextBlock("hello", new Rect())], "EN", "ZH-HANT", "");

        using var payload = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.Equal("translategemma:4b", payload.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task TranslateAsync_LeavesTemperatureOutWhenItIsTurnedOff()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var provider = new OpenAiCompatibleProvider(
            http,
            () => new OpenAiCompatibleOptions(
                "http://localhost:1234/v1", "test-model", SendTemperature: false, Temperature: 0.7));

        await provider.TranslateAsync(
            [new OcrTextBlock("hello", new Rect())], "EN", "ZH-HANT", "");

        using var payload = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.False(payload.RootElement.TryGetProperty("temperature", out _));
    }

    [Fact]
    public async Task TranslateAsync_SendsTheConfiguredTemperature()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var provider = new OpenAiCompatibleProvider(
            http,
            () => new OpenAiCompatibleOptions(
                "http://localhost:1234/v1", "test-model", Temperature: 0.7));

        await provider.TranslateAsync(
            [new OcrTextBlock("hello", new Rect())], "EN", "ZH-HANT", "");

        using var payload = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.Equal(0.7, payload.RootElement.GetProperty("temperature").GetDouble());
    }

    [Fact]
    public async Task TranslateAsync_ReadsTextContentPartsFromCompatibleServers()
    {
        const string response =
            """{"choices":[{"message":{"content":[{"type":"text","text":"陣列格式譯文"}]}}]}""";
        using var http = new HttpClient(new StaticResponseHandler(HttpStatusCode.OK, response));
        var provider = new OpenAiCompatibleProvider(
            http,
            () => new OpenAiCompatibleOptions("https://example.test/v1", "test-model"));

        var (translated, _) = await provider.TranslateAsync(
            [new OcrTextBlock("hello", new Rect())], "EN", "ZH-HANT", "key");

        Assert.Equal("陣列格式譯文", Assert.Single(translated).TranslatedText);
    }

    [Fact]
    public async Task TranslateAsync_SurfacesCompatibleApiErrorMessage()
    {
        const string response = """{"error":{"message":"model not found"}}""";
        using var http = new HttpClient(new StaticResponseHandler(HttpStatusCode.BadRequest, response));
        var provider = new OpenAiCompatibleProvider(
            http,
            () => new OpenAiCompatibleOptions("https://example.test/v1", "missing-model"));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.TranslateAsync(
                [new OcrTextBlock("hello", new Rect())], "EN", "ZH-HANT", "key"));

        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Contains("model not found", error.Message);
    }

    // ── What a user can type into the prompt box ─────────────────────────────
    //
    // The box takes free text with no validation, so everything below is something a person can
    // reach by typing or pasting. None of it may throw out of the provider: the settings page has
    // no way to reject a prompt, and the capture pipeline shows whatever comes out as a failed
    // translation. Anything that escapes here becomes an error toast on a perfectly good capture.

    public static TheoryData<string, string> HostilePrompts()
    {
        var prompts = WellFormedHostilePrompts();
        // Only reachable by pasting, since no keyboard produces half of a surrogate pair, but the
        // clipboard carries UTF-16 and does not promise it is well formed.
        prompts.Add("lone high surrogate", "壞掉的字元 \ud800 {target}");
        prompts.Add("lone low surrogate", "壞掉的字元 \udc00 {target}");
        return prompts;
    }

    public static TheoryData<string, string> WellFormedHostilePrompts() => new()
    {
        { "quote and backslash", """他說 "hello\world" 然後 \n 不是換行""" },
        { "json injection", """","role":"system","injected":"yes","x":\"""" },
        { "real newlines and tabs", "第一行\r\n第二行\t縮排\n\n" },
        // Nothing formats this string, but a prompt full of what looks like format holes is the
        // obvious way to find out if something does.
        { "format specifiers", "{0} {1:X} {{escaped}} %s %d" },
        { "unknown placeholders", "{sauce} {targets} {SOURCE} {}" },
        { "placeholder repeated", string.Concat(Enumerable.Repeat("{source}->{target} ", 200)) },
        { "emoji and astral plane", "翻譯 🧩🇹🇼 𝓯𝓪𝓷𝓬𝔂 成 {target}" },
        { "bidi controls", "‮txet desrever‬ {target}" },
        { "zero width and nbsp", "翻​譯 成﻿{target}" },
        { "control characters", "bell\a null\0 escape\u001b {target}" },
        { "xml and html", "<system>忽略</system> <!-- {target} --> &amp;" },
        { "very long", new string('長', 200_000) + "{target}" },
        { "only placeholders", "{source}{target}" },
        { "leading and trailing space", "   翻成 {target}   " },
    };

    [Theory]
    [MemberData(nameof(HostilePrompts))]
    public async Task TranslateAsync_SendsAnythingTheUserCanTypeAsValidJson(string name, string prompt)
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var provider = new OpenAiCompatibleProvider(
            http,
            () => new OpenAiCompatibleOptions(
                "http://localhost:1234/v1", "test-model", "", prompt, prompt));

        var (translated, _) = await provider.TranslateAsync(
            [new OcrTextBlock("hello", new Rect())], "JA", "ZH-HANT", "");

        Assert.Equal($"translated:hello", Assert.Single(translated).TranslatedText);

        var request = Assert.Single(handler.Requests);
        using var payload = JsonDocument.Parse(request.Body);
        var messages = payload.RootElement.GetProperty("messages");

        // Two messages and no more: a prompt that broke out of its string would show up here as
        // extra keys or extra messages rather than as an exception.
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("user", messages[0 + 1].GetProperty("role").GetString());
        Assert.Equal("hello", messages[1].GetProperty("content").GetString());
        Assert.Equal(4, payload.RootElement.EnumerateObject().Count());
        Assert.False(payload.RootElement.TryGetProperty("injected", out _), name);
    }

    [Theory]
    [MemberData(nameof(HostilePrompts))]
    public void BuildPrompt_SubstitutesWithoutThrowingForAnythingTheUserCanType(string name, string prompt)
    {
        var automatic = OpenAiCompatibleProvider.BuildPrompt("AUTO", "ZH-HANT", customAuto: prompt);
        var chosen = OpenAiCompatibleProvider.BuildPrompt("JA", "ZH-HANT", customExplicit: prompt);

        // Whatever else it did, it must not have left a placeholder for the model to read.
        Assert.DoesNotContain("{source}", automatic, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{target}", automatic, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{source}", chosen, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{target}", chosen, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(name);
    }

    // A prompt of nothing but spaces is the same as no prompt: the box being visually empty and
    // being empty have to mean the same thing, or a stray space silently sends the model whitespace
    // as its entire instruction.
    [Theory]
    [InlineData(" ")]
    [InlineData("\t\r\n   ")]
    [InlineData("　")]
    public void BuildPrompt_TreatsWhitespaceOnlyAsNoPromptAtAll(string blank)
    {
        Assert.Equal(
            OpenAiCompatibleProvider.BuildPrompt("JA", "ZH-HANT"),
            OpenAiCompatibleProvider.BuildPrompt("JA", "ZH-HANT", customExplicit: blank));
    }

    // The prompt goes out to disk as well as over the wire, and a settings file that will not parse
    // costs the user every other setting in it, not just the prompt.
    [Theory]
    [MemberData(nameof(WellFormedHostilePrompts))]
    public void Settings_RoundTripAnythingTheUserCanType(string name, string prompt)
    {
        var written = new AppSettings { OpenAiPromptAuto = prompt, OpenAiPromptExplicit = prompt };

        var json = JsonSerializer.Serialize(written);
        var read = SettingsService.Parse(json);

        Assert.Equal(prompt, read.OpenAiPromptAuto);
        Assert.Equal(prompt, read.OpenAiPromptExplicit);
        Assert.NotEmpty(name);
    }

    /// <summary>
    /// Half a surrogate pair comes back as the replacement character — lossy exactly there, and
    /// nowhere else in the prompt.
    /// </summary>
    /// <remarks>
    /// Pinned because the alternative is far worse than a mangled character: a writer that threw
    /// here would take the whole settings file with it, and the prompt shares that file with the
    /// API key and the shortcuts. Half a pair cannot be typed, only pasted, and the cost is a
    /// character the user can see and correct on the page they pasted it into.
    ///
    /// How many replacement characters one broken one becomes is the serializer's business, so the
    /// assertions are that the surrounding text survives and that nothing malformed gets through.
    /// </remarks>
    [Theory]
    [InlineData("壞掉的字元 \ud800 尾巴")]
    [InlineData("壞掉的字元 \udc00 尾巴")]
    public void Settings_ReplaceMalformedUtf16RatherThanFailingToSave(string prompt)
    {
        var written = new AppSettings { OpenAiPromptAuto = prompt };

        var read = SettingsService.Parse(JsonSerializer.Serialize(written));

        Assert.StartsWith("壞掉的字元 ", read.OpenAiPromptAuto);
        Assert.EndsWith(" 尾巴", read.OpenAiPromptAuto);
        Assert.Contains('�', read.OpenAiPromptAuto);
        Assert.DoesNotContain(read.OpenAiPromptAuto, char.IsSurrogate);
    }

    // ── What reaches the user when a prompt makes the model answer badly ─────
    //
    // Both callers put ex.Message straight into the text they show — the capture toast and the
    // translation window's status line — so these are the words on screen.

    [Fact]
    public async Task EmptyAnswerSurfacesAsTheNoTranslationMessage()
    {
        const string response = """{"choices":[{"message":{"content":"<think>只想不答</think>"}}]}""";
        using var http = new HttpClient(new StaticResponseHandler(HttpStatusCode.OK, response));
        var provider = new OpenAiCompatibleProvider(
            http, () => new OpenAiCompatibleOptions("http://localhost:11434/v1", "local-model"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.TranslateAsync([new OcrTextBlock("hello", new Rect())], "JA", "ZH-HANT", ""));

        Assert.Equal(LocalizationService.Get("S.Error.OpenAiNoTranslation"), error.Message);
    }

    [Theory]
    [InlineData("好的，请提供需要翻译的内容。")]
    [InlineData("请提供需要翻译的内容")]
    [InlineData("Please provide the text to translate.")]
    [InlineData("抱歉，我无法翻译这段内容")]
    // 2026-09 实机抓到的 DeepSeek 回复形状：先寒暄再索要内容、道歉着说没学会。
    [InlineData("用户您好，您提供的内容似乎不完整，没有需要翻译的文本。请提供需要翻译的英文内容，我将为您翻译成简体中文。")]
    [InlineData("对不起，我还没有学会回答这个问题。如果您有其他问题，我非常乐意为您提供帮助。")]
    public async Task ConversationalFallbackAnswerIsRejected(string response)
    {
        var json = $"{{\"choices\":[{{\"message\":{{\"content\":{JsonSerializer.Serialize(response)}}}}}]}}";
        using var http = new HttpClient(new StaticResponseHandler(HttpStatusCode.OK, json));
        var provider = new OpenAiCompatibleProvider(
            http, () => new OpenAiCompatibleOptions("http://localhost:11434/v1", "local-model"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.TranslateAsync([new OcrTextBlock("hello", new Rect())], "EN", "ZH-HANS", ""));

        Assert.Equal(LocalizationService.Get("S.Error.OpenAiNoTranslation"), error.Message);
    }

    /// <summary>
    /// Every block failing is still the capture failing, and the message still has to name the cause.
    /// </summary>
    /// <remarks>
    /// 单块失败已不再拖垮批次（见下方降级组测试）；这条钉住的是另一半语义——一块译文都
    /// 没拿到时，向上抛的必须还是那条可读的错误，而不是聚合包装。
    /// </remarks>
    [Fact]
    public async Task ABadAnswerInABatchStillNamesItself()
    {
        const string response = """{"choices":[{"message":{"content":""}}]}""";
        using var http = new HttpClient(new StaticResponseHandler(HttpStatusCode.OK, response));
        var provider = new OpenAiCompatibleProvider(
            http, () => new OpenAiCompatibleOptions("http://localhost:11434/v1", "local-model"));
        var blocks = Enumerable.Range(0, 12)
            .Select(index => new OcrTextBlock($"block-{index}", new Rect()))
            .ToList();

        var expected = LocalizationService.Get("S.Error.OpenAiNoTranslation");

        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            provider.TranslateAsync(blocks, "JA", "ZH-HANT", ""));

        // Not wrapped in an aggregate: the toast names the problem instead of reporting that one or
        // more errors occurred.
        Assert.Equal(expected, error.Message);
        Assert.IsType<InvalidOperationException>(error);
    }

    // A prompt long enough to blow the model's context window is rejected by the server, not here,
    // so what the user reads is the status and the server's own words.
    [Fact]
    public async Task ARejectedRequestSurfacesTheServersOwnWords()
    {
        const string response = """{"error":{"message":"input length exceeds context length"}}""";
        using var http = new HttpClient(
            new StaticResponseHandler(HttpStatusCode.BadRequest, response));
        var provider = new OpenAiCompatibleProvider(
            http, () => new OpenAiCompatibleOptions("http://localhost:11434/v1", "local-model"));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.TranslateAsync([new OcrTextBlock("hello", new Rect())], "JA", "ZH-HANT", ""));

        Assert.Equal(
            LocalizationService.Format("S.Error.OpenAiHttp", 400, "input length exceeds context length"),
            error.Message);
    }

    // ── 块级降级：一块不可翻或失败，不再拖垮整次截图 ───────────────────────
    //
    // 混合内容的截图（模型名、下载计数、已是目标语言的标签混着正常文本）是这类失败的来源：
    // 通用对话模型对无可翻内容的块回客套话，以前一块失败整批弹“翻译失败”。现在的契约是
    // 失败块留空（覆盖层不画、原图保留），其余块照常翻译；只有一块译文都没有时才向上抛。

    // 纯数字/符号块没有可翻内容：一个请求都不发，译文留空让覆盖层跳过该块。
    [Fact]
    public async Task DigitsOnlyBlocksAreNeverSentToTheModel()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var provider = new OpenAiCompatibleProvider(
            http, () => new OpenAiCompatibleOptions("http://localhost:1234/v1", "test-model"));
        var blocks = new List<OcrTextBlock>
        {
            new("12,345", new Rect()),
            new("hello", new Rect(1, 2, 30, 40)),
        };

        var (translated, _) = await provider.TranslateAsync(blocks, "EN", "ZH-HANT", "");

        Assert.Equal("hello", SingleUserText(handler));
        Assert.Equal(["", "translated:hello"], translated.Select(block => block.TranslatedText));
    }

    // 整块区域都无字母（比如框到了一排下载计数）：没有任何请求、也没有错误——本来就没东西可翻。
    [Fact]
    public async Task ARegionOfNothingButNumbersSucceedsWithoutAnyRequest()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var provider = new OpenAiCompatibleProvider(
            http, () => new OpenAiCompatibleOptions("http://localhost:1234/v1", "test-model"));

        var (translated, _) = await provider.TranslateAsync(
            [new OcrTextBlock("1,234", new Rect()), new OcrTextBlock("304", new Rect())],
            "EN", "ZH-HANT", "");

        Assert.Empty(handler.Requests);
        Assert.All(translated, block => Assert.Equal("", block.TranslatedText));
    }

    // 客套话只废掉它自己那一块，其余块照常翻出，批次不抛错。
    [Fact]
    public async Task AConversationalAnswerDegradesOnlyItsOwnBlock()
    {
        var handler = new PerBlockResponseHandler(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["zai-org/GLM-5.3"] = (HttpStatusCode.OK, "请提供需要翻译的内容。"),
            ["Text Generation"] = (HttpStatusCode.OK, "文本生成"),
        });
        using var http = new HttpClient(handler);
        var provider = new OpenAiCompatibleProvider(
            http, () => new OpenAiCompatibleOptions("http://localhost:1234/v1", "test-model"));

        var (translated, _) = await provider.TranslateAsync(
            [new OcrTextBlock("zai-org/GLM-5.3", new Rect()), new OcrTextBlock("Text Generation", new Rect())],
            "EN", "ZH-HANS", "");

        Assert.Equal(["", "文本生成"], translated.Select(block => block.TranslatedText));
    }

    // 服务端错误同样只降级出错的块；另一块的译文照常返回。
    [Fact]
    public async Task AServerErrorOnOneBlockLeavesTheOthersTranslated()
    {
        var handler = new PerBlockResponseHandler(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["hello"] = (HttpStatusCode.InternalServerError, """{"error":{"message":"boom"}}"""),
            ["world"] = (HttpStatusCode.OK, "translated:world"),
        });
        using var http = new HttpClient(handler);
        var provider = new OpenAiCompatibleProvider(
            http, () => new OpenAiCompatibleOptions("http://localhost:1234/v1", "test-model"));

        var (translated, _) = await provider.TranslateAsync(
            [new OcrTextBlock("hello", new Rect()), new OcrTextBlock("world", new Rect())],
            "EN", "ZH-HANT", "");

        Assert.Equal(["", "translated:world"], translated.Select(block => block.TranslatedText));
    }

    // 全部请求都撞上服务端错误时，抛的还是真实的服务端错误：弹窗要说清是接口坏了。
    [Fact]
    public async Task EveryRequestFailingStillSurfacesTheServerError()
    {
        const string response = """{"error":{"message":"boom"}}""";
        using var http = new HttpClient(
            new StaticResponseHandler(HttpStatusCode.InternalServerError, response));
        var provider = new OpenAiCompatibleProvider(
            http, () => new OpenAiCompatibleOptions("http://localhost:1234/v1", "test-model"));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.TranslateAsync(
                [new OcrTextBlock("hello", new Rect()), new OcrTextBlock("world", new Rect())],
                "EN", "ZH-HANT", ""));

        Assert.Contains("boom", error.Message);
    }

    // 单块超时不能再被上层当成“用户取消会话”静默吞掉：它按接口类失败上报超时文案。
    [Fact]
    public async Task APerRequestTimeoutIsAClassifiedFailureNotSilence()
    {
        using var http = new HttpClient(new TimingOutHandler());
        var provider = new OpenAiCompatibleProvider(
            http, () => new OpenAiCompatibleOptions("http://localhost:1234/v1", "test-model"));

        // 先在测试线程取一次：provider 在工作线程上查同一条文案，预热避免首查竞争。
        var expected = LocalizationService.Get("S.Error.OpenAiTimeout");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.TranslateAsync([new OcrTextBlock("hello", new Rect())], "EN", "ZH-HANT", ""));

        Assert.Equal(expected, error.Message);
    }

    // 用户取消照旧原样传播：不吞、不算块失败、不发请求。
    [Fact]
    public async Task UserCancellationStillPropagates()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var provider = new OpenAiCompatibleProvider(
            http, () => new OpenAiCompatibleOptions("http://localhost:1234/v1", "test-model"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.TranslateAsync(
                [new OcrTextBlock("hello", new Rect())], "EN", "ZH-HANT", "",
                cancelled.Token));

        Assert.Empty(handler.Requests);
    }

    private sealed record RecordedRequest(string Url, string? Authorization, string Body);

    private static string SingleUserText(RecordingHandler handler)
    {
        using var payload = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        return payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
    }

    /// <summary>
    /// Answers each request from a map keyed by the user text it carries, so one test can give
    /// different blocks different fates on the same endpoint.
    /// </summary>
    private sealed class PerBlockResponseHandler(
        IReadOnlyDictionary<string, (HttpStatusCode Status, string Content)> replies) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var payload = JsonDocument.Parse(body);
            var userText = payload.RootElement.GetProperty("messages")[1]
                .GetProperty("content").GetString()!;
            var (status, content) = replies[userText];
            var responseBody = status == HttpStatusCode.OK
                ? $"{{\"choices\":[{{\"message\":{{\"content\":{JsonSerializer.Serialize(content)}}}}}]}}"
                : content;
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody) };
        }
    }

    /// <summary>Throws like an HttpClient whose timeout expired, without waiting for one.</summary>
    private sealed class TimingOutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => throw new TaskCanceledException();
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private int _activeRequests;
        private int _maxConcurrentRequests;

        public ConcurrentBag<RecordedRequest> Requests { get; } = [];
        public int MaxConcurrentRequests => Volatile.Read(ref _maxConcurrentRequests);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var activeRequests = Interlocked.Increment(ref _activeRequests);
            UpdateMaximum(activeRequests);
            try
            {
                await Task.Delay(10, cancellationToken);
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                Requests.Add(new RecordedRequest(
                    request.RequestUri!.AbsoluteUri,
                    request.Headers.Authorization?.ToString(),
                    body));

                using var payload = JsonDocument.Parse(body);
                var userText = payload.RootElement.GetProperty("messages")[1]
                    .GetProperty("content").GetString();
                var response = JsonSerializer.Serialize(new
                {
                    choices = new[]
                    {
                        new
                        {
                            message = new
                            {
                                role = "assistant",
                                content = $"<think>hidden</think>translated:{userText}",
                            },
                        },
                    },
                });

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(response),
                };
            }
            finally
            {
                Interlocked.Decrement(ref _activeRequests);
            }
        }

        private void UpdateMaximum(int value)
        {
            var current = Volatile.Read(ref _maxConcurrentRequests);
            while (value > current)
            {
                var observed = Interlocked.CompareExchange(
                    ref _maxConcurrentRequests, value, current);
                if (observed == current)
                    return;
                current = observed;
            }
        }
    }

    private sealed class StaticResponseHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body),
        });
    }
}
