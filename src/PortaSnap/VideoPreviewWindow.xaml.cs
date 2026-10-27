using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PortaSnap.Imaging;
using PortaSnap.Infrastructure;
using PortaSnap.Model;

namespace PortaSnap;

/// <summary>Plays back a recording with basic controls; can grab the current frame into the editor.</summary>
public partial class VideoPreviewWindow : Window
{
    readonly App _app;
    readonly string _path;
    readonly DispatcherTimer _timer;
    bool _playing;
    bool _updatingSeek;

    public VideoPreviewWindow(App app, string path)
    {
        _app = app;
        _path = path;
        InitializeComponent();
        Title = $"{Path.GetFileName(path)} — PortaSnap";
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => UpdateTime();
        Player.Source = new Uri(path);
        Loaded += (_, _) => Play();
        Closed += (_, _) =>
        {
            _timer.Stop();
            Player.Close();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Space) { Play_Click(this, e); e.Handled = true; }
        };
    }

    void Play()
    {
        Player.Play();
        _playing = true;
        PlayGlyph.Text = "";
        _timer.Start();
    }

    void Pause()
    {
        Player.Pause();
        _playing = false;
        PlayGlyph.Text = "";
    }

    void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_playing) Pause(); else Play();
    }

    void Player_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (Player.NaturalDuration.HasTimeSpan) Seek.Maximum = Player.NaturalDuration.TimeSpan.TotalSeconds;
        UpdateTime();
    }

    void Player_MediaEnded(object sender, RoutedEventArgs e)
    {
        Pause();
        Player.Position = TimeSpan.Zero;
        UpdateTime();
    }

    void Player_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        Log.Error($"Video preview failed: {_path}", e.ErrorException);
        ErrorText.Text = "This recording can't be previewed here (Windows media playback components may be missing).\nThe file itself is saved — use Copy, Save as or Show in folder.";
        ErrorText.Visibility = Visibility.Visible;
    }

    void UpdateTime()
    {
        var pos = Player.Position;
        var dur = Player.NaturalDuration.HasTimeSpan ? Player.NaturalDuration.TimeSpan : TimeSpan.Zero;
        TimeText.Text = $"{pos:mm\\:ss} / {dur:mm\\:ss}";
        _updatingSeek = true;
        if (dur > TimeSpan.Zero) Seek.Maximum = dur.TotalSeconds;
        Seek.Value = pos.TotalSeconds;
        _updatingSeek = false;
    }

    void Seek_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSeek) return;
        Player.Position = TimeSpan.FromSeconds(e.NewValue);
        TimeText.Text = $"{Player.Position:mm\\:ss}";
    }

    void SaveFrame_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            int w = Player.NaturalVideoWidth, h = Player.NaturalVideoHeight;
            if (w <= 0 || h <= 0 || Player.ActualWidth <= 0) return;
            if (_playing) Pause();
            // Map the letterboxed video area of the element onto a full-resolution bitmap.
            double scale = Math.Min(Player.ActualWidth / w, Player.ActualHeight / h);
            double cw = w * scale, ch = h * scale;
            var content = new Rect((Player.ActualWidth - cw) / 2, (Player.ActualHeight - ch) / 2, cw, ch);
            var brush = new VisualBrush(Player) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = content, Stretch = Stretch.Fill };
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            _app.HandleNewCapture(ImageUtil.Detach(rtb), new CaptureMetadata
            {
                CapturedAt = DateTime.Now,
                WindowTitle = $"Frame {Player.Position:mm\\:ss} of {Path.GetFileName(_path)}",
                Source = "video-frame",
                SourcePath = _path,
            });
        }
        catch (Exception ex)
        {
            Log.Error("Frame grab failed", ex);
        }
    }

    void CopyFile_Click(object sender, RoutedEventArgs e) => ClipboardUtil.CopyFiles(_path);

    void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "MP4 video|*.mp4", FileName = Path.GetFileName(_path) };
        if (dlg.ShowDialog(this) != true) return;
        try { File.Copy(_path, dlg.FileName, overwrite: true); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "PortaSnap", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    void Reveal_Click(object sender, RoutedEventArgs e) => App.RevealFile(_path);
}
