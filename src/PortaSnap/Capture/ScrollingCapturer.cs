using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PortaSnap.Imaging;
using PortaSnap.Infrastructure;

namespace PortaSnap.Capture;

/// <summary>
/// Scrolls the content under a selected region with synthetic mouse-wheel input and stitches the frames.
/// Overlap is found by matching per-row hashes, ignoring static headers/footers and the scrollbar column.
/// </summary>
public static class ScrollingCapturer
{
    /// <summary>WPF cannot display textures taller than this on most GPUs, so stop there.</summary>
    public const int MaxHeight = 15000;

    public static async Task<BitmapSource?> CaptureAsync(PixelRect region, IntPtr window)
    {
        var status = new ScrollStatusWindow();
        status.Show();
        var monitor = Monitors.WorkAreaFromPoint(region.X + region.Width / 2, region.Y + region.Height / 2);
        WindowPlacement.SetPhysicalPosition(status, monitor.X + (monitor.Width - (int)(status.ActualWidth * WindowPlacement.DpiScale(status))) / 2, monitor.Y + 16);

        Native.GetCursorPos(out var original);
        int cx = region.X + region.Width / 2, cy = region.Y + region.Height / 2;
        try
        {
            if (window != IntPtr.Zero) Native.ForceForeground(window);
            Native.SetCursorPos(cx, cy);
            await Task.Delay(250);

            using var grabber = new GdiFrameGrabber(region.Width, region.Height);
            grabber.Grab(region.X, region.Y, false, captureLayered: true);
            var stitcher = new Stitcher(region.Width, region.Height);
            stitcher.First(grabber.CopyPixels());
            int notches = Math.Max(1, region.Height / 360);
            int unchanged = 0;

            for (int i = 0; i < 400; i++)
            {
                if (status.StopRequested || Native.IsKeyDown(Native.VK_ESCAPE)) break;
                Native.SetCursorPos(cx, cy);
                Native.SendMouseWheel(-120 * notches);
                var frame = await WaitForStableFrameAsync(grabber, region);
                int added = stitcher.Append(frame);
                if (added == 0)
                {
                    if (++unchanged >= 2) break; // reached the end
                    continue;
                }
                unchanged = 0;
                if (added < 0)
                {
                    Log.Info("Scrolling capture: could not match the next frame; stopping.");
                    break;
                }
                status.SetProgress(stitcher.Height);
                if (stitcher.Height >= MaxHeight) break;
            }
            return stitcher.Build();
        }
        finally
        {
            Native.SetCursorPos(original.X, original.Y);
            status.Close();
        }
    }

    /// <summary>Waits for smooth-scrolling animations to settle: two identical grabs in a row (max ~1 s).</summary>
    static async Task<byte[]> WaitForStableFrameAsync(GdiFrameGrabber g, PixelRect region)
    {
        await Task.Delay(120);
        g.Grab(region.X, region.Y, false, captureLayered: true);
        var last = g.CopyPixels();
        for (int i = 0; i < 8; i++)
        {
            await Task.Delay(110);
            g.Grab(region.X, region.Y, false, captureLayered: true);
            var cur = g.CopyPixels();
            if (cur.AsSpan().SequenceEqual(last)) return cur;
            last = cur;
        }
        return last;
    }

    sealed class Stitcher
    {
        readonly int _w;
        readonly int _h;
        readonly int _stride;
        byte[] _buffer;
        ulong[] _prevHashes = Array.Empty<ulong>();

        public Stitcher(int w, int h)
        {
            _w = w;
            _h = h;
            _stride = w * 4;
            _buffer = new byte[_stride * h * 4];
        }

        public int Height { get; private set; }

        public void First(byte[] frame)
        {
            AppendRows(frame, 0, _h);
            _prevHashes = HashRows(frame, out _);
        }

