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
public record ServiceOption(string Value, string Display, bool RequiresSetup, bool IsCustom);

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

    /// <summary>The built-in providers followed by the user's services, in picker order.</summary>
    public static List<ServiceOption> Options()
    {
        var options = LanguageData.Providers
            .Select(p => new ServiceOption(
                p.Provider.ToString(), p.Display, p.RequiresApiKey, IsCustom: false))
            .ToList();

        foreach (var service in SettingsService.Instance.Current.CustomServices)
        {
            var name = service.Name.Trim();
            if (name.Length == 0) name = LocalizationService.Get("S.Services.CustomUntitled");
            options.Add(new ServiceOption(
                CustomPrefix + service.Id, name,
                RequiresSetup: false, IsCustom: true));
        }

        return options;
    }

    /// <summary>The encoded value of the service the shared preference currently names.</summary>
    public static string CurrentValue()
    {
        var s = SettingsService.Instance.Current;
        if (s.Provider == TranslationProvider.OpenAI &&
            s.CustomServices.Any(c => c.Id == s.ActiveCustomServiceId))
            return CustomPrefix + s.ActiveCustomServiceId;
        return s.Provider.ToString();
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
