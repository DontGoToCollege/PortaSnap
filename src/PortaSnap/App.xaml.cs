using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PortaSnap.Capture;
using PortaSnap.Editor;
using PortaSnap.Imaging;
using PortaSnap.Import;
using PortaSnap.Infrastructure;
using PortaSnap.Library;
using PortaSnap.Model;
using PortaSnap.Video;

namespace PortaSnap;

public partial class App : Application
{
    TrayIcon? _tray;
    Mutex? _mutex;
    EventWaitHandle? _showSignal;
    volatile bool _exiting;
    bool _panelHiddenForCapture;
    MainWindow? _panel;
    EditorWindow? _editor;
    LibraryWindow? _library;
    SettingsWindow? _settingsWindow;

    public static new App Current => (App)Application.Current;

    public AppSettings Settings { get; private set; } = new();
    public LibraryStore Library { get; private set; } = null!;
    public CaptureController Capture { get; private set; } = null!;
    public KeyboardHook Hook { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (CommandLine.IsCommand(e.Args))
        {
            PortablePaths.Initialize();
            Log.Initialize(PortablePaths.LogFile);
            Dispatcher.InvokeAsync(async () => Shutdown(await CommandLine.RunAsync(e.Args)));
            return;
        }

        _mutex = new Mutex(true, @"Local\PortaSnap.SingleInstance", out bool first);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\PortaSnap.ShowPanel");
        var fileArg = e.Args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal) && File.Exists(a));
        if (!first)
        {
            // Hand a file to the running instance (e.g. double-clicked .snagx/.psnap) instead of starting a second copy.
            if (fileArg != null)
            {
                try { File.WriteAllText(OpenRequestFile, Path.GetFullPath(fileArg)); } catch { }
            }
            _showSignal.Set();
            Shutdown();
            return;
        }

        PortablePaths.Initialize();
        Log.Initialize(PortablePaths.LogFile);
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => Log.Error("Unhandled (domain)", ev.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ev) => { Log.Error("Unobserved task exception", ev.Exception); ev.SetObserved(); };

        Settings = SettingsStore.Load();
        SaveSettings(); // persists any settings migration
        ThemeManager.Initialize(this);
        if (Settings.StartWithWindows) StartupRegistration.Set(true); // keeps the entry pointing at wherever the exe lives now
        Library = new LibraryStore(PortablePaths.LibraryDir);
        Capture = new CaptureController(this);
        Hook = new KeyboardHook(Dispatcher);
        Hook.Install();
        RegisterHotkeys();
        _tray = new TrayIcon(this);

        var signal = _showSignal;
        new Thread(() =>
        {
            while (!_exiting && signal.WaitOne())
            {
                if (_exiting) break;
                Dispatcher.BeginInvoke(OnSecondInstance);
            }
        }) { IsBackground = true, Name = "PortaSnap instance signal" }.Start();

        if (PortablePaths.IsFallback) Log.Info("Exe folder is not writable; data stored in LocalAppData instead");

        if (fileArg != null) OpenFile(fileArg);
        else if (!Settings.StartInTray && !e.Args.Contains("--tray")) ShowCapturePanel();
    }

    static string OpenRequestFile => Path.Combine(Path.GetTempPath(), "PortaSnap-open-request.txt");

    void OnSecondInstance()
    {
        string? path = null;
        try
        {
            if (File.Exists(OpenRequestFile))
            {
                path = File.ReadAllText(OpenRequestFile).Trim();
                File.Delete(OpenRequestFile);
            }
        }
        catch { }
        if (!string.IsNullOrEmpty(path) && File.Exists(path)) OpenFile(path);
        else ShowCapturePanel();
    }

    /// <summary>Runs an async command from a menu/hotkey on the UI thread, logging failures.</summary>
    public void Run(Func<Task> action)
    {
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            try { await action(); }
            catch (Exception ex) { Log.Error("Command failed", ex); }
        }));
    }

    public void RegisterHotkeys()
    {
        Hook.Clear();
        if (!Settings.HotkeysEnabled) return;
        var h = Settings.Hotkeys;
        Hook.Register(h.Capture, () => Run(() => Capture.CaptureImageAsync(ImageCaptureType.Region)));
        Hook.Register(h.FullScreen, () => Run(() => Capture.CaptureImageAsync(ImageCaptureType.FullScreen)));
        Hook.Register(h.Scrolling, () => Run(() => Capture.CaptureImageAsync(ImageCaptureType.Scrolling)));
        Hook.Register(h.Video, () => Run(() => Capture.ToggleVideoAsync()));
    }

    public void SaveSettings() => SettingsStore.Save(Settings);

    public void RefreshPanel() => _panel?.RefreshHotkeyHint();

    // ---------------- windows ----------------

    public void ShowCapturePanel()
    {
        if (_exiting) return;
        if (_panel == null)
        {
            _panel = new MainWindow(this);
            _panel.Closed += (_, _) => _panel = null;
        }
        _panel.Show();
        if (_panel.WindowState == WindowState.Minimized) _panel.WindowState = WindowState.Normal;
        _panel.Activate();
    }

    public bool HideCapturePanelForCapture()
    {
        if (_panel is { IsVisible: true })
        {
            _panel.Hide();
            _panelHiddenForCapture = true;
            return true;
        }
        return false;
    }

    public void RestoreCapturePanelAfterCapture()
    {
        if (!_panelHiddenForCapture || _panel == null) return;
        _panelHiddenForCapture = false;
        _panel.ShowActivated = false;
        _panel.Show();
        _panel.ShowActivated = true;
    }

    public EditorWindow ShowEditor(SnapDocument? doc = null)
    {
        if (_editor == null)
        {
            _editor = new EditorWindow(this);
            _editor.Closed += (_, _) => _editor = null;
        }
        if (doc != null) _editor.OpenDocument(doc);
        else if (!_editor.HasDocument) _editor.OpenMostRecent();
        _editor.Show();
        if (_editor.WindowState == WindowState.Minimized) _editor.WindowState = WindowState.Normal;
        _editor.Activate();
        Native.ForceForeground(WindowPlacement.Handle(_editor));
        return _editor;
    }

    public void ShowLibrary()
    {
        if (_library == null)
        {
            _library = new LibraryWindow(this);
            _library.Closed += (_, _) => _library = null;
        }
        _library.Show();
        if (_library.WindowState == WindowState.Minimized) _library.WindowState = WindowState.Normal;
        _library.Activate();
    }

    public void ShowSettings()
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void ShowVideo(string path)
    {
        var w = new VideoPreviewWindow(this, path);
        w.Show();
        w.Activate();
    }

    // ---------------- captures & files ----------------

    public void HandleNewCapture(BitmapSource image, CaptureMetadata meta)
    {
        var doc = new SnapDocument(image) { Metadata = meta };
        try { Library.Save(doc); }
        catch (Exception ex) { Log.Error("Saving capture to library failed", ex); }

        if (Settings.CopyToClipboard) ClipboardUtil.CopyImage(image);
        if (Settings.OpenEditor) ShowEditor(doc);
        else _tray?.ShowBalloon("Capture saved", $"{image.PixelWidth} × {image.PixelHeight} — saved to the library" + (Settings.CopyToClipboard ? " and copied" : ""));
    }

    public void OnVideoSaved(string path)
    {
        _tray?.ShowBalloon("Recording saved", Path.GetFileName(path));
        ShowVideo(path);
    }

    /// <summary>Opens a .psnap, .snagx, image or video file.</summary>
    public void OpenFile(string path)
    {
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case SnapFile.Extension:
                    ShowEditor(SnapFile.Load(path));
                    break;
                case ".snagx":
                {
                    var result = SnagxImporter.Import(path);
                    Library.Save(result.Document);
                    ShowEditor(result.Document);
                    break;
                }
                case ".mp4":
                    ShowVideo(path);
                    break;
                default:
                {
                    var image = ImageUtil.LoadFile(path);
                    var doc = new SnapDocument(image)
                    {
                        Metadata = new CaptureMetadata { CapturedAt = DateTime.Now, WindowTitle = Path.GetFileName(path), Source = "file", SourcePath = path },
                    };
                    Library.Save(doc);
                    ShowEditor(doc);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Open failed: {path}", ex);
            MessageBox.Show($"Could not open {Path.GetFileName(path)}:\n\n{ex.Message}", "PortaSnap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public static void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Open folder failed", ex); }
    }

    public static void RevealFile(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Reveal failed", ex); }
    }

    public void ShowTrayHintOnce()
    {
        if (Settings.TrayHintShown) return;
        _tray?.ShowBalloon("PortaSnap is still running", "Press Print Screen to capture, or use the tray icon. Right-click it to exit.");
        Settings.TrayHintShown = true;
        SaveSettings();
    }

    public bool IsExiting => _exiting;

    public async void ExitApp()
    {
        if (_exiting) return;
        if (RecordingSession.Current is { } rec) await rec.StopAsync();
        _exiting = true;
        try { _editor?.SaveIfDirty(); } catch (Exception ex) { Log.Error("Autosave on exit failed", ex); }
        SaveSettings();
        Hook.Dispose();
        _tray?.Dispose();
        _tray = null;
        _showSignal?.Set();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Hook?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }

    void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception", e.Exception);
        MessageBox.Show($"Something went wrong:\n\n{e.Exception.Message}\n\nDetails were written to the log in the data folder.", "PortaSnap", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