        /// <summary>Returns rows added, 0 if nothing moved, -1 if no confident overlap was found.</summary>
        public int Append(byte[] cur)
        {
            var ch = HashRows(cur, out var uniform);
            var ph = _prevHashes;
            if (ch.AsSpan().SequenceEqual(ph)) return 0;

            int top = 0;
            while (top < _h && ch[top] == ph[top]) top++;
            int bottom = 0;
            while (bottom < _h - top && ch[_h - 1 - bottom] == ph[_h - 1 - bottom]) bottom++;
            int contentTop = top, contentBottom = _h - bottom;
            int contentHeight = contentBottom - contentTop;
            if (contentHeight < 16) return -1;

            int best = -1;
            double bestScore = 0;
            for (int s = 1; s < contentHeight - 8; s++)
            {
                int matches = 0, informative = 0, compared = 0;
                for (int i = contentTop; i + s < contentBottom; i++)
                {
                    compared++;
                    if (ph[i + s] == ch[i])
                    {
                        matches++;
                        if (!uniform[i]) informative++;
                    }
                }
                if (compared < 8 || informative < 4) continue;
                double score = (double)matches / compared;
                if (score > bestScore + 1e-9)
                {
                    bestScore = score;
                    best = s;
                }
            }
            if (best < 0 || bestScore < 0.85) return -1;

            // Drop the previous frame's static footer, then add the newly revealed rows and the footer again.
            Height -= bottom;
            AppendRows(cur, contentBottom - best, best + bottom);
            _prevHashes = ch;
            return best;
        }

        void AppendRows(byte[] src, int fromRow, int count)
        {
            if (count <= 0) return;
            int needed = (Height + count) * _stride;
            if (needed > _buffer.Length) Array.Resize(ref _buffer, Math.Max(needed, _buffer.Length * 2));
            Buffer.BlockCopy(src, fromRow * _stride, _buffer, Height * _stride, count * _stride);
            Height += count;
        }

        ulong[] HashRows(byte[] px, out bool[] uniform)
        {
            var hashes = new ulong[_h];
            uniform = new bool[_h];
            // Ignore the right-most columns, where scrollbar thumbs move between frames.
            int cols = Math.Max(1, _w - Math.Min(24, _w / 8));
            var ints = MemoryMarshal.Cast<byte, uint>(px.AsSpan());
            for (int y = 0; y < _h; y++)
            {
                var row = ints.Slice(y * _w, cols);
                ulong hash = 14695981039346656037UL;
                uint first = row[0];
                bool same = true;
                for (int x = 0; x < row.Length; x++)
                {
                    uint v = row[x] & 0x00FFFFFF;
                    hash = (hash ^ v) * 1099511628211UL;
                    if (same && v != (first & 0x00FFFFFF)) same = false;
                }
                hashes[y] = hash;
                uniform[y] = same;
            }
            return hashes;
        }

        public BitmapSource Build()
        {
            var bmp = BitmapSource.Create(_w, Height, 96, 96, PixelFormats.Bgr32, null, _buffer, _stride);
            bmp.Freeze();
            return bmp;
        }
    }
}

/// <summary>Small status bubble shown during scrolling capture.</summary>
public sealed class ScrollStatusWindow : Window
{
    readonly TextBlock _text;

    public ScrollStatusWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27));
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;

        _text = new TextBlock { Text = "Scrolling capture…", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), FontSize = 13 };
        var stop = new Button { Content = "Stop", Padding = new Thickness(12, 3, 12, 3) };
        stop.Click += (_, _) => StopRequested = true;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 8, 8, 8) };
        panel.Children.Add(_text);
        panel.Children.Add(stop);
        panel.Children.Add(new TextBlock { Text = "or press Esc", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0), FontSize = 12 });
        Content = panel;
        SourceInitialized += (_, _) => Native.ExcludeFromCapture(WindowPlacement.Handle(this));
    }

    public bool StopRequested { get; private set; }

    public void SetProgress(int height) => _text.Text = $"Scrolling capture… {height:N0} px";
}
