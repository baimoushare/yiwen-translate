using OverTranslate.Models;

namespace OverTranslate.Services;

/// <summary>
/// One pre-filled starting point on the add-a-service sheet: a name, the endpoint, and a model
/// that works there, so a user adding DeepSeek types a key and nothing else.
/// </summary>
/// <param name="Initial">
/// The letter shown on the service page's card for this vendor, or empty for 「空白」 which has
/// no card. The app ships no vendor logos (they are trademarks, and drawing them keeps none of
/// the recognition at this size) — a brand-coloured tile with the initial reads as fast.
/// </param>
/// <param name="BrandColor">The tile's fill, or empty to take the neutral tile.</param>
/// <param name="DarkLetter">True when the letter needs the dark ink against the brand fill.</param>
/// <remarks>
/// Endpoints are the vendors' public OpenAI-compatible bases; the model named is one that exists
/// there today. A template only ever fills the form — every field stays editable afterwards, and
/// 「空白」 exists for a provider this list has never heard of.
///
/// The service page matches a preset card to a user's service by BaseUrl, so the base URLs here
/// are also the card's identity: change one and the card stops claiming services added under the
/// old one, which is the honest outcome of pointing at a different endpoint.
/// </remarks>
public enum CustomServicePlan
{
    Standard,
    Coding,
    Blank,
}

public record CustomServiceTemplate(
    string Vendor,
    CustomServicePlan Plan,
    string Name,
    string BaseUrl,
    string Model,
    string Initial = "",
    string BrandColor = "",
    bool DarkLetter = false)
{
    public static readonly List<CustomServiceTemplate> Presets =
    [
        new("ollama", CustomServicePlan.Standard, "Ollama（本地）", "http://localhost:11434/v1", "translategemma:4b", "O", "#F5F5F5", DarkLetter: true),
        new("deepseek", CustomServicePlan.Standard, "DeepSeek", "https://api.deepseek.com/v1", "deepseek-chat", "D", "#4D6BFE"),
        new("glm", CustomServicePlan.Standard, "智谱 GLM", "https://open.bigmodel.cn/api/paas/v4", "glm-4-flash", "智", "#3859FF"),
        new("glm", CustomServicePlan.Coding, "智谱 GLM Coding Plan", "https://open.bigmodel.cn/api/coding/paas/v4", "glm-4.5-air", "智", "#3859FF"),
        new("kimi", CustomServicePlan.Standard, "Kimi (Moonshot)", "https://api.moonshot.cn/v1", "moonshot-v1-8k", "K", "#1F232B"),
        new("kimi", CustomServicePlan.Coding, "Kimi Coding Plan", "https://api.kimi.com/coding/v1", "kimi-k2-0711-preview", "K", "#1F232B"),
        new("siliconflow", CustomServicePlan.Standard, "硅基流动", "https://api.siliconflow.cn/v1", "Qwen/Qwen2.5-7B-Instruct", "S", "#7C3AED"),
        new("siliconflow", CustomServicePlan.Coding, "硅基流动 Coding Plan", "https://api.siliconflow.cn/v1", "Qwen/Qwen3-30B-A3B", "S", "#7C3AED"),
        new("qwen", CustomServicePlan.Standard, "千问", "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen-plus", "Q", "#1677FF"),
        new("qwen", CustomServicePlan.Coding, "千问 Coding Plan", "https://coding.dashscope.aliyuncs.com/v1", "qwen-plus", "Q", "#1677FF"),
        new("openrouter", CustomServicePlan.Standard, "OpenRouter", "https://openrouter.ai/api/v1", "google/gemini-2.0-flash-001", "O", "#6467F2"),
        new("grok", CustomServicePlan.Standard, "Grok (xAI)", "https://api.x.ai/v1", "grok-2-latest", "G", "#101418"),
        new("blank", CustomServicePlan.Blank, "空白", "", ""),
    ];

    /// <summary>One card per vendor; Standard is the card's preferred default plan.</summary>
    public static IEnumerable<CustomServiceTemplate> VendorCards =>
        Presets.Where(template => template.Plan == CustomServicePlan.Standard);

    public static IEnumerable<CustomServiceTemplate> OptionsFor(CustomServiceTemplate? vendor) =>
        vendor is null
            ? Presets.Where(template => template.Plan == CustomServicePlan.Blank)
            : Presets.Where(template => template.Vendor == vendor.Vendor);

    /// <summary>
    /// Whether a service's endpoint belongs to this vendor — any of its plans counts, so a Coding
    /// Plan service (whose endpoint differs from the standard API's) is still claimed by its card.
    /// </summary>
    public static bool BelongsToVendor(string baseUrl, string vendor) =>
        Presets.Any(template =>
            template.Vendor == vendor &&
            template.BaseUrl.Length > 0 &&
            SameEndpoint(baseUrl, template.BaseUrl));

    /// <summary>Whether two base URLs point at the same endpoint, slash conventions aside.</summary>
    public static bool SameEndpoint(string a, string b) =>
        a.Trim().TrimEnd('/').Equals(b.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}
