using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PortaSnap.Infrastructure;

namespace PortaSnap.Capture;

public enum OverlayPurpose { Image, Video, Scrolling }

public sealed class CaptureSelection
{
    public PixelRect Rect { get; init; }
    public WindowInfo? Window { get; init; }
}

/// <summary>
/// Full-desktop overlay over a frozen screenshot: hover highlights windows/panes, drag selects a region,
/// with crosshair, size badge and a pixel magnifier. Works in physical pixels across mixed-DPI monitors.
/// </summary>
public sealed class CaptureOverlay : Window
{
    readonly int LoupePixels;
    const double LoupeSize = 135;

    readonly BitmapSource _frozen;
    readonly PixelRect _vs;
    readonly List<WindowInfo> _windows;
    readonly OverlayPurpose _purpose;
    readonly bool _magnifier;

    readonly Canvas _root = new();
    readonly Image _image = new();
    readonly RectangleGeometry _dimOuter = new();
    readonly RectangleGeometry _dimHole = new();
    readonly Rectangle _selection;
    readonly Border _badge;
    readonly TextBlock _badgeText;
    readonly Line _hLine;
    readonly Line _vLine;
    readonly Border _loupe;
    readonly Image _loupeImage;
    readonly TextBlock _loupeText;
    readonly Border _hint;
    readonly byte[] _pixel = new byte[4];

    double _scale = 1;
    bool _mouseDown;
    bool _dragging;
    Native.POINT _down;
    PixelRect? _current;
    WindowInfo? _currentWindow;
    int _depth;

    public CaptureSelection? Result { get; private set; }

    public CaptureOverlay(BitmapSource frozen, PixelRect virtualScreen, List<WindowInfo> windows, OverlayPurpose purpose, bool magnifier, int magnifierPixels = 15)
    {
        // The loupe is always LoupeSize across; showing more pixels in it means less magnification.
        LoupePixels = Math.Clamp(magnifierPixels | 1, 7, 75);
        _frozen = frozen;
        _vs = virtualScreen;
        _windows = windows;
        _purpose = purpose;
        _magnifier = magnifier;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;
        Width = 200;
        Height = 200;
        Title = "PortaSnap capture";

        _image.Source = frozen;
        _image.Stretch = Stretch.Fill;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
        _root.Children.Add(_image);

        var dimGeometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        dimGeometry.Children.Add(_dimOuter);
        dimGeometry.Children.Add(_dimHole);
        _root.Children.Add(new System.Windows.Shapes.Path { Data = dimGeometry, Fill = new SolidColorBrush(Color.FromArgb(0x78, 0, 0, 0)), IsHitTestVisible = false });

        var accent = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
        _selection = new Rectangle { Stroke = accent, StrokeThickness = 2, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        _root.Children.Add(_selection);

        var guide = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
        _hLine = new Line { Stroke = guide, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false };
        _vLine = new Line { Stroke = guide, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false };
        _root.Children.Add(_hLine);
        _root.Children.Add(_vLine);

        _badgeText = new TextBlock { Foreground = Brushes.White, FontSize = 12, FontFamily = new FontFamily("Segoe UI") };
        _badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x11, 0x18, 0x27)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3, 6, 3),
            Child = _badgeText,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        _root.Children.Add(_badge);

        _loupeImage = new Image { Width = LoupeSize, Height = LoupeSize, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(_loupeImage, BitmapScalingMode.NearestNeighbor);
        double cell = LoupeSize / LoupePixels;
        var loupeGrid = new Grid { Width = LoupeSize, Height = LoupeSize };
        loupeGrid.Children.Add(_loupeImage);
        loupeGrid.Children.Add(new Rectangle
        {
            Width = cell + 2, Height = cell + 2, Stroke = Brushes.White, StrokeThickness = 1.5,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        _loupeText = new TextBlock { Foreground = Brushes.White, FontSize = 11, Margin = new Thickness(6, 3, 6, 4), FontFamily = new FontFamily("Consolas") };
        var loupeStack = new StackPanel();
        loupeStack.Children.Add(loupeGrid);
        loupeStack.Children.Add(_loupeText);
        _loupe = new Border
        {
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
            CornerRadius = new CornerRadius(6),
            Child = loupeStack,
            IsHitTestVisible = false,
            Visibility = magnifier ? Visibility.Visible : Visibility.Collapsed,
        };
        _root.Children.Add(_loupe);

        _hint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x11, 0x18, 0x27)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 8, 14, 8),
            IsHitTestVisible = false,
            Child = new TextBlock { Text = HintText(purpose), Foreground = Brushes.White, FontSize = 13, FontFamily = new FontFamily("Segoe UI") },
        };
        _root.Children.Add(_hint);

