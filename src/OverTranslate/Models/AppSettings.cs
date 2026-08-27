using System.Text.Json.Serialization;

namespace OverTranslate.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TranslationProvider { Google, Google2, Bing, Microsoft, DeepL, OpenAI }

/// <summary>
/// A user's nudge on top of the automatic OCR-height-to-font-size curve: the overlay already sizes
/// translated text from the height of the original, and this says "a little smaller / as-is / a
/// little larger than that" for readers whose eyes disagree with the default.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OverlayFontCalibration
{
    /// <summary>The automatic curve unchanged. The default, and what every capture used before
    /// the setting existed.</summary>
    Standard,

    /// <summary>Smaller than the automatic result — dense screens, crowded game UIs.</summary>
    Compact,

    /// <summary>Larger than the automatic result.</summary>
    Large,
}

/// <summary>The multiplier each calibration applies, kept beside the enum it belongs to.</summary>
public static class OverlayFontCalibrationExtensions
{
    public static double FontScale(this OverlayFontCalibration calibration) => calibration switch
    {
        OverlayFontCalibration.Compact => 0.85,
        OverlayFontCalibration.Large   => 1.15,
        _                              => 1.0,
    };
}

/// <summary>
/// What reads text aloud. Windows voices are the default: they are local, they need no key and no
/// network, and the online chain (Google/Bing/…) stays as the fallback and as the explicit choice
/// for a voice the machine does not have.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TtsEngine
{
    /// <summary>The Windows system voices, whatever this machine has installed.</summary>
    Windows,

    /// <summary>The online chain the app has always used: Google, Microsoft, Bing, Yandex.</summary>
    Online,
}

/// <summary>
/// What a surface does with its result once a translation lands: say nothing (the behaviour every
/// release before this setting had), or read one half or both halves of it aloud.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AutoSpeakMode
{
    Off,
    Source,
    Target,
    Both,
}

public class AppSettings
{
    // Ctrl+Alt+D: the capture shortcut shares its Ctrl+Alt neighbourhood with the quick-lookup
    // one below, and took D when 划詞 took over the A that capture used to own.
    public uint HotkeyModifiers { get; set; } = 3;
    public uint HotkeyVirtualKey { get; set; } = 0x44;
    public string HotkeyDisplay { get; set; } = "Ctrl+Alt+D";
    public ShortcutInputKind HotkeyInputKind { get; set; } = ShortcutInputKind.Keyboard;
    public GamepadShortcutButton HotkeyGamepadButton { get; set; } = GamepadShortcutButton.None;

    /// <summary>
    /// The shortcut that opens the translation window. Ctrl+Alt+W by default.
    /// </summary>
    /// <remarks>
    /// A convenience, not a headline: unlike the capture shortcut above it is not announced at
    /// startup and nothing in the interface advertises it, because the window it opens is already
    /// one click away in the tray. It opens and only opens — pressing it again brings the window
    /// forward rather than closing it, which is what every other way into this window does.
    /// </remarks>
    public uint TranslationWindowHotkeyModifiers { get; set; } = 3;

    public uint TranslationWindowHotkeyVirtualKey { get; set; } = 0x57;

    public string TranslationWindowHotkeyDisplay { get; set; } = "Ctrl+Alt+W";
    public ShortcutInputKind TranslationWindowHotkeyInputKind { get; set; } = ShortcutInputKind.Keyboard;
    public GamepadShortcutButton TranslationWindowHotkeyGamepadButton { get; set; } = GamepadShortcutButton.None;

    /// <summary>
    /// Whether the translation-window shortcut is registered at all. Off by default: the window is
    /// one tray click away, and a fourth global combination by default is one more thing to collide
    /// with something else the machine already claims.
    /// </summary>
    /// <remarks>
    /// There is no matching field for the capture shortcut, and deliberately not: that one is the
    /// feature the application exists for, so its checkbox is ticked and disabled rather than backed
    /// by a value. A stored flag that must always be true is a way to end up with it false.
    /// </remarks>
    public bool TranslationWindowHotkeyEnabled { get; set; } = false;

