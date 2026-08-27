using OverTranslate.Services;

namespace OverTranslate.Models;

/// <param name="Name">The language's name in the interface's own language, which is what a
/// message says it back with.</param>
/// <param name="English">
/// Its English name, carried separately rather than baked into one label: the pickers show both so
/// a user scanning a long list can find theirs either way, while somewhere with one line to spare
/// wants only the first. Composing here means neither has to take a string apart to get at one.
/// </param>
/// <param name="NameSimplified">
/// The Simplified-Chinese spelling of <paramref name="Name"/>, for when the interface runs in
/// zh-Hans. Carried per entry rather than converted at read time because no converter ships with
/// the app, and the list is finite. Null falls back to <paramref name="Name"/> — a code added
/// without one shows Traditional rather than nothing.
/// </param>
/// <param name="StandsAlone">
/// True when <paramref name="Name"/> and <paramref name="English"/> are each a complete label on
/// their own, so the Chinese interface shows the name alone instead of the usual pair. For the OCR
/// picker's automatic entry, whose name already carries its own parenthetical.
/// </param>
public record LangItem(
    string Code, string Name, string English, string? NameSimplified = null, bool StandsAlone = false)
{
    /// <summary>
    /// The name alone, in whichever written form of Chinese the interface is currently in.
    /// </summary>
    private string ChineseName =>
        LocalizationService.Current == LocalizationService.SimplifiedChinese && !string.IsNullOrEmpty(NameSimplified)
            ? NameSimplified!
            : Name;

    /// <summary>
    /// The label for the pickers, in whichever language the interface is currently in.
    /// </summary>
    /// <remarks>
    /// The Chinese interface shows both names, which is what the pair was carried separately for.
    /// The English one shows only <see cref="English"/>: "英语 English" beside an English menu is
    /// noise to a reader who cannot read the first half, and the doubled-up label is what pushed
    /// these combo boxes wide in the first place.
    /// </remarks>
    public string Display
    {
        get
        {
            if (LocalizationService.Current == LocalizationService.English)
                return string.IsNullOrEmpty(English) ? Name : English;

            return StandsAlone || string.IsNullOrEmpty(English) ? ChineseName : $"{ChineseName} {English}";
        }
    }

    /// <summary>
    /// The name alone, in the interface's language, for somewhere with one line to spare.
    /// </summary>
    /// <remarks>
    /// The realtime control bar names the pair it is translating between and has room for neither
    /// the doubled-up label <see cref="Display"/> produces in Chinese nor a language the reader
    /// cannot read.
    /// </remarks>
    public string ShortName =>
        LocalizationService.Current == LocalizationService.English && !string.IsNullOrEmpty(English)
            ? English
            : ChineseName;
}
/// <param name="DisplayKey">Resource key for the name shown in the pickers.</param>
/// <param name="HintKey">Resource key for the line under the picker, or null for no hint.</param>
/// <remarks>
/// Keys rather than text: the list is static, the interface language is not, and a record built
/// once at type-initialisation would otherwise keep whichever language the app started in.
/// Resolving in the properties means callers still just read <see cref="Display"/>.
/// </remarks>
public record ProviderItem(
    TranslationProvider Provider, string DisplayKey, bool RequiresApiKey, string? HintKey = null)
{
    public string Display => LocalizationService.Get(DisplayKey);
    public string? Hint => HintKey is null ? null : LocalizationService.Get(HintKey);
}

public static class LanguageData
{
    public const string AutomaticSourceLanguage = "AUTO";
    public const string DefaultSourceLanguage = AutomaticSourceLanguage;
    public const string DefaultOcrSourceLanguage = AutomaticSourceLanguage;
    public const string DefaultTargetLanguage = "ZH-HANS";

    /// <summary>
    /// What 即時翻譯 reads from until the user says otherwise — see
    /// <see cref="GetValidRealtimeSourceCode"/> for why it cannot be 自動 like the screenshot flow's.
    /// </summary>
    public const string DefaultRealtimeSourceLanguage = "EN";

