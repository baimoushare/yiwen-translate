using System.Drawing;
using System.Windows;

namespace OverTranslate.Services;

/// <summary>
/// The geometry between a selection drawn on the capture window and the crop inside the frozen
/// screenshot, derived from the window's live pixel rectangle rather than values cached at setup.
/// </summary>
/// <remarks>
/// A capture window spans the whole virtual desktop on a machine whose monitors may run at
/// different scales, and WPF gives such a window ONE render scale for all of them. The window is
/// pinned to the desktop's pixel rectangle at creation, but WPF's own bookkeeping — its Left/Top,
/// the scale it lays content out at — can drift from that pin. That was measured on 2026-09-01:
/// on a mixed-DPI desktop (one monitor at 175% on the negative-X side) that was reconfigured
/// while the process lived, selections came out thousands of pixels off screen (logged
/// Selection.X = -4634 against a desktop whose left edge is -2560) while the frame the user drew
/// sat exactly where they put it, and the clamped crop read whatever sat at the bitmap's corner.
///
/// The mouse path is anchored to reality by the OS: WM_MOUSEMOVE carries client pixels relative
/// to the window's REAL origin, whatever WPF believes about it. So converting the DIP rect with
/// the real window rectangle and the CURRENT render scale recovers the true screen rectangle in
/// every state, stale or not. And because the frozen image is Stretch=Fill across the whole
/// client area, the pixels shown inside that rectangle are a fixed linear map of the bitmap —
/// no DPI arithmetic anywhere in the crop.
/// </remarks>
internal static class CaptureCropMath
{
    /// <summary>
    /// A window-DIP rectangle as absolute physical screen coordinates.
    /// </summary>
    public static Rect ToScreen(Rect wpfRect, Rectangle windowRect, double scaleX, double scaleY) =>
        new(
            windowRect.Left + wpfRect.X * scaleX,
            windowRect.Top + wpfRect.Y * scaleY,
            wpfRect.Width * scaleX,
            wpfRect.Height * scaleY);

    /// <summary>
    /// The screenshot region a screen-space selection is framing. Null when the selection misses
    /// the window entirely — there is no image outside it.
    /// </summary>
    public static Rectangle? Crop(Rect selection, Rectangle windowRect, int bitmapWidth, int bitmapHeight)
    {
        if (windowRect.Width <= 0 || windowRect.Height <= 0) return null;

        // Entirely outside the window the image fills: nothing was framed. Checked before the
        // clamping below, which would otherwise quietly manufacture a one-pixel crop at the edge
        // — exactly the failure mode this class exists to remove.
        if (selection.Right <= windowRect.Left || selection.Bottom <= windowRect.Top ||
            selection.Left >= windowRect.Right || selection.Top >= windowRect.Bottom)
            return null;

        double x0 = (selection.Left - windowRect.Left) * bitmapWidth / windowRect.Width;
        double y0 = (selection.Top - windowRect.Top) * bitmapHeight / windowRect.Height;
        double x1 = (selection.Right - windowRect.Left) * bitmapWidth / windowRect.Width;
        double y1 = (selection.Bottom - windowRect.Top) * bitmapHeight / windowRect.Height;

        // Floor on the top-left and ceil on the bottom-right, so an edge glyph is never cut by
        // rounding — the same discipline the previous bounds-relative arithmetic used.
        int left = Math.Clamp((int)Math.Floor(x0), 0, bitmapWidth - 1);
        int top = Math.Clamp((int)Math.Floor(y0), 0, bitmapHeight - 1);
        int right = Math.Clamp((int)Math.Ceiling(x1), left + 1, bitmapWidth);
        int bottom = Math.Clamp((int)Math.Ceiling(y1), top + 1, bitmapHeight);

        return new Rectangle(left, top, right - left, bottom - top);
    }
}
