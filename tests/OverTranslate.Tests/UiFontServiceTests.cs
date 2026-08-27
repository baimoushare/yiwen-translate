using System.Windows.Media;
using OverTranslate.Services;
using Xunit;

namespace OverTranslate.Tests;

public class UiFontServiceTests
{
    // 空、纯空白、已卸载的字体名都回退系统默认链：装了又卸的字体不能让界面渲染成豆腐块。
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NoSuchFontFamily-9F3C7B2E")]
    public void EmptyOrUninstalledName_FallsBackToSystemDefault(string? stored)
    {
        UiFontService.Apply(stored);

        Assert.Equal(new FontFamily(UiFontService.DefaultUiStack),     UiFontService.UiFamily);
        Assert.Equal(new FontFamily(UiFontService.DefaultOverlayStack), UiFontService.OverlayFamily);
    }

    [Fact]
    public void InstalledName_IsUsedVerbatim()
    {
        // Segoe UI 在 Windows 上必然存在；两条链统一成所选字体的单一家族。
        UiFontService.Apply("Segoe UI");

        Assert.Equal(new FontFamily("Segoe UI"), UiFontService.UiFamily);
        Assert.Equal(new FontFamily("Segoe UI"), UiFontService.OverlayFamily);
    }

    [Fact]
    public void DefaultStacks_AreSimplifiedChinese_WithYaHeiFallback()
    {
        // 曾经回退 Microsoft JhengHei（繁体），简体文本渲染出繁体字形观感——这是回归测试。
        Assert.DoesNotContain("JhengHei", UiFontService.DefaultUiStack, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JhengHei", UiFontService.DefaultOverlayStack, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Microsoft YaHei UI", UiFontService.DefaultUiStack);
        Assert.Contains("Microsoft YaHei UI", UiFontService.DefaultOverlayStack);
    }

    [Fact]
    public void PickerOptions_LeadsWithTheSystemDefaultEntry()
    {
        var options = UiFontService.PickerOptions();

        Assert.Equal("", options[0].Family);
        Assert.Contains(options, o => o.Family == "Segoe UI");
    }
}
