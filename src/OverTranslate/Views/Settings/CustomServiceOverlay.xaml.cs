using System.Windows;
using System.Windows.Controls;
using OverTranslate.Models;
using OverTranslate.Services;
using OverTranslate.Services.Providers;

namespace OverTranslate.Views.Settings;

/// <summary>
/// Adds and edits one of the user's custom OpenAI-compatible services, over the shell the way
/// <see cref="ServiceSettingsOverlay"/> covers it for the built-in ones.
/// </summary>
/// <remarks>
/// Two modes out of one sheet: Open-for-add shows the template strip and saves a new service;
/// Open-for-edit hides the strip and saves over an existing one. The strip only ever fills the
/// form — the save below it is what creates the service, so a user who picks DeepSeek can still
/// rename it or point it at a relay before it exists.
///
/// Deleting the service that is active falls back to the built-in OpenAI slot rather than leaving
/// the preference naming a service that is no longer there — ServiceSelection.ApplyValue makes
/// that same fall-back for a hand-edited settings file, and this is the one-click version of it.
/// </remarks>
// UseWindowsForms puts System.Windows.Forms in the implicit usings, so this name collides
using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;

public partial class CustomServiceOverlay : UserControl
{
    /// <summary>The template chosen on the strip, or null until one is.</summary>
    private CustomServiceTemplate? _pickedTemplate;

    /// <summary>The service being edited, or null in add mode.</summary>
    private CustomTranslatorService? _editing;

    public event EventHandler? Closed;

    public CustomServiceOverlay()
    {
        InitializeComponent();

        // The listing answers the values currently on this form, saved or not — a key that was
        // just typed is as good as one that was stored.
        ModelField.Source = () => (
            BaseUrlBox.Text.Trim(),
            ApiKeyBox.Secret,
            ReadTimeoutSeconds());
    }

    /// <summary>Add mode: templates on show, empty form, no delete button.</summary>
    public void OpenForAdd() => OpenForAdd(null);

    /// <summary>
    /// Add mode pre-aimed at one template: the service page's preset cards come in through here,
    /// so the sheet opens already filled for that vendor the way a click on the strip fills it.
    /// </summary>
    public void OpenForAdd(CustomServiceTemplate? template)
    {
        _editing = null;
        TemplatePanel.Visibility = Visibility.Visible;
        DeleteBtn.Visibility = Visibility.Collapsed;
        TitleText.Text = LocalizationService.Get("S.Custom.AddTitle");

        NameBox.Text = "";
        BaseUrlBox.Text = "";
        ApiKeyBox.Secret = "";
        ModelField.Text = "";
        TimeoutBox.Text = "";
        TemperatureEnabledBox.IsChecked = true;
        TemperatureBox.Text = "0";
        // 预填内置默认提示词：两个框都不再从空白开始，“留空等于默认”的约定改成看得见的文字
        PromptAutoBox.Text = OpenAiCompatibleProvider.DefaultPromptTemplate(automatic: true);
        PromptExplicitBox.Text = OpenAiCompatibleProvider.DefaultPromptTemplate(automatic: false);

        BuildTemplateStrip(CustomServiceTemplate.OptionsFor(template));

        if (template is null)
        {
            _pickedTemplate = null;
        }
        else
        {
            ApplyTemplate(template);
        }
        MarkPickedTemplate();

        Show();
    }

    /// <summary>Edit mode: the form loaded from the service, no templates, delete available.</summary>
    public void OpenForEdit(CustomTranslatorService service)
    {
        _editing = service;
        TemplatePanel.Visibility = Visibility.Collapsed;
        DeleteBtn.Visibility = Visibility.Visible;
        TitleText.Text = LocalizationService.Get("S.Custom.Title");

        NameBox.Text = service.Name;
        BaseUrlBox.Text = service.BaseUrl;
        ApiKeyBox.Secret = service.ApiKey;
        ModelField.Text = service.Model;
        TimeoutBox.Text = service.TimeoutSeconds == 60 ? "" : service.TimeoutSeconds.ToString();
        TemperatureEnabledBox.IsChecked = service.TemperatureEnabled;
        TemperatureBox.Text = service.Temperature == 0 ? "" : service.Temperature.ToString();
        // 空的提示词照旧意味着“用内置默认”，编辑时也把那份默认亮出来——用户看到的就是会被
        // 发出的那份文字，保存后即成为自己的版本
        PromptAutoBox.Text = service.PromptAuto.Trim().Length > 0
            ? service.PromptAuto
            : OpenAiCompatibleProvider.DefaultPromptTemplate(automatic: true);
        PromptExplicitBox.Text = service.PromptExplicit.Trim().Length > 0
            ? service.PromptExplicit
            : OpenAiCompatibleProvider.DefaultPromptTemplate(automatic: false);

        Show();
    }

    private void Show()
    {
        Visibility = Visibility.Visible;
        Focus();
    }

