using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace OverTranslate.Tests;

/// <summary>
/// Holds the string dictionaries to each other, so a key added to one is never left out of the
/// other.
/// </summary>
/// <remarks>
/// A missing key does not break the build and does not throw: LocalizationService.Get returns the
/// key itself, so the mistake ships as a label reading "S.Settings.SavePath" on someone's screen.
/// That is exactly the kind of thing nobody notices in the language they don't use, which is what
/// this test is for.
///
/// The files are parsed as XML rather than loaded as ResourceDictionaries — no WPF application
/// object is needed to compare keys, and parsing keeps the failure message pointed at the file.
/// </remarks>
public class StringsParityTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>Placeholders like {0}, ignoring {} escapes.</summary>
    private static readonly Regex Placeholder = new(@"\{(\d+)\}", RegexOptions.Compiled);

    /// <summary>The src/OverTranslate directory, found by walking up from the test binaries.</summary>
    /// <remarks>Public because other tests that read the project's own files need the same walk.</remarks>
    public static string ProjectDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "OverTranslate");
            if (Directory.Exists(Path.Combine(candidate, "Resources"))) return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not find src/OverTranslate above {AppContext.BaseDirectory}");
    }

    private static string ResourcesDirectory() => Path.Combine(ProjectDirectory(), "Resources");

    private static Dictionary<string, string> Load(string fileName)
    {
        var path = Path.Combine(ResourcesDirectory(), fileName);
        return XDocument.Load(path).Root!
            .Elements()
            .Where(e => e.Attribute(X + "Key") is not null)
            .ToDictionary(e => e.Attribute(X + "Key")!.Value, e => e.Value);
    }

    public static readonly string ChineseFile = "Strings.zh-Hant.xaml";
    public static readonly string SimplifiedFile = "Strings.zh-Hans.xaml";
    public static readonly string EnglishFile = "Strings.en.xaml";

    /// <summary>Every dictionary that must stay in step — Chinese both scripts, and English.</summary>
    public static readonly string[] AllFiles = [ChineseFile, SimplifiedFile, EnglishFile];

    [Fact]
    public void All_dictionaries_define_exactly_the_same_keys()
    {
        var zh = Load(ChineseFile);
        var en = Load(EnglishFile);
        var hans = Load(SimplifiedFile);

        var missingFromEnglish  = zh.Keys.Except(en.Keys).OrderBy(k => k).ToList();
        var missingFromChinese  = en.Keys.Except(zh.Keys).OrderBy(k => k).ToList();
        var missingFromHans     = en.Keys.Except(hans.Keys).OrderBy(k => k).ToList();
        var extraInHans         = hans.Keys.Except(en.Keys).OrderBy(k => k).ToList();

        Assert.True(missingFromEnglish.Count == 0,
            $"Missing from {EnglishFile}: {string.Join(", ", missingFromEnglish)}");
        Assert.True(missingFromChinese.Count == 0,
            $"Missing from {ChineseFile}: {string.Join(", ", missingFromChinese)}");
        Assert.True(missingFromHans.Count == 0,
            $"Missing from {SimplifiedFile}: {string.Join(", ", missingFromHans)}");
        Assert.True(extraInHans.Count == 0,
            $"Defined only in {SimplifiedFile}: {string.Join(", ", extraInHans)}");
    }

    /// <summary>
    /// A translation that drops or invents a {0} is a FormatException at the moment the message is
    /// shown — which is to say, on the error path, where it replaces a real diagnostic with a crash.
    /// </summary>
    [Fact]
    public void Composite_formats_use_the_same_placeholders_in_all_languages()
    {
        var en = Load(EnglishFile);

        foreach (var file in new[] { ChineseFile, SimplifiedFile })
        {
            var other = Load(file);
            foreach (var (key, text) in other)
            {
                if (!en.TryGetValue(key, out var english)) continue;

                var inText    = Placeholder.Matches(text).Select(m => m.Groups[1].Value).ToHashSet();
                var inEnglish = Placeholder.Matches(english).Select(m => m.Groups[1].Value).ToHashSet();

                Assert.True(
                    inText.SetEquals(inEnglish),
                    $"{key} uses {{{string.Join(",", inText.Order())}}} in {file} " +
                    $"but {{{string.Join(",", inEnglish.Order())}}} in {EnglishFile}");
            }
        }
    }

    /// <summary>Every "S.…" key named in XAML or code-behind.</summary>
    /// <remarks>
    /// Keys are looked up by string at runtime, so neither a typo nor a rename is a compile error —
    /// the reference just resolves to nothing. DynamicResource silently renders empty and
    /// LocalizationService.Get returns the key itself, which is how a label saying
    /// "S.Settings.SavePath" would reach a user.
    /// </remarks>
    private static Dictionary<string, List<string>> KeysReferencedInSource()
    {
        var project = ProjectDirectory();
        var references = new Dictionary<string, List<string>>();

        var inXaml = new Regex(@"DynamicResource\s+(S\.[A-Za-z0-9_.]+)", RegexOptions.Compiled);
        var inCode = new Regex(@"""(S\.[A-Za-z0-9_.]+)""", RegexOptions.Compiled);

        foreach (var path in Directory.EnumerateFiles(project, "*.*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(project, path);

            // obj/ holds generated copies of the same XAML; Resources/ is the definitions themselves.
            if (relative.StartsWith("obj") || relative.StartsWith("bin") ||
                relative.StartsWith("Resources")) continue;

            var pattern = Path.GetExtension(path) switch
            {
                ".xaml" => inXaml,
                ".cs"   => inCode,
                _       => null
            };
            if (pattern is null) continue;

            foreach (Match match in pattern.Matches(File.ReadAllText(path)))
            {
                var key = match.Groups[1].Value;
                if (!references.TryGetValue(key, out var files))
                    references[key] = files = [];
                files.Add(relative);
            }
        }

        return references;
    }

    [Fact]
    public void Every_key_referenced_in_source_is_defined()
    {
        var defined = Load(ChineseFile).Keys.ToHashSet();

        var missing = KeysReferencedInSource()
            .Where(entry => !defined.Contains(entry.Key))
            .Select(entry => $"{entry.Key} (in {string.Join(", ", entry.Value.Distinct())})")
            .Order()
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"Referenced but not defined:{Environment.NewLine}" +
            string.Join(Environment.NewLine, missing));
    }

    /// <summary>
    /// Catches the other half of a rename: the old key left behind in the dictionaries.
    /// </summary>
    [Fact]
    public void Every_defined_key_is_referenced_somewhere()
    {
        var referenced = KeysReferencedInSource().Keys.ToHashSet();

        var orphans = Load(ChineseFile).Keys
            .Where(key => !referenced.Contains(key))
            .Order()
            .ToList();

        Assert.True(
            orphans.Count == 0,
            $"Defined but never referenced:{Environment.NewLine}" +
            string.Join(Environment.NewLine, orphans));
    }

    /// <summary>
    /// The OpenAI hint is the only place the interface tells anyone that a local model is an
    /// option, and Ollama is the route it points at. Losing that in one language loses it for
    /// those users entirely.
    /// </summary>
    [Fact]
    public void OpenAi_hint_recommends_Ollama_in_all_languages()
    {
        foreach (var file in AllFiles)
            Assert.Contains("Ollama", Load(file)["S.Provider.OpenAIHint"]);
    }

    /// <summary>
    /// A hint's [[…]] marks decide which words carry the accent colour — see HighlightedText — and
    /// they are the easiest thing to drop when translating, because the sentence still reads fine
    /// without them. It just reads flat, in the language nobody here is checking.
    /// </summary>
    [Fact]
    public void Highlight_marks_are_balanced_and_appear_in_all_languages()
    {
        var en = Load(EnglishFile);

        foreach (var file in new[] { ChineseFile, SimplifiedFile })
        {
            var other = Load(file);
            foreach (var (key, text) in other)
            {
                if (!en.TryGetValue(key, out var english)) continue;

                var inText    = HighlightCount(text);
                var inEnglish = HighlightCount(english);

                Assert.True(
                    inText == inEnglish,
                    $"{key} highlights {inText} stretch(es) in {file} " +
                    $"but {inEnglish} in {EnglishFile}");

                // A count that matches on both sides is still no use if one of them is a stray bracket,
                // which HighlightedText renders as ordinary text.
                foreach (var (loaded, str) in new[] { (file, text), (EnglishFile, english) })
                    Assert.True(
                        Occurrences(str, "[[") == Occurrences(str, "]]"),
                        $"{key} has unbalanced [[ ]] in {loaded}: {str}");
            }
        }
    }

    private static int HighlightCount(string text) =>
        OverTranslate.Views.Controls.HighlightedText.Split(text).Count(s => s.Highlighted);

    private static int Occurrences(string text, string token)
    {
        int count = 0, at = 0;
        while ((at = text.IndexOf(token, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += token.Length;
        }

        return count;
    }

    /// <summary>
    /// The strip of controls a running session puts on screen has one name in each language.
    /// </summary>
    /// <remarks>
    /// It had four in Chinese — 浮動列, 浮動工具列, 浮動控制列, 浮動視窗 — spread across the steps on
    /// 即時翻譯, the running hint and the settings page, so nothing told a reader that all four were
    /// the same strip. Each was written by someone naming it afresh in the sentence they happened to
    /// be writing, which is exactly what this stops.
    /// </remarks>
    [Theory]
    [InlineData("Strings.zh-Hant.xaml", "浮動(?!視窗列)", "浮動視窗列")]
    [InlineData("Strings.zh-Hans.xaml", "浮动(?!窗口栏)", "浮动窗口栏")]
    [InlineData("Strings.en.xaml", "(?i)floating (?!bar)", "floating bar")]
    public void The_floating_bar_is_called_the_same_thing_everywhere(
        string file, string pattern, string agreedName)
    {
        var offenders = Load(file)
            .Where(kv => Regex.IsMatch(kv.Value, pattern))
            .Select(kv => $"{kv.Key}: {kv.Value}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"{file} names it something other than \"{agreedName}\":{Environment.NewLine}" +
            string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Full-width punctuation is correct in Chinese and wrong in English, and it is the single
    /// easiest thing to carry over when translating by copying a line and editing it in place.
    /// </summary>
    [Fact]
    public void English_strings_use_no_full_width_punctuation()
    {
        var offenders = Load(EnglishFile)
            .Where(kv => kv.Value.Any("，。：；「」（）、？！／".Contains))
            .Select(kv => $"{kv.Key}: {kv.Value}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"Full-width punctuation in {EnglishFile}:{Environment.NewLine}" +
            string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// The Simplified dictionary is translated from the Traditional one, and the easiest way to
    /// write it is to copy each line and convert it in place — which is also the easiest way to
    /// leave a character behind. A Traditional-only character left in a value renders the
    /// interface as a mixed-script soup that is readable but always slightly wrong.
    /// </summary>
    /// <remarks>
    /// The set is curated rather than derived from a conversion table: it only needs to cover the
    /// characters the Traditional dictionary actually uses, and every character in it has a
    /// different Simplified counterpart, so none of them is legitimate inside a Simplified value.
    /// 「」 count too — this app's Simplified convention is curly quotes, and corner brackets can
    /// only have arrived by copy-paste.
    /// </remarks>
    [Fact]
    public void Simplified_strings_contain_no_traditional_characters()
    {
        char[] traditionalOnly =
        [
            '譯','設','體','讀','統','請','關','閉','視','圖','層','隱','選','單','暫','鍵','盤','們',
            '備','還','問','號','碼','較','適','變','錯','誤','訊','網','連','標','題','軟','螢','應',
            '執','記','錄','詳','資','傳','載','點','擊','這','裡','將','對','話','遊','戲','聲','語',
            '韓','簡','義','麥','臘','愛','蘭','脫','羅','馬','烏','動','內','後','來','時','間','區',
            '塊','數','減','觀','顏','預','覽','啟','檢','測','發','現','復','響','繼','續','認','識',
            '轉','換','輸','離','開','戶','際','項','聽','寫','邊','緣','環','況','條','為','於','從',
            '讓','確','麼','鐘','長','寬','儲','夾','匯','斷','調','擇','線','偵','畫','雲','電','載',
            '「','」',
        ];

        var offenders = Load(SimplifiedFile)
            .Where(kv => kv.Value.Any(traditionalOnly.Contains))
            .Select(kv => $"{kv.Key}: {kv.Value}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"Traditional characters left in {SimplifiedFile}:{Environment.NewLine}" +
            string.Join(Environment.NewLine, offenders));
    }
}
