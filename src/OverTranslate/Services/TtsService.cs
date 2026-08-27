using System.IO;
using System.Speech.Synthesis;
using System.Windows.Media;
using GTranslate.Translators;
using NLog;
using OverTranslate.Models;

namespace OverTranslate.Services;

public class TtsService : IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly GoogleTranslator2    _google2   = new();
    private readonly GoogleTranslator     _google    = new();
    private readonly MicrosoftTranslator  _microsoft = new();
    private readonly BingTranslator       _bing      = new();
    private readonly YandexTranslator     _yandex    = new();
    private readonly MediaPlayer _player = new();
    private CancellationTokenSource? _cts;
    private bool _active;

    /// <summary>
    /// The running Windows-voice utterance, if any. One at a time: a new SpeakAsync cancels the
    /// previous, exactly as the online path's Stop-and-restart always has.
    /// </summary>
    private SpeechSynthesizer? _synth;

    /// <summary>True while fetching audio or playing. Lets the UI toggle a play/stop button.</summary>
    public bool IsActive => _active;

    /// <summary>
    /// Bumped by every speak and every stop, so a caller chaining utterances (原文 then 译文) can
    /// tell "finished on its own" — the number it holds is the number it still holds — from
    /// "someone interrupted or spoke something new".
    /// </summary>
    public int Serial { get; private set; }

    /// <summary>Completes when the current utterance ends or is stopped; already complete when idle.</summary>
    /// <remarks>
    /// For a caller that wants to say one thing after another. Pair it with <see cref="Serial"/>:
    /// awaiting this alone cannot tell a natural end from the user's stop.
    /// </remarks>
    public Task WhenIdleAsync() => _idleTcs?.Task ?? Task.CompletedTask;

    private TaskCompletionSource? _idleTcs;

    /// <summary>Raised (on the UI thread) whenever playback starts, ends, fails, or is stopped.</summary>
    public event EventHandler? StateChanged;

    public TtsService()
    {
        // Natural end / playback error must flip the button back to "play".
        _player.MediaEnded  += (_, _) => SetActive(false);
        _player.MediaFailed += (_, _) => SetActive(false);
    }

    private void SetActive(bool value)
    {
        if (_active == value) return;
        _active = value;

        if (value)
        {
            // RunContinuationsAsynchronously: completions resume on the threadpool, never inline on
            // whatever thread happened to end the playback.
            _idleTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        else
        {
            _idleTcs?.TrySetResult();
            _idleTcs = null;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static readonly string TempFile =
        Path.Combine(Path.GetTempPath(), "overtranslate_tts.mp3");

    /// <summary>Stops any in-flight fetch and playback, on either engine.</summary>
    public void Stop()
    {
        Serial++;
        _cts?.Cancel();
        TearDownSynthesizer();
        System.Windows.Application.Current.Dispatcher.Invoke(() => { _player.Stop(); _player.Close(); });
        SetActive(false);
    }

    public async Task SpeakAsync(string text, string langCode)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        Serial++;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        TearDownSynthesizer();
        System.Windows.Application.Current.Dispatcher.Invoke(() => { _player.Stop(); _player.Close(); });
        SetActive(true);

        // Windows voices first unless the user said otherwise: local, offline, no key. A machine
        // with no voice for the language — or a synthesizer that fails outright — falls through to
        // the online chain rather than failing the click.
        var settings = SettingsService.Instance.Current;
        if (settings.TtsEngine == TtsEngine.Windows)
        {
            if (token.IsCancellationRequested) return;
            if (StartWindowsSpeech(text, langCode, settings, token)) return;
            if (token.IsCancellationRequested) return;
            Log.Debug("Windows TTS unavailable for lang={Lang}; falling back to online providers", langCode);
        }

        var providers = BuildProviders(text, langCode);
        Exception? lastEx = null;

        foreach (var (name, speak) in providers)
        {
            if (token.IsCancellationRequested) return;
            try
            {
                Log.Debug("TTS trying {Provider}, lang={Lang}", name, langCode);
                var stream = await speak();
                token.ThrowIfCancellationRequested();

                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, token);
                await File.WriteAllBytesAsync(TempFile, ms.ToArray(), token);

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    _player.Open(new Uri(TempFile));
                    _player.Play();
                });

                Log.Debug("TTS success via {Provider}", name);
                return;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Log.Warn(ex, "TTS provider {Provider} failed, trying next", name);
                lastEx = ex;
            }
        }

        // Every provider failed (and we weren't cancelled) — clear state so the button resets.
        if (!token.IsCancellationRequested) SetActive(false);
        if (lastEx != null) throw lastEx;
    }

    /// <summary>
    /// Starts the utterance on a Windows system voice and returns true once it is under way.
    /// </summary>
    /// <remarks>
    /// <see cref="SpeechSynthesizer.SpeakAsync(string)"/> plays on its own thread and raises
    /// <see cref="SpeechSynthesizer.SpeakCompleted"/> there, so completion is marshalled back to
    /// the UI thread to keep <see cref="StateChanged"/>'s contract. A voice chosen by name is used
    /// whatever the text's language; with none chosen, the first enabled voice whose culture
    /// matches the language is preferred, and a machine with no such voice still speaks on the
    /// synthesizer's default voice rather than staying silent.
    /// </remarks>
    private bool StartWindowsSpeech(
        string text, string langCode, AppSettings settings, CancellationToken token)
    {
        try
        {
            var synth = new SpeechSynthesizer();
            _synth = synth;

            synth.Rate   = Math.Clamp(settings.TtsRate, -10, 10);
            synth.Volume = Math.Clamp(settings.TtsVolume, 0, 100);

            var voice = ResolveVoice(synth, langCode, text, settings.TtsVoiceId);
            if (voice is not null) synth.SelectVoice(voice.VoiceInfo.Name);

            synth.SpeakCompleted += (_, _) =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    // A Stop() between completion and this callback has already torn the
                    // synthesizer down; only the natural end disposes it here.
                    if (!ReferenceEquals(_synth, synth)) return;
                    _synth = null;
                    synth.Dispose();
                    SetActive(false);
                });
            };

            if (token.IsCancellationRequested) { TearDownSynthesizer(); return false; }

            synth.SetOutputToDefaultAudioDevice();
            synth.SpeakAsync(text);

            Log.Debug("TTS via Windows voice {Voice}",
                voice?.VoiceInfo.Name ?? "(system default)");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Windows system TTS failed before speaking");
            TearDownSynthesizer();
            return false;
        }
    }

    private void TearDownSynthesizer()
    {
        var synth = _synth;
        _synth = null;
        if (synth is null) return;
        try { synth.SpeakAsyncCancelAll(); } catch { /* already finished */ }
        try { synth.Dispose(); } catch { /* already disposed */ }
    }

    /// <summary>
    /// The voice to speak with: the user's named choice if it is installed and enabled, else the
    /// first enabled voice for the text's language, else null for the synthesizer's default.
    /// </summary>
    private static InstalledVoice? ResolveVoice(
        SpeechSynthesizer synth, string langCode, string text, string voiceId)
    {
        var voices = synth.GetInstalledVoices().Where(v => v.Enabled).ToList();

        if (!string.IsNullOrEmpty(voiceId))
        {
            var named = voices.FirstOrDefault(v =>
                string.Equals(v.VoiceInfo.Id, voiceId, StringComparison.OrdinalIgnoreCase));
            if (named is not null) return named;
        }

        var prefix = ResolveWindowsLanguagePrefix(langCode, text);
        return voices.FirstOrDefault(v =>
            v.VoiceInfo.Culture.TwoLetterISOLanguageName.Equals(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The two-letter language a Windows voice should be picked for. AUTO has no language to name,
    /// so it is decided from the text itself: kana means Japanese, han means Chinese, hangul means
    /// Korean, and anything else gets English — the same guess an English reader makes reading it.
    /// </summary>
    public static string ResolveWindowsLanguagePrefix(string langCode) => langCode.ToUpperInvariant() switch
    {
        "ZH" or "ZH-HANS" or "ZH-HANT" => "zh",
        "JA" => "ja",
        "KO" => "ko",
        "EN" or "EN-US" or "EN-GB" => "en",
        "AUTO" => "", // resolved from the text by the caller-side overload below
        var other => other.Length >= 2 ? other[..2].ToLowerInvariant() : "en",
    };

    /// <inheritdoc cref="ResolveWindowsLanguagePrefix(string)"/>
    public static string ResolveWindowsLanguagePrefix(string langCode, string text)
    {
        var fromCode = ResolveWindowsLanguagePrefix(langCode);
        if (fromCode.Length > 0) return fromCode;

        if (text.Any(c => c is >= '\u3040' and <= '\u30FF')) return "ja"; // kana
        if (text.Any(c => c is >= '\uAC00' and <= '\uD7AF')) return "ko"; // hangul
        if (text.Any(c => c is >= '\u4E00' and <= '\u9FFF')) return "zh"; // han
        return "en";
    }

    /// <summary>
    /// Says one or both halves of a finished translation aloud, in order, without overlapping —
    /// and abandons the second half the moment the user stops the first or speaks something else.
    /// </summary>
    /// <remarks>
    /// Shared by every auto-speak caller (划词翻译, 截图翻译) so the ordering, the gap between the
    /// halves, and the give-up-on-interrupt rule are one behaviour rather than two near-copies.
    /// Callers have already decided the result is worth reading: not empty, not a repeat.
    /// </remarks>
    public static async Task SpeakTranslationAsync(
        TtsService tts, AutoSpeakMode mode,
        string sourceText, string sourceLang,
        string targetText, string targetLang)
    {
        if (mode == AutoSpeakMode.Source || mode == AutoSpeakMode.Both)
        {
            var serial = tts.Serial;
            await tts.SpeakAsync(sourceText, sourceLang);

            if (mode == AutoSpeakMode.Both)
            {
                await tts.WhenIdleAsync();
                // The serial moved when the first half was stopped or replaced; reading on would
                // resurrect a utterance the user just silenced.
                if (tts.Serial != serial) return;
                await Task.Delay(150);
            }
        }

        if (mode == AutoSpeakMode.Target || mode == AutoSpeakMode.Both)
            await tts.SpeakAsync(targetText, targetLang);
    }

    private List<(string name, Func<Task<Stream>> speak)> BuildProviders(string text, string langCode)
    {
        var gLang = MapGoogle(langCode);
        var bLang = MapBing(langCode);
        var yLang = MapYandex(langCode);

        var mLang = MapMicrosoft(langCode);

        return
        [
            ("Google2",    () => _google2.TextToSpeechAsync(text, gLang, false)),
            ("Google",     () => _google.TextToSpeechAsync(text, gLang)),
            ("Microsoft",  () => _microsoft.TextToSpeechAsync(text, mLang)),
            ("Bing",       () => _bing.TextToSpeechAsync(text, bLang)),
            ("Yandex",     () => _yandex.TextToSpeechAsync(text, yLang)),
        ];
    }

    private static string MapGoogle(string code) => code.ToUpperInvariant() switch
    {
        "ZH" or "ZH-HANS" or "AUTO" => "zh-CN",
        "ZH-HANT"                    => "zh-TW",
        "JA"                         => "ja",
        "KO"                         => "ko",
        "EN" or "EN-US" or "EN-GB"   => "en",
        "DE"                         => "de",
        "FR"                         => "fr",
        "ES"                         => "es",
        "IT"                         => "it",
        "PT" or "PT-BR"              => "pt",
        "RU"                         => "ru",
        "UK"                         => "uk",
        "PL"                         => "pl",
        "NL"                         => "nl",
        "TR"                         => "tr",
        _                            => "en",
    };

    private static string MapBing(string code) => code.ToUpperInvariant() switch
    {
        "ZH" or "ZH-HANS" or "AUTO" => "zh-Hans",
        "ZH-HANT"                    => "zh-Hant",
        "JA"                         => "ja",
        "KO"                         => "ko",
        "EN" or "EN-US" or "EN-GB"   => "en",
        "DE"                         => "de",
        "FR"                         => "fr",
        "ES"                         => "es",
        "IT"                         => "it",
        "PT" or "PT-BR"              => "pt",
        "RU"                         => "ru",
        "UK"                         => "uk",
        "PL"                         => "pl",
        "NL"                         => "nl",
        "TR"                         => "tr",
        _                            => "en",
    };

    private static string MapMicrosoft(string code) => MapBing(code);

    private static string MapYandex(string code) => code.ToUpperInvariant() switch
    {
        "ZH" or "ZH-HANS" or "ZH-HANT" or "AUTO" => "zh",
        "JA"                                       => "ja",
        "KO"                                       => "ko",
        "EN" or "EN-US" or "EN-GB"                 => "en",
        "DE"                                       => "de",
        "FR"                                       => "fr",
        "ES"                                       => "es",
        "IT"                                       => "it",
        "PT" or "PT-BR"                            => "pt",
        "RU"                                       => "ru",
        "UK"                                       => "uk",
        "PL"                                       => "pl",
        "NL"                                       => "nl",
        "TR"                                       => "tr",
        _                                          => "en",
    };

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _google2.Dispose();
        _google.Dispose();
        _microsoft.Dispose();
        _bing.Dispose();
        _yandex.Dispose();
        TearDownSynthesizer();
        System.Windows.Application.Current.Dispatcher.Invoke(() => { _player.Stop(); _player.Close(); });
    }
}
