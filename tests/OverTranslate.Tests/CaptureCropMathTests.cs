using System.Drawing;
using System.Windows;
using OverTranslate.Services;
using Xunit;

namespace OverTranslate.Tests;

/// <summary>
/// 选区几何的纯数学部分。用例数字取自 2026-09-01 混合 DPI 事故的真实坐标（桌面
/// -2560..2560、Selection.X 被算到 -4634、对话框在左屏 1005..1540），保证修复量的是那次
/// 量到的失败，而不是一个想象的几何。
/// </summary>
public class CaptureCropMathTests
{
    // 桌面 5120x1600、左缘 -2560：与事故当晚的捕获会话一致。
    private static readonly Rectangle Desktop = new(-2560, 0, 5120, 1600);

    [Fact]
    public void HealthyWindowPlacesTheSelectionWhereItWasDrawn()
    {
        // 对话框在左屏的 1005..1540：用户在那里画框，屏幕坐标就应该是那里。
        var wpf = new Rect(1005, 545, 535, 245);

        var screen = CaptureCropMath.ToScreen(wpf, Desktop, 1.0, 1.0);

        Assert.Equal(new Rect(-1555, 545, 535, 245), screen);
    }

    [Fact]
    public void HighDpiMonitorSelectionConvertsWithTheLiveScale()
    {
        // 同一框、175% 缩放：DIP 值是物理值除以 1.75 后的量，乘回同一比例必须还原。
        var wpf = new Rect(1005 / 1.75, 545 / 1.75, 535 / 1.75, 245 / 1.75);

        var screen = CaptureCropMath.ToScreen(wpf, Desktop, 1.75, 1.75);

        Assert.Equal(-1555, screen.X, 0);
        Assert.Equal(545, screen.Y, 0);
        Assert.Equal(535, screen.Width, 1);
        Assert.Equal(245, screen.Height, 1);
    }

    [Fact]
    public void CropThroughAHealthyWindowIsTheSelectionOffsetByItsOrigin()
    {
        var selection = new Rect(-1555, 545, 535, 245);

        var crop = CaptureCropMath.Crop(selection, Desktop, 5120, 1600);

        Assert.Equal(new Rectangle(1005, 545, 535, 245), crop);
    }

    [Fact]
    public void CropThroughADriftedWindowFollowsWhatTheImageActuallyShowed()
    {
        // 窗口被 WPF 重放到别处（漂移 100px、放大 10%）：冻结图跟着窗口走，裁剪必须映射到
        // 屏幕上真实显示的那块位图，而不是记忆里钉住的矩形。
        var drifted = new Rectangle(-2460, 0, 5632, 1760);
        var selection = new Rect(-1555, 545, 535, 245);

        var crop = CaptureCropMath.Crop(selection, drifted, 5120, 1600);

        Assert.NotNull(crop);
        var region = crop!.Value;
        // (sel.Left - drifted.Left) * 5120 / 5632 = 905 * 5120 / 5632 = 822.7 -> 822
        Assert.Equal(822, region.X);
        // (sel.Top - drifted.Top) * 1600 / 1760 = 545 * 1600 / 1760 = 495.45 -> 495
        Assert.Equal(495, region.Y);
    }

    [Fact]
    public void ASelectionEntirelyOffTheDesktopYieldsNoCrop()
    {
        // 事故形态：X 被算到屏幕外（-4634，右缘 -4136 仍在桌面左缘 -2560 之前）。整条选区
        // 都不在窗口内，没有图片可裁 —— null 而不是在位图角落里凭空裁一块。
        var selection = new Rect(-4634, 530, 498, 158);

        var crop = CaptureCropMath.Crop(selection, Desktop, 5120, 1600);

        Assert.Null(crop);
    }

    [Fact]
    public void ASelectionStraddlingTheDesktopEdgeCropsTheVisiblePart()
    {
        // 部分越界：左缘超出桌面 140px，交集为桌面内的 395px。
        var selection = new Rect(-2700, 545, 535, 245);

        var crop = CaptureCropMath.Crop(selection, Desktop, 5120, 1600);

        Assert.NotNull(crop);
        Assert.Equal(new Rectangle(0, 545, 395, 245), crop!.Value);
    }

    [Fact]
    public void DegenerateWindowRectYieldsNoCrop()
    {
        Assert.Null(CaptureCropMath.Crop(new Rect(0, 0, 10, 10), Rectangle.Empty, 5120, 1600));
        Assert.Null(CaptureCropMath.Crop(
            new Rect(0, 0, 10, 10), new Rectangle(0, 0, 0, 100), 5120, 1600));
    }
}