    public void Close()
    {
        Visibility = Visibility.Collapsed;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ── Template strip ─────────────────────────────────────────────────────

    private void BuildTemplateStrip(IEnumerable<CustomServiceTemplate> templates)
    {
        TemplateList.Children.Clear();
        foreach (var template in templates)
        {
            var button = new Button
            {
                Style = (Style)FindResource("FlatButton"),
                Content = template.Name,
                Tag = template,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(10, 5, 10, 5),
            };
            button.Click += Template_Click;
            TemplateList.Children.Add(button);
        }
    }

    private void Template_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not CustomServiceTemplate template) return;
        ApplyTemplate(template);
        MarkPickedTemplate();
    }

    /// <summary>
    /// Fills rather than merges: picking another template replaces what the last one put in,
    /// because mixing two vendors' endpoints and models is never what the click meant.
    /// The prompts are left alone — they start as the built-in defaults and may already be edited.
    /// </summary>
    private void ApplyTemplate(CustomServiceTemplate template)
    {
        _pickedTemplate = template;
        NameBox.Text = template.Plan == CustomServicePlan.Blank ? "" : template.Name;
        BaseUrlBox.Text = template.BaseUrl;
        ModelField.Text = template.Model;
    }

    private void MarkPickedTemplate()
    {
        foreach (var child in TemplateList.Children)
            if (child is Button button)
                button.FontWeight = ReferenceEquals(button.Tag, _pickedTemplate)
                    ? FontWeights.SemiBold
                    : FontWeights.Normal;
    }

    // ── 表单读取（保存与测试共用） ─────────────────────────────────────────

    private int ReadTimeoutSeconds() =>
        int.TryParse(TimeoutBox.Text.Trim(), out var seconds) && seconds > 0 ? seconds : 60;

    private double ReadTemperature() =>
        double.TryParse(TemperatureBox.Text.Trim(), out var temperature)
            ? Math.Clamp(temperature, 0, 2) : 0;

    /// <summary>What the boxes currently say, as one options record — saved by 保存, spent by 测试.</summary>
    private OpenAiCompatibleOptions BuildOptions() => new(
        BaseUrlBox.Text.Trim(),
        ModelField.Text.Trim(),
        ApiKeyBox.Secret,
        PromptAutoBox.Text,
        PromptExplicitBox.Text,
        TemperatureEnabledBox.IsChecked == true,
        ReadTemperature(),
        ReadTimeoutSeconds());

    // ── 连通测试 ───────────────────────────────────────────────────────────

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        TestBtn.IsEnabled = false;
        TestResult.Text = LocalizationService.Get("S.Custom.Testing");
        TestResult.SetResourceReference(TextBlock.ForegroundProperty, "AppTextSecondary");

        try
        {
            var (ok, detail) = await OpenAiCompatibleProvider.TestConnectionAsync(BuildOptions());
            TestResult.Text = LocalizationService.Format(
                ok ? "S.Custom.TestOk" : "S.Custom.TestFail", detail);
            TestResult.SetResourceReference(
                TextBlock.ForegroundProperty, ok ? "AppSuccess" : "AppError");
            TestResult.ToolTip = detail;
        }
        catch (Exception ex)
        {
            TestResult.Text = LocalizationService.Format("S.Custom.TestFail", ex.Message);
            TestResult.SetResourceReference(TextBlock.ForegroundProperty, "AppError");
            TestResult.ToolTip = ex.Message;
        }
        finally
        {
            TestBtn.IsEnabled = true;
        }
    }

    // ── 恢复默认提示词 ─────────────────────────────────────────────────────

    private void PromptAutoReset_Click(object sender, RoutedEventArgs e) =>
        PromptAutoBox.Text = OpenAiCompatibleProvider.DefaultPromptTemplate(automatic: true);

    private void PromptExplicitReset_Click(object sender, RoutedEventArgs e) =>
        PromptExplicitBox.Text = OpenAiCompatibleProvider.DefaultPromptTemplate(automatic: false);

    // ── Save / delete ──────────────────────────────────────────────────────

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var settings = SettingsService.Instance.Current;

        var service = _editing ?? new CustomTranslatorService();
        service.Name         = NameBox.Text.Trim();
        service.BaseUrl      = BaseUrlBox.Text.Trim();
        service.ApiKey       = ApiKeyBox.Secret;
        service.Model        = ModelField.Text.Trim();
        service.TimeoutSeconds = ReadTimeoutSeconds();
        service.TemperatureEnabled = TemperatureEnabledBox.IsChecked == true;
        service.Temperature = ReadTemperature();
        service.PromptAuto     = PromptAutoBox.Text;
        service.PromptExplicit = PromptExplicitBox.Text;

        if (_editing is null)
            settings.CustomServices.Add(service);

        SettingsService.Instance.Save();
        Close();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_editing is null) return;

        var settings = SettingsService.Instance.Current;
        settings.CustomServices.Remove(_editing);
        if (settings.ActiveCustomServiceId == _editing.Id)
        {
            settings.ActiveCustomServiceId = "";
            settings.Provider = TranslationProvider.OpenAI;
        }

        SettingsService.Instance.Save();
        Close();
    }
}