    /// <summary>
    /// Pauses and resumes a running realtime session. Ctrl+Alt+S by default.
    /// </summary>
    /// <remarks>
    /// Stored as three fields — the modifiers and key Windows is given, plus the text the settings
    /// page shows — because the display string cannot be derived from the other two without a
    /// key-name table, and the recorder already has the user's own spelling of it at the moment they
    /// press the combination.
    ///
    /// Ctrl+Alt+S was the block-framing shortcut's default until that shortcut was removed: a session
    /// now begins by naming what it reads, which is a live window handle chosen from what is open,
    /// and no settings file can answer that. The key it left behind goes to the one realtime shortcut
    /// there still is. Anyone who had already recorded their own combination keeps it — a default
    /// only fills in what nobody has answered.
    /// </remarks>
    public uint RealtimePauseHotkeyModifiers { get; set; } = 3;

    /// <inheritdoc cref="RealtimePauseHotkeyModifiers"/>
    public uint RealtimePauseHotkeyVirtualKey { get; set; } = 0x53;

    /// <inheritdoc cref="RealtimePauseHotkeyModifiers"/>
    public string RealtimePauseHotkeyDisplay { get; set; } = "Ctrl+Alt+S";
    public ShortcutInputKind RealtimePauseHotkeyInputKind { get; set; } = ShortcutInputKind.Keyboard;
    public GamepadShortcutButton RealtimePauseHotkeyGamepadButton { get; set; } = GamepadShortcutButton.None;

    /// <inheritdoc cref="TranslationWindowHotkeyEnabled"/>
    public bool RealtimePauseHotkeyEnabled { get; set; } = true;

    /// <summary>
    /// Summons 取詞翻譯's popup over whatever the user is reading. Ctrl+Alt+Q by default.
    /// </summary>
    /// <remarks>
    /// Flat, with every other shortcut: 設定 owns them as one set on one page, and a shortcut filed
    /// under the feature it starts would be the only one of the four the user could not find beside
    /// its siblings.
    ///
    /// Q because the three combinations already taken are the letters of what they do (A, W, S) and
    /// this one is the 取 of 取詞 — and because Ctrl+Alt+Q is claimed by nothing on a stock Windows.
    /// </remarks>
    public uint QuickLookupHotkeyModifiers { get; set; } = 3;

    /// <inheritdoc cref="QuickLookupHotkeyModifiers"/>
    /// <remarks>
    /// Ctrl+Alt+A, which capture used to own. The old default, Ctrl+Alt+Q, is QQ's screenshot key —
    /// on any machine running QQ the registration fails silently and the shortcut does nothing,
    /// which is how this default earned its replacement.
    /// </remarks>
    public uint QuickLookupHotkeyVirtualKey { get; set; } = 0x41;

    /// <inheritdoc cref="QuickLookupHotkeyModifiers"/>
    public string QuickLookupHotkeyDisplay { get; set; } = "Ctrl+Alt+A";
    public ShortcutInputKind QuickLookupHotkeyInputKind { get; set; } = ShortcutInputKind.Keyboard;
    public GamepadShortcutButton QuickLookupHotkeyGamepadButton { get; set; } = GamepadShortcutButton.None;

    /// <inheritdoc cref="TranslationWindowHotkeyEnabled"/>
    public bool QuickLookupHotkeyEnabled { get; set; } = true;

