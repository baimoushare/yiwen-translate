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
        var (provider, customId, services, deepLKey, translationApis, chatGptKey) =
            (settings.Provider, settings.ActiveCustomServiceId, settings.CustomServices,
             settings.ApiKey, settings.TranslationApis, settings.ChatGptApiKey);
        try
        {
            settings.Provider = TranslationProvider.Microsoft;
            settings.ActiveCustomServiceId = "";
            settings.CustomServices = [];
            settings.ApiKey = "";
            settings.TranslationApis = new TranslationApiSettings();
            settings.ChatGptApiKey = "";
            arrange(settings);
            assert();
        }
        finally
        {
            settings.Provider = provider;
            settings.ActiveCustomServiceId = customId;
            settings.CustomServices = services;
            settings.ApiKey = deepLKey;
            settings.TranslationApis = translationApis;
            settings.ChatGptApiKey = chatGptKey;
        }
    }

    [Fact]
    public void Options_ListFreeBuiltInsAndConfiguredCustomServices()
    {
        WithServices(
            s =>
            {
                s.CustomServices.Add(new CustomTranslatorService { Name = "未配置" });
                s.CustomServices.Add(new CustomTranslatorService
                {
                    Name = "我的DeepSeek",
                    BaseUrl = "https://api.deepseek.com/v1",
                    ApiKey = "deepseek-key",
                });
            },
            () =>
            {
                var options = ServiceSelection.Options();

                Assert.All(
                    LanguageData.Providers.Where(p => !p.RequiresApiKey),
                    provider => Assert.Contains(options, o => o.Value == provider.Provider.ToString()));
                Assert.DoesNotContain(options, o => o.Value == "DeepL");
                Assert.DoesNotContain(options, o => o.IsCustom && o.Display == "未配置");
                Assert.Contains(options, o => o.IsCustom && o.Display == "我的DeepSeek");
            });
    }

    [Fact]
    public void Options_ListConfiguredLocalServiceWithoutApiKey()
    {
        WithServices(
            s => s.CustomServices.Add(new CustomTranslatorService
            {
                Name = "Ollama",
                BaseUrl = "http://localhost:11434/v1",
            }),
            () => Assert.Contains(
                ServiceSelection.Options(),
                option => option.IsCustom && option.Display == "Ollama"));
    }

    [Fact]
    public void Options_ListCredentialedBuiltInsOnlyWhenFullyConfigured()
    {
        WithServices(
            s =>
            {
                s.ApiKey = "deepl-key";
                s.TranslationApis.BaiduAppId = "baidu-id";
                s.TranslationApis.TencentSecretId = "tencent-id";
                s.TranslationApis.TencentSecretKey = "tencent-key";
                s.ChatGptApiKey = "openai-key";
            },
            () =>
            {
                var options = ServiceSelection.Options();

                Assert.Contains(options, o => o.Value == "DeepL");
                Assert.DoesNotContain(options, o => o.Value == "Baidu");
                Assert.Contains(options, o => o.Value == "Tencent");
                Assert.Contains(options, o => o.Value == "ChatGPT");
            });
    }

    [Fact]
    public void ACustomSelection_WritesProviderAndIdTogether()
    {
        WithServices(
            s =>
            {
                var service = new CustomTranslatorService { Name = "x", BaseUrl = "http://localhost:11434/v1" };
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
                s.CustomServices.Add(new CustomTranslatorService
                {
                    Name = "x",
                    BaseUrl = "http://localhost:11434/v1",
                });
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
    public void AnUnavailableCurrentProvider_FallsBackToDefaultFreeProvider()
    {
        WithServices(
            s => s.Provider = TranslationProvider.DeepL,
            () =>
            {
                Assert.Equal("Microsoft", ServiceSelection.CurrentValue());
                Assert.Equal(TranslationProvider.Microsoft, SettingsService.Instance.Current.Provider);
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
