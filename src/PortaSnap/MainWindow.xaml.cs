using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using PortaSnap.Infrastructure;

namespace PortaSnap;

/// <summary>The capture panel (Snagit's "capture window"): choose image/video, options, and capture.</summary>
public partial class MainWindow : Window
{
    readonly App _app;
    bool _loading;

    public MainWindow(App app)
    {
        _app = app;
        InitializeComponent();
        LoadOptions();
        SubtitleText.Text = PortablePaths.IsFallback
            ? "Data: " + PortablePaths.DataDir
            : $"v{typeof(App).Assembly.GetName().Version?.ToString(3)} · portable · data next to the exe";
    }

    void LoadOptions()
    {
        _loading = true;
        var s = _app.Settings;
        ImageMode.IsChecked = !s.PanelVideoMode;
        VideoMode.IsChecked = s.PanelVideoMode;
        SelectionCombo.SelectedIndex = (int)s.ImageType;
        DelayCombo.SelectedIndex = s.DelaySeconds switch { 3 => 1, 5 => 2, 10 => 3, _ => 0 };
        CursorCheck.IsChecked = s.CaptureCursor;
        MagnifierCheck.IsChecked = s.ShowMagnifier;
        ClipboardCheck.IsChecked = s.CopyToClipboard;
        EditorCheck.IsChecked = s.OpenEditor;
        FpsCombo.SelectedIndex = s.VideoFps switch { 15 => 0, 24 => 1, 60 => 3, _ => 2 };
        MicCheck.IsChecked = s.VideoMicrophone;
        VideoCursorCheck.IsChecked = s.VideoCursor;
        CountdownCheck.IsChecked = s.VideoCountdown;
        _loading = false;
        UpdateMode();
    }

    void UpdateMode()
    {
        bool video = VideoMode.IsChecked == true;
        ImageOptions.Visibility = video ? Visibility.Collapsed : Visibility.Visible;
        VideoOptions.Visibility = video ? Visibility.Visible : Visibility.Collapsed;
        CaptureButtonText.Text = video ? "Record" : "Capture";
        var h = _app.Settings.Hotkeys;
        if (!_app.Settings.HotkeysEnabled)
        {
            HotkeyHint.Text = "Global hotkeys are off (Settings)";
        }
        else if (video)
        {
            HotkeyHint.Text = $"Hotkey: {h.Video?.ToString() ?? "none"}  ·  press again to stop";
        }
        else
        {
            HotkeyHint.Text = $"{h.Capture?.ToString() ?? "—"}  region   ·   {h.FullScreen?.ToString() ?? "—"}  full screen\n{h.Scrolling?.ToString() ?? "—"}  scrolling";
        }
    }

    public void RefreshHotkeyHint() => UpdateMode();

    void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || _loading) return;
        _app.Settings.PanelVideoMode = VideoMode.IsChecked == true;
        _app.SaveSettings();
        UpdateMode();
    }

    void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || _loading) return;
        var s = _app.Settings;
        s.ImageType = (ImageCaptureType)Math.Max(0, SelectionCombo.SelectedIndex);
        s.DelaySeconds = DelayCombo.SelectedItem is ComboBoxItem { Tag: string d } ? int.Parse(d) : 0;
        s.CaptureCursor = CursorCheck.IsChecked == true;
        s.ShowMagnifier = MagnifierCheck.IsChecked == true;
        s.CopyToClipboard = ClipboardCheck.IsChecked == true;
        s.OpenEditor = EditorCheck.IsChecked == true;
        s.VideoFps = FpsCombo.SelectedItem is ComboBoxItem { Tag: string f } ? int.Parse(f) : 30;
        s.VideoMicrophone = MicCheck.IsChecked == true;
        s.VideoCursor = VideoCursorCheck.IsChecked == true;
        s.VideoCountdown = CountdownCheck.IsChecked == true;
        _app.SaveSettings();
    }

    void Capture_Click(object sender, RoutedEventArgs e)
    {
        if (VideoMode.IsChecked == true) _app.Run(() => _app.Capture.ToggleVideoAsync());
        else _app.Run(() => _app.Capture.CaptureImageAsync(_app.Settings.ImageType));
    }

    void Editor_Click(object sender, RoutedEventArgs e) => _app.ShowEditor();
    void Library_Click(object sender, RoutedEventArgs e) => _app.ShowLibrary();
    void Settings_Click(object sender, RoutedEventArgs e) => _app.ShowSettings();

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing the panel keeps PortaSnap in the tray so hotkeys keep working.
        if (!_app.IsExiting)
        {
            e.Cancel = true;
            Hide();
            _app.ShowTrayHintOnce();
        }
        base.OnClosing(e);
    }
}
