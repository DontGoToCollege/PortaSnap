using System.Diagnostics;
using PortaSnap.Infrastructure;

namespace PortaSnap.Capture;

public sealed record WindowInfo(IntPtr Handle, PixelRect Bounds, string Title, int ProcessId, List<PixelRect> Children)
{
    public string? ProcessName
    {
        get
        {
            try
            {
                using var p = Process.GetProcessById(ProcessId);
                try { return p.MainModule?.FileVersionInfo.ProductName is { Length: > 0 } n ? n : p.ProcessName; }
                catch { return p.ProcessName; }
            }
            catch { return null; }
        }
    }
}

/// <summary>Snapshot of visible top-level windows (front to back) and their child panes, for hover-to-select capture.</summary>
public static class WindowEnumerator
{
    const int MaxChildrenPerWindow = 400;

    public static List<WindowInfo> GetWindows(int excludeProcessId)
    {
        var screen = Monitors.VirtualScreen;
        var list = new List<WindowInfo>();
        Native.EnumWindows((h, lParam) =>
        {
            try
            {
                if (!Native.IsWindowVisible(h) || Native.IsIconic(h) || Native.IsCloaked(h)) return true;
                Native.GetWindowThreadProcessId(h, out uint pid);
                if (pid == excludeProcessId) return true;
                var cls = Native.GetClassName(h);
                if (cls is "Progman" or "WorkerW") return true;

                long ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
                if ((ex & Native.WS_EX_TRANSPARENT) != 0 && (ex & Native.WS_EX_LAYERED) != 0) return true; // click-through overlays
                if ((ex & Native.WS_EX_LAYERED) != 0 && Native.GetLayeredWindowAttributes(h, out _, out byte alpha, out uint flags)
                    && (flags & Native.LWA_ALPHA) != 0 && alpha == 0) return true; // fully transparent

                var bounds = Native.GetWindowBounds(h).Intersect(screen);
                if (bounds.Width < 8 || bounds.Height < 8) return true;

                var children = new List<PixelRect>();
                Native.EnumChildWindows(h, (c, _) =>
                {
                    if (Native.IsWindowVisible(c) && Native.GetWindowRect(c, out var cr))
                    {
                        var r = PixelRect.FromLTRB(cr.Left, cr.Top, cr.Right, cr.Bottom).Intersect(bounds);
                        if (r.Width >= 16 && r.Height >= 16 && r != bounds) children.Add(r);
                    }
                    return children.Count < MaxChildrenPerWindow;
                }, IntPtr.Zero);

                list.Add(new WindowInfo(h, bounds, Native.GetWindowTitle(h), (int)pid, children));
            }
            catch (Exception ex)
            {
                Log.Error("Window enumeration entry failed", ex);
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
