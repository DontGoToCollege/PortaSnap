namespace PortaSnap.Infrastructure;

public static class Log
{
    static readonly object Gate = new();
    static string? _path;

    public static void Initialize(string path)
    {
        _path = path;
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024)
                File.Move(path, path + ".old", overwrite: true);
        }
        catch { }
        Info($"PortaSnap {typeof(Log).Assembly.GetName().Version} starting; data dir {Path.GetDirectoryName(path)}");
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", ex == null ? message : $"{message}: {ex}");

    static void Write(string level, string message)
    {
        if (_path == null) return;
        lock (Gate)
        {
            try { File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}"); }
            catch { }
        }
    }
}