    public string SourceLanguage { get; set; } = LanguageData.DefaultOcrSourceLanguage;
    public string TargetLanguage { get; set; } = "ZH-HANS";
    public TranslationProvider Provider { get; set; } = TranslationProvider.Microsoft;
    public string ApiKey { get; set; } = "";
    /// <summary>
    /// The OpenAI-compatible server to talk to, or empty for
    /// <see cref="Services.Providers.OpenAiCompatibleProvider.DefaultBaseUrl"/>.
    /// </summary>
    /// <remarks>
    /// Empty rather than a copy of the default, for the same reason the prompt and the model are —
    /// see <see cref="OpenAiPromptAuto"/>.
    /// </remarks>
    public string OpenAiBaseUrl { get; set; } = "";
    public string OpenAiApiKey { get; set; } = "";
    public string OpenAiModel { get; set; } = "";

    /// <summary>
    /// The instruction sent to an OpenAI-compatible model, or empty to use the built-in one.
    /// </summary>
    /// <remarks>
    /// Editable because the right wording belongs to the model, not to this app: a translation-only
    /// model is trained on "translate from X to Y" and degrades when handed anything else, while a
    /// general chat model and a reasoning model each want something different again — the
    /// &lt;think&gt; stripping elsewhere in the provider is the same problem showing through. One
    /// built-in wording cannot serve all three, and whoever picked a local model can write a line
    /// of prose.
    ///
    /// Two of them because these are two different sentences rather than one sentence with a blank:
    /// with a source language chosen the model is told to translate from it, and with 自動 there is
    /// no language to name, so that wording has no <c>{source}</c> to fill at all.
    ///
    /// Empty rather than a copy of the default text, so anyone who never edits keeps following the
    /// built-in wording as it improves — stamping today's into the settings file would freeze them
    /// on it forever. See <see cref="Services.Providers.OpenAiCompatibleProvider.BuildPrompt"/>.
    /// </remarks>
    public string OpenAiPromptAuto { get; set; } = "";

    /// <inheritdoc cref="OpenAiPromptAuto"/>
    public string OpenAiPromptExplicit { get; set; } = "";

    /// <summary>
    /// The user's own OpenAI-compatible services. Which one (if any) is in force is
    /// <see cref="ActiveCustomServiceId"/> alongside <see cref="Provider"/> — see
    /// Services.ServiceSelection for how the two halves resolve.
    /// </summary>
    public List<CustomTranslatorService> CustomServices { get; set; } = [];

    /// <summary>Empty = the built-in providers; an id names a <see cref="CustomTranslatorService"/>.</summary>
    public string ActiveCustomServiceId { get; set; } = "";

    /// <summary>
    /// Whether the request carries a temperature at all.
    /// </summary>
    /// <remarks>
    /// Separate from the value because "no temperature" is not a number: the reasoning models on the
    /// hosted APIs reject the field outright rather than clamping it, so a request to them has to
    /// leave it out. On by default, which is what every local server expects.
    /// </remarks>
    public bool OpenAiTemperatureEnabled { get; set; } = true;

    /// <summary>
    /// How much randomness the model is asked for, when <see cref="OpenAiTemperatureEnabled"/>.
    /// </summary>
    /// <remarks>
    /// Zero because this is translation: the same line on screen should come back the same way twice.
    /// Editable because the value that means "as literal as possible" is the model's to define — some
    /// small local ones loop on repeated output at 0 and need a little slack to come out of it.
    /// </remarks>
    public double OpenAiTemperature { get; set; }
    public string Theme { get; set; } = "Dark";

    /// <summary>
    /// The user's size nudge on the overlay's automatic OCR-height font sizing — one setting for
    /// the screenshot overlay and the realtime blocks alike, because they share the curve and a
    /// reader's eyes are the same on both. Standard by default: every existing profile keeps the
    /// sizing it has always had.
    /// </summary>
    public OverlayFontCalibration FontCalibration { get; set; } = OverlayFontCalibration.Standard;

    /// <summary>Which engine reads text aloud. Windows voices by default: local, offline, no key.</summary>
    public TtsEngine TtsEngine { get; set; } = TtsEngine.Windows;

