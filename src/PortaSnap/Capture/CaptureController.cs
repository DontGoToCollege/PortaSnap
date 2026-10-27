using System.Windows;
using System.Windows.Media.Imaging;
using PortaSnap.Imaging;
using PortaSnap.Infrastructure;
using PortaSnap.Model;
using PortaSnap.Video;

namespace PortaSnap.Capture;

/// <summary>Runs the capture flows: hide panel → optional delay → freeze screen → select → capture → library/editor.</summary>
public sealed class CaptureController
{
    readonly App _app;
    bool _busy;

    public CaptureController(App app) => _app = app;

    public bool IsBusy => _busy || RecordingSession.Current != null;

    public async Task CaptureImageAsync(ImageCaptureType type)
    {
        if (_busy || RecordingSession.Current != null) return;
        _busy = true;
        bool panelWasVisible = false;
        try
        {
            var s = _app.Settings;
            panelWasVisible = _app.HideCapturePanelForCapture();
            await Task.Delay(panelWasVisible ? 220 : 40); // let DWM repaint without our panel
            if (s.DelaySeconds > 0) await CountdownWindow.RunAsync(s.DelaySeconds);

            var meta = new CaptureMetadata { CapturedAt = DateTime.Now, Source = "capture" };
            BitmapSource? image;
            if (type == ImageCaptureType.FullScreen)
            {
                var mon = Monitors.FromCursor();
                image = ScreenGrabber.Capture(mon, s.CaptureCursor);
                meta.WindowTitle = "Full screen";
            }
            else
            {
                var vs = Monitors.VirtualScreen;
                var frozen = ScreenGrabber.Capture(vs, s.CaptureCursor && type == ImageCaptureType.Region);
                // Our own windows stay selectable (e.g. capturing the editor); the overlay doesn't exist yet at this point.
                var windows = WindowEnumerator.GetWindows(excludeProcessId: 0);
                var overlay = new CaptureOverlay(frozen, vs, windows,
                    type == ImageCaptureType.Scrolling ? OverlayPurpose.Scrolling : OverlayPurpose.Image, s.ShowMagnifier);
                overlay.ShowDialog();
                var sel = overlay.Result;
                if (sel == null) return;
                if (sel.Window != null)
                {
                    meta.WindowTitle = sel.Window.Title;
                    meta.AppName = sel.Window.ProcessName;
                }
                if (type == ImageCaptureType.Scrolling)
                {
                    await Task.Delay(80);
                    image = await ScrollingCapturer.CaptureAsync(sel.Rect, sel.Window?.Handle ?? IntPtr.Zero);
                    meta.Source = "scrolling-capture";
                }
                else
                {
                    image = ImageUtil.Crop(frozen, new Int32Rect(sel.Rect.X - vs.X, sel.Rect.Y - vs.Y, sel.Rect.Width, sel.Rect.Height));
                }
            }
            if (image != null) _app.HandleNewCapture(image, meta);
        }
        catch (Exception ex)
        {
            Log.Error("Capture failed", ex);
            MessageBox.Show($"Capture failed:\n\n{ex.Message}", "PortaSnap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _busy = false;
            if (panelWasVisible) _app.RestoreCapturePanelAfterCapture();
        }
    }

    /// <summary>Starts a recording (after region selection), or stops the one in progress.</summary>
    public async Task ToggleVideoAsync()
    {
        if (RecordingSession.Current is { } current)
        {
            await current.StopAsync();
            return;
        }
        if (_busy) return;
        _busy = true;
        PixelRect? region = null;
        try
        {
            _app.HideCapturePanelForCapture();
            await Task.Delay(200);
            var vs = Monitors.VirtualScreen;
            var frozen = ScreenGrabber.Capture(vs, false);
            var windows = WindowEnumerator.GetWindows(excludeProcessId: 0);
            var overlay = new CaptureOverlay(frozen, vs, windows, OverlayPurpose.Video, _app.Settings.ShowMagnifier);
            overlay.ShowDialog();
            region = overlay.Result?.Rect;
        }
        catch (Exception ex)
        {
            Log.Error("Video region selection failed", ex);
        }
        finally
        {
            _busy = false;
        }
        if (region is PixelRect r && r.Width >= 16 && r.Height >= 16)
            await RecordingSession.StartAsync(_app, r);
    }
}
