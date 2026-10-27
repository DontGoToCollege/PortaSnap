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
        ShowCaptureHints.IsChecked = s.ShowCaptureHints;
        FormatCombo.SelectedItem = FormatCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == s.LibraryImageFormat)
                                   ?? FormatCombo.Items[0];
        UpdateJpegRow();
        UpdateAssociation();
        MaxHeight = SystemParameters.WorkArea.Height * 0.95;
        ConfirmDelete.IsChecked = s.ConfirmDelete;
        StartWithWindows.IsChecked = s.StartWithWindows && StartupRegistration.IsRegistered();
        ThemeCombo.SelectedIndex = (int)s.Theme;
        MagnifierCombo.SelectedItem = MagnifierCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == s.MagnifierPixels.ToString())
                                      ?? MagnifierCombo.Items[0];
        JpegQuality.Value = s.JpegQuality;
        JpegValue.Text = $"{s.JpegQuality}%";
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
        _app.Settings.ShowCaptureHints = ShowCaptureHints.IsChecked == true;
        _app.Settings.ConfirmDelete = ConfirmDelete.IsChecked == true;
        _app.Settings.StartWithWindows = StartWithWindows.IsChecked == true;
        StartupRegistration.Set(_app.Settings.StartWithWindows);
        _app.SaveSettings();
    }

    void Magnifier_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || MagnifierCombo.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        _app.Settings.MagnifierPixels = int.Parse(tag);
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
        JpegValue.Text = $"{(int)e.NewValue}%";
        if (_loading) return;
        _app.Settings.JpegQuality = (int)e.NewValue;
        _app.ApplyLibrarySettings();
        _app.SaveSettings();
    }

    void Format_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (FormatCombo.SelectedItem is not ComboBoxItem { Tag: string fmt }) return;
        UpdateJpegRow();
        if (_loading) return;
        _app.Settings.LibraryImageFormat = fmt;
        _app.ApplyLibrarySettings();
        _app.SaveSettings();
    }

    /// <summary>The quality slider only matters for JPG/JPEG.</summary>
    void UpdateJpegRow()
    {
        if (JpegRow == null || FormatCombo == null) return;
        bool jpeg = FormatCombo.SelectedItem is ComboBoxItem { Tag: "jpg" or "jpeg" };
        JpegRow.IsEnabled = jpeg;
        JpegRow.Opacity = jpeg ? 1 : 0.45;
    }

    void UpdateAssociation()
    {
        bool on = FileAssociation.IsRegistered();
        AssociationText.Text = !on
            ? ".psnap files aren't associated with PortaSnap yet."
            : FileAssociation.IsMachineWide
                ? "✓ .psnap files open in PortaSnap and show its icon. Click the button again to re-point them at this copy."
                : "✓ Registered for your account. If Explorer still asks which app to use, run PortaSnap as administrator once and click the button again.";
        RemoveAssociation.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    void CreateAssociation_Click(object sender, RoutedEventArgs e)
    {
        try { FileAssociation.Register(); }
        catch (Exception ex)
        {
            Log.Error("Creating the file association failed", ex);
            MessageBox.Show(this, "Couldn't create the association:\n\n" + ex.Message, "PortaSnap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        UpdateAssociation();
    }

    void RemoveAssociation_Click(object sender, RoutedEventArgs e)
    {
        FileAssociation.Unregister();
        UpdateAssociation();
    }

    void OpenData_Click(object sender, RoutedEventArgs e) => App.OpenFolder(PortablePaths.DataDir);

    void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(PortablePaths.LogFile) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Open log failed", ex); }
    }

    void Done_Click(object sender, RoutedEventArgs e) => Close();
}
