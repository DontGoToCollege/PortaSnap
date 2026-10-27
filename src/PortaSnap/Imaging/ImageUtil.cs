using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PortaSnap.Imaging;

public static class ImageUtil
{
    /// <summary>Copies pixels into a new, frozen 96-DPI bitmap that does not reference its source.</summary>
    public static BitmapSource Detach(BitmapSource s)
    {
        var fmt = s.Format;
        if (fmt != PixelFormats.Bgra32 && fmt != PixelFormats.Bgr32 && fmt != PixelFormats.Pbgra32)
        {
            s = new FormatConvertedBitmap(s, PixelFormats.Bgra32, null, 0);
            fmt = PixelFormats.Bgra32;
        }
        int stride = s.PixelWidth * 4;
        var px = new byte[stride * s.PixelHeight];
        s.CopyPixels(px, stride, 0);
        var b = BitmapSource.Create(s.PixelWidth, s.PixelHeight, 96, 96, fmt, null, px, stride);
        b.Freeze();
        return b;
    }

    public static BitmapSource Crop(BitmapSource src, Int32Rect r)
    {
        r.X = Math.Clamp(r.X, 0, src.PixelWidth - 1);
        r.Y = Math.Clamp(r.Y, 0, src.PixelHeight - 1);
        r.Width = Math.Clamp(r.Width, 1, src.PixelWidth - r.X);
        r.Height = Math.Clamp(r.Height, 1, src.PixelHeight - r.Y);
        return Detach(new CroppedBitmap(src, r));
    }

    public static byte[] EncodePng(BitmapSource s)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(s));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    public static BitmapSource Decode(byte[] bytes, int decodeWidth = 0)
    {
        using var ms = new MemoryStream(bytes);
        if (decodeWidth > 0)
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.DecodePixelWidth = decodeWidth;
            bi.StreamSource = ms;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        var dec = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return Detach(dec.Frames[0]);
    }

    public static BitmapSource? TryDecode(byte[]? bytes)
    {
        if (bytes == null || bytes.Length < 8) return null;
        try { return Decode(bytes); } catch { return null; }
    }

    public static BitmapSource LoadFile(string path) => Decode(File.ReadAllBytes(path));

    public static bool LooksLikeImage(byte[] b) =>
        b.Length > 8 && ((b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) // PNG
                      || (b[0] == 0xFF && b[1] == 0xD8)                                   // JPEG
                      || (b[0] == 0x42 && b[1] == 0x4D)                                   // BMP
                      || (b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46));                 // GIF

    public static BitmapSource Thumbnail(BitmapSource s, int max)
    {
        double scale = Math.Min(1.0, (double)max / Math.Max(s.PixelWidth, s.PixelHeight));
        if (scale >= 1.0) return s;
        int w = Math.Max(1, (int)Math.Round(s.PixelWidth * scale)), h = Math.Max(1, (int)Math.Round(s.PixelHeight * scale));
        return Resample(s, w, h);
    }

    /// <summary>High-quality resize via WPF's renderer.</summary>
    public static BitmapSource Resample(BitmapSource s, int w, int h)
    {
        var dv = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
        using (var dc = dv.RenderOpen()) dc.DrawImage(s, new Rect(0, 0, w, h));
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    public static BitmapSource SolidColor(int w, int h, Color c)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) dc.DrawRectangle(new SolidColorBrush(c), null, new Rect(0, 0, w, h));
        var rtb = new RenderTargetBitmap(Math.Max(1, w), Math.Max(1, h), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    public static void Save(BitmapSource s, string path, int jpegQuality = 92)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        BitmapEncoder enc;
        switch (ext)
        {
            case ".jpg":
            case ".jpeg":
                enc = new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 10, 100) };
                s = Flatten(s, Colors.White);
                break;
            case ".bmp":
                enc = new BmpBitmapEncoder();
                s = Flatten(s, Colors.White);
                break;
            case ".gif": enc = new GifBitmapEncoder(); break;
            case ".tif":
            case ".tiff": enc = new TiffBitmapEncoder(); break;
            default: enc = new PngBitmapEncoder(); break;
        }
        enc.Frames.Add(BitmapFrame.Create(s));
        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp)) enc.Save(fs);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Composites over a solid colour (for formats without alpha).</summary>
    public static BitmapSource Flatten(BitmapSource s, Color background)
    {
        var dv = new DrawingVisual();
        var r = new Rect(0, 0, s.PixelWidth, s.PixelHeight);
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(background), null, r);
            dc.DrawImage(s, r);
        }
        var rtb = new RenderTargetBitmap(s.PixelWidth, s.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return new FormatConvertedBitmap(rtb, PixelFormats.Bgr32, null, 0);
    }

    public static BitmapSource FromPixels(byte[] px, int w, int h, PixelFormat fmt)
    {
        var b = BitmapSource.Create(w, h, 96, 96, fmt, null, px, w * 4);
        b.Freeze();
        return b;
    }

    public static BitmapSource FromPointer(IntPtr bits, int w, int h, int stride, PixelFormat fmt)
    {
        var px = new byte[stride * h];
        Marshal.Copy(bits, px, 0, px.Length);
        return FromPixels(px, w, h, fmt);
    }
}