    public static readonly List<LangItem> SourceLanguages =
    [
        new(AutomaticSourceLanguage, "自動", "Automatic", "自动"),
        new("EN",      "英語", "English", "英语"),
        new("JA",      "日語", "Japanese", "日语"),
        new("KO",      "韓語", "Korean", "韩语"),
        new("ZH",      "簡體中文", "Simplified Chinese", "简体中文"),
        new("ZH-HANT", "繁體中文", "Traditional Chinese", "繁体中文"),
        new("BG",      "保加利亞語", "Bulgarian", "保加利亚语"),
        new("CS",      "捷克語", "Czech", "捷克语"),
        new("DA",      "丹麥語", "Danish", "丹麦语"),
        new("DE",      "德語", "German", "德语"),
        new("EL",      "希臘語", "Greek", "希腊语"),
        new("ES",      "西班牙語", "Spanish", "西班牙语"),
        new("ET",      "愛沙尼亞語", "Estonian", "爱沙尼亚语"),
        new("FI",      "芬蘭語", "Finnish", "芬兰语"),
        new("FR",      "法語", "French", "法语"),
        new("HU",      "匈牙利語", "Hungarian", "匈牙利语"),
        new("ID",      "印尼語", "Indonesian", "印尼语"),
        new("IT",      "義大利語", "Italian", "意大利语"),
        new("LT",      "立陶宛語", "Lithuanian", "立陶宛语"),
        new("LV",      "拉脫維亞語", "Latvian", "拉脱維亚语"),
        new("NB",      "挪威語", "Norwegian", "挪威语"),
        new("NL",      "荷蘭語", "Dutch", "荷兰语"),
        new("PL",      "波蘭語", "Polish", "波兰语"),
        new("PT",      "葡萄牙語", "Portuguese", "葡萄牙语"),
        new("RO",      "羅馬尼亞語", "Romanian", "罗马尼亚语"),
        new("RU",      "俄語", "Russian", "俄语"),
        new("SK",      "斯洛伐克語", "Slovak", "斯洛伐克语"),
        new("SL",      "斯洛文尼亞語", "Slovenian", "斯洛文尼亚语"),
        new("SV",      "瑞典語", "Swedish", "瑞典语"),
        new("TR",      "土耳其語", "Turkish", "土耳其语"),
        new("UK",      "烏克蘭語", "Ukrainian", "乌克兰语"),
    ];

    public static readonly List<LangItem> OcrSourceLanguages =
    [
        new(AutomaticSourceLanguage, "自動（中英日）", "Automatic (ZH/EN/JA)", "自动（中英日）", StandsAlone: true),
        new("EN",      "英語", "English", "英语"),
        new("JA",      "日語", "Japanese", "日语"),
        new("KO",      "韓語", "Korean", "韩语"),
        new("ZH",      "簡體中文", "Simplified Chinese", "简体中文"),
        new("ZH-HANT", "繁體中文", "Traditional Chinese", "繁体中文"),
    ];

    public static readonly List<ProviderItem> Providers =
    [
        new(TranslationProvider.Google,    "S.Provider.Google",    false, "S.Provider.GoogleHint"),
        new(TranslationProvider.Google2,   "S.Provider.Google2",   false, "S.Provider.Google2Hint"),
        new(TranslationProvider.Bing,      "S.Provider.Bing",      false),
        new(TranslationProvider.Microsoft, "S.Provider.Microsoft", false),
        new(TranslationProvider.DeepL,     "S.Provider.DeepL",     true,  "S.Provider.DeepLHint"),
        new(TranslationProvider.OpenAI,    "S.Provider.OpenAI",    false, "S.Provider.OpenAIHint"),
    ];

    /// <summary>
    /// Display name of a provider as shown in the selectors, for use in user-facing messages.
    /// Falls back to the enum name so a newly added provider never shows an empty label.
    /// </summary>
    public static string GetProviderDisplay(TranslationProvider provider) =>
        Providers.FirstOrDefault(p => p.Provider == provider)?.Display ?? provider.ToString();

