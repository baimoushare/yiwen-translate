namespace OverTranslate.Models;

/// <summary>
/// One user-added translation service speaking the OpenAI-compatible Chat Completions contract.
/// </summary>
/// <remarks>
/// The providers a machine can reach are not knowable in advance — every aggregator, every local
/// runtime, every national AI vendor exposes the same wire format — so the set of services is the
/// user's to build: a name for the picker, an endpoint, a key when the endpoint wants one, a model,
/// and the prompt/temperature knobs <see cref="AppSettings"/> already carried for the single
/// built-in OpenAI slot. Everything empty-able falls back exactly the way the built-in slot does.
/// </remarks>
public class CustomTranslatorService
{
    /// <summary>Stable identity across renames, used to say which one is active.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>The label in pickers. Empty shows as 「自定义服务」.</summary>
    public string Name { get; set; } = "";

    /// <summary>Empty means the OpenAI official endpoint, as with the built-in slot.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Never logged, never exported — DiagnosticBundleService redacts it.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Empty means the provider's default translation model.</summary>
    public string Model { get; set; } = "";

    public bool TemperatureEnabled { get; set; } = true;
    public double Temperature { get; set; }

    public string PromptAuto { get; set; } = "";
    public string PromptExplicit { get; set; } = "";

    /// <summary>Per-request budget; the shared HttpClient's own timeout stays the outer bound.</summary>
    public int TimeoutSeconds { get; set; } = 60;
}