/// <summary>CPU pixel effects used by the blur tool (deterministic, so screen and export match exactly).</summary>
public static class ImageFx
{
    static byte[] ReadBgra(BitmapSource src, Int32Rect area)
    {
        BitmapSource s = new CroppedBitmap(src, area);
        if (s.Format != PixelFormats.Bgra32) s = new FormatConvertedBitmap(s, PixelFormats.Bgra32, null, 0);
        var px = new byte[area.Width * area.Height * 4];
        s.CopyPixels(px, area.Width * 4, 0);
        // Screen captures often carry alpha=0 in Bgr32; treat everything as opaque.
        if (src.Format == PixelFormats.Bgr32) for (int i = 3; i < px.Length; i += 4) px[i] = 255;
        return px;
    }

    public static BitmapSource Pixelate(BitmapSource src, Int32Rect area, int block)
    {
        int w = area.Width, h = area.Height;
        var px = ReadBgra(src, area);
        for (int by = 0; by < h; by += block)
        {
            for (int bx = 0; bx < w; bx += block)
            {
                int bw = Math.Min(block, w - bx), bh = Math.Min(block, h - by);
                long b = 0, g = 0, r = 0, a = 0;
                for (int y = by; y < by + bh; y++)
                {
                    int row = y * w * 4;
                    for (int x = bx; x < bx + bw; x++)
                    {
                        int i = row + x * 4;
                        b += px[i]; g += px[i + 1]; r += px[i + 2]; a += px[i + 3];
                    }
                }
                int n = bw * bh;
                byte cb = (byte)(b / n), cg = (byte)(g / n), cr = (byte)(r / n), ca = (byte)(a / n);
                for (int y = by; y < by + bh; y++)
                {
                    int row = y * w * 4;
                    for (int x = bx; x < bx + bw; x++)
                    {
                        int i = row + x * 4;
                        px[i] = cb; px[i + 1] = cg; px[i + 2] = cr; px[i + 3] = ca;
                    }
                }
            }
        }
        return ImageUtil.FromPixels(px, w, h, PixelFormats.Bgra32);
    }

    /// <summary>
    /// Cuts the band [from, to) out of the image — columns for a vertical cut, rows for a horizontal one.
    /// With <paramref name="join"/> the remaining pieces are butted together (the image shrinks); without it the band
    /// becomes transparent and the pieces stay where they were.
    /// </summary>
    public static BitmapSource Slice(BitmapSource src, bool horizontal, int from, int to, bool join)
    {
        int w = src.PixelWidth, h = src.PixelHeight;
        var px = ReadBgra(src, new Int32Rect(0, 0, w, h));
        int stride = w * 4;
        if (!join)
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool inBand = horizontal ? y >= from && y < to : x >= from && x < to;
                    if (inBand) Array.Clear(px, y * stride + x * 4, 4);
                }
            }
            return ImageUtil.FromPixels(px, w, h, PixelFormats.Bgra32);
        }
        int cut = to - from;
        if (horizontal)
        {
            int nh = h - cut;
            var dst = new byte[stride * nh];
            Buffer.BlockCopy(px, 0, dst, 0, from * stride);
            Buffer.BlockCopy(px, to * stride, dst, from * stride, (h - to) * stride);
            return ImageUtil.FromPixels(dst, w, nh, PixelFormats.Bgra32);
        }
        else
        {
            int nw = w - cut, nstride = nw * 4;
            var dst = new byte[nstride * h];
            for (int y = 0; y < h; y++)
            {
                Buffer.BlockCopy(px, y * stride, dst, y * nstride, from * 4);
                Buffer.BlockCopy(px, y * stride + to * 4, dst, y * nstride + from * 4, (w - to) * 4);
            }
            return ImageUtil.FromPixels(dst, nw, h, PixelFormats.Bgra32);
        }
    }

    /// <summary>Approximate gaussian blur: three box-blur passes in each direction.</summary>
    public static BitmapSource Blur(BitmapSource src, Int32Rect area, int radius)
    {
        int w = area.Width, h = area.Height;
        var px = ReadBgra(src, area);
        var tmp = new byte[px.Length];
        int r = Math.Max(1, radius);
        for (int pass = 0; pass < 3; pass++)
        {
            BoxHorizontal(px, tmp, w, h, r);
            BoxVertical(tmp, px, w, h, r);
        }
        return ImageUtil.FromPixels(px, w, h, PixelFormats.Bgra32);
    }

    static void BoxHorizontal(byte[] src, byte[] dst, int w, int h, int r)
    {
        int window = r * 2 + 1;
        for (int y = 0; y < h; y++)
        {
            int row = y * w * 4;
            for (int c = 0; c < 4; c++)
            {
                int sum = 0;
                for (int k = -r; k <= r; k++) sum += src[row + Math.Clamp(k, 0, w - 1) * 4 + c];
                for (int x = 0; x < w; x++)
                {
                    dst[row + x * 4 + c] = (byte)(sum / window);
                    int add = Math.Min(x + r + 1, w - 1), sub = Math.Max(x - r, 0);
                    sum += src[row + add * 4 + c] - src[row + sub * 4 + c];
                }
            }
        }
    }

    static void BoxVertical(byte[] src, byte[] dst, int w, int h, int r)
    {
        int window = r * 2 + 1;
        int stride = w * 4;
        for (int x = 0; x < w; x++)
        {
            for (int c = 0; c < 4; c++)
            {
                int col = x * 4 + c;
                int sum = 0;
                for (int k = -r; k <= r; k++) sum += src[Math.Clamp(k, 0, h - 1) * stride + col];
                for (int y = 0; y < h; y++)
                {
                    dst[y * stride + col] = (byte)(sum / window);
                    int add = Math.Min(y + r + 1, h - 1), sub = Math.Max(y - r, 0);
                    sum += src[add * stride + col] - src[sub * stride + col];
                }
            }
        }
    }
}
