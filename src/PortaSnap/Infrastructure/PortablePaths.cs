namespace PortaSnap.Infrastructure;

/// <summary>
/// Everything PortaSnap writes lives in a "PortaSnap-Data" folder next to the exe, so the app can be carried
/// between computers on a USB stick or synced folder. Falls back to %LocalAppData% only if the exe folder is read-only.
/// </summary>
public static class PortablePaths
{
    public const string DataFolderName = "PortaSnap-Data";

    public static string AppDir { get; } = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
    public static string DataDir { get; private set; } = Path.Combine(AppDir, DataFolderName);
    public static bool IsFallback { get; private set; }

    public static string LibraryDir => Path.Combine(DataDir, "Library");
    public static string ThumbDir => Path.Combine(LibraryDir, ".thumbs");
    public static string TempDir => Path.Combine(DataDir, "Temp");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string LogFile => Path.Combine(DataDir, "portasnap.log");

    public static void Initialize()
    {
        var preferred = Path.Combine(AppDir, DataFolderName);
        if (TryPrepare(preferred))
        {
            DataDir = preferred;
        }
        else
        {
            DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PortaSnap");
            IsFallback = true;
            TryPrepare(DataDir);
        }
        Directory.CreateDirectory(LibraryDir);
        Directory.CreateDirectory(ThumbDir);
        Directory.CreateDirectory(TempDir);
        try { File.SetAttributes(ThumbDir, File.GetAttributes(ThumbDir) | FileAttributes.Hidden); } catch { }
        CleanTemp();
    }

    static bool TryPrepare(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static void CleanTemp()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(TempDir))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-1)) File.Delete(f);
        }
        catch { }
    }
}
