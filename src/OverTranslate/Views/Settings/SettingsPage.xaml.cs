using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using OverTranslate.Models;
using OverTranslate.Services;
using OverTranslate.Services.Providers;
using OverTranslate.Views;
using OverTranslate.Views.Controls;
using System.Windows.Shapes;
using Microsoft.Win32;
// UseWindowsForms puts System.Windows.Forms in the implicit usings, so these names collide
using UserControl = System.Windows.Controls.UserControl;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Clipboard = System.Windows.Clipboard;
using Orientation = System.Windows.Controls.Orientation;
using FontFamily = System.Windows.Media.FontFamily;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace OverTranslate.Views.Settings;

/// <summary>
/// Settings persist the moment a control changes — there is no save button, so every handler
/// routes through <see cref="Persist"/>, which is inert while <see cref="_loading"/> is set.
/// </summary>
/// <remarks>
/// What a single translation service has to be told is not on this page: DeepL's key and OpenAI's
/// endpoint, model and prompt live in <see cref="ServiceSettingsOverlay"/>, reached from the tile
/// for that service. Everything here applies whichever service is chosen.
/// </remarks>
public partial class SettingsPage : UserControl
{
    private readonly DispatcherTimer _statusHold;

    /// <summary>
    /// The bundle written by the last press, so the row that appears afterwards can open the
    /// thing that was just sent. Null until the first export of this session.
    /// </summary>
    private string? _lastBundlePath;
    private readonly DispatcherTimer _hotkeyGamepadRecordTimer;
    private readonly ushort[] _recordGamepadButtons = new ushort[4];

    /// <summary>
    /// One editable shortcut: the controls that edit it and the settings it reads and writes.
    /// </summary>
    /// <param name="Action">
    /// Which shortcut this is, so the row can be matched against <see cref="HotkeyBindings.Resolve"/>
    /// — the one place that decides which of two shortcuts sharing a combination stays on.
    /// </param>
    /// <param name="AdvertisedInShell">
    /// Whether the shell's nav rail prints this combination beside a 快速工具 row, and so has to be
    /// told when it changes. True for the two shortcuts those rows name; false for the rest, which
    /// the interface names nowhere.
    /// </param>
    /// <remarks>
    /// There is no record button in the record because there is none on the page: the box is
    /// read-only and starts recording when it is clicked, so a button beside it would have been a
    /// second way to do the one thing clicking the box already does.
    /// </remarks>
    /// <param name="EnabledBox">
    /// The tick that turns this shortcut off, or null for the capture one, which has no tick at all
    /// and no setting behind it because that shortcut is the feature the application is for. Null
    /// here is what says "this row cannot be turned off".
    /// </param>
    /// <param name="EnabledLabel">
    /// The word beside the switch saying which way it is set. Null for the capture row, which has
    /// no switch to describe.
    /// </param>
    /// <remarks>
    /// A row shadowed by a higher-priority shortcut says nothing about it. Priority still decides
    /// which of two shortcuts sharing a combination is registered — see
    /// <see cref="HotkeyBindings.Resolve"/>, and MainWindow logs the one it dropped — but the page
    /// does not carry a line for a state a user reaches only by having set the clash themselves.
    /// </remarks>
    private sealed record HotkeyField(
        HotkeyAction Action,
        string NameKey,
        TextBox Box,
        Func<AppSettings, string> Display,
        Func<AppSettings, ShortcutTrigger> Trigger,
        Action<AppSettings, ShortcutTrigger, string> Apply,
        bool AdvertisedInShell,
        // Fully qualified: WinForms is also referenced here and has its own CheckBox.
        System.Windows.Controls.CheckBox? EnabledBox = null,
        Action<AppSettings, bool>? SetEnabled = null,
        TextBlock? EnabledLabel = null);

    private HotkeyField[] _hotkeyFields = [];

    /// <summary>The field waiting for a key, or null. At most one at a time.</summary>
    private HotkeyField? _recording;

    /// <summary>True while the controls are being populated, so initialization never writes back.</summary>
    private bool _loading;

    private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

    public SettingsPage()
    {
        InitializeComponent();

        // In the order the page lists them, which is not the order HotkeyBindings resolves them in:
        // there, position is priority and the newest shortcut goes last, while here it is what a
        // reader meets first. Nothing depends on this order — rows are looked up by action.
        _hotkeyFields =
        [
            new HotkeyField(
                HotkeyAction.Capture,
                "S.Settings.CaptureHotkey",
                HotkeyBox,
                s => s.HotkeyDisplay,
                s => HotkeyBindings.TriggerFor(s, HotkeyAction.Capture),
                ApplyCaptureTrigger,
                AdvertisedInShell: true),
            new HotkeyField(
                HotkeyAction.QuickLookup,
                "S.Settings.QuickLookupHotkey",
                QuickLookupHotkeyBox,
                s => s.QuickLookupHotkeyDisplay,
                s => HotkeyBindings.TriggerFor(s, HotkeyAction.QuickLookup),
                ApplyQuickLookupTrigger,
                AdvertisedInShell: true,
                QuickLookupHotkeyEnabledCheckBox,
                (s, on) => s.QuickLookupHotkeyEnabled = on,
                QuickLookupHotkeyEnabledLabel),
            new HotkeyField(
                HotkeyAction.TranslationWindow,
                "S.Settings.WindowHotkey",
                WindowHotkeyBox,
                s => s.TranslationWindowHotkeyDisplay,
                s => HotkeyBindings.TriggerFor(s, HotkeyAction.TranslationWindow),
                ApplyWindowTrigger,
                AdvertisedInShell: false,
                WindowHotkeyEnabledCheckBox,
                (s, on) => s.TranslationWindowHotkeyEnabled = on,
                WindowHotkeyEnabledLabel),
            new HotkeyField(
                HotkeyAction.RealtimePause,
                "S.Settings.RealtimePauseHotkey",
                RealtimePauseHotkeyBox,
                s => s.RealtimePauseHotkeyDisplay,
                s => HotkeyBindings.TriggerFor(s, HotkeyAction.RealtimePause),
                ApplyRealtimePauseTrigger,
                AdvertisedInShell: false,
                RealtimePauseHotkeyEnabledCheckBox,
                (s, on) => s.RealtimePauseHotkeyEnabled = on,
                RealtimePauseHotkeyEnabledLabel),
        ];

        _hotkeyGamepadRecordTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
        _hotkeyGamepadRecordTimer.Tick += (_, _) => PollRecordingGamepad();

        _statusHold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        _statusHold.Tick += (_, _) => { _statusHold.Stop(); FadeStatusOut(); };

        // Paired with Loaded rather than subscribed once: the shell keeps one instance of this
        // page for its lifetime and swaps it in and out of the content host, so unsubscribing on
        // Unloaded without re-subscribing on Loaded would leave it deaf from the first time the
        // user navigated away. A static event holding an instance handler also has to be let go
        // of at some point, which rules out subscribing only once.
        Loaded   += (_, _) => LocalizationService.LanguageChanged += OnLanguageChanged;
        Unloaded += (_, _) =>
        {
            LocalizationService.LanguageChanged -= OnLanguageChanged;
            StopRecording();
        };

        ApplyDiagnosticUploadAvailability();
        LoadSettings();
    }

    /// <summary>
    /// Re-reads the stored settings, so a change made elsewhere — a key typed into the service
    /// panel, a shortcut rebound — is on screen when this page is next shown.
    /// </summary>
    public void Reload() => LoadSettings();

    // ── 分组显示 ──────────────────────────────────────────────────────────
    // 导航在外层 Shell 侧栏(常规/快捷键/翻译服务/朗读四项);本页按 ShellWindow 传入的组名
    // 切换可见分组,自己不再持有任何导航控件。

