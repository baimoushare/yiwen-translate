using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OverTranslate.Models;

namespace OverTranslate.Services.Providers;

/// <summary>百度通用翻译 API vip provider。</summary>
public sealed class BaiduProvider : ITranslationProvider
{
    private const string Endpoint = "https://fanyi-api.baidu.com/api/trans/vip/translate";
    private readonly HttpClient _http;

    public BaiduProvider(HttpClient? httpClient = null) => _http = httpClient ?? new HttpClient();

    public bool RequiresApiKey => true;

    public async Task<(List<TranslatedBlock> Blocks, string DetectedLang)> TranslateAsync(
        List<OcrTextBlock> blocks, string sourceLang, string targetLang, string apiKey,
        CancellationToken cancellationToken = default)
    {
        var settings = SettingsService.Instance.Current.TranslationApis;
        var appId = settings.BaiduAppId.Trim();
        var secretKey = settings.BaiduSecretKey.Trim();
        if (appId.Length == 0 || secretKey.Length == 0)
            throw new InvalidOperationException("百度翻译未配置 AppId 或 SecretKey。");
        if (blocks.Count == 0) return ([], "");

        var source = MapLanguage(sourceLang, true);
        var target = MapLanguage(targetLang, false);
        var results = await Task.WhenAll(blocks.Select(block => TranslateOneAsync(
            block.Text, source, target, appId, secretKey, cancellationToken)));
        var translated = new List<TranslatedBlock>(blocks.Count);
        var detected = results.Select(r => r.Detected).FirstOrDefault(s => s.Length > 0) ?? "";
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            translated.Add(new(block.Text, results[i].Text, block.Bounds, block.Lines, block.SourceGlyphHeight));
        }
        return (translated, detected);
    }

    private async Task<(string Text, string Detected)> TranslateOneAsync(
        string text, string source, string target, string appId, string secretKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var salt = RandomNumberGenerator.GetInt32(int.MaxValue).ToString();
        var sign = Md5(appId + text + salt + secretKey);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["q"] = text, ["from"] = source, ["to"] = target,
            ["appid"] = appId, ["salt"] = salt, ["sign"] = sign,
        });
        using var response = await _http.PostAsync(Endpoint, content, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"百度翻译请求失败（HTTP {(int)response.StatusCode}）。");
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("error_code", out var error))
            {
                var code = error.ValueKind == JsonValueKind.String
                    ? error.GetString()
                    : error.ToString();
                if (!string.IsNullOrWhiteSpace(code) && code != "0")
                    throw new InvalidOperationException($"百度翻译返回错误（代码 {code}）。");
            }
            var textResult = string.Join("\n", root.GetProperty("trans_result").EnumerateArray()
                .Select(item => item.GetProperty("dst").GetString() ?? ""));
            var detected = root.TryGetProperty("from", out var from) ? ToAppLanguage(from.GetString()) : "";
            return (textResult, detected);
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidOperationException("百度翻译返回了无法解析的响应。", ex);
        }
    }

    internal static string MapLanguage(string code, bool source) => code.Trim().ToUpperInvariant() switch
    {
        "AUTO" when source => "auto",
        "ZH" or "ZH-HANS" => "zh",
        "ZH-HANT" => "cht",
        "EN" or "EN-US" or "EN-GB" => "en",
        "JA" => "jp", "KO" => "kor", "FR" => "fra", "ES" => "spa",
        "DE" => "de", "IT" => "it", "PT" or "PT-BR" => "pt", "RU" => "ru",
        "NL" => "nl", "PL" => "pl", "BG" => "bul", "CS" => "cs",
        "DA" => "dan", "FI" => "fin", "EL" => "el", "ET" => "est",
        "HU" => "hu", "ID" => "id", "LT" => "lt", "LV" => "lv",
        "RO" => "rom", "SK" => "slo", "SL" => "slo", "SV" => "swe", "TR" => "tr", "UK" => "uk",
        _ => code.Trim().ToLowerInvariant().Split('-')[0],
    };

    private static string ToAppLanguage(string? code) => (code ?? "").ToLowerInvariant() switch
    {
        "zh" => "ZH", "cht" or "zh-tw" => "ZH-HANT", "en" => "EN", "jp" or "ja" => "JA",
        "kor" or "ko" => "KO", "fra" or "fr" => "FR", "spa" or "es" => "ES",
        _ => (code ?? "").ToUpperInvariant(),
    };

    private static string Md5(string value) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
