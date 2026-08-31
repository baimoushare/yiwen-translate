using System.Xml.Linq;
using Xunit;

namespace OverTranslate.Tests;

/// <summary>
/// 截图选择窗的首帧显示约束：窗口还没有完成截图合成时，不能先暴露黑色底。
/// </summary>
public class CaptureWindowMarkupTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XDocument Window() => XDocument.Load(Path.Combine(
        StringsParityTests.ProjectDirectory(), "Views", "Capture", "ScreenCaptureWindow.xaml"));

    [Fact]
    public void The_capture_window_must_be_transparent_before_the_screenshot_frame_is_ready()
    {
        var window = Window().Root!;

        Assert.Equal("True", (string?)window.Attribute("AllowsTransparency"));
        Assert.Equal("Transparent", (string?)window.Attribute("Background"));
    }
}