    /// <summary>The four groups the shell rail offers, as the rail names them.</summary>
    public static class Sections
    {
        public const string General  = "General";
        public const string Hotkeys  = "Hotkeys";
        public const string Services = "Services";
        public const string Tts      = "Tts";
    }

    private static readonly string[] SectionNames =
        [Sections.General, Sections.Hotkeys, Sections.Services, Sections.Tts];

    /// <summary>Shows one settings group and hides the rest. Unknown names show General.</summary>
    public void ShowSection(string section)
    {
        if (string.IsNullOrEmpty(section)) section = Sections.General;

        foreach (var name in SectionNames)
        {
            if (FindName("Section" + name) is System.Windows.Controls.Panel panel)
                panel.Visibility = name == section ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void LoadSettings()
    {
        _loading = true;
        try
        {
            var s = SettingsService.Instance.Current;

            foreach (var field in _hotkeyFields) field.Box.Text = field.Display(s);

            // The ticks, then the "a shortcut above took this" lines the ticks can change. The
            // availability pass is explicit because the toggle handler ignores changes made while
            // loading, so nothing else would grey these rows out on the way in.
            foreach (var binding in HotkeyBindings.Resolve(s))
                if (FieldFor(binding.Action)?.EnabledBox is { } box)
                    box.IsChecked = binding.Enabled;

            foreach (var field in _hotkeyFields) ApplyHotkeyRowAvailability(field);

            LightThemeRadio.IsChecked = s.Theme != ThemeService.Dark;
            DarkThemeRadio.IsChecked  = s.Theme == ThemeService.Dark;

            // LocalizationService.Current, not s.UiLanguage: an unset preference is showing the
            // system default right now, and the picker has to agree with what is on screen.
            UiLanguageBox.ItemsSource = LocalizationService.Options;
            UiLanguageBox.SelectedValue = LocalizationService.Current;
            if (UiLanguageBox.SelectedValue == null) UiLanguageBox.SelectedIndex = 0;

            // 首项“跟随系统”的文案来自字符串字典，语言切换后随 LoadSettings 重建；
            // 选过但已卸载的字体在列表里找不到，FontBox 显示回原名，重选即归位。
            UiFontBox.SetOptions(UiFontService.PickerOptions());
            UiFontBox.SetSelected(s.UiFontFamily);

            // Rebuilt rather than static: the labels come from the string dictionary, and this
            // method re-runs on every language swap.
            FontCalibrationBox.ItemsSource = new[]
            {
                new CalibrationOption(OverlayFontCalibration.Standard, "S.Settings.FontCalibrationStandard"),
                new CalibrationOption(OverlayFontCalibration.Compact, "S.Settings.FontCalibrationCompact"),
                new CalibrationOption(OverlayFontCalibration.Large,    "S.Settings.FontCalibrationLarge"),
            };
            FontCalibrationBox.SelectedValue = s.FontCalibration;
            if (FontCalibrationBox.SelectedValue == null) FontCalibrationBox.SelectedIndex = 0;

            LoadTtsControls(s);

            StartupCheckBox.IsChecked = StartupService.IsEnabled;

            AutoTranslateCheckBox.IsChecked = s.AutoTranslateAfterSelection;

            SaveScreenshotCheckBox.IsChecked = s.SaveScreenshotToDisk;
            ScreenshotPathBox.Text = ScreenshotSaveService.ResolveDirectory(s.ScreenshotSavePath);

            VerboseLoggingCheckBox.IsChecked = s.VerboseLogging;

            RefreshServiceTiles();
            UpdateScreenshotPathVisibility();
            UpdateVerboseLoggingAvailability();

            // 分组可见性由 ShellWindow.ShowPage 通过 ShowSection 决定,这里不再触碰。
        }
        finally
        {
            _loading = false;
        }
    }

    // ── Persistence ──────────────────────────────────────────────────────────

    private void Persist(Action<AppSettings> apply)
    {
        if (_loading) return;
        apply(SettingsService.Instance.Current);
        SettingsService.Instance.Save();
        FlashSaved();
    }

    private void FlashSaved() => FlashSuccess(LocalizationService.Get("S.Settings.Saved"));

    /// <summary>
    /// The same line the auto-save confirmation uses, for the handful of actions that finish with
    /// something to say other than 已儲存 — an export naming the file it wrote, so far.
    /// </summary>
    private void FlashSuccess(string message)
    {
        StatusText.Text       = message;
        StatusText.Foreground = (Brush)FindResource("AppSuccess");

        StatusText.BeginAnimation(OpacityProperty, null);
        StatusText.Opacity = 1;
        StatusBar.Visibility = Visibility.Visible;

        _statusHold.Stop();
        _statusHold.Start();
    }

    private void FadeStatusOut()
    {
        var fade = new DoubleAnimation
        {
            From = 1, To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(300))
        };
        fade.Completed += (_, _) => StatusBar.Visibility = Visibility.Collapsed;
        StatusText.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>
    /// The status line for something that has started and has not finished. Neither colour fits:
    /// green would be a claim, red an accusation, and the fade the success path uses would take the
    /// line away while the thing it describes is still running — so this one holds until whoever
    /// started it replaces it.
    /// </summary>
    private void ShowProgress(string message)
    {
        _statusHold.Stop();
        StatusText.BeginAnimation(OpacityProperty, null);
        StatusText.Text       = message;
        StatusText.Foreground = (Brush)FindResource("AppTextSecondary");
        StatusText.Opacity    = 1;
        StatusBar.Visibility   = Visibility.Visible;
    }

    /// <summary>
    /// For an outcome that is neither. Red would say the press failed when the file it produced is
    /// sitting on the disk and is the whole point; green would say it went through when it did not.
    /// Held rather than faded, for the same reason the error line is: there is something left to do.
    /// </summary>
    private void ShowWarning(string message)
    {
        _statusHold.Stop();
        StatusText.BeginAnimation(OpacityProperty, null);
        StatusText.Text       = message;
        StatusText.Foreground = (Brush)FindResource("AppWarning");
        StatusText.Opacity    = 1;
        StatusBar.Visibility   = Visibility.Visible;
    }

    private void ShowError(string message)
    {
        _statusHold.Stop();
        StatusText.BeginAnimation(OpacityProperty, null);
        StatusText.Text       = message;
        StatusText.Foreground = (Brush)FindResource("AppError");
        StatusText.Opacity    = 1;
        StatusBar.Visibility   = Visibility.Visible;
    }

    // ── Field handlers ───────────────────────────────────────────────────────

    // ── Translation services ─────────────────────────────────────────────────

    /// <summary>One icon action on a service card: a glyph, the tooltip key that names it, what a
    /// click does, and whether the card offers it at all.</summary>
    private sealed record CardAction(string Glyph, string TipKey, RoutedEventHandler Handler, bool Enabled = true);

    private void ConfigureBuiltIn(TranslationProvider provider)
    {
        // The panel belongs to the shell rather than to this page so that it covers the nav rail
        // too: a modal the user can navigate out from behind is not one.
        if (Window.GetWindow(this) is Shell.ShellWindow shell)
            shell.OpenServiceSettings(provider);
    }

    /// <summary>Makes one service the one the pickers name, and re-reads the page so every card
    /// answers the new state.</summary>
    private void UseService(string value)
    {
        ServiceSelection.ApplyValue(value);
        SettingsService.Instance.Save();
        LoadSettings();
    }

    // ── 卡片 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds the service cards: the two built-in slots, the preset vendors, the services that
    /// belong to none of them, and the standing “add one” entry.
    /// </summary>
    /// <remarks>
    /// Public because the shell calls it when a settings panel is dismissed: what was typed in
    /// there is exactly what these cards report. Rebuilt rather than templated because what a
    /// card holds — the status word, which of use/edit/test/delete exist — is decided per service
    /// at read time, and the cards are few.
    /// </remarks>
    public void RefreshServiceTiles()
    {
        var settings = SettingsService.Instance.Current;
        var active = ServiceSelection.CurrentValue();

        PresetCardHost.Children.Clear();
        CustomCardHost.Children.Clear();

        // 内置:DeepL 与 OpenAI 兼容(本地)。两者映射到 TranslationProvider 枚举。
        PresetCardHost.Children.Add(BuildDeepLCard(settings, active));
        PresetCardHost.Children.Add(BuildOpenAiCard(settings, active));

        // 预设厂商:按端点认领已添加的服务,认领的不再进自定义区。多个服务指向同一端点时,
        // 第一个上卡,其余的照旧在自定义区各占一张——删掉任何一个都不影响另一个。
        var claimed = new HashSet<CustomTranslatorService>();
        foreach (var template in CustomServiceTemplate.Presets)
        {
            if (template.Initial.Length == 0) continue; // 「空白」没有卡

            var service = settings.CustomServices.FirstOrDefault(
                c => CustomServiceTemplate.SameEndpoint(c.BaseUrl, template.BaseUrl));
            if (service is not null) claimed.Add(service);

            PresetCardHost.Children.Add(BuildPresetCard(template, service, active));
        }

        foreach (var service in settings.CustomServices)
            if (!claimed.Contains(service))
                CustomCardHost.Children.Add(BuildCustomCard(service, active));

        CustomCardHost.Children.Add(BuildAddCard());
    }

    private Border BuildDeepLCard(AppSettings s, string active)
    {
        // DeepL cannot translate a word without a key, so an empty one is something still to do.
        var configured = s.ApiKey.Trim().Length > 0;

        return BuildCard(
            MarkTile("DeepLMarkGeometry", "AppDeepLMark", 20),
            LocalizationService.Get("S.Provider.DeepL"),
            configured ? "S.Settings.ServiceReady" : "S.Settings.ServiceNeedsSetup",
            configured ? "AppAccent" : "AppWarning",
            isActive: active == nameof(TranslationProvider.DeepL),
            [
                new CardAction("\uE73E", "S.Custom.UseButton",
                    (_, _) => UseService(nameof(TranslationProvider.DeepL))),
                new CardAction("\uE713", "S.Common.Configure",
                    (_, _) => ConfigureBuiltIn(TranslationProvider.DeepL)),
            ]);
    }

    private Border BuildOpenAiCard(AppSettings s, string active)
    {
        // OpenAI has a working endpoint, model and prompt built in — it is aimed at a local model —
        // so nothing here is missing, only either left as shipped or changed.
        var touched =
            s.OpenAiBaseUrl.Trim().Length > 0 ||
            s.OpenAiApiKey.Trim().Length > 0 ||
            s.OpenAiModel.Trim().Length > 0;

        return BuildCard(
            MarkTile("OpenAiMarkGeometry", "AppOpenAiMark", 22),
            LocalizationService.Get("S.Provider.OpenAI"),
            touched ? "S.Settings.ServiceReady" : "S.Settings.ServiceDefault",
            "AppAccent",
            isActive: active == nameof(TranslationProvider.OpenAI),
            [
                new CardAction("\uE73E", "S.Custom.UseButton",
                    (_, _) => UseService(nameof(TranslationProvider.OpenAI))),
                new CardAction("\uE713", "S.Common.Configure",
                    (_, _) => ConfigureBuiltIn(TranslationProvider.OpenAI)),
            ]);
    }

    private Border BuildPresetCard(CustomServiceTemplate template, CustomTranslatorService? service, string active)
    {
        if (service is null)
        {
            // 未添加:设置即添加(表单按模板预填);测试亮着但不可按,提示先完成设置。
            return BuildCard(
                LetterTile(template.Initial, template.BrandColor, template.DarkLetter),
                template.Name,
                "S.Settings.ServiceNeedsSetup",
                "AppWarning",
                isActive: false,
                [
                    new CardAction("\uE713", "S.Common.Configure", (_, _) =>
                    {
                        if (Window.GetWindow(this) is Shell.ShellWindow shell)
                            shell.OpenCustomServiceAdd(template);
                    }),
                    new CardAction("\uE945", "S.Custom.TestNeedsSetup", (_, _) => { }, Enabled: false),
                ]);
        }

        return BuildCard(
            LetterTile(template.Initial, template.BrandColor, template.DarkLetter),
            template.Name,
            active == ServiceSelection.CustomPrefix + service.Id ? "S.Custom.Active" : null,
            "AppAccent",
            isActive: active == ServiceSelection.CustomPrefix + service.Id,
            ServiceActions(service, active));
    }

    private Border BuildCustomCard(CustomTranslatorService service, string active)
    {
        var name = service.Name.Trim();
        if (name.Length == 0) name = LocalizationService.Get("S.Services.CustomUntitled");

        return BuildCard(
            NeutralLetterTile(name),
            name,
            active == ServiceSelection.CustomPrefix + service.Id ? "S.Custom.Active" : null,
            "AppAccent",
            isActive: active == ServiceSelection.CustomPrefix + service.Id,
            ServiceActions(service, active));
    }

    /// <summary>The standing “add a custom service” entry: opens the sheet with the template
    /// strip, so uncommon vendors pick their way through it.</summary>
    private Border BuildAddCard()
    {
        return BuildCard(
            AddTile(),
            LocalizationService.Get("S.Custom.AddButton").TrimStart('+', ' '),
            statusKey: null,
            "AppAccent",
            isActive: false,
            [
                new CardAction("\uE710", "S.Custom.AddTitle", (_, _) =>
                {
                    if (Window.GetWindow(this) is Shell.ShellWindow shell)
                        shell.OpenCustomServiceAdd();
                }),
            ]);
    }

    /// <summary>The actions an existing service's card carries. 使用 drops off while the service
    /// already is the active one — a button that does nothing would be the only card control that
    /// lies about having something to do.</summary>
    private List<CardAction> ServiceActions(CustomTranslatorService service, string active)
    {
        var actions = new List<CardAction>();

        if (active != ServiceSelection.CustomPrefix + service.Id)
            actions.Add(new CardAction("\uE73E", "S.Custom.UseButton",
                (_, _) => UseService(ServiceSelection.CustomPrefix + service.Id)));

        actions.Add(new CardAction("\uE713", "S.Common.Configure", (_, _) =>
        {
            if (Window.GetWindow(this) is Shell.ShellWindow shell)
                shell.OpenCustomServiceEditor(service);
        }));

        actions.Add(new CardAction("\uE945", "S.Custom.Test",
            (sender, _) => TestCardService(sender, service)));

        actions.Add(new CardAction("\uE74D", "S.Custom.Delete",
            (_, _) => DeleteService(service)));

        return actions;
    }

    /// <summary>
    /// Assembles one card: the mark, the name, the status word, and the icon actions on the
    /// right. The active card carries the accent edge so the service in use is found by scan
    /// rather than by reading badges.
    /// </summary>
    /// <param name="statusKey">Null for cards whose status is carried by the active edge alone
    /// (preset and custom services: not-added needs no word, and added says nothing worth
    /// one).</param>
    private Border BuildCard(
        FrameworkElement mark,
        string title,
        string? statusKey,
        string statusBrushKey,
        bool isActive,
        IEnumerable<CardAction> actions)
    {
        var card = new Border { Style = (Style)FindResource("ServiceCardBorder") };
        if (isActive)
            card.SetResourceReference(Border.BorderBrushProperty, "AppAccent");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        mark.Margin = new Thickness(0, 0, 10, 0);
        mark.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(mark, 0);
        grid.Children.Add(mark);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "AppTextPrimary");
        titleBlock.SetResourceReference(TextBlock.FontFamilyProperty, "AppFont");
        text.Children.Add(titleBlock);

        if (statusKey is not null)
        {
            var status = new TextBlock
            {
                Text = LocalizationService.Get(statusKey),
                FontSize = 11,
                Margin = new Thickness(0, 3, 0, 0),
            };
            status.SetResourceReference(TextBlock.ForegroundProperty, statusBrushKey);
            status.SetResourceReference(TextBlock.FontFamilyProperty, "AppFont");
            text.Children.Add(status);
        }

        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var action in actions)
        {
            var button = new Button
            {
                Style = (Style)FindResource("ServiceActionButton"),
                Content = action.Glyph,
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 13,
                ToolTip = LocalizationService.Get(action.TipKey),
                Margin = new Thickness(2, 0, 2, 0),
                IsEnabled = action.Enabled,
            };
            button.Click += action.Handler;
            buttons.Children.Add(button);
        }
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);

