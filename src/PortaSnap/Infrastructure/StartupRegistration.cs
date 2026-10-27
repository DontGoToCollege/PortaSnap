using Microsoft.Win32;

namespace PortaSnap.Infrastructure;

/// <summary>
/// "Start with Windows" via the per-user Run key (no admin rights needed). The entry launches PortaSnap
/// with --tray so it starts quietly. Because the exe is portable, the entry is re-pointed at the current
/// exe location on every launch while the option is on.
/// </summary>
public static class StartupRegistration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "PortaSnap";

    static string Command => $"\"{Environment.ProcessPath}\" --tray";

    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string s && s.Length > 0;
        }
        catch { return false; }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
            {
                if (!string.Equals(key.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase))
                {
                    key.SetValue(ValueName, Command);
                    Log.Info($"Startup entry set: {Command}");
                }
            }
            else if (key.GetValue(ValueName) != null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Log.Info("Startup entry removed");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Updating the Windows startup entry failed", ex);
        }
    }
}
