using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using PortaSnap.Infrastructure;

namespace PortaSnap;

public partial class SettingsWindow : Window
{
    readonly App _app;
    bool _loading = true;
    Button? _recording;

    public SettingsWindow(App app)
    {
        _app = app;
        InitializeComponent();
        var s = _app.Settings;
        HotkeysEnabled.IsChecked = s.HotkeysEnabled;
        StartInTray.IsChecked = s.StartInTray;
        StartWithWindows.IsChecked = s.StartWithWindows && StartupRegistration.IsRegistered();
        ThemeCombo.SelectedIndex = (int)s.Theme;
        JpegQuality.Value = s.JpegQuality;
        JpegValue.Text = s.JpegQuality.ToString();
        StorageText.Text = PortablePaths.IsFallback
            ? "The folder PortaSnap.exe is in isn't writable, so data is stored in your user profile instead. Move the exe to a writable folder (or USB stick) to make it fully portable."
            : "PortaSnap is portable: settings, your capture library and logs live in this folder next to PortaSnap.exe. Copy both together to move to another computer.";
        DataPath.Text = PortablePaths.DataDir;
        VersionText.Text = $"PortaSnap {typeof(App).Assembly.GetName().Version?.ToString(3)} · works offline · no installer";
        RefreshLabels();
        _loading = false;
        Closed += (_, _) => _app.Hook.CancelRecord();
    }

    void RefreshLabels()
    {
        var h = _app.Settings.Hotkeys;
        CaptureKey.Content = h.Capture?.ToString() ?? "Not set";
        FullScreenKey.Content = h.FullScreen?.ToString() ?? "Not set";
        ScrollingKey.Content = h.Scrolling?.ToString() ?? "Not set";
        VideoKey.Content = h.Video?.ToString() ?? "Not set";
        HotkeyGrid.IsEnabled = _app.Settings.HotkeysEnabled;
    }

    void Set(string which, HotkeyBinding? binding)
    {
        var h = _app.Settings.Hotkeys;
        switch (which)
        {
            case "Capture": h.Capture = binding; break;
            case "FullScreen": h.FullScreen = binding; break;
            case "Scrolling": h.Scrolling = binding; break;
            case "Video": h.Video = binding; break;
        }
        _app.SaveSettings();
        _app.RegisterHotkeys();
        _app.RefreshPanel();
    }

    void Record_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string which) return;
        _recording = b;
        b.Content = "Press keys…  (Esc to cancel)";
        _app.Hook.BeginRecord(binding =>
        {
            _recording = null;
            if (binding != null) Set(which, binding);
            RefreshLabels();
        });
    }

    void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string which })
        {
            Set(which, null);
            RefreshLabels();
        }
    }

    void HotkeysEnabled_Click(object sender, RoutedEventArgs e)
    {
        _app.Settings.HotkeysEnabled = HotkeysEnabled.IsChecked == true;
        _app.SaveSettings();
        _app.RegisterHotkeys();
        _app.RefreshPanel();
        RefreshLabels();
    }

    void General_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _app.Settings.StartInTray = StartInTray.IsChecked == true;
        _app.Settings.StartWithWindows = StartWithWindows.IsChecked == true;
        StartupRegistration.Set(_app.Settings.StartWithWindows);
        _app.SaveSettings();
    }

    void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeCombo.SelectedIndex < 0) return;
        _app.Settings.Theme = (AppTheme)ThemeCombo.SelectedIndex;
        _app.SaveSettings();
        ThemeManager.Apply(_app.Settings.Theme);
    }

    void Jpeg_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (JpegValue == null) return;
        JpegValue.Text = ((int)e.NewValue).ToString();
        if (_loading) return;
        _app.Settings.JpegQuality = (int)e.NewValue;
        _app.SaveSettings();
    }

    void OpenData_Click(object sender, RoutedEventArgs e) => App.OpenFolder(PortablePaths.DataDir);

    void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(PortablePaths.LogFile) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Open log failed", ex); }
    }

    void Done_Click(object sender, RoutedEventArgs e) => Close();
}
