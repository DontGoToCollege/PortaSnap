using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PortaSnap.Imaging;
using PortaSnap.Infrastructure;

namespace PortaSnap.Capture;

public static class ScreenGrabber
{
    /// <summary>Captures a physical-pixel rectangle of the desktop.</summary>
    public static BitmapSource Capture(PixelRect r, bool includeCursor)
    {
        using var g = new GdiFrameGrabber(r.Width, r.Height);
        g.Grab(r.X, r.Y, includeCursor, captureLayered: true);
        return g.ToBitmapSource();
    }
}

/// <summary>
/// Reusable GDI capture surface (a 32-bit DIB section). Used for stills, scrolling capture and video frames.
/// </summary>
public sealed class GdiFrameGrabber : IDisposable
{
    readonly IntPtr _screenDc;
    readonly IntPtr _memDc;
    readonly IntPtr _bitmap;
    readonly IntPtr _old;

    public GdiFrameGrabber(int width, int height, bool topDown = true)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Stride = Width * 4;
        _screenDc = Native.GetDC(IntPtr.Zero);
        _memDc = Native.CreateCompatibleDC(_screenDc);
        var bmi = new Native.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
            biWidth = Width,
            biHeight = topDown ? -Height : Height,
            biPlanes = 1,
            biBitCount = 32,
        };
        _bitmap = Native.CreateDIBSection(_memDc, ref bmi, 0, out var bits, IntPtr.Zero, 0);
        if (_bitmap == IntPtr.Zero) throw new InvalidOperationException($"Could not allocate a {Width}x{Height} capture surface.");
        Bits = bits;
        _old = Native.SelectObject(_memDc, _bitmap);
    }

    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public IntPtr Bits { get; }
    public int ByteCount => Stride * Height;

    /// <param name="captureLayered">CAPTUREBLT includes layered windows but can make the cursor flicker, so video skips it.</param>
    public bool Grab(int x, int y, bool includeCursor, bool captureLayered)
    {
        int rop = Native.SRCCOPY | (captureLayered ? Native.CAPTUREBLT : 0);
        bool ok = Native.BitBlt(_memDc, 0, 0, Width, Height, _screenDc, x, y, rop);
        if (includeCursor) DrawCursor(x, y);
        return ok;
    }

    void DrawCursor(int originX, int originY)
    {
        var ci = new Native.CURSORINFO { cbSize = Marshal.SizeOf<Native.CURSORINFO>() };
        if (!Native.GetCursorInfo(ref ci) || (ci.flags & Native.CURSOR_SHOWING) == 0 || ci.hCursor == IntPtr.Zero) return;
        int hx = 0, hy = 0;
        if (Native.GetIconInfo(ci.hCursor, out var ii))
        {
            hx = ii.xHotspot;
            hy = ii.yHotspot;
            if (ii.hbmMask != IntPtr.Zero) Native.DeleteObject(ii.hbmMask);
            if (ii.hbmColor != IntPtr.Zero) Native.DeleteObject(ii.hbmColor);
        }
        Native.DrawIconEx(_memDc, ci.ptScreenPos.X - hx - originX, ci.ptScreenPos.Y - hy - originY, ci.hCursor, 0, 0, 0, IntPtr.Zero, Native.DI_NORMAL);
    }

    public BitmapSource ToBitmapSource() => ImageUtil.FromPointer(Bits, Width, Height, Stride, PixelFormats.Bgr32);

    public byte[] CopyPixels()
    {
        var px = new byte[ByteCount];
        Marshal.Copy(Bits, px, 0, px.Length);
        return px;
    }

    public void CopyTo(byte[] dest) => Marshal.Copy(Bits, dest, 0, ByteCount);

    public void Dispose()
    {
        Native.SelectObject(_memDc, _old);
        Native.DeleteObject(_bitmap);
        Native.DeleteDC(_memDc);
        Native.ReleaseDC(IntPtr.Zero, _screenDc);
    }
}
