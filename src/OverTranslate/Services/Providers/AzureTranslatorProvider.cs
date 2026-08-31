using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using OverTranslate.Models;
using OverTranslate.Services;

namespace OverTranslate.Services.Providers;

/// <summary>
/// Azure AI Translator Text v3.0 的官方 REST 客户端。
/// 订阅密钥和区域从 TranslationApis 读取，避免将平台特有凭据塞入通用参数。
/// </summary>
public sealed class AzureTranslatorProvider : ITranslationProvider
{
    private const string Endpoint = "https://api.cognitive.microsofttranslator.com/translate?api-version=3.0";
    private readonly HttpClient _httpClient;

    public AzureTranslatorProvider(HttpClient? httpClient = null)
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
        var settings = SettingsService.Instance.Current.TranslationApis;
        var subscriptionKey = settings.AzureSubscriptionKey?.Trim();
        var region = settings.AzureRegion?.Trim();
        if (string.IsNullOrEmpty(subscriptionKey))
            throw new InvalidOperationException("Azure Translator subscription key is not configured.");
        if (string.IsNullOrEmpty(region))
            throw new InvalidOperationException("Azure Translator region is not configured.");

        var target = MapLanguage(targetLang);
        if (string.IsNullOrEmpty(target))
            throw new ArgumentException("A target language is required.", nameof(targetLang));

        var query = $"{Endpoint}&to={Uri.EscapeDataString(target)}";
        // Azure AUTO 的官方语义是省略 from 参数，而非发送 AUTO。
        if (!LanguageData.IsAutomaticSource(sourceLang))
        {
            var source = MapLanguage(sourceLang);
            if (!string.IsNullOrEmpty(source))
                query += $"&from={Uri.EscapeDataString(source)}";
        }

        // v3 API 接受数组对象，响应数组与请求数组严格按序对应。
        var body = blocks.Select(block => new { Text = block.Text }).ToArray();
        using var request = new HttpRequestMessage(HttpMethod.Post, query);
        request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", subscriptionKey);
        request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Region", region);
        request.Content = new StringContent(
            JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Azure Translator returned an invalid response.");

        var result = new List<TranslatedBlock>(blocks.Count);
        var votes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var responseCount = document.RootElement.GetArrayLength();
        for (var i = 0; i < blocks.Count; i++)
        {
            var translated = "";
            var detected = "";
            if (i < responseCount)
            {
                var item = document.RootElement[i];
                if (item.TryGetProperty("translations", out var translations) &&
                    translations.ValueKind == JsonValueKind.Array && translations.GetArrayLength() > 0)
                {
                    var translation = translations[0];
                    if (translation.TryGetProperty("text", out var text))
                        translated = text.GetString() ?? "";
                }

                if (item.TryGetProperty("detectedLanguage", out var detectedLanguage) &&
                    detectedLanguage.TryGetProperty("language", out var language))
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
            "ZH" or "ZH-HANS" => "zh-Hans",
            "ZH-HANT" => "zh-Hant",
            "PT" or "PT-BR" or "PT-PT" => "pt",
            "NB" => "nb",
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
            "ZH-HANS" or "ZH-CN" => "ZH",
            "ZH-HANT" or "ZH-TW" => "ZH-HANT",
            "NO" => "NB",
            _ => normalized.Split('-')[0]
        };
    }

    private static string MostFrequent(Dictionary<string, int> votes) =>
        votes.Count == 0 ? "" : votes.MaxBy(pair => pair.Value).Key;
}