        Content = _root;

        SourceInitialized += (_, _) => ApplyBounds();
        Loaded += (_, _) =>
        {
            ApplyBounds();
            Native.ForceForeground(WindowPlacement.Handle(this));
            Activate();
            Focus();
            Keyboard.Focus(this);
            UpdateFromCursor();
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(ApplyBounds);
        MouseMove += (_, _) => UpdateFromCursor();
        MouseLeftButtonDown += OnMouseDown;
        MouseLeftButtonUp += OnMouseUp;
        MouseRightButtonUp += (_, _) => Close();
        MouseWheel += OnWheel;
        KeyDown += OnKeyDown;
    }

    static string HintText(OverlayPurpose p) => p switch
    {
        OverlayPurpose.Video => "Drag the area to record, or click a window   ·   F full screen   ·   Esc cancel",
        OverlayPurpose.Scrolling => "Click the window or pane to scroll-capture (or drag an area)   ·   Esc cancel",
        _ => "Drag to select an area   ·   Click to capture the highlighted window   ·   Scroll for smaller panes   ·   F full screen   ·   A all screens   ·   Esc cancel",
    };

    void ApplyBounds()
    {
        WindowPlacement.SetPhysicalBounds(this, _vs);
        _scale = WindowPlacement.DpiScale(this);
        double w = _vs.Width / _scale, h = _vs.Height / _scale;
        _root.Width = w;
        _root.Height = h;
        _image.Width = w;
        _image.Height = h;
        _dimOuter.Rect = new Rect(0, 0, w, h);
        _hLine.X1 = 0;
        _hLine.X2 = w;
        _vLine.Y1 = 0;
        _vLine.Y2 = h;
        PositionHint();
        UpdateFromCursor();
    }

    Point ToLocal(int x, int y) => new((x - _vs.X) / _scale, (y - _vs.Y) / _scale);

