using OverTranslate.Models;
using OverTranslate.Services;
using Xunit;

namespace OverTranslate.Tests;

/// <summary>
/// The encoding that lets one picker value name either a built-in provider or a user-added service,
/// and the fall-back when a named service no longer exists.
/// </summary>
public class ServiceSelectionTests
{
    private static void WithServices(Action<AppSettings> arrange, Action assert)
    {
        var settings = SettingsService.Instance.Current;
        var (provider, customId, services) =
            (settings.Provider, settings.ActiveCustomServiceId, settings.CustomServices);
        try
        {
            settings.Provider = TranslationProvider.Microsoft;
            settings.ActiveCustomServiceId = "";
            settings.CustomServices = [];
            arrange(settings);
            assert();
        }
        finally
        {
            settings.Provider = provider;
            settings.ActiveCustomServiceId = customId;
            settings.CustomServices = services;
        }
    }

    [Fact]
    public void Options_ListBuiltInsThenCustom()
    {
        WithServices(
            s => s.CustomServices.Add(new CustomTranslatorService { Name = "我的DeepSeek" }),
            () =>
            {
                var options = ServiceSelection.Options();

                Assert.Equal(LanguageData.Providers.Count + 1, options.Count);
                Assert.Contains(options, o => o.Value == "Microsoft");
                Assert.Contains(options, o => o.IsCustom && o.Display == "我的DeepSeek");
            });
    }

    [Fact]
    public void ACustomSelection_WritesProviderAndIdTogether()
    {
        WithServices(
            s =>
            {
                var service = new CustomTranslatorService { Name = "x" };
                s.CustomServices.Add(service);
                ServiceSelection.ApplyValue(ServiceSelection.CustomPrefix + service.Id);
            },
            () =>
            {
                var settings = SettingsService.Instance.Current;
                Assert.Equal(TranslationProvider.OpenAI, settings.Provider);
                Assert.NotEqual("", settings.ActiveCustomServiceId);
                Assert.StartsWith(ServiceSelection.CustomPrefix, ServiceSelection.CurrentValue());
            });
    }

    [Fact]
    public void ADeletedCustomSelection_FallsBackToBuiltinOpenAi()
    {
        WithServices(
            s =>
            {
                s.CustomServices.Add(new CustomTranslatorService { Name = "x" });
                var id = s.CustomServices[0].Id;
                ServiceSelection.ApplyValue(ServiceSelection.CustomPrefix + id);
                // then the service is deleted behind the preference's back
                s.CustomServices.RemoveAt(0);
            },
            () =>
            {
                Assert.Equal(TranslationProvider.OpenAI, SettingsService.Instance.Current.Provider);
                Assert.Equal("OpenAI", ServiceSelection.CurrentValue());
            });
    }

    [Fact]
    public void AnUnrecognisedValue_LeavesAPickerSelectableState()
    {
        WithServices(
            _ => ServiceSelection.ApplyValue("nonsense"),
            () => Assert.Equal(
                "Microsoft", ServiceSelection.CurrentValue()));
    }
}