    public static readonly List<LangItem> TargetLanguages =
    [
        new("EN-US",   "英語", "English", "英语"),
        new("JA",      "日語", "Japanese", "日语"),
        new("KO",      "韓語", "Korean", "韩语"),
        new("ZH-HANS", "簡體中文", "Simplified Chinese", "简体中文"),
        new("ZH-HANT", "繁體中文", "Traditional Chinese", "繁体中文"),
        new("BG",      "保加利亞語", "Bulgarian", "保加利亚语"),
        new("CS",      "捷克語", "Czech", "捷克语"),
        new("DA",      "丹麥語", "Danish", "丹麦语"),
        new("DE",      "德語", "German", "德语"),
        new("EL",      "希臘語", "Greek", "希腊语"),
        new("ES",      "西班牙語", "Spanish", "西班牙语"),
        new("ET",      "愛沙尼亞語", "Estonian", "爱沙尼亚语"),
        new("FI",      "芬蘭語", "Finnish", "芬兰语"),
        new("FR",      "法語", "French", "法语"),
        new("HU",      "匈牙利語", "Hungarian", "匈牙利语"),
        new("ID",      "印尼語", "Indonesian", "印尼语"),
        new("IT",      "義大利語", "Italian", "意大利语"),
        new("LT",      "立陶宛語", "Lithuanian", "立陶宛语"),
        new("LV",      "拉脫維亞語", "Latvian", "拉脱維亚语"),
        new("NB",      "挪威語", "Norwegian", "挪威语"),
        new("NL",      "荷蘭語", "Dutch", "荷兰语"),
        new("PL",      "波蘭語", "Polish", "波兰语"),
        new("PT-BR",   "葡萄牙語", "Portuguese", "葡萄牙语"),
        new("RO",      "羅馬尼亞語", "Romanian", "罗马尼亚语"),
        new("RU",      "俄語", "Russian", "俄语"),
        new("SK",      "斯洛伐克語", "Slovak", "斯洛伐克语"),
        new("SL",      "斯洛文尼亞語", "Slovenian", "斯洛文尼亚语"),
        new("SV",      "瑞典語", "Swedish", "瑞典语"),
        new("TR",      "土耳其語", "Turkish", "土耳其语"),
        new("UK",      "烏克蘭語", "Ukrainian", "乌克兰语"),
    ];

    public static string? MapTargetToSourceCode(string targetCode)
    {
        if (string.IsNullOrWhiteSpace(targetCode)) return null;

        var mapped = targetCode.ToUpperInvariant() switch
        {
            "ZH-HANT" => "ZH-HANT",
            "ZH-HANS" => "ZH",
            _ => targetCode.Split('-')[0]
        };

        return SourceLanguages.Any(l => l.Code.Equals(mapped, StringComparison.OrdinalIgnoreCase))
            ? SourceLanguages.First(l => l.Code.Equals(mapped, StringComparison.OrdinalIgnoreCase)).Code
            : null;
    }

    public static string? MapSourceToTargetCode(string sourceCode)
    {
        if (string.IsNullOrWhiteSpace(sourceCode)) return null;

        var mapped = sourceCode.ToUpperInvariant() switch
        {
            "ZH-HANT" => "ZH-HANT",
            "ZH" => "ZH-HANS",
            "EN" => "EN-US",
            _ => sourceCode
        };

        var exact = TargetLanguages.FirstOrDefault(l =>
            l.Code.Equals(mapped, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact.Code;

        var prefix = TargetLanguages.FirstOrDefault(l =>
            l.Code.StartsWith(mapped + "-", StringComparison.OrdinalIgnoreCase));
        return prefix?.Code;
    }

    public static bool IsAutomaticSource(string? sourceCode) =>
        AutomaticSourceLanguage.Equals(sourceCode, StringComparison.OrdinalIgnoreCase);

    public static string GetValidSourceCode(string? sourceCode)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
            return DefaultSourceLanguage;

        var match = SourceLanguages.FirstOrDefault(l =>
            l.Code.Equals(sourceCode, StringComparison.OrdinalIgnoreCase));
        return match?.Code ?? DefaultSourceLanguage;
    }

    public static string GetValidOcrSourceCode(string? sourceCode)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
            return DefaultOcrSourceLanguage;

        var match = OcrSourceLanguages.FirstOrDefault(l =>
            l.Code.Equals(sourceCode, StringComparison.OrdinalIgnoreCase));
        return match?.Code ?? DefaultOcrSourceLanguage;
    }

