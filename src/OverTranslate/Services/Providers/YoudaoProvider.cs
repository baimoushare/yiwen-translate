using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OverTranslate.Models;

namespace OverTranslate.Services.Providers;

/// <summary>有道智云自然语言翻译 API provider。</summary>
public sealed class YoudaoProvider : ITranslationProvider
{
    private const string Endpoint = "https://openapi.youdao.com/api";
    private readonly HttpClient _http;

    public YoudaoProvider(HttpClient? httpClient = null) => _http = httpClient ?? new HttpClient();

    public bool RequiresApiKey => true;

    public async Task<(List<TranslatedBlock> Blocks, string DetectedLang)> TranslateAsync(
        List<OcrTextBlock> blocks, string sourceLang, string targetLang, string apiKey,
        CancellationToken cancellationToken = default)
    {
        var settings = SettingsService.Instance.Current.TranslationApis;
        var appKey = settings.YoudaoAppKey.Trim();
        var appSecret = settings.YoudaoAppSecret.Trim();
        if (appKey.Length == 0 || appSecret.Length == 0)
            throw new InvalidOperationException("有道翻译未配置 AppKey 或 AppSecret。");
        if (blocks.Count == 0) return ([], "");

        var source = MapLanguage(sourceLang, true);
        var target = MapLanguage(targetLang, false);
        var results = await Task.WhenAll(blocks.Select(block => TranslateOneAsync(
            block.Text, source, target, appKey, appSecret, cancellationToken)));
        var translated = new List<TranslatedBlock>(blocks.Count);
        var detected = results.Select(r => r.Detected).FirstOrDefault(s => s.Length > 0) ?? "";
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            translated.Add(new(block.Text, results[i].Text, block.Bounds, block.Lines, block.SourceGlyphHeight));
        }
        return (translated, detected);
    }

    private async Task<(string Text, string Detected)> TranslateOneAsync(string text, string source,
        string target, string appKey, string appSecret, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var salt = Guid.NewGuid().ToString("N");
        var curtime = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var sign = Sha256(appKey + Truncate(text) + salt + curtime + appSecret);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["q"] = text, ["from"] = source, ["to"] = target,
            ["appKey"] = appKey, ["salt"] = salt, ["sign"] = sign,
            ["signType"] = "v3", ["curtime"] = curtime,
        });
        using var response = await _http.PostAsync(Endpoint, content, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"有道翻译请求失败（HTTP {(int)response.StatusCode}）。");
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var errorCode = root.TryGetProperty("errorCode", out var error) ? error.GetString() : null;
            if (!string.IsNullOrEmpty(errorCode) && errorCode != "0")
                throw new InvalidOperationException($"有道翻译返回错误（代码 {errorCode}）。");
            var translation = root.GetProperty("translation").EnumerateArray()
                .Select(item => item.GetString() ?? "");
            var detected = root.TryGetProperty("l", out var language) ? ToAppLanguage(language.GetString()) : "";
            return (string.Join("\n", translation), detected);
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException)
        {
            throw new InvalidOperationException("有道翻译返回了无法解析的响应。", ex);
        }
    }

    // 有道 v3 签名要求 q 长度超过 20 时只拼接首尾各 10 个字符。
    private static string Truncate(string text) => text.Length <= 20 ? text : text[..10] + text[^10..];

    internal static string MapLanguage(string code, bool source) => code.Trim().ToUpperInvariant() switch
    {
        "AUTO" when source => "auto",
        "ZH" or "ZH-HANS" => "zh-CHS",
        "ZH-HANT" => "zh-CHT",
        "EN" or "EN-US" or "EN-GB" => "en", "JA" => "ja", "KO" => "ko",
        "FR" => "fr", "ES" => "es", "DE" => "de", "IT" => "it", "PT" or "PT-BR" => "pt",
        "RU" => "ru", "NL" => "nl", "PL" => "pl", "AR" => "ar", "TR" => "tr",
        "VI" => "vi", "ID" => "id", "TH" => "th",
        _ => code.Trim().ToLowerInvariant().Split('-')[0],
    };

    private static string ToAppLanguage(string? code) => (code ?? "").ToLowerInvariant() switch
    {
        "zh-chs" or "zh" => "ZH", "zh-cht" => "ZH-HANT", "en" => "EN", "ja" => "JA", "ko" => "KO",
        _ => (code ?? "").ToUpperInvariant(),
    };

    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
