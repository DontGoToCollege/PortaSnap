using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using PortaSnap.Infrastructure;

namespace PortaSnap.Video;

/// <summary>One screen recording: red frame around the region, floating toolbar, countdown, recorder lifecycle.</summary>
public sealed class RecordingSession
{
    readonly App _app;
    readonly PixelRect _region;
    readonly RecordingFrameWindow _frame;
    readonly RecordingToolbar _toolbar;
    readonly DispatcherTimer _timer;
    VideoRecorder? _recorder;
    bool _finishing;

    public static RecordingSession? Current { get; private set; }

    RecordingSession(App app, PixelRect region)
    {
        _app = app;
        _region = new PixelRect(region.X, region.Y, Math.Max(2, region.Width & ~1), Math.Max(2, region.Height & ~1));
        _frame = new RecordingFrameWindow(_region);
        _toolbar = new RecordingToolbar(this);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) =>
        {
            if (_recorder != null) _toolbar.SetTime(_recorder.Elapsed, _recorder.IsPaused);
        };
    }

    public static async Task StartAsync(App app, PixelRect region)
    {
        if (Current != null) return;
        var session = new RecordingSession(app, region);
        Current = session;
        try
        {
            await session.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Recording failed to start", ex);
            session.CloseWindows();
            Current = null;
            MessageBox.Show($"Recording could not start:\n\n{ex.Message}", "PortaSnap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    async Task RunAsync()
    {
        _frame.Show();
        _toolbar.Show();
        PositionToolbar();

        var s = _app.Settings;
        if (s.VideoCountdown) await _toolbar.CountdownAsync(3);
        if (_finishing) return; // cancelled during countdown

        var path = _app.Library.NewVideoPath(DateTime.Now);
        _recorder = new VideoRecorder(_region, s.VideoFps, s.VideoCursor, s.VideoMicrophone, path);
        await _recorder.StartAsync();
        Log.Info($"Recording {_region} at {s.VideoFps} fps to {path} (audio: {_recorder.HasAudio})");
        if (_recorder.MicrophoneError != null) _toolbar.ShowWarning("No mic: " + _recorder.MicrophoneError);
        _toolbar.SetRecording(_recorder.HasAudio);
        _timer.Start();
    }

    void PositionToolbar()
    {
        var work = Monitors.WorkAreaFromPoint(_region.X + _region.Width / 2, _region.Y + _region.Height / 2);
        var tb = WindowPlacement.GetPhysicalBounds(_toolbar);
        int x = Math.Clamp(_region.X + (_region.Width - tb.Width) / 2, work.X, Math.Max(work.X, work.Right - tb.Width));
        int y = _region.Bottom + 10;
        if (y + tb.Height > work.Bottom) y = _region.Y - tb.Height - 10;
        if (y < work.Y) y = _region.Bottom - tb.Height - 10;
        WindowPlacement.SetPhysicalPosition(_toolbar, x, y);
    }

    public void TogglePause()
    {
        if (_recorder == null) return;
        if (_recorder.IsPaused) _recorder.Resume(); else _recorder.Pause();
        _toolbar.SetTime(_recorder.Elapsed, _recorder.IsPaused);
    }

    public async Task StopAsync(bool discard = false)
    {
        if (_finishing) return;
        _finishing = true;
        _timer.Stop();
        _toolbar.SetSaving();
        string? saved = null;
        try
        {
            if (_recorder != null)
            {
                await _recorder.StopAsync();
                if (discard || _recorder.FramesWritten == 0)
                {
                    TryDelete(_recorder.OutputPath);
                }
                else
                {
                    saved = _recorder.OutputPath;
                    _app.Library.AddVideo(saved, _recorder.FirstFrame);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Stopping recording failed", ex);
            MessageBox.Show($"The recording could not be finished:\n\n{ex.Message}", "PortaSnap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            CloseWindows();
            Current = null;
        }
        if (saved != null) _app.OnVideoSaved(saved);
    }

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    void CloseWindows()
    {
        _timer.Stop();
        try { _frame.Close(); } catch { }
        try { _toolbar.Close(); } catch { }
    }
}

/// <summary>Click-through dashed frame drawn just outside the recorded area.</summary>
public sealed class RecordingFrameWindow : Window
{
    const int FrameMargin = 4;
    readonly PixelRect _region;

    public RecordingFrameWindow(PixelRect region)
    {
        _region = region;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;
        Content = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 2 },
        };
        SourceInitialized += (_, _) =>
        {
            var h = WindowPlacement.Handle(this);
            Native.MakeClickThrough(h);
            Native.ExcludeFromCapture(h);
            Place();
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Place);
    }

    void Place() => WindowPlacement.SetPhysicalBounds(this, new PixelRect(_region.X - FrameMargin, _region.Y - FrameMargin, _region.Width + FrameMargin * 2, _region.Height + FrameMargin * 2));
}

/// <summary>Floating recording controls: timer, pause/resume, stop, cancel.</summary>
public sealed class RecordingToolbar : Window
{
    readonly RecordingSession _session;
    readonly Ellipse _dot;
    readonly TextBlock _time;
    readonly TextBlock _status;
    readonly Button _pause;
    readonly TextBlock _pauseGlyph;

    public RecordingToolbar(RecordingSession session)
    {
        _session = session;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27));
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;
        Title = "PortaSnap recording";

        _dot = new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        _time = new TextBlock { Text = "Get ready…", Foreground = Brushes.White, FontSize = 15, FontFamily = new FontFamily("Segoe UI Semibold"), MinWidth = 90, VerticalAlignment = VerticalAlignment.Center };
        _status = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), MaxWidth = 260, TextTrimming = TextTrimming.CharacterEllipsis, Visibility = Visibility.Collapsed };

        _pauseGlyph = Glyph("");
        _pause = MakeButton(_pauseGlyph, "Pause / resume", (_, _) => _session.TogglePause());
        _pause.IsEnabled = false;
        var stop = MakeButton(Glyph("", Color.FromRgb(0xEF, 0x44, 0x44)), "Stop and save", async (_, _) => await _session.StopAsync());
        var cancel = MakeButton(Glyph(""), "Cancel (discard)", async (_, _) => await _session.StopAsync(discard: true));

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 6, 6, 6) };
        panel.Children.Add(_dot);
        panel.Children.Add(_time);
        panel.Children.Add(_pause);
        panel.Children.Add(stop);
        panel.Children.Add(cancel);
        panel.Children.Add(_status);
        Content = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)), BorderThickness = new Thickness(1), Child = panel };

        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        SourceInitialized += (_, _) => Native.ExcludeFromCapture(WindowPlacement.Handle(this));
    }

    static TextBlock Glyph(string glyph, Color? color = null) => new()
    {
        Text = glyph,
        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
        FontSize = 14,
        Foreground = new SolidColorBrush(color ?? Colors.White),
    };

    static Button MakeButton(UIElement content, string tip, RoutedEventHandler click)
    {
        var b = new Button
        {
            Content = content,
            ToolTip = tip,
            Width = 34,
            Height = 30,
            Margin = new Thickness(4, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)),
            Cursor = Cursors.Hand,
        };
        b.Click += click;
        return b;
    }

    public async Task CountdownAsync(int seconds)
    {
        for (int i = seconds; i > 0; i--)
        {
            _time.Text = $"Starting in {i}…";
            await Task.Delay(1000);
        }
    }

    public void SetRecording(bool audio)
    {
        _dot.Fill = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
        _pause.IsEnabled = true;
        _time.Text = "00:00";
        ToolTip = audio ? "Recording screen + microphone" : "Recording screen";
    }

    public void SetTime(TimeSpan t, bool paused)
    {
        _time.Text = (t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss")) + (paused ? "  paused" : "");
        _pauseGlyph.Text = paused ? "" : "";
        _dot.Opacity = paused ? 0.4 : 1;
    }

    public void SetSaving()
    {
        _time.Text = "Saving…";
        IsEnabled = false;
    }

    public void ShowWarning(string text)
    {
        _status.Text = text;
        _status.ToolTip = text;
        _status.Visibility = Visibility.Visible;
    }
}
