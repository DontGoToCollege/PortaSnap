using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace PortaSnap.Infrastructure;

/// <summary>A rectangle in physical screen pixels.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public long Area => (long)Math.Max(0, Width) * Math.Max(0, Height);

    public static PixelRect FromLTRB(int l, int t, int r, int b) => new(l, t, r - l, b - t);

    public bool Contains(int x, int y) => x >= X && y >= Y && x < Right && y < Bottom;

    public PixelRect Intersect(PixelRect o)
    {
        int l = Math.Max(X, o.X), t = Math.Max(Y, o.Y), r = Math.Min(Right, o.Right), b = Math.Min(Bottom, o.Bottom);
        return r <= l || b <= t ? default : FromLTRB(l, t, r, b);
    }

    public PixelRect Offset(int dx, int dy) => new(X + dx, Y + dy, Width, Height);

    public override string ToString() => $"{X},{Y} {Width}x{Height}";
}

public static class Monitors
{
    public static PixelRect VirtualScreen => new(
        Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN), Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN),
        Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN), Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN));

    public static PixelRect FromPoint(int x, int y)
    {
        var mon = Native.MonitorFromPoint(new Native.POINT(x, y), Native.MONITOR_DEFAULTTONEAREST);
        var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        Native.GetMonitorInfo(mon, ref info);
        var r = info.rcMonitor;
        return PixelRect.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    public static PixelRect WorkAreaFromPoint(int x, int y)
    {
        var mon = Native.MonitorFromPoint(new Native.POINT(x, y), Native.MONITOR_DEFAULTTONEAREST);
        var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        Native.GetMonitorInfo(mon, ref info);
        var r = info.rcWork;
        return PixelRect.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    public static PixelRect FromCursor()
    {
        Native.GetCursorPos(out var p);
        return FromPoint(p.X, p.Y);
    }

    public static List<PixelRect> All()
    {
        var list = new List<PixelRect>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr dc, ref Native.RECT r, IntPtr d) =>
        {
            list.Add(PixelRect.FromLTRB(r.Left, r.Top, r.Right, r.Bottom));
            return true;
        }, IntPtr.Zero);
        return list;
    }
}

/// <summary>Positions WPF windows in physical pixels, which WPF's DIP-based Left/Top cannot do reliably across mixed-DPI monitors.</summary>
public static class WindowPlacement
{
    public static IntPtr Handle(Window w) => new WindowInteropHelper(w).EnsureHandle();

    public static void SetPhysicalBounds(Window w, PixelRect r, bool topmost = true)
    {
        var hwnd = Handle(w);
        Native.SetWindowPos(hwnd, topmost ? Native.HWND_TOPMOST : IntPtr.Zero, r.X, r.Y, r.Width, r.Height,
            Native.SWP_NOACTIVATE | (topmost ? 0 : Native.SWP_NOZORDER));
    }

    public static void SetPhysicalPosition(Window w, int x, int y, bool topmost = true)
    {
        var hwnd = Handle(w);
        Native.SetWindowPos(hwnd, topmost ? Native.HWND_TOPMOST : IntPtr.Zero, x, y, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | (topmost ? 0 : Native.SWP_NOZORDER));
    }

    public static PixelRect GetPhysicalBounds(Window w)
    {
        Native.GetWindowRect(Handle(w), out var r);
        return PixelRect.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    /// <summary>Centres an already-shown window on the given monitor rectangle.</summary>
    public static void CenterOn(Window w, PixelRect area, bool topmost = true)
    {
        var b = GetPhysicalBounds(w);
        SetPhysicalPosition(w, area.X + (area.Width - b.Width) / 2, area.Y + (area.Height - b.Height) / 2, topmost);
    }

    public static double DpiScale(Visual v) => VisualTreeHelper.GetDpi(v).DpiScaleX;
}