    void PositionHint()
    {
        var mon = Monitors.FromCursor();
        var top = ToLocal(mon.X + mon.Width / 2, mon.Y);
        _hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_hint, top.X - _hint.DesiredSize.Width / 2);
        Canvas.SetTop(_hint, top.Y + 24);
    }

    void UpdateFromCursor()
    {
        Native.GetCursorPos(out var p);
        var l = ToLocal(p.X, p.Y);
        double half = 0.5 / _scale;
        _hLine.Y1 = _hLine.Y2 = l.Y + half;
        _vLine.X1 = _vLine.X2 = l.X + half;

        PixelRect? rect;
        WindowInfo? win = null;
        if (_mouseDown)
        {
            if (!_dragging && (Math.Abs(p.X - _down.X) > 3 || Math.Abs(p.Y - _down.Y) > 3)) _dragging = true;
            if (_dragging) rect = DragRect(_down, p);
            else { rect = _current; win = _currentWindow; }
        }
        else
        {
            (rect, win) = HitTest(p.X, p.Y);
        }
        _current = rect;
        _currentWindow = _dragging ? null : win;
        ShowSelection(rect);
        UpdateLoupe(p, l);
    }

    PixelRect DragRect(Native.POINT a, Native.POINT b)
    {
        int l = Math.Min(a.X, b.X), t = Math.Min(a.Y, b.Y), r = Math.Max(a.X, b.X) + 1, bt = Math.Max(a.Y, b.Y) + 1;
        return PixelRect.FromLTRB(l, t, r, bt).Intersect(_vs);
    }

    (PixelRect?, WindowInfo?) HitTest(int x, int y)
    {
        foreach (var w in _windows)
        {
            if (!w.Bounds.Contains(x, y)) continue;
            if (_depth <= 0) return (w.Bounds, w);
            var chain = w.Children.Where(c => c.Contains(x, y)).Distinct().OrderByDescending(c => c.Area).ToList();
            if (chain.Count == 0) return (w.Bounds, w);
            return (chain[Math.Min(_depth, chain.Count) - 1], w);
        }
        return (Monitors.FromPoint(x, y).Intersect(_vs), null);
    }

    void ShowSelection(PixelRect? rect)
    {
        if (rect is not PixelRect r || r.IsEmpty)
        {
            _selection.Visibility = Visibility.Collapsed;
            _badge.Visibility = Visibility.Collapsed;
            _dimHole.Rect = Rect.Empty;
            return;
        }
        var tl = ToLocal(r.X, r.Y);
        var rr = new Rect(tl, new Size(r.Width / _scale, r.Height / _scale));
        _dimHole.Rect = rr;
        Canvas.SetLeft(_selection, rr.X);
        Canvas.SetTop(_selection, rr.Y);
        _selection.Width = rr.Width;
        _selection.Height = rr.Height;
        _selection.Visibility = Visibility.Visible;

        _badgeText.Text = $"{r.Width} × {r.Height}";
        _badge.Visibility = Visibility.Visible;
        _badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double by = rr.Y - _badge.DesiredSize.Height - 4;
        if (by < 0) by = rr.Y + 4;
        Canvas.SetLeft(_badge, rr.X + (rr.X < 4 ? 4 : 0));
        Canvas.SetTop(_badge, by);
    }

    void UpdateLoupe(Native.POINT p, Point local)
    {
        if (!_magnifier) return;
        int half = LoupePixels / 2;
        int cx = Math.Clamp(p.X - _vs.X, 0, _frozen.PixelWidth - 1);
        int cy = Math.Clamp(p.Y - _vs.Y, 0, _frozen.PixelHeight - 1);
        int sx = Math.Clamp(cx - half, 0, Math.Max(0, _frozen.PixelWidth - LoupePixels));
        int sy = Math.Clamp(cy - half, 0, Math.Max(0, _frozen.PixelHeight - LoupePixels));
        int sw = Math.Min(LoupePixels, _frozen.PixelWidth), sh = Math.Min(LoupePixels, _frozen.PixelHeight);
        _loupeImage.Source = new CroppedBitmap(_frozen, new Int32Rect(sx, sy, sw, sh));
        _frozen.CopyPixels(new Int32Rect(cx, cy, 1, 1), _pixel, 4, 0);
        _loupeText.Text = $"{p.X}, {p.Y}   #{_pixel[2]:X2}{_pixel[1]:X2}{_pixel[0]:X2}";

        _loupe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _loupe.DesiredSize;
        double x = local.X + 22, y = local.Y + 22;
        if (x + size.Width > _root.Width) x = local.X - 22 - size.Width;
        if (y + size.Height > _root.Height) y = local.Y - 22 - size.Height;
        Canvas.SetLeft(_loupe, x);
        Canvas.SetTop(_loupe, y);
    }

    void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _mouseDown = true;
        _dragging = false;
        Native.GetCursorPos(out _down);
        CaptureMouse();
    }

    void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_mouseDown) return;
        _mouseDown = false;
        ReleaseMouseCapture();
        Native.GetCursorPos(out var p);
        if (_dragging)
        {
            var r = DragRect(_down, p);
            if (r.Width >= 2 && r.Height >= 2) Finish(r, null);
            _dragging = false;
            UpdateFromCursor();
        }
        else if (_current is PixelRect r)
        {
            Finish(r, _currentWindow);
        }
    }

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (_mouseDown) return;
        _depth = Math.Clamp(_depth + (e.Delta < 0 ? 1 : -1), 0, 12);
        UpdateFromCursor();
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        Native.GetCursorPos(out var p);
        int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.F:
                Finish(Monitors.FromPoint(p.X, p.Y).Intersect(_vs), null);
                break;
            case Key.A when _purpose != OverlayPurpose.Scrolling:
                Finish(_vs, null);
                break;
            case Key.Enter:
                if (_current is PixelRect r) Finish(r, _currentWindow);
                break;
            case Key.Left: Native.SetCursorPos(p.X - step, p.Y); UpdateFromCursor(); break;
            case Key.Right: Native.SetCursorPos(p.X + step, p.Y); UpdateFromCursor(); break;
            case Key.Up: Native.SetCursorPos(p.X, p.Y - step); UpdateFromCursor(); break;
            case Key.Down: Native.SetCursorPos(p.X, p.Y + step); UpdateFromCursor(); break;
            default: return;
        }
        e.Handled = true;
    }

    void Finish(PixelRect r, WindowInfo? w)
    {
        if (r.IsEmpty) return;
        Result = new CaptureSelection { Rect = r, Window = w };
        Close();
    }
}