    /// <summary>
    /// What 即時翻譯 reads from, for a setting that may be blank, invalid, or 自動.
    /// </summary>
    /// <remarks>
    /// Its own resolver rather than <see cref="GetValidOcrSourceCode"/> because the two fall back to
    /// different places: 自動 is a fine answer for a screenshot, which can be read again, and not one
    /// realtime can use — its picker does not offer it, recognition there gets one look at a frame,
    /// and a mode that guesses per frame makes a subtitle track flicker between languages.
    ///
    /// So everything that is not a real language lands on <see cref="DefaultRealtimeSourceLanguage"/>:
    /// a blank from before this had a default, 自動 from a hand-edited file, and a code retired since.
    /// One branch covers all three, and the result is always something the session can actually read.
    /// </remarks>
    public static string GetValidRealtimeSourceCode(string? sourceCode)
    {
        var resolved = GetValidOcrSourceCode(sourceCode);
        return IsAutomaticSource(resolved) ? DefaultRealtimeSourceLanguage : resolved;
    }

    /// <summary>
    /// Codes whose model-facing spelling is not just this application's own code in lower case.
    /// </summary>
    /// <remarks>
    /// The pickers carry codes chosen for the translation APIs — DeepL's <c>EN-US</c>, <c>PT-BR</c>,
    /// <c>ZH-HANT</c> — and a local translation model expects BCP 47 instead. Only the entries that
    /// actually differ are listed; everything else is a plain two-letter code that lower-casing
    /// already gets right, and listing those too would be a table to keep in step for no gain.
    ///
    /// <c>ZH</c> maps to Simplified rather than bare <c>zh</c> because that is what it means in this
    /// application's own OCR picker (簡體中文), and a model given <c>zh</c> has to guess between the
    /// two scripts.
    /// </remarks>
    private static readonly Dictionary<string, string> ModelLanguageTags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EN-US"] = "en",
        ["ZH"] = "zh-Hans",
        ["ZH-HANS"] = "zh-Hans",
        ["ZH-HANT"] = "zh-Hant",
        ["PT-BR"] = "pt-BR",
    };

    /// <summary>
    /// The language tag to name a language by when instructing a translation model, e.g. <c>ja</c>.
    /// </summary>
    /// <remarks>
    /// Translation-only models are trained with the language named as "Japanese (ja)" — the tag is
    /// not decoration beside the name, it is the part the model was trained to key on. See
    /// <see cref="Services.Providers.OpenAiCompatibleProvider.DefaultPromptTemplate"/>.
    ///
    /// Returns empty for 自動, which has no language to name: the prompt says "any language" there
    /// and a tag would be inventing one.
    /// </remarks>
    public static string GetModelLanguageTag(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || IsAutomaticSource(code))
            return "";

        return ModelLanguageTags.TryGetValue(code, out var mapped)
            ? mapped
            : code.ToLowerInvariant();
    }

    public static string GetValidTargetCode(string? targetCode)
    {
        if (string.IsNullOrWhiteSpace(targetCode))
            return DefaultTargetLanguage;

        var match = TargetLanguages.FirstOrDefault(l =>
            l.Code.Equals(targetCode, StringComparison.OrdinalIgnoreCase));
        return match?.Code ?? DefaultTargetLanguage;
    }

    /// <summary>
    /// A source language's name for saying back to the user what is in effect: the interface's own
    /// name for it, without the English one the pickers also carry, because a line with one
    /// language's worth of room has a reader who already knows which language they are reading in.
    /// Falls back to the code, which is still readable, rather than to an empty string.
    /// </summary>
    public static string GetSourceDisplayName(string? code) =>
        OcrSourceLanguages.FirstOrDefault(l =>
            l.Code.Equals(code, StringComparison.OrdinalIgnoreCase))?.ShortName ?? code ?? "";

    /// <inheritdoc cref="GetSourceDisplayName"/>
    public static string GetTargetDisplayName(string? code) =>
        TargetLanguages.FirstOrDefault(l =>
            l.Code.Equals(code, StringComparison.OrdinalIgnoreCase))?.ShortName ?? code ?? "";
}
