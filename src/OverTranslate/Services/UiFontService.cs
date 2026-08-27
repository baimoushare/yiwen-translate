using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using OverTranslate.Models;
// UseWindowsForms 把 System.Windows.Forms 也放进隐式 using，Application/FontFamily 与 WPF 的重名
using Application = System.Windows.Application;
using FontFamily  = System.Windows.Media.FontFamily;

namespace OverTranslate.Services;

/// <param name="Family">存储用的规范字体名（FontFamily.Source），空字符串表示跟随系统。</param>
/// <param name="Display">下拉框里显示的名字：有中文名的字体显示中文名（如 霞鹜文楷），否则显示规范名。</param>
public record UiFontOption(string Family, string Display);

/// <summary>
/// 界面字体的解析与应用，与 <see cref="ThemeService"/> 换主题同一套路：换的是资源字典里的
/// FontFamily，XAML 端全部经 DynamicResource 引用，替换后即时生效、无需重启。
/// </summary>
/// <remarks>
/// 历史问题：全局字体链曾经回退到 Microsoft JhengHei（繁体中文字体），简体码位用它渲染
/// 出来是繁体字形观感，这也是“明明设置了简体却满屏繁体”的根源之一。默认链因此改回
/// Windows 简体中文系统的默认界面字体 Microsoft YaHei UI；用户另可选本机任意已安装字体
/// （如霞鹜文楷 LXGW WenKai，开源字体，装在本机即会出现在列表中）。
///
/// 两个资源键、两条默认链：
/// - AppFont（界面正文）拉丁字体在前，西文走 Segoe UI、中文回退 YaHei；
/// - OverlayTextFont（截图/字幕译文）中文字体在前——译文以 CJK 为主，字号和排版由
///   中文字形撑起，让 YaHei 先接才能量得准。
/// 用户选定自定义字体后两条链统一为该字体单一家族。
/// </remarks>
public static class UiFontService
{
    /// <summary>AppFont 的默认链：即 Windows 界面默认字体（Win11 Segoe UI Variable / Win10 Segoe UI），中文回退微软雅黑。</summary>
    public const string DefaultUiStack =
        "Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI, Sans-Serif";

    /// <summary>OverlayTextFont 的默认链：中文字体在前，原因见类备注。</summary>
    public const string DefaultOverlayStack =
        "Microsoft YaHei UI, Segoe UI Variable Text, Segoe UI, Sans-Serif";

    // Apply 之后的解析结果；null 表示用默认链。Resolve 惰性兜底，保证任何
    // Apply 之前就取字体的调用（单元测试、早于 OnStartup 的窗口）也能拿到一致结果。
    private static FontFamily? _custom;
    private static bool _resolved;

    /// <summary>
    /// 把存储的字体名变成两个资源键里的实际 FontFamily。启动时、以及设置页每次改动时调用。
    /// 名字为空或本机已卸载该字体时回退默认链——卸载后无声地渲染成豆腐块是最坏结果。
    /// </summary>
    public static void Apply(string? stored)
    {
        _resolved = true;
        _custom   = ResolveCustom(stored);

        if (Application.Current is null) return;

        Application.Current.Resources["AppFont"]         = UiFamily;
        Application.Current.Resources["OverlayTextFont"] = OverlayFamily;
    }

    /// <summary>界面正文当前字体（AppFont 同款）。</summary>
    public static FontFamily UiFamily =>
        Custom() ?? new FontFamily(DefaultUiStack);

    /// <summary>译文叠加当前字体（OverlayTextFont 同款），截图气泡与实时字幕都用它。</summary>
    public static FontFamily OverlayFamily =>
        Custom() ?? new FontFamily(DefaultOverlayStack);

    private static FontFamily? Custom()
    {
        if (!_resolved)
        {
            _resolved = true;
            _custom   = ResolveCustom(SettingsService.Instance.Current.UiFontFamily);
        }
        return _custom;
    }

    private static FontFamily? ResolveCustom(string? stored)
    {
        var name = stored?.Trim();
        if (string.IsNullOrEmpty(name) || !IsInstalled(name)) return null;
        return new FontFamily(name);
    }

    /// <summary>本机是否装了这个字体：规范名或任意本地化名（如 LXGW WenKai / 霞鹜文楷）都算命中。</summary>
    internal static bool IsInstalled(string name) =>
        Fonts.SystemFontFamilies.Any(f =>
            string.Equals(f.Source, name, StringComparison.OrdinalIgnoreCase) ||
            f.FamilyNames.Values.Contains(name));

    /// <summary>
    /// 设置页字体下拉的选项表：首项“跟随系统”，其余为本机全部已安装字体，按显示名排序。
    /// 每次进设置页/切换界面语言时重建——首项文案要跟着界面语言走。
    /// </summary>
    public static List<UiFontOption> PickerOptions()
    {
        var options = new List<UiFontOption>
        {
            new("", LocalizationService.Get("S.Settings.UiFontDefault")),
        };

        options.AddRange(Fonts.SystemFontFamilies
            .Select(f => new UiFontOption(f.Source, DisplayName(f)))
            .OrderBy(o => o.Display, StringComparer.CurrentCulture));

        return options;
    }

    /// <summary>字体在下拉框里的显示名：有中文名显示中文名，否则显示规范名。</summary>
    private static string DisplayName(FontFamily family)
    {
        var names = family.FamilyNames;
        if (names.TryGetValue(XmlLanguage.GetLanguage("zh-CN"), out var hans)) return hans;
        if (names.Keys.FirstOrDefault(k => k.IetfLanguageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                is { } anyZh &&
            names.TryGetValue(anyZh, out var zh))
            return zh;
        return family.Source;
    }
}
