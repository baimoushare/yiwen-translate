using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using OverTranslate.Models;
using OverTranslate.Services;

namespace OverTranslate.Services.Providers;

/// <summary>
/// Google Cloud Translation Basic v2 的官方 REST 客户端。
/// 凭据从设置读取，而不是使用接口中为兼容旧提供商保留的 apiKey 参数。
/// </summary>
public sealed class GoogleCloudProvider : ITranslationProvider
{
    private const string Endpoint = "https://translation.googleapis.com/language/translate/v2";
    private readonly HttpClient _httpClient;

    public GoogleCloudProvider(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public bool RequiresApiKey => true;

    public async Task<(List<TranslatedBlock> Blocks, string DetectedLang)> TranslateAsync(
        List<OcrTextBlock> blocks, string sourceLang, string targetLang, string apiKey,
        CancellationToken cancellationToken = default)
    {
        if (blocks.Count == 0)
            return ([], "");

        cancellationToken.ThrowIfCancellationRequested();
        var configuredKey = SettingsService.Instance.Current.TranslationApis.GoogleCloudApiKey?.Trim();
        if (string.IsNullOrEmpty(configuredKey))
            throw new InvalidOperationException("Google Cloud Translation API key is not configured.");

        var target = MapLanguage(targetLang);
        if (string.IsNullOrEmpty(target))
            throw new ArgumentException("A target language is required.", nameof(targetLang));

        var requestBody = new Dictionary<string, object?>
        {
            ["q"] = blocks.Select(block => block.Text).ToArray(),
            ["target"] = target,
            ["format"] = "text"
        };
        // AUTO 的官方语义是省略 source，而不是发送字面量 AUTO。
        if (!LanguageData.IsAutomaticSource(sourceLang))
        {
            var source = MapLanguage(sourceLang);
            if (!string.IsNullOrEmpty(source))
                requestBody["source"] = source;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        // 使用请求头传递密钥，避免密钥和 OCR 原文出现在 URL、代理或 HTTP 日志中。
        request.Headers.TryAddWithoutValidation("x-goog-api-key", configuredKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("translations", out var translations) ||
            translations.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Google Cloud Translation returned an invalid response.");

        var result = new List<TranslatedBlock>(blocks.Count);
        var votes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < blocks.Count; i++)
        {
            var translated = "";
            var detected = "";
            if (i < translations.GetArrayLength())
            {
                var item = translations[i];
                if (item.TryGetProperty("translatedText", out var text))
                    translated = text.GetString() ?? "";
                if (item.TryGetProperty("detectedSourceLanguage", out var language))
                    detected = MapDetectedLanguage(language.GetString());
            }

            if (!string.IsNullOrEmpty(detected))
                votes[detected] = votes.GetValueOrDefault(detected) + 1;
            result.Add(CopyMetadata(blocks[i], translated));
        }

        return (result, MostFrequent(votes));
    }

    private static TranslatedBlock CopyMetadata(OcrTextBlock source, string translated) =>
        new(source.Text, translated, source.Bounds, source.Lines, source.SourceGlyphHeight);

    private static string MapLanguage(string language)
    {
        var normalized = language.Trim().ToUpperInvariant();
        return normalized switch
        {
            "EN" or "EN-US" or "EN-GB" => "en",
            "ZH" or "ZH-HANS" => "zh-CN",
            "ZH-HANT" => "zh-TW",
            "PT" or "PT-BR" or "PT-PT" => "pt",
            "NB" => "no",
            "AUTO" => "",
            _ => normalized.ToLowerInvariant().Split('-')[0]
        };
    }

    private static string MapDetectedLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return "";

        var normalized = language.Trim().ToUpperInvariant();
        return normalized switch
        {
            "ZH-CN" or "ZH-HANS" => "ZH",
            "ZH-TW" or "ZH-HANT" => "ZH-HANT",
            "NO" => "NB",
            _ => normalized.Split('-')[0]
        };
    }

    private static string MostFrequent(Dictionary<string, int> votes) =>
        votes.Count == 0 ? "" : votes.MaxBy(pair => pair.Value).Key;
}