        card.Child = grid;
        return card;
    }

    // ── 卡片左侧的品牌块 ───────────────────────────────────────────────────

    /// <summary>The 30px square every card leads with, on neutral ground: the app ships no vendor
    /// logos (trademarks, and unrecognisable at this size), so the two marks it does ship sit on
    /// the same tile the letters do.</summary>
    private static Border TileGround(FrameworkElement content)
    {
        var tile = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            Child = content,
        };
        tile.SetResourceReference(Border.BackgroundProperty, "AppCardBg");
        tile.SetResourceReference(Border.BorderBrushProperty, "AppSubtleBorder");
        return tile;
    }

    private Border MarkTile(string geometryKey, string fillKey, double width)
    {
        var mark = new Path
        {
            Data = (Geometry)FindResource(geometryKey),
            Stretch = Stretch.Uniform,
            Width = width,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        mark.SetResourceReference(Shape.FillProperty, fillKey);
        return TileGround(mark);
    }

    /// <summary>A vendor's initial on its brand colour. The colour is fixed rather than themed:
    /// it is the vendor's, not this app's, and the whole point is recognition.</summary>
    private static Border LetterTile(string letter, string brand, bool darkLetter)
    {
        var text = new TextBlock
        {
            Text = letter,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(darkLetter ? "#1F1F1F" : "#FFFFFF")),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var tile = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(brand)),
            Child = text,
        };
        return tile;
    }

    /// <summary>A custom service's initial, on the app's own colours: nothing about it is a
    /// vendor's.</summary>
    private Border NeutralLetterTile(string name)
    {
        var text = new TextBlock
        {
            Text = name[..1],
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "AppAccent");

        var tile = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(7),
            Child = text,
        };
        tile.SetResourceReference(Border.BackgroundProperty, "AppAccentSubtle");
        return tile;
    }

    private Border AddTile()
    {
        var text = new TextBlock
        {
            Text = "\uE710",
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "AppTextSecondary");
        return TileGround(text);
    }

    // ── 卡片动作 ───────────────────────────────────────────────────────────

    /// <summary>
    /// Deletes one custom service. The service that is active falls back to the built-in OpenAI
    /// slot — the same fall-back a hand-edited settings file gets.
    /// </summary>
    private void DeleteService(CustomTranslatorService service)
    {
        var settings = SettingsService.Instance.Current;
        settings.CustomServices.Remove(service);
        if (settings.ActiveCustomServiceId == service.Id)
        {
            settings.ActiveCustomServiceId = "";
            settings.Provider = TranslationProvider.OpenAI;
        }
        SettingsService.Instance.Save();
        LoadSettings();
    }

    /// <summary>
    /// Runs one real request with the service's stored configuration. The answer lands on the
    /// button itself — green or red until the next rebuild, the detail on hover — so the grid
    /// stays compact: no row of result lines under the cards.
    /// </summary>
    private async void TestCardService(object sender, CustomTranslatorService service)
    {
        if (sender is not Button button) return;

        button.IsEnabled = false;
        try
        {
            var (ok, detail) = await OpenAiCompatibleProvider.TestConnectionAsync(
                new OpenAiCompatibleOptions(
                    service.BaseUrl,
                    service.Model,
                    service.ApiKey,
                    service.PromptAuto,
                    service.PromptExplicit,
                    service.TemperatureEnabled,
                    service.Temperature,
                    service.TimeoutSeconds));

            button.SetResourceReference(Button.ForegroundProperty, ok ? "AppSuccess" : "AppError");
            button.ToolTip = detail;
        }
        catch (Exception ex)
        {
            button.SetResourceReference(Button.ForegroundProperty, "AppError");
            button.ToolTip = ex.Message;
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    // ── General ──────────────────────────────────────────────────────────────

    private void AutoTranslate_Toggled(object sender, RoutedEventArgs e)
        => Persist(s => s.AutoTranslateAfterSelection = AutoTranslateCheckBox.IsChecked == true);

    // Startup lives in the registry rather than the settings file, so it saves on its own path
    private void Startup_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        try
        {
            StartupService.Set(StartupCheckBox.IsChecked == true);
            FlashSaved();
        }
        catch (Exception ex)
        {
            ShowError(LocalizationService.Format("S.Settings.StartupFailed", ex.Message));
        }
    }

    // Takes effect on the next line written, not on the next launch — the level is applied before it
    // is stored, so a user who is mid-reproduction can tick the box and keep going.
    private void VerboseLogging_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        bool verbose = VerboseLoggingCheckBox.IsChecked == true;
        LogLevelService.Apply(verbose);
        Persist(s => s.VerboseLogging = verbose);
    }

    /// <summary>
    /// An environment variable outranks this setting, so when one is set the checkbox says so rather
    /// than accepting clicks that change nothing.
    /// </summary>
    private void UpdateVerboseLoggingAvailability()
    {
        if (!LogLevelService.IsOverriddenByEnvironment) return;

        VerboseLoggingCheckBox.IsEnabled = false;
        VerboseLoggingHint.Text = LocalizationService.Get("S.Settings.LoggingEnvOverride");
    }

    private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var theme = DarkThemeRadio.IsChecked == true ? ThemeService.Dark : ThemeService.Light;
        ThemeService.Apply(theme);
        Persist(s => s.Theme = theme);
    }

    private void UiLanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;

        if (UiLanguageBox.SelectedValue as string is not { } language) return;

        // Persist first: the swap re-runs LoadSettings through LanguageChanged, and both that and
        // LocalizationService.Current read the stored value back.
        Persist(s => s.UiLanguage = language);
        LocalizationService.Apply(language);

        // Persist already flashed the confirmation, but it did so in the outgoing language and
        // LoadSettings does not touch the status line. Say it again in the new one.
        FlashSaved();
    }

    private void UiFontBox_SelectionCommitted(object? sender, EventArgs e)
    {
        if (_loading) return;

        // 先存后套用：Apply 原位替换 AppFont/OverlayTextFont 两个资源键，
        // 界面与译文叠加即时换字体，无需重启。
        Persist(s => s.UiFontFamily = UiFontBox.Family);
        UiFontService.Apply(UiFontBox.Family);
    }

    private void FontCalibrationBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;

        // Applies to the next capture — overlay windows read it when they rebuild, so no restart.
        if (FontCalibrationBox.SelectedValue is OverlayFontCalibration calibration)
            Persist(s => s.FontCalibration = calibration);
    }

    /// <summary>
    /// One row of the calibration picker: the value stored in settings beside the resource key its
    /// label resolves through, rebuilt by <see cref="LoadSettings"/> on every language swap.
    /// </summary>
    private record CalibrationOption(OverlayFontCalibration Calibration, string Key)
    {
        public string Display => LocalizationService.Get(Key);
    }

    // ══════════════════════════ 朗讀設定 ══════════════════════════

    /// <summary>
    /// Fills every read-aloud control from the stored settings. Labelled lists are rebuilt rather
    /// than static because their text comes from the string dictionary, which a language swap
    /// replaces under them; the voice list additionally depends on what this machine has installed.
    /// </summary>
    private void LoadTtsControls(AppSettings s)
    {
        TtsEngineBox.ItemsSource = new[]
        {
            new EngineOption(Models.TtsEngine.Windows, "S.Settings.TtsEngineWindows"),
            new EngineOption(Models.TtsEngine.Online,  "S.Settings.TtsEngineOnline"),
        };
        TtsEngineBox.SelectedValue = s.TtsEngine;
        if (TtsEngineBox.SelectedValue == null) TtsEngineBox.SelectedIndex = 0;

        var voices = new List<VoiceOption>
        {
            new("", "S.Settings.TtsVoiceAuto"),
        };
        try
        {
            using var synth = new System.Speech.Synthesis.SpeechSynthesizer();
            foreach (var voice in synth.GetInstalledVoices().Where(v => v.Enabled))
                voices.Add(new VoiceOption(
                    voice.VoiceInfo.Id, $"{voice.VoiceInfo.Name} ({voice.VoiceInfo.Culture})"));
        }
        catch (Exception ex)
        {
            // Listing voices failing is not worth a settings page that will not open; the picker
            // keeps its automatic entry and speaking still works on the default voice.
            Log.Warn(ex, "Could not enumerate installed voices");
        }
        TtsVoiceBox.ItemsSource = voices;
        TtsVoiceBox.SelectedValue = voices.Any(v => v.Id == s.TtsVoiceId)
            ? s.TtsVoiceId
            : "";
        if (TtsVoiceBox.SelectedValue == null) TtsVoiceBox.SelectedIndex = 0;

        TtsRateSlider.Value = s.TtsRate;
        TtsVolumeSlider.Value = s.TtsVolume;

        QuickLookupAutoSpeakBox.ItemsSource = AutoSpeakOptions();
        QuickLookupAutoSpeakBox.SelectedValue = s.QuickLookupAutoSpeak;
        if (QuickLookupAutoSpeakBox.SelectedValue == null) QuickLookupAutoSpeakBox.SelectedIndex = 0;

        CaptureAutoSpeakBox.ItemsSource = AutoSpeakOptions();
        CaptureAutoSpeakBox.SelectedValue = s.CaptureAutoSpeak;
        if (CaptureAutoSpeakBox.SelectedValue == null) CaptureAutoSpeakBox.SelectedIndex = 0;
    }

    private static AutoSpeakOption[] AutoSpeakOptions() =>
    [
        new(AutoSpeakMode.Off,    "S.Settings.AutoSpeakOff"),
        new(AutoSpeakMode.Source, "S.Settings.AutoSpeakSource"),
        new(AutoSpeakMode.Target, "S.Settings.AutoSpeakTarget"),
        new(AutoSpeakMode.Both,   "S.Settings.AutoSpeakBoth"),
    ];

    private void TtsEngineBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (TtsEngineBox.SelectedValue is Models.TtsEngine engine)
            Persist(s => s.TtsEngine = engine);
    }

    private void TtsVoiceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (TtsVoiceBox.SelectedValue is string voiceId)
            Persist(s => s.TtsVoiceId = voiceId);
    }

    private void TtsRateSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        TtsRateLabel.Text = ((int)e.NewValue).ToString("+0;-0;0");
        if (_loading) return;
        Persist(s => s.TtsRate = (int)e.NewValue);
    }

    private void TtsVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        TtsVolumeLabel.Text = ((int)e.NewValue).ToString();
        if (_loading) return;
        Persist(s => s.TtsVolume = (int)e.NewValue);
    }

    private void QuickLookupAutoSpeakBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (QuickLookupAutoSpeakBox.SelectedValue is AutoSpeakMode mode)
            Persist(s => s.QuickLookupAutoSpeak = mode);
    }

    private void CaptureAutoSpeakBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (CaptureAutoSpeakBox.SelectedValue is AutoSpeakMode mode)
            Persist(s => s.CaptureAutoSpeak = mode);
    }

    private record EngineOption(Models.TtsEngine Engine, string Key)
    {
        public string Display => LocalizationService.Get(Key);
    }

    private record VoiceOption(string Id, string Display);

    private record AutoSpeakOption(AutoSpeakMode Mode, string Key)
    {
        public string Display => LocalizationService.Get(Key);
    }

    /// <summary>
    /// Re-renders the text on this page that DynamicResource cannot reach.
    /// </summary>
    /// <remarks>
    /// Four things on this page are composed in code and so hold a string from the language that
    /// was in effect when they were built: the provider list and its hint, the pickers' language
    /// labels, the service tiles' state words, and the environment-override notice. LoadSettings
    /// rebuilds all of them, and is already guarded against writing back.
    /// </remarks>
    private void OnLanguageChanged(object? sender, EventArgs e) => LoadSettings();

    private void SaveScreenshotCheckBox_Toggled(object sender, RoutedEventArgs e)
    {
        UpdateScreenshotPathVisibility();
        Persist(s => s.SaveScreenshotToDisk = SaveScreenshotCheckBox.IsChecked == true);
    }

    private void UpdateScreenshotPathVisibility()
    {
        ScreenshotPathRow.Visibility = SaveScreenshotCheckBox.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ScreenshotPathBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // The box is read-only and acts as a button: clicking anywhere in it opens the folder picker.
        e.Handled = true;

        var dialog = new OpenFolderDialog
        {
            Title = LocalizationService.Get("S.Settings.FolderPickerTitle"),
            InitialDirectory = ScreenshotPathBox.Text
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        ScreenshotPathBox.Text = dialog.FolderName;
        // Store "" when the folder matches the default, so the setting follows the system
        // Pictures folder instead of freezing today's expanded path.
        Persist(s => s.ScreenshotSavePath = string.Equals(
            dialog.FolderName, ScreenshotSaveService.DefaultDirectory, StringComparison.OrdinalIgnoreCase)
            ? ""
            : dialog.FolderName);
    }

    private void OpenScreenshotFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ScreenshotSaveService.OpenFolder(ScreenshotPathBox.Text);
        }
        catch (Exception ex)
        {
            ShowError(LocalizationService.Format("S.Settings.OpenFolderFailed", ex.Message));
        }
    }

    /// <remarks>
    /// The diagnostics folder rather than the log folder, because the export is what a person coming
    /// off this card is looking for — the raw logs are inside the zip anyway, and this is where the
    /// zips accumulate. That is also why the button no longer says "log folder": a button that opens
    /// one folder while naming another is worse than either name on its own.
    /// </remarks>
    private void OpenDiagnosticsFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            DiagnosticBundleService.OpenExportFolder();
        }
        catch (Exception ex)
        {
            ShowError(LocalizationService.Format("S.Settings.OpenFolderFailed", ex.Message));
        }
    }

    /// <summary>
    /// Collects the bundle and, where an endpoint is compiled in, sends it and shows the code that
    /// comes back.
    /// </summary>
    /// <remarks>
    /// One button for both halves, because the two halves are one intention: nobody collects a
    /// diagnostic bundle for its own sake. What that costs is the chance to open the zip before it
    /// goes — paid for by the explanation on the heading, by a label that says it uploads, and by
    /// the row afterwards that opens what was sent.
    ///
    /// The upload is nested inside its own try on purpose. A failure there is not a failure of the
    /// press: the bundle is already written, and every one of those paths ends by opening Explorer
    /// on it — which is the whole of #126, still there for the offline machine, the blocked network
    /// and the person who would simply rather attach it themselves.
    ///
    /// Off the UI thread because the bundle copies and compresses every log file there is, which on
    /// a machine that has filled its five archives is a dozen megabytes — not long, but long enough
    /// to freeze the window on a slow disk, and freezing while collecting a bug report is its own
    /// bug report. The button is disabled meanwhile so the same zip cannot be started twice.
    /// </remarks>
    private async void ExportDiagnosticsBtn_Click(object sender, RoutedEventArgs e)
    {
        ExportDiagnosticsBtn.IsEnabled = false;

        // A code from an earlier press describes an earlier upload. Leaving it on screen through
        // the next one invites it to be copied as though it were the new one.
        DiagnosticsResultPanel.Visibility = Visibility.Collapsed;

        var uploading = DiagnosticUploadService.IsConfigured;
        try
        {
            if (uploading) ShowProgress(LocalizationService.Get("S.Settings.DiagnosticsUploading"));

            var path = await Task.Run(() => DiagnosticBundleService.Export());
            _lastBundlePath = path;

            if (!uploading)
            {
                FlashSuccess(LocalizationService.Get("S.Settings.DiagnosticsExported"));

                // The real confirmation: the status line fades after a moment and cannot show a path
                // worth reading anyway, whereas Explorer opens with the file already selected and
                // ready to be dragged into a forum post — which is the entire point of the feature.
                DiagnosticBundleService.Reveal(path);
                return;
            }

            try
            {
                var code = await DiagnosticUploadService.UploadAsync(path);

                ShowUploaded(code);
                FlashSuccess(LocalizationService.Format("S.Settings.DiagnosticsUploaded", code));
            }
            catch (DiagnosticUploadException)
            {
                // No Explorer window here, unlike the path with no endpoint at all. The panel that
                // appears says to press Open file, and a window opening by itself at the same moment
                // is a second instruction contradicting the first.
                ShowNotUploaded();
                ShowWarning(LocalizationService.Get("S.Settings.DiagnosticsUploadFailed"));
            }
        }
        catch (Exception ex)
        {
            // Only the collection can land here now, and if that failed there is no file to fall
            // back to — which is why this one still names the error.
            ShowError(LocalizationService.Format("S.Settings.DiagnosticsFailed", ex.Message));
        }
        finally
        {
            ExportDiagnosticsBtn.IsEnabled = true;
        }
    }

    /// <summary>The panel as it looks when a bundle went up and came back with a code.</summary>
    private void ShowUploaded(string code)
    {
        DiagnosticsResultGlyph.Text = CompletedGlyph;
        DiagnosticsResultTitle.SetResourceReference(
            TextBlock.TextProperty, "S.Settings.DiagnosticsCodeLabel");
        DiagnosticsResultHint.SetResourceReference(
            TextBlock.TextProperty, "S.Settings.DiagnosticsUploadedHint");

        DiagnosticsCodeText.Text = code;
        DiagnosticsCodeText.Visibility = Visibility.Visible;
        CopyDiagnosticsCodeBtn.Visibility = Visibility.Visible;

        DiagnosticsResultPanel.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// The panel as it looks when the bundle was written but did not leave the machine.
    /// </summary>
    /// <remarks>
    /// The same panel rather than a different one, because the thing it is built around — Open file,
    /// pointing at the zip that exists either way — is what the user needs in both cases. What goes
    /// away is everything that would be a lie here: there is no code to copy, and nothing was
    /// uploaded for the thirty-day line to be about.
    ///
    /// One wording for every reason an upload can fail. Whether it was the network, the size or a
    /// refusal, the next move is the same: report it by hand and attach the file. Telling the four
    /// apart would offer a distinction the user cannot act on.
    ///
    /// The thirty-day line stays, unlike everything else that goes: it is about the copy on disk,
    /// which is kept for the same thirty days either way.
    /// </remarks>
    private void ShowNotUploaded()
    {
        DiagnosticsResultGlyph.Text = FailedGlyph;
        DiagnosticsResultTitle.SetResourceReference(
            TextBlock.TextProperty, "S.Settings.DiagnosticsUploadFailedTitle");
        DiagnosticsResultHint.SetResourceReference(
            TextBlock.TextProperty, "S.Settings.DiagnosticsNotUploadedHint");

        DiagnosticsCodeText.Visibility = Visibility.Collapsed;
        CopyDiagnosticsCodeBtn.Visibility = Visibility.Collapsed;

        DiagnosticsResultPanel.Visibility = Visibility.Visible;
    }

    /// <summary>Segoe MDL2 Completed, the tick in a circle.</summary>
    private const string CompletedGlyph = "\uE930";

    /// <summary>Segoe MDL2 Important, the exclamation mark in a circle.</summary>
    private const string FailedGlyph = "\uE7BA";

    /// <summary>
    /// Points the export button and its explanation at whichever of the two stories is true for this
    /// build: with an endpoint compiled in, one press collects and sends; without one, it collects
    /// and nothing leaves the machine.
    /// </summary>
    /// <remarks>
    /// Resource references rather than assignments, so both survive a language change — the page
    /// rebuilds itself on one, and a plain Text assignment would come back in the old language.
    ///
    /// A build with no endpoint is a supported state, not a broken one: it is what every build was
    /// until the worker was deployed, and what a user gets by pointing OVERTRANSLATE_DIAG_ENDPOINT
    /// at something that is not an address.
    /// </remarks>
    private void ApplyDiagnosticUploadAvailability()
    {
        var configured = DiagnosticUploadService.IsConfigured;

        ExportDiagnosticsLabel.SetResourceReference(
            TextBlock.TextProperty,
            configured ? "S.Settings.UploadDiagnostics" : "S.Settings.ExportDiagnostics");

        DiagnosticsHintText.SetResourceReference(
            TextBlock.TextProperty,
            configured ? "S.Settings.DiagnosticsUploadHint" : "S.Settings.DiagnosticsHint");
    }

    /// <remarks>
    /// The clipboard is a shared, lockable resource, and the one thing that must not happen is the
    /// user walking away believing they have the code. On failure the code stays on screen and the
    /// line says to read it from there.
    /// </remarks>
    private void CopyDiagnosticsCodeBtn_Click(object sender, RoutedEventArgs e)
    {
        var code = DiagnosticsCodeText.Text;
        if (string.IsNullOrEmpty(code)) return;

        try
        {
            Clipboard.SetText(code);
            FlashSuccess(LocalizationService.Get("S.Settings.DiagnosticsCodeCopied"));
        }
        catch (Exception)
        {
            ShowError(LocalizationService.Get("S.Settings.CopyFailed"));
        }
    }

    /// <remarks>
    /// Opens the zip rather than selecting it, because the question this button answers is "what did
    /// I just send" — and Explorer shows the three files inside a zip it opens.
    /// </remarks>
    private void OpenDiagnosticsBundleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_lastBundlePath is null) return;

        try
        {
            DiagnosticBundleService.Open(_lastBundlePath);
        }
        catch (Exception ex)
        {
            ShowError(LocalizationService.Format("S.Settings.OpenFolderFailed", ex.Message));
        }
    }

    // ── Hotkey recording ─────────────────────────────────────────────────────

    /// <summary>The field this box edits, or null for anything else.</summary>
    private HotkeyField? FieldOf(object sender) =>
        _hotkeyFields.FirstOrDefault(field => ReferenceEquals(field.Box, sender));

    /// <summary>The row that edits one action.</summary>
    private HotkeyField? FieldFor(HotkeyAction action) =>
        _hotkeyFields.FirstOrDefault(field => field.Action == action);

    /// <summary>
    /// Greys out the box and the record button of a shortcut that is switched off.
    /// </summary>
    /// <remarks>
    /// The tick is the whole row's switch, so leaving the rest of the row live invites the user to
    /// record a combination for a shortcut that will not be registered — an edit that appears to work
    /// and changes nothing.
    ///
    /// Only the tick does this. A row shadowed by a higher-priority shortcut stays editable on
    /// purpose: re-recording it onto a free combination is exactly how that is fixed, and disabling
    /// the row would take the fix away along with the problem.
    /// </remarks>
    private static void ApplyHotkeyRowAvailability(HotkeyField field)
    {
        // No tick means the row cannot be switched off at all — the capture shortcut.
        var on = field.EnabledBox is not { } box || box.IsChecked == true;

        field.Box.IsEnabled = on;

        // Written here rather than beside the switch, because this is the one place both the load
        // and the toggle already go through — and a switch whose word disagreed with it would be
        // worse than no word at all.
        if (field.EnabledLabel is { } label)
        {
            // The third state: the tick says on and Windows disagreed. Registration failed — the
            // combination belongs to another program — and the row has to say so, because from the
            // keyboard the shortcut is simply dead.
            var conflict = on && MainWindow.HotkeyRegistrationFailures.Contains(field.Action);
            label.Text = LocalizationService.Get(
                !on          ? "S.Settings.HotkeyOff"
                : conflict   ? "S.Settings.HotkeyConflict"
                :              "S.Settings.HotkeyOn");
        }
    }

    private void HotkeyEnabled_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        var field = _hotkeyFields.FirstOrDefault(f => ReferenceEquals(f.EnabledBox, sender));
        if (field?.SetEnabled is not { } setEnabled || field.EnabledBox is not { } box) return;

        // Switching a row off while it is waiting for a key would leave it recording into a control
        // about to be disabled, and the tick would look like it had done nothing.
        if (ReferenceEquals(_recording, field)) StopRecording();

        Persist(s => setEnabled(s, box.IsChecked == true));
        ApplyHotkeyRowAvailability(field);

        // The global hooks are bound from these settings, so the tick means nothing until they are
        // rebound — without this the shortcut keeps working until the application is restarted, and
        // a switch that takes effect later is worse than no switch.
        if (System.Windows.Application.Current.MainWindow is MainWindow main)
            main.ReRegisterHotkey();

        // After the re-registration, so a combination that just collided is the one this row
        // reports. Before it, the label would still be speaking for the previous trigger.
        ApplyHotkeyRowAvailability(field);

        // A row the rail advertises drops its combination when it is switched off, so the rail is
        // as wrong after a tick as it is after a re-record.
        if (field.AdvertisedInShell && Window.GetWindow(this) is Shell.ShellWindow shell)
            shell.RefreshHotkeyHints();
    }

    private static void ApplyCaptureTrigger(AppSettings s, ShortcutTrigger trigger, string display)
    {
        s.HotkeyInputKind = trigger.Kind;
        s.HotkeyDisplay = display;
        if (trigger.Kind == ShortcutInputKind.Keyboard)
        {
            s.HotkeyModifiers = trigger.Modifiers;
            s.HotkeyVirtualKey = trigger.VirtualKey;
            s.HotkeyGamepadButton = GamepadShortcutButton.None;
        }
        else if (trigger.Kind == ShortcutInputKind.Gamepad)
        {
            s.HotkeyGamepadButton = trigger.GamepadButton;
        }
    }

    private static void ApplyWindowTrigger(AppSettings s, ShortcutTrigger trigger, string display)
    {
        s.TranslationWindowHotkeyInputKind = trigger.Kind;
        s.TranslationWindowHotkeyDisplay = display;
        if (trigger.Kind == ShortcutInputKind.Keyboard)
        {
            s.TranslationWindowHotkeyModifiers = trigger.Modifiers;
            s.TranslationWindowHotkeyVirtualKey = trigger.VirtualKey;
            s.TranslationWindowHotkeyGamepadButton = GamepadShortcutButton.None;
        }
        else if (trigger.Kind == ShortcutInputKind.Gamepad)
        {
            s.TranslationWindowHotkeyGamepadButton = trigger.GamepadButton;
        }
    }

    private static void ApplyRealtimePauseTrigger(AppSettings s, ShortcutTrigger trigger, string display)
    {
        s.RealtimePauseHotkeyInputKind = trigger.Kind;
        s.RealtimePauseHotkeyDisplay = display;
        if (trigger.Kind == ShortcutInputKind.Keyboard)
        {
            s.RealtimePauseHotkeyModifiers = trigger.Modifiers;
            s.RealtimePauseHotkeyVirtualKey = trigger.VirtualKey;
            s.RealtimePauseHotkeyGamepadButton = GamepadShortcutButton.None;
        }
        else if (trigger.Kind == ShortcutInputKind.Gamepad)
        {
            s.RealtimePauseHotkeyGamepadButton = trigger.GamepadButton;
        }
    }

    private static void ApplyQuickLookupTrigger(AppSettings s, ShortcutTrigger trigger, string display)
    {
        s.QuickLookupHotkeyInputKind = trigger.Kind;
        s.QuickLookupHotkeyDisplay = display;
        if (trigger.Kind == ShortcutInputKind.Keyboard)
        {
            s.QuickLookupHotkeyModifiers = trigger.Modifiers;
            s.QuickLookupHotkeyVirtualKey = trigger.VirtualKey;
            s.QuickLookupHotkeyGamepadButton = GamepadShortcutButton.None;
        }
        else if (trigger.Kind == ShortcutInputKind.Gamepad)
        {
            s.QuickLookupHotkeyGamepadButton = trigger.GamepadButton;
        }
    }

    private void StartRecording(HotkeyField field)
    {
        // Only one at a time: two boxes both asking to be pressed would leave the next key press
        // ambiguous to the user long before it was ambiguous to the code.
        StopRecording();

        _recording = field;
        field.Box.Text = LocalizationService.Get("S.Settings.HotkeyPromptAnyInput");
        field.Box.Focus();

        for (int i = 0; i < _recordGamepadButtons.Length; i++)
            _recordGamepadButtons[i] = GamepadInput.TryGetButtons(i, out var buttons) ? buttons : (ushort)0;
        _hotkeyGamepadRecordTimer.Start();
    }

    /// <summary>Ends recording and puts the stored trigger back in the box.</summary>
    /// <remarks>
    /// Reads the setting rather than remembering what was there, so this also serves as the way
    /// back after a successful capture: the new value has been persisted by then, and restoring
    /// from the settings shows it.
    /// </remarks>
    private void StopRecording()
    {
        _hotkeyGamepadRecordTimer.Stop();
        if (_recording is not { } field) return;

        field.Box.Text = field.Display(SettingsService.Instance.Current);
        _recording = null;

        // The box asks to be pressed by being focused, so it has to stop being focused once it has
        // been: a box left carrying the focus ring after the shortcut is taken reads as still
        // waiting for one. Only when the focus is still here — the path in from LostFocus runs
        // after the user has already put it somewhere else, and clearing then would take it back.
        if (field.Box.IsKeyboardFocused) Keyboard.ClearFocus();
    }

    /// <summary>
    /// Every click on a focused box toggles recording — start, then stop, then start again.
    /// </summary>
    /// <remarks>
    /// GotFocus can only answer the first click, because the box stays focused after it. Without
    /// this the box would record once and then ignore every further click until focus had left and
    /// come back, which is the state a user lands in the moment they change their mind.
    ///
    /// The focusing click is left alone so it is not handled twice: GotFocus starts the recording
    /// that click asked for.
    /// </remarks>
    private void HotkeyBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FieldOf(sender) is not { } field || !field.Box.IsFocused) return;

        e.Handled = true;
        if (ReferenceEquals(_recording, field)) StopRecording();
        else StartRecording(field);
    }

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (FieldOf(sender) is { } field && !ReferenceEquals(_recording, field))
            StartRecording(field);
    }

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (FieldOf(sender) is not { } field || !ReferenceEquals(_recording, field)) return;

        // Keep recording while focus moves inside this settings page. This is what lets the user
        // press a mouse button anywhere on the page after starting to record instead of having to
        // aim at the read-only box itself.
        if (Keyboard.FocusedElement is DependencyObject focused && IsDescendantOfThisPage(focused)) return;

        StopRecording();
    }

    private bool IsDescendantOfThisPage(DependencyObject child)
    {
        for (DependencyObject? current = child; current is not null; current = LogicalTreeHelper.GetParent(current))
            if (ReferenceEquals(current, this)) return true;
        return false;
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_recording is not { } recording || !ReferenceEquals(recording.Box, sender)) return;
        e.Handled = true;

        bool isSystemKey = e.Key == Key.System;
        var key = isSystemKey ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            StopRecording();
            return;
        }

        if (key == Key.LeftCtrl || key == Key.RightCtrl ||
            key == Key.LeftAlt  || key == Key.RightAlt  ||
            key == Key.LeftShift || key == Key.RightShift ||
            key == Key.LWin || key == Key.RWin)
            return;

        uint mods = 0;
        if (Keyboard.IsKeyDown(Key.LeftCtrl)  || Keyboard.IsKeyDown(Key.RightCtrl))  mods |= GlobalHotkey.MOD_CONTROL;
        if (isSystemKey || Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt)) mods |= GlobalHotkey.MOD_ALT;
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) mods |= GlobalHotkey.MOD_SHIFT;

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return;

        var trigger = ShortcutTrigger.Keyboard(mods, vk);

        // A bare key is not merely watched, it is taken from every other application — so only the
        // keys that can afford to be taken are offered. See HotkeyBindings.IsBindable.
        if (!HotkeyBindings.IsBindable(trigger))
        {
            ShowError(LocalizationService.Get("S.Settings.HotkeyNeedsModifier"));
            StopRecording();
            return;
        }

        var prefix = GlobalHotkey.ModifiersToString(mods);
        var display = string.IsNullOrEmpty(prefix) ? key.ToString() : $"{prefix}+{key}";
        CommitShortcut(recording, trigger, display);
    }

    /// <remarks>
    /// The left and right buttons are not offered, and cannot be: left is how the box is clicked to
    /// start recording in the first place, and a shortcut on either would fire on every click the
    /// user makes anywhere. That leaves middle and the two side buttons — matching what
    /// <see cref="GlobalAuxiliaryHotkeys"/> watches for.
    ///
    /// Handled is set so the press is not also acted on as a press: XButton1 and XButton2 are the
    /// browser's Back and Forward, which WPF routes to navigation.
    /// </remarks>
    private void SettingsPage_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_recording is not { } recording) return;

        var kind = e.ChangedButton switch
        {
            MouseButton.Middle => ShortcutInputKind.MouseMiddle,
            MouseButton.XButton1 => ShortcutInputKind.MouseX1,
            MouseButton.XButton2 => ShortcutInputKind.MouseX2,
            _ => ShortcutInputKind.Keyboard,
        };

        if (kind == ShortcutInputKind.Keyboard) return;

        e.Handled = true;
        CommitShortcut(
            recording,
            ShortcutTrigger.Mouse(kind),
            LocalizationService.Get(MouseButtonNameKey(kind)));
    }

    /// <summary>The string naming one mouse button in the shortcut box.</summary>
    private static string MouseButtonNameKey(ShortcutInputKind kind) => kind switch
    {
        ShortcutInputKind.MouseX1 => "S.Settings.MouseX1",
        ShortcutInputKind.MouseX2 => "S.Settings.MouseX2",
        _ => "S.Settings.MouseMiddle",
    };

    private void PollRecordingGamepad()
    {
        if (_recording is not { } recording)
        {
            _hotkeyGamepadRecordTimer.Stop();
            return;
        }

        for (int i = 0; i < _recordGamepadButtons.Length; i++)
        {
            if (!GamepadInput.TryGetButtons(i, out var current))
            {
                _recordGamepadButtons[i] = 0;
                continue;
            }

            ushort pressed = (ushort)(current & ~_recordGamepadButtons[i]);
            _recordGamepadButtons[i] = current;
            var button = GamepadInput.FirstButton(pressed);
            if (button == GamepadShortcutButton.None) continue;

            var display = LocalizationService.Format(
                "S.Settings.GamepadButton",
                GamepadInput.ButtonName(button));
            CommitShortcut(recording, ShortcutTrigger.Gamepad(button), display);
            return;
        }
    }

    /// <remarks>
    /// Windows keys a registration by window and combination, so the second shortcut to claim one is
    /// simply refused — RegisterHotKey returns false and nothing else happens. Left to itself that
    /// reads as a shortcut that stopped working for no reason, so the clash is refused here, where
    /// there is something to say about it. The mouse and controller buttons are not registered with
    /// Windows at all, but they go through the same refusal so that one button cannot silently do two
    /// different things.
    ///
    /// A shortcut that is switched off does not hold its trigger — same rule as
    /// <see cref="HotkeyBindings"/>, so what the page refuses and what actually gets registered
    /// agree.
    /// </remarks>
    private void CommitShortcut(HotkeyField recording, ShortcutTrigger trigger, string display)
    {
        var settings = SettingsService.Instance.Current;
        var enabled = HotkeyBindings.Resolve(settings)
            .Where(binding => binding.Enabled)
            .Select(binding => binding.Action)
            .ToHashSet();

        var taken = _hotkeyFields.FirstOrDefault(
            field => !ReferenceEquals(field, recording) &&
                     enabled.Contains(field.Action) &&
                     field.Trigger(settings) == trigger);

        if (taken is not null)
        {
            ShowError(LocalizationService.Format(
                "S.Settings.HotkeyTaken", display, LocalizationService.Get(taken.NameKey)));
            StopRecording();
            return;
        }

        Persist(s => recording.Apply(s, trigger, display));

        // After the write, so the box picks the new trigger up out of the settings.
        StopRecording();

        // The global hook holds the old trigger until it is rebound.
        if (System.Windows.Application.Current.MainWindow is MainWindow main)
            main.ReRegisterHotkey();

        // After the re-registration, for the same reason as the toggle path above: the conflict
        // state is only accurate once the new combination has actually been tried.
        ApplyHotkeyRowAvailability(recording);

        // The nav rail advertises these beside 截圖翻譯 and 取詞翻譯 and is on screen right now, so it
        // has to be told; nothing else re-reads them until the shell is next shown or activated. The
        // other shortcuts are advertised nowhere, so there is nothing to refresh.
        if (recording.AdvertisedInShell && Window.GetWindow(this) is Shell.ShellWindow shell)
            shell.RefreshHotkeyHints();
    }
}
