using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls.Primitives;
using NLog;
using OverTranslate.Services;
using WPoint = System.Windows.Point;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace OverTranslate.Views.Capture;

public partial class ScreenCaptureWindow : Window
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private const int WM_NCHITTEST  = 0x0084;
    private const int HTTRANSPARENT = -1;
    private static readonly Uri CrosshairCursorUri = new("pack://application:,,,/icons/capture_crosshair.cur", UriKind.Absolute);

    /// <summary>Corner radius of the frame, and of the hole cut for it in the dim layer.</summary>
    private const double FrameCorner = 3;

    /// <summary>
    /// Dashes while the box is being drawn, a solid edge once it is settled — the same distinction
    /// the realtime edit layer draws, and the same reason: a dashed edge reads as "still happening",
    /// so keeping it after the button is released would leave the selection looking unfinished.
    /// </summary>
    private static readonly DoubleCollection PreviewDashes = Freeze([4, 3]);

    private HwndSource? _hwndSource;
    private bool _inBackgroundMode;

    private readonly Bitmap _screenshot;
    private readonly System.Drawing.Rectangle _physBounds;
    private readonly TaskCompletionSource<bool> _selectionTcs = new();

    private WPoint _startPoint;
    private Rect _selectionWpfRect;
    private bool _isDragging;
    private bool _processingStarted;
    private bool _hasSelection;

    public Rect Selection { get; private set; }
    public Bitmap? CroppedBitmap { get; private set; }
    public bool HasSelection => _hasSelection;

    private double _dpiX = 1.0;
    private double _dpiY = 1.0;

    public ScreenCaptureWindow(Bitmap screenshot, System.Drawing.Rectangle physBounds)
    {
        _screenshot = screenshot;
        _physBounds = physBounds;
        InitializeComponent();

        // Keep the native window transparent until WPF has painted the frozen screenshot. The XAML
        // background is transparent as well, so the compositor cannot expose a black full-screen
        // frame while the first bitmap-backed frame is still being prepared.
        Opacity = 0;

        // Provisional: OnSourceInitialized replaces this with the pixel rect the screenshot was
        // captured from. Needed only so the window has a size before its handle exists.
        Left   = SystemParameters.VirtualScreenLeft;
        Top    = SystemParameters.VirtualScreenTop;
        Width  = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        Cursor = LoadCrosshairCursor();

        Closed += (_, _) =>
        {
            _selectionTcs.TrySetResult(false);
            CroppedBitmap?.Dispose();
            CroppedBitmap = null;
            _screenshot.Dispose();
        };
    }

    public Task<bool> WaitForSelectionAsync() => _selectionTcs.Task;

    /// <summary>
    /// Raised with the new selection, in physical pixels, whenever the user moves or resizes a
    /// settled box — so the toolbar anchored to it can follow rather than sit next to where the box
    /// used to be. Never raised while the box is first being drawn: nothing is anchored to it yet.
    /// </summary>
    public event EventHandler<Rect>? SelectionAdjusted;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        DimPath.Data = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        Opacity = 1;

        // Claimed only once the first frame is on screen. Activating before that forces a
        // foreground switch while the window is still empty, which flashes its black background.
        Activate();
    }

    // One card per monitor, positioned at each screen's top-left corner. This window spans the whole
    // virtual desktop, so a single corner-anchored card would land on whichever monitor happens to
    // hold the virtual origin — often not the one being looked at.
    private List<HintSpot> BuildHintSpots()
    {
        const double margin = 12;

        return System.Windows.Forms.Screen.AllScreens
            .Select(screen =>
            {
                // This window renders at a single DPI across the whole desktop, so a card sitting on
                // a monitor at another scale comes out the wrong physical size. Scaling it by the
                // ratio between the two restores the size the monitor's own DPI would have given.
                double relScale = ScreenGeometry.ScaleAt(
                    screen.Bounds.Left + screen.Bounds.Width  / 2,
                    screen.Bounds.Top  + screen.Bounds.Height / 2) / _dpiX;

                return new HintSpot(
                    (screen.Bounds.Left - _physBounds.Left) / _dpiX + margin * relScale,
                    (screen.Bounds.Top  - _physBounds.Top)  / _dpiY + margin * relScale,
                    relScale);
            })
            .ToList();
    }

    // Window-local DIP position of one hint card, and the scale that makes it read the same
    // physical size as the monitor it sits on would give it.
    private sealed record HintSpot(double X, double Y, double Scale);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);

        // Before the DPI is read: pinning the window settles which monitor it belongs to, and that
        // is what the DPI below describes.
        ScreenGeometry.PinPhysicalBounds(this, _physBounds);

        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget != null)
        {
            _dpiX = src.CompositionTarget.TransformToDevice.M11;
            _dpiY = src.CompositionTarget.TransformToDevice.M22;
        }

        // Tag the capture with the DPI that makes its DIP size equal this window's, so WPF maps it
        // 1:1 instead of rescaling a full virtual-desktop image on the first frame. At 96 DPI a
        // 5120px-wide capture claims to be 5120 DIP while the window is only ~4130 DIP, and that
        // mismatch is paid for on every render of the largest visual in the app.
        ScreenshotImage.Source = BitmapToDisplaySource(_screenshot, 96.0 * _dpiX);

        // Filled as soon as the DPI is known, so the cards take part in the window's first layout
        // pass and are already painted when it becomes visible. (They used to be deferred to
        // OnContentRendered so their reveal animation would not play against a hidden window; with
        // no animation left, deferring only adds work between the first frame and the reveal.)
        HintHost.ItemsSource = BuildHintSpots();

        // The pin above asked for _physBounds; whether the window actually landed there is worth
        // one line at startup, because everything downstream now corrects through the live rect
        // but a window outside the desktop is a user-visible breakage of its own.
        var placed = ScreenGeometry.PhysicalBounds(this);
        if (!placed.IsEmpty && placed != _physBounds)
            Log.Warn(
                "Capture window pinned to {Pinned} but actually at {Actual}",
                _physBounds, placed);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_inBackgroundMode && msg == WM_NCHITTEST)
        {
            handled = true;
            return (IntPtr)HTTRANSPARENT;
        }
        return IntPtr.Zero;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // Esc must cancel at any stage, including after processing started. This is the fallback
        // path for when OverlayWindow's low-level keyboard hook is not in play (not yet installed,
        // already torn down, or dropped by Windows) — without it, a session that fails mid-flight
        // leaves this full-screen dim window on top of everything with no way to close it.
        if (e.Key == Key.Escape)
        {
            _selectionTcs.TrySetResult(false);
            Close();
        }
    }

    // Same cancellation as Esc. The button lives inside the hint cards, which are collapsed the
    // moment a drag begins, so this only ever runs while no selection exists.
    private void CancelCaptureBtn_Click(object sender, RoutedEventArgs e)
    {
        _selectionTcs.TrySetResult(false);
        Close();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_processingStarted || _hasSelection) return;
        base.OnMouseLeftButtonDown(e);
        _startPoint  = e.GetPosition(this);
        _isDragging  = true;
        HintHost.Visibility      = Visibility.Collapsed;
        SelectionRect.StrokeDashArray = PreviewDashes;
        SetFrameVisibility(true);
        CaptureMouse();
        DrawRect(_startPoint, _startPoint);
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        if (_processingStarted) return;
        base.OnMouseRightButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_isDragging) return;
        DrawRect(_startPoint, e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_isDragging) return;
        _isDragging = false;
        ReleaseMouseCapture();

        var rect = Normalize(_startPoint, e.GetPosition(this));
        _selectionWpfRect = rect;
        if (rect.Width < 4 || rect.Height < 4)
        {
            _hasSelection = false;
            SetFrameVisibility(false);
            SetHandlesVisibility(false);
            UpdateDimLayer();
            HintHost.Visibility = Visibility.Visible;
            return;
        }

        _hasSelection = true;
        SelectionRect.StrokeDashArray = null;
        UpdateSelectionMetadata();
        UpdateSelectionVisuals();

        _selectionTcs.TrySetResult(true);
    }

    public bool PrepareForTranslation()
    {
        if (!_hasSelection) return false;

        if (_processingStarted)
            return CroppedBitmap != null;

        if (!TryCreateCroppedBitmap())
            return false;

        _processingStarted = true;
        SwitchToBackgroundMode();
        return true;
    }

    // Returns a clean crop of the ORIGINAL capture for the current selection, as a frozen
    // BitmapSource. Unlike a live screen grab, this never contains the selection rectangle or
    // resize handles (those are this window's chrome, not part of _screenshot). Returns null when
    // there is no usable selection.
    public BitmapSource? CreateSelectionImage()
    {
        if (!_hasSelection) return null;

        // Same window-anchored mapping as TryCreateCroppedBitmap: the copy-screenshot image must
        // show the same pixels the translation read, or the two disagree about what was framed.
        var windowRect = LiveWindowRect();
        if (CaptureCropMath.Crop(Selection, windowRect, _screenshot.Width, _screenshot.Height) is not
            { } region)
            return null;

        using var crop = _screenshot.Clone(region, _screenshot.PixelFormat);
        return BitmapToDisplaySource(crop);
    }

    public void SwitchToBackgroundMode()
    {
        SetFrameVisibility(false);
        SetHandlesVisibility(false);

        // Square-cornered from here on, unlike the hole under the frame: the rounded one matched the
        // frame's corners, and the frame has just gone. What is left is the area the translation is
        // painted into, and that is a plain rectangle.
        var outer = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        var inner = new RectangleGeometry(_selectionWpfRect);
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(outer);
        group.Children.Add(inner);
        DimPath.Data = group;

        Cursor = null;
        _inBackgroundMode = true;
        _hwndSource?.AddHook(WndProc);
    }

    private static System.Windows.Input.Cursor LoadCrosshairCursor()
    {
        var streamInfo = System.Windows.Application.GetResourceStream(CrosshairCursorUri);
        if (streamInfo?.Stream == null)
            return System.Windows.Input.Cursors.Cross;

        return new System.Windows.Input.Cursor(streamInfo.Stream);
    }

    private void DrawRect(WPoint p1, WPoint p2)
    {
        var r = Normalize(p1, p2);
        _selectionWpfRect = r;
        PlaceFrame(r);
        UpdateDimLayer();
    }

    // The tint and the edge are two elements — see the XAML — and are always given the same box.
    private void PlaceFrame(Rect r)
    {
        PlacePart(SelectionFill, r);
        PlacePart(SelectionRect, r);

        static void PlacePart(System.Windows.Shapes.Rectangle part, Rect r)
        {
            System.Windows.Controls.Canvas.SetLeft(part, r.X);
            System.Windows.Controls.Canvas.SetTop(part,  r.Y);
            part.Width  = r.Width;
            part.Height = r.Height;
        }
    }

    private void SetFrameVisibility(bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        SelectionFill.Visibility = visibility;
        SelectionRect.Visibility = visibility;
    }

    /// <summary>
    /// Dims everything except the box being framed. The hole follows the frame's rounded corners, so
    /// the two read as one shape rather than as a rectangle sitting inside a slightly larger one.
    /// </summary>
    /// <remarks>
    /// Rebuilt on every move of the pointer, which is two rectangles and a group — cheap next to the
    /// full-screen screenshot this window is already compositing on each frame.
    /// </remarks>
    private void UpdateDimLayer()
    {
        var outer = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));

        if (!_isDragging && !_hasSelection)
        {
            DimPath.Data = outer;
            return;
        }

        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(outer);
        group.Children.Add(new RectangleGeometry(_selectionWpfRect, FrameCorner, FrameCorner));
        DimPath.Data = group;
    }

    private void UpdateSelectionMetadata()
    {
        // Live values, not the ones cached at OnSourceInitialized. On a mixed-DPI desktop that was
        // reconfigured while the process lived, those caches were measured disagreeing with the
        // window's real position by thousands of pixels (2026-09-01: Selection.X = -4634 against
        // a desktop whose left edge is -2560, while the frame sat exactly where the user drew it).
        // The mouse events that filled _selectionWpfRect are anchored to the window's real client
        // origin by the OS, so converting with the real rectangle and the CURRENT scale recovers
        // the true screen position in every state — stale bookkeeping included.
        var screen = CaptureCropMath.ToScreen(_selectionWpfRect, LiveWindowRect(), LiveScaleX(), LiveScaleY());
        int x = (int)Math.Floor(screen.X);
        int y = (int)Math.Floor(screen.Y);
        int right = (int)Math.Ceiling(screen.Right);
        int bottom = (int)Math.Ceiling(screen.Bottom);
        Selection = new Rect(x, y, Math.Max(1, right - x), Math.Max(1, bottom - y));
    }

    // Where the window really is, in pixels. GetWindowRect reads the HWND the moment it is asked,
    // which is the whole point: this cannot be stale the way Window.Left or a cached rect can.
    // Falls back to the pinned rectangle only before the handle exists, where nothing can select.
    private System.Drawing.Rectangle LiveWindowRect()
    {
        var rect = ScreenGeometry.PhysicalBounds(this);
        return rect.IsEmpty ? _physBounds : rect;
    }

    // The render scale read NOW, in the same space the mouse events arrive in — see
    // UpdateSelectionMetadata for why the setup-time _dpiX must not be trusted to still match.
    private double LiveScaleX()
    {
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget?.TransformToDevice.M11 ?? _dpiX;
    }

    /// <inheritdoc cref="LiveScaleX"/>
    private double LiveScaleY()
    {
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget?.TransformToDevice.M22 ?? _dpiY;
    }

    private bool TryCreateCroppedBitmap()
    {
        UpdateSelectionMetadata();

        // The crop follows what the frozen image actually showed inside the drawn frame. The
        // image is Stretch=Fill across the whole client area, so bitmap pixels are a fixed
        // linear map of the window's pixel rectangle — a mapping that holds no matter which DPI
        // state WPF is in, and the only anchor the crop needs.
        var windowRect = LiveWindowRect();
        if (windowRect != _physBounds)
            Log.Warn(
                "Capture window is not on its pinned rect: window={Window}, pinned={Pinned}, selection={Selection}",
                windowRect, _physBounds, Selection);

        if (CaptureCropMath.Crop(Selection, windowRect, _screenshot.Width, _screenshot.Height) is not
            { } region)
            return false;

        CroppedBitmap?.Dispose();
        CroppedBitmap = _screenshot.Clone(region, _screenshot.PixelFormat);
        // Back on screen as the region actually cropped, so the overlay paints over the pixels
        // that were really read — identical to Selection wherever the geometry is healthy.
        Selection = new Rect(
            windowRect.Left + region.X,
            windowRect.Top + region.Y,
            region.Width,
            region.Height);
        return true;
    }

    /// <summary>
    /// Every coordinate space the selection passed through, for the log an empty OCR result
    /// writes — the numbers that separate "framed the wrong pixels" from "the detector found
    /// nothing", which no amount of re-recognising can.
    /// </summary>
    public string SelectionDiagnostics() =>
        $"pinned={_physBounds} window={LiveWindowRect()} " +
        $"scale={LiveScaleX():0.###}x{LiveScaleY():0.###} initScale={_dpiX:0.###}x{_dpiY:0.###} " +
        $"wpf={_selectionWpfRect} selection={Selection} bitmap={_screenshot.Width}x{_screenshot.Height}";

    private void UpdateSelectionVisuals()
    {
        PlaceFrame(_selectionWpfRect);
        SetFrameVisibility(_hasSelection);

        System.Windows.Controls.Canvas.SetLeft(SelectionBody, _selectionWpfRect.X);
        System.Windows.Controls.Canvas.SetTop(SelectionBody,  _selectionWpfRect.Y);
        SelectionBody.Width  = _selectionWpfRect.Width;
        SelectionBody.Height = _selectionWpfRect.Height;

        const double halfHandle = 7;
        System.Windows.Controls.Canvas.SetLeft(TopLeftHandle, _selectionWpfRect.Left - halfHandle);
        System.Windows.Controls.Canvas.SetTop(TopLeftHandle, _selectionWpfRect.Top - halfHandle);
        System.Windows.Controls.Canvas.SetLeft(TopRightHandle, _selectionWpfRect.Right - halfHandle);
        System.Windows.Controls.Canvas.SetTop(TopRightHandle, _selectionWpfRect.Top - halfHandle);
        System.Windows.Controls.Canvas.SetLeft(BottomLeftHandle, _selectionWpfRect.Left - halfHandle);
        System.Windows.Controls.Canvas.SetTop(BottomLeftHandle, _selectionWpfRect.Bottom - halfHandle);
        System.Windows.Controls.Canvas.SetLeft(BottomRightHandle, _selectionWpfRect.Right - halfHandle);
        System.Windows.Controls.Canvas.SetTop(BottomRightHandle, _selectionWpfRect.Bottom - halfHandle);

        SetHandlesVisibility(_hasSelection && !_processingStarted);
        UpdateDimLayer();
    }

    private void SetHandlesVisibility(bool visible)
    {
        var handleVisibility = visible ? Visibility.Visible : Visibility.Collapsed;
        TopLeftHandle.Visibility = handleVisibility;
        TopRightHandle.Visibility = handleVisibility;
        BottomLeftHandle.Visibility = handleVisibility;
        BottomRightHandle.Visibility = handleVisibility;
        // Movable exactly as long as it is resizable: once translation has started the window goes
        // click-through and the box is no longer the user's to rearrange.
        SelectionBody.Visibility = handleVisibility;
    }

    /// <summary>
    /// Moves the whole selection, clamped to the desktop rather than rubber-banded — a part hanging
    /// off the edge would be a region the crop cannot include.
    /// </summary>
    private void SelectionBody_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_processingStarted || !_hasSelection) return;

        double x = Math.Clamp(
            _selectionWpfRect.X + e.HorizontalChange, 0, Math.Max(0, ActualWidth - _selectionWpfRect.Width));
        double y = Math.Clamp(
            _selectionWpfRect.Y + e.VerticalChange, 0, Math.Max(0, ActualHeight - _selectionWpfRect.Height));

        _selectionWpfRect = new Rect(x, y, _selectionWpfRect.Width, _selectionWpfRect.Height);
        UpdateSelectionMetadata();
        UpdateSelectionVisuals();
        SelectionAdjusted?.Invoke(this, Selection);
    }

    private void TopLeftHandle_DragDelta(object sender, DragDeltaEventArgs e) =>
        ResizeSelectionCorner(
            new WPoint(_selectionWpfRect.Right, _selectionWpfRect.Bottom),
            new WPoint(_selectionWpfRect.Left + e.HorizontalChange, _selectionWpfRect.Top + e.VerticalChange));

    private void TopRightHandle_DragDelta(object sender, DragDeltaEventArgs e) =>
        ResizeSelectionCorner(
            new WPoint(_selectionWpfRect.Left, _selectionWpfRect.Bottom),
            new WPoint(_selectionWpfRect.Right + e.HorizontalChange, _selectionWpfRect.Top + e.VerticalChange));

    private void BottomLeftHandle_DragDelta(object sender, DragDeltaEventArgs e) =>
        ResizeSelectionCorner(
            new WPoint(_selectionWpfRect.Right, _selectionWpfRect.Top),
            new WPoint(_selectionWpfRect.Left + e.HorizontalChange, _selectionWpfRect.Bottom + e.VerticalChange));

    private void BottomRightHandle_DragDelta(object sender, DragDeltaEventArgs e) =>
        ResizeSelectionCorner(
            new WPoint(_selectionWpfRect.Left, _selectionWpfRect.Top),
            new WPoint(_selectionWpfRect.Right + e.HorizontalChange, _selectionWpfRect.Bottom + e.VerticalChange));

    private void ResizeSelectionCorner(WPoint fixedPoint, WPoint movingPoint)
    {
        if (_processingStarted || !_hasSelection) return;

        movingPoint.X = Math.Clamp(movingPoint.X, 0, ActualWidth);
        movingPoint.Y = Math.Clamp(movingPoint.Y, 0, ActualHeight);

        const double minSize = 4;
        if (Math.Abs(movingPoint.X - fixedPoint.X) < minSize)
            movingPoint.X = fixedPoint.X + (movingPoint.X >= fixedPoint.X ? minSize : -minSize);
        if (Math.Abs(movingPoint.Y - fixedPoint.Y) < minSize)
            movingPoint.Y = fixedPoint.Y + (movingPoint.Y >= fixedPoint.Y ? minSize : -minSize);

        movingPoint.X = Math.Clamp(movingPoint.X, 0, ActualWidth);
        movingPoint.Y = Math.Clamp(movingPoint.Y, 0, ActualHeight);

        _selectionWpfRect = Normalize(movingPoint, fixedPoint);
        UpdateSelectionMetadata();
        UpdateSelectionVisuals();
        SelectionAdjusted?.Invoke(this, Selection);
    }

    private static Rect Normalize(WPoint a, WPoint b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
            Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));

    private static DoubleCollection Freeze(DoubleCollection dashes)
    {
        dashes.Freeze();
        return dashes;
    }

    private static BitmapSource BitmapToDisplaySource(Bitmap bmp, double dpi = 96) =>
        BitmapInterop.ToBitmapSource(bmp, dpi);
}
