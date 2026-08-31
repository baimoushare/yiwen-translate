using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OverTranslate.Models;

namespace OverTranslate.Services.Providers;

/// <summary>腾讯云机器翻译 TextTranslate provider，使用官方 TC3-HMAC-SHA256 签名。</summary>
public sealed class TencentProvider : ITranslationProvider
{
    private const string Endpoint = "https://tmt.tencentcloudapi.com";
    private const string Service = "tmt";
    private const string Version = "2018-03-21";
    private readonly HttpClient _http;

    public TencentProvider(HttpClient? httpClient = null) => _http = httpClient ?? new HttpClient();

    public bool RequiresApiKey => true;

    public async Task<(List<TranslatedBlock> Blocks, string DetectedLang)> TranslateAsync(
        List<OcrTextBlock> blocks, string sourceLang, string targetLang, string apiKey,
        CancellationToken cancellationToken = default)
    {
        var settings = SettingsService.Instance.Current.TranslationApis;
        var secretId = settings.TencentSecretId.Trim();
        var secretKey = settings.TencentSecretKey.Trim();
        var region = settings.TencentRegion.Trim();
        if (secretId.Length == 0 || secretKey.Length == 0)
            throw new InvalidOperationException("腾讯云翻译未配置 SecretId 或 SecretKey。");
        if (blocks.Count == 0) return ([], "");
        if (region.Length == 0) region = "ap-beijing";

        var source = MapLanguage(sourceLang, true);
        var target = MapLanguage(targetLang, false);
        var results = await Task.WhenAll(blocks.Select(block => TranslateOneAsync(
            block.Text, source, target, secretId, secretKey, region, cancellationToken)));
        var translated = new List<TranslatedBlock>(blocks.Count);
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            translated.Add(new(block.Text, results[i], block.Bounds, block.Lines, block.SourceGlyphHeight));
        }
        return (translated, LanguageData.IsAutomaticSource(sourceLang) ? "" : sourceLang.ToUpperInvariant());
    }

    private async Task<string> TranslateOneAsync(string text, string source, string target,
        string secretId, string secretKey, string region, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = JsonSerializer.Serialize(new { SourceText = text, Source = source, Target = target, ProjectId = 0 });
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var date = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime.ToString("yyyy-MM-dd");
        const string contentType = "application/json; charset=utf-8";
        var payloadHash = Sha256(payload);
        var canonicalHeaders = $"content-type:{contentType}\nhost:tmt.tencentcloudapi.com\n";
        const string signedHeaders = "content-type;host";
        var canonicalRequest = $"POST\n/\n\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";
        var credentialScope = $"{date}/{Service}/tc3_request";
        var stringToSign = $"TC3-HMAC-SHA256\n{timestamp}\n{credentialScope}\n{Sha256(canonicalRequest)}";
        var secretDate = HmacSha256(Encoding.UTF8.GetBytes("TC3" + secretKey), date);
        var secretService = HmacSha256(secretDate, Service);
        var secretSigning = HmacSha256(secretService, "tc3_request");
        var signature = Convert.ToHexString(HmacSha256(secretSigning, stringToSign)).ToLowerInvariant();

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        request.Headers.Host = "tmt.tencentcloudapi.com";
        request.Headers.TryAddWithoutValidation("X-TC-Action", "TextTranslate");
        request.Headers.TryAddWithoutValidation("X-TC-Version", Version);
        request.Headers.TryAddWithoutValidation("X-TC-Timestamp", timestamp.ToString());
        request.Headers.TryAddWithoutValidation("X-TC-Region", region);
        request.Headers.TryAddWithoutValidation("X-TC-Language", "zh-CN");
        request.Headers.TryAddWithoutValidation("Authorization",
            $"TC3-HMAC-SHA256 Credential={secretId}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}");
        using var response = await _http.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"腾讯云翻译请求失败（HTTP {(int)response.StatusCode}）。");
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement.GetProperty("Response");
            if (root.TryGetProperty("Error", out var error))
            {
                var code = error.TryGetProperty("Code", out var codeElement) ? codeElement.GetString() : null;
                throw new InvalidOperationException($"腾讯云翻译返回错误（代码 {code ?? "未知"}）。");
            }
            return root.GetProperty("TargetText").GetString() ?? "";
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException)
        {
            throw new InvalidOperationException("腾讯云翻译返回了无法解析的响应。", ex);
        }
    }

    internal static string MapLanguage(string code, bool source) => code.Trim().ToUpperInvariant() switch
    {
        "AUTO" when source => "auto",
        "ZH" or "ZH-HANS" => "zh",
        "ZH-HANT" => "zh-TW",
        "EN" or "EN-US" or "EN-GB" => "en",
        "JA" => "ja", "KO" => "ko", "FR" => "fr", "ES" => "es", "DE" => "de",
        "IT" => "it", "PT" or "PT-BR" => "pt", "RU" => "ru", "TR" => "tr",
        "ID" => "id", "NL" => "nl", "TH" => "th", "AR" => "ar",
        _ => code.Trim().ToLowerInvariant().Split('-')[0],
    };

    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static byte[] HmacSha256(byte[] key, string value) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value));
}
