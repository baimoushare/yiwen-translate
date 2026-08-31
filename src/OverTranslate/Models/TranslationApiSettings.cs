namespace OverTranslate.Models;

/// <summary>
/// Credentials for traditional translation platforms. They stay separate because each platform has
/// its own authentication contract; copying them into one generic API-key field loses required
/// identifiers such as Azure region or Tencent SecretId.
/// </summary>
public class TranslationApiSettings
{
    public string BaiduAppId { get; set; } = "";
    public string BaiduSecretKey { get; set; } = "";

    public string TencentSecretId { get; set; } = "";
    public string TencentSecretKey { get; set; } = "";
    public string TencentRegion { get; set; } = "ap-beijing";

    public string YoudaoAppKey { get; set; } = "";
    public string YoudaoAppSecret { get; set; } = "";

    public string GoogleCloudApiKey { get; set; } = "";

    public string AzureSubscriptionKey { get; set; } = "";
    public string AzureRegion { get; set; } = "";
}
