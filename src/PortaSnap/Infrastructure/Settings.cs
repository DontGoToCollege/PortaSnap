using System.Text.Json;
using System.Windows.Input;
using PortaSnap.Model;

namespace PortaSnap.Infrastructure;

public enum ImageCaptureType { Region, FullScreen, Scrolling }

public enum AppTheme { System, Light, Dark }

/// <summary>A global hotkey: one virtual key plus modifiers.</summary>
public sealed record HotkeyBinding(int Key, bool Ctrl = false, bool Alt = false, bool Shift = false, bool Win = false)
{
    public override string ToString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join(" + ", parts);
    }

    static string KeyName(int vk)
    {
        if (vk == Native.VK_SNAPSHOT) return "Print Screen";
        var key = KeyInterop.KeyFromVirtualKey(vk);
        return key switch
        {
            System.Windows.Input.Key.Pause => "Pause",
            System.Windows.Input.Key.Scroll => "Scroll Lock",
            >= System.Windows.Input.Key.D0 and <= System.Windows.Input.Key.D9 => ((char)('0' + (key - System.Windows.Input.Key.D0))).ToString(),
            _ => key.ToString(),
        };
    }
}

public sealed class HotkeySettings
{
    public HotkeyBinding? Capture { get; set; } = new(Native.VK_SNAPSHOT);
    public HotkeyBinding? FullScreen { get; set; } = new(Native.VK_SNAPSHOT, Ctrl: true);
    public HotkeyBinding? Scrolling { get; set; } = new(Native.VK_SNAPSHOT, Ctrl: true, Shift: true);
    public HotkeyBinding? Video { get; set; } = new(Native.VK_SNAPSHOT, Shift: true);
}

public sealed class AppSettings
{
    public const int CurrentVersion = 3;

    public int Version { get; set; } = CurrentVersion;

    // Capture
    public HotkeySettings Hotkeys { get; set; } = new();
    public bool HotkeysEnabled { get; set; } = true;
    public ImageCaptureType ImageType { get; set; } = ImageCaptureType.Region;
    public int DelaySeconds { get; set; }
    public bool CaptureCursor { get; set; }
    public bool ShowMagnifier { get; set; } = true;
    /// <summary>The "Drag to select an area · …" help text at the top of the screen while capturing.</summary>
    public bool ShowCaptureHints { get; set; } = true;
    public bool CopyToClipboard { get; set; } = true;
    public bool OpenEditor { get; set; } = true;
    public bool PanelVideoMode { get; set; }

    // Video
    public int VideoFps { get; set; } = 30;
    public bool VideoCursor { get; set; } = true;
    public bool VideoMicrophone { get; set; }
    public bool VideoCountdown { get; set; } = true;

    // App
    public AppTheme Theme { get; set; } = AppTheme.System;
    public bool StartWithWindows { get; set; }
    public bool StartInTray { get; set; }

    /// <summary>Ask before moving a capture to the Recycle Bin (turned off by "Don't ask me again").</summary>
    public bool ConfirmDelete { get; set; } = true;
    public bool TrayHintShown { get; set; }

    /// <summary>Pixels across the capture magnifier (odd). Fewer pixels = more zoomed in; the loupe's size never changes.</summary>
    public int MagnifierPixels { get; set; } = 15;

    // Editor / export
    /// <summary>Zoom tag the editor opens with: "border" (fit to border), "fit", or a factor such as "1".</summary>
    public string EditorDefaultZoom { get; set; } = "border";
    public bool RecentNewestFirst { get; set; } = true;
    public bool SliceHorizontal { get; set; }
    public bool SliceSnap { get; set; } = true;
    public string? LastExportFolder { get; set; }
    public string ExportFormat { get; set; } = "jpg";
    /// <summary>JPEG quality (10–100) for library images and JPG exports.</summary>
    public int JpegQuality { get; set; } = 100;
    /// <summary>File type captures are saved as in the library: jpg, jpeg, png, bmp, gif or tif.</summary>
    public string LibraryImageFormat { get; set; } = "jpg";
    public Dictionary<ToolKind, ToolStyle> ToolStyles { get; set; } = ToolStyle.Defaults();

    // Import bookkeeping (source path|timestamp) so re-importing a Snagit folder skips files already brought in.
    public HashSet<string> ImportedSnagx { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public ToolStyle StyleFor(ToolKind kind)
    {
        if (!ToolStyles.TryGetValue(kind, out var s))
        {
            s = ToolStyle.Defaults().TryGetValue(kind, out var d) ? d : new ToolStyle();
            ToolStyles[kind] = s;
        }
        return s;
    }
}

public static class SettingsStore
{
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(PortablePaths.SettingsFile))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(PortablePaths.SettingsFile), SnapJson.Indented);
                if (s != null)
                {
                    s.Hotkeys ??= new HotkeySettings();
                    s.ToolStyles ??= ToolStyle.Defaults();
                    foreach (var kv in ToolStyle.Defaults())
                        s.ToolStyles.TryAdd(kv.Key, kv.Value);
                    s.ImportedSnagx = new HashSet<string>(s.ImportedSnagx ?? new(), StringComparer.OrdinalIgnoreCase);
                    if (s.Version < 2)
                    {
                        // v0.1 only had "start in tray", which people reasonably read as "start at sign-in".
                        s.StartWithWindows = s.StartInTray;
                        s.Version = 2;
                    }
                    if (s.Version < 3)
                    {
                        // 0.5: captures are saved as JPG at 100% by default and exports default to JPG.
                        if (s.JpegQuality == 92) s.JpegQuality = 100;
                        if (s.ExportFormat == "png") s.ExportFormat = "jpg";
                        s.Version = 3;
                    }
                    return s;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Settings load failed; using defaults", ex);
        }
        return new AppSettings();
    }

    public static void Save(AppSettings s)
    {
        try
        {
            var tmp = PortablePaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(s, SnapJson.Indented));
            File.Move(tmp, PortablePaths.SettingsFile, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("Settings save failed", ex);
        }
    }
}