    /// <summary>
    /// The system voice to use, by <c>VoiceInfo.Id</c>. Empty means pick by language: a Chinese
    /// result gets a zh-* voice, an English one an en-* voice, and so on. A chosen voice is used
    /// whatever the text's language — that is what choosing it means.
    /// </summary>
    public string TtsVoiceId { get; set; } = "";

    /// <summary>Speak rate, on the synthesizer's own −10…10 scale. 0 is each voice's normal pace.</summary>
    public int TtsRate { get; set; } = 0;

    /// <summary>Speak volume, 0–100.</summary>
    public int TtsVolume { get; set; } = 100;

    /// <summary>Whether 划词翻译 reads its result aloud once it arrives, and which half of it.</summary>
    public AutoSpeakMode QuickLookupAutoSpeak { get; set; } = AutoSpeakMode.Off;

    /// <summary>Same, for a finished 截图翻译 capture.</summary>
    public AutoSpeakMode CaptureAutoSpeak { get; set; } = AutoSpeakMode.Off;
    /// <summary>
    /// The interface language, "zh-Hant" or "en". Empty means "not chosen yet".
    /// </summary>
    /// <remarks>
    /// Empty rather than a hardcoded default so a first run can follow the OS language — see
    /// <see cref="Services.LocalizationService.ResolveSystemDefault"/>. Once the user picks one it
    /// is stored verbatim and the OS is never consulted again, because an explicit choice should
    /// survive someone changing their Windows display language.
    ///
    /// This is the interface language only. It has no bearing on
    /// <see cref="TargetLanguage"/> or <see cref="RealtimeSettings.TargetLanguage"/>: what someone reads the
    /// buttons in and what they want subtitles translated into are unrelated, and a Taiwanese user
    /// running the app in English still wants Chinese output.
    /// </remarks>
    public string UiLanguage { get; set; } = "";

    /// <summary>
    /// 界面字体：空 = 跟随系统（Segoe UI + 微软雅黑），否则为本机已安装字体的名字
    /// （规范名或中文名均可，如 "LXGW WenKai" / "霞鹜文楷"）。见 <see cref="Services.UiFontService"/>。
    /// </summary>
    public string UiFontFamily { get; set; } = "";

    public bool AutoTranslateAfterSelection { get; set; } = false;
    public bool SaveScreenshotToDisk { get; set; } = false;
    /// <summary>Empty means "use ScreenshotSaveService.DefaultDirectory" (圖片\Yiwen).</summary>
    public string ScreenshotSavePath { get; set; } = "";
    /// <summary>Off by default: Debug records the recognised text, i.e. the user's screen contents.</summary>
    public bool VerboseLogging { get; set; } = false;
    /// <summary>
    /// The newest version the user has told us to stop interrupting them about, or empty for none.
    /// </summary>
    /// <remarks>
    /// Compared as a version rather than for equality, so it silences that release and nothing later:
    /// skipping 1.9.0 leaves 1.9.1 free to prompt again. It suppresses only the startup dialog — the
    /// nav rail still offers the update — because what the user declined was being interrupted, not
    /// the update itself. See <see cref="Services.UpdateNotifier"/>.
    /// </remarks>
    public string SkippedUpdateVersion { get; set; } = "";

    /// <summary>
    /// What 即時翻譯 keeps between sittings, grouped.
    /// </summary>
    /// <remarks>
    /// The first grouped section of this file, and the shape anything added from here on should
    /// follow — see <see cref="RealtimeSettings"/> for why the flat keys above it stayed flat.
    ///
    /// Declared last, and every later group belongs after it rather than beside the flat keys it
    /// relates to. Properties are written in declaration order, so this splits appsettings.json into
    /// two halves a reader can tell apart at a glance: everything that shipped before grouping
    /// existed, then everything that is grouped. Interleaving them would give the file no readable
    /// order at all — neither alphabetical, nor by feature, nor by age — and every group added later
    /// would have to find a home in the middle of the flat keys.
    /// </remarks>
    public RealtimeSettings Realtime { get; set; } = new();
}
