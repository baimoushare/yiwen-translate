using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;
using OverTranslate.Models;

namespace OverTranslate.Services;

/// <summary>
/// One row of a service picker: a wire value that says which service, and the label to show.
/// </summary>
/// <param name="Value">
/// "Microsoft" for a built-in provider (the enum name), "custom:&lt;id&gt;" for a user-added one.
/// One string so every picker — translation page, quick lookup, capture toolbar — can hold its
/// selection in one field regardless of which kind of service it points at.
/// </param>
public record ServiceOption(
    string Value, string Display, bool RequiresSetup, bool IsCustom,
    string? Hint = null,
    string GroupKey = "S.Provider.Group.Traditional")
{
    public string Group => LocalizationService.Get(GroupKey);
}

/// <summary>
/// The one place that knows a picker's selection resolves to a built-in provider or to one of the
/// user's custom OpenAI-compatible services.
/// </summary>
/// <remarks>
/// <see cref="AppSettings.Provider"/> stays the built-in half of the choice and
/// <see cref="AppSettings.ActiveCustomServiceId"/> the custom half, so a settings file from before
/// custom services existed reads back exactly as it was written. An id whose service has been
/// deleted falls back to the built-in OpenAI slot rather than to nothing: that slot works as
/// shipped, and "the service I picked is gone" is better served by a working default than by a
/// picker showing no selection at all.
/// </remarks>
public static class ServiceSelection
{
    public const string CustomPrefix = "custom:";

    /// <summary>The built-in providers followed by configured user services, in picker order.</summary>
    public static List<ServiceOption> Options()
    {
        var options = LanguageData.Providers
            .Where(p => IsAvailable(p.Provider))
            .Select(p => new ServiceOption(
                p.Provider.ToString(), p.Display, p.RequiresApiKey, IsCustom: false,
                Hint: p.Hint, GroupKey: p.GroupKey))
            .ToList();

        foreach (var service in SettingsService.Instance.Current.CustomServices.Where(IsAvailable))
        {
            var name = service.Name.Trim();
            if (name.Length == 0) name = LocalizationService.Get("S.Services.CustomUntitled");
            options.Add(new ServiceOption(
                CustomPrefix + service.Id, name,
                RequiresSetup: false, IsCustom: true,
                GroupKey: "S.Provider.Group.AI"));
        }

        return options;
    }

    /// <summary>
    /// Returns the current selection and repairs a preference that points to a service which is no
    /// longer configured. This keeps the value shown by the picker and the provider used for the
    /// subsequent translation on the same service.
    /// </summary>
    public static string CurrentValue()
    {
        var s = SettingsService.Instance.Current;
        var custom = s.CustomServices.FirstOrDefault(c => c.Id == s.ActiveCustomServiceId);
        if (s.Provider == TranslationProvider.OpenAI && custom is not null)
        {
            if (IsAvailable(custom)) return CustomPrefix + custom.Id;

            // A custom service uses the built-in OpenAI slot underneath it. If its configuration is
            // incomplete, clear only the custom half and retain that usable local slot.
            s.ActiveCustomServiceId = "";
            return s.Provider.ToString();
        }

        if (IsAvailable(s.Provider)) return s.Provider.ToString();

        // A credentialed provider can become unavailable after its settings are cleared. Fall back
        // to the application's established keyless default rather than leaving the picker with no
        // matching item or tying the fallback to provider-list ordering.
        s.ActiveCustomServiceId = "";
        s.Provider = TranslationProvider.Microsoft;
        return s.Provider.ToString();
    }

    /// <summary>Whether a built-in or custom service has enough configuration to be used.</summary>
    private static bool IsAvailable(TranslationProvider provider)
    {
        var s = SettingsService.Instance.Current;
        return provider switch
        {
            TranslationProvider.DeepL => !string.IsNullOrWhiteSpace(s.ApiKey),
            TranslationProvider.Baidu => !string.IsNullOrWhiteSpace(s.TranslationApis.BaiduAppId) &&
                                         !string.IsNullOrWhiteSpace(s.TranslationApis.BaiduSecretKey),
            TranslationProvider.Tencent => !string.IsNullOrWhiteSpace(s.TranslationApis.TencentSecretId) &&
                                           !string.IsNullOrWhiteSpace(s.TranslationApis.TencentSecretKey),
            TranslationProvider.Youdao => !string.IsNullOrWhiteSpace(s.TranslationApis.YoudaoAppKey) &&
                                          !string.IsNullOrWhiteSpace(s.TranslationApis.YoudaoAppSecret),
            TranslationProvider.GoogleCloud => !string.IsNullOrWhiteSpace(s.TranslationApis.GoogleCloudApiKey),
            TranslationProvider.AzureTranslator => !string.IsNullOrWhiteSpace(s.TranslationApis.AzureSubscriptionKey),
            TranslationProvider.ChatGPT => !string.IsNullOrWhiteSpace(s.ChatGptApiKey),
            _ => true,
        };
    }

    private static bool IsAvailable(CustomTranslatorService service)
    {
        if (!string.IsNullOrWhiteSpace(service.ApiKey)) return true;
        if (!Uri.TryCreate(service.BaseUrl.Trim(), UriKind.Absolute, out var endpoint)) return false;

        // Local OpenAI-compatible servers commonly need no key. A remote preset with only its
        // pre-filled URL is still incomplete and must not appear until its API key is saved.
        return endpoint.IsLoopback;
    }

    /// <summary>Returns all available options as a grouped WPF view for ComboBox menus.</summary>
    public static ICollectionView GroupedOptions() => GroupedView(Options());

    /// <summary>Returns available built-in options as a grouped WPF view.</summary>
    public static ICollectionView GroupedBuiltInOptions() =>
        GroupedView(Options().Where(option => !option.IsCustom).ToList());

    private static ICollectionView GroupedView(IEnumerable<ServiceOption> options)
    {
        var view = CollectionViewSource.GetDefaultView(options.ToList());
        view.GroupDescriptions.Clear();
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ServiceOption.Group)));
        return view;
    }

    /// <summary>Writes a picker's choice back as the built-in half plus the custom half.</summary>
    public static void ApplyValue(string? value)
    {
        var s = SettingsService.Instance.Current;

        if (value is not null && value.StartsWith(CustomPrefix, StringComparison.Ordinal))
        {
            var id = value[CustomPrefix.Length..];
            if (s.CustomServices.Any(c => c.Id == id))
            {
                s.Provider = TranslationProvider.OpenAI;
                s.ActiveCustomServiceId = id;
                return;
            }
        }

        // A built-in name, an id that no longer exists, anything unrecognisable: the built-in
        // choice, with no custom id claiming to be active alongside it.
        s.ActiveCustomServiceId = "";
        if (Enum.TryParse(value, out TranslationProvider parsed) &&
            LanguageData.Providers.Any(p => p.Provider == parsed))
            s.Provider = parsed;
    }

    /// <summary>The service a custom: value names, or null for anything else.</summary>
    public static CustomTranslatorService? ResolveCustom(string? value) =>
        value is not null && value.StartsWith(CustomPrefix, StringComparison.Ordinal)
            ? SettingsService.Instance.Current.CustomServices
                .FirstOrDefault(c => c.Id == value[CustomPrefix.Length..])
            : null;
}
