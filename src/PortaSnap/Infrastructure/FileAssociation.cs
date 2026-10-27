using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PortaSnap.Infrastructure;

/// <summary>
/// ".psnap opens in PortaSnap" association with the PortaSnap icon.
///
/// Written to the per-user classes (HKCU\Software\Classes) and, when Windows allows it, to the machine classes
/// (HKLM\Software\Classes) as well. The machine copy matters: when Explorer runs with an admin token (e.g. UAC
/// turned off) it ignores per-user class registrations entirely and would keep asking which app to use.
/// Because the exe is portable, an existing association is re-pointed at the current exe on each launch.
/// </summary>
public static class FileAssociation
{
    const string Extension = ".psnap";
    const string ProgId = "PortaSnap.Project";

    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    static string Exe => Environment.ProcessPath ?? "PortaSnap.exe";
    static string OpenCommand => $"\"{Exe}\" \"%1\"";
    static string IconValue => $"\"{Exe}\",0";

    /// <summary>Where the last registration landed, for the Settings text.</summary>
    public static string Scope { get; private set; } = "";

    public static bool IsRegistered() => IsRegisteredIn(Registry.CurrentUser) || IsRegisteredIn(Registry.LocalMachine);

    static bool IsRegisteredIn(RegistryKey root)
    {
        try
        {
            using var ext = root.OpenSubKey($@"Software\Classes\{Extension}");
            return string.Equals(ext?.GetValue("") as string, ProgId, StringComparison.Ordinal);
        }
        catch { return false; }
    }

    public static bool IsMachineWide => IsRegisteredIn(Registry.LocalMachine);

    /// <summary>Registers per user, and machine-wide when permitted. Returns true if the machine-wide copy was written.</summary>
    public static bool Register()
    {
        WriteTo(Registry.CurrentUser);
        bool machine = false;
        try
        {
            WriteTo(Registry.LocalMachine);
            machine = true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Info("Machine-wide .psnap association not permitted (no admin rights); using the per-user one only");
        }
        Scope = machine ? "for all users of this PC and your account" : "for your account";
        Notify();
        Log.Info($"Associated {Extension} with {Exe} ({Scope})");
        return machine;
    }

    static void WriteTo(RegistryKey root)
    {
        using (var ext = root.CreateSubKey($@"Software\Classes\{Extension}", writable: true))
        {
            ext.SetValue("", ProgId);
            ext.SetValue("Content Type", "application/x-portasnap");
            ext.SetValue("PerceivedType", "document");
            using var openWith = ext.CreateSubKey("OpenWithProgids", writable: true);
            openWith.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }
        using (var prog = root.CreateSubKey($@"Software\Classes\{ProgId}", writable: true))
        {
            prog.SetValue("", "PortaSnap project");
            prog.SetValue("FriendlyTypeName", "PortaSnap project");
            using (var icon = prog.CreateSubKey("DefaultIcon", writable: true)) icon.SetValue("", IconValue);
            using (var open = prog.CreateSubKey(@"shell\open", writable: true)) open.SetValue("Icon", IconValue);
            using (var cmd = prog.CreateSubKey(@"shell\open\command", writable: true)) cmd.SetValue("", OpenCommand);
        }
    }

    public static void Unregister()
    {
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                if (IsRegisteredIn(root)) root.DeleteSubKeyTree($@"Software\Classes\{Extension}", throwOnMissingSubKey: false);
                root.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                Log.Info($"Couldn't remove the {root.Name} association (no admin rights)");
            }
            catch (Exception ex)
            {
                Log.Error("Removing the file association failed", ex);
            }
        }
        try
        {
            // Explorer's remembered choice for the extension, so Windows doesn't keep pointing at PortaSnap.
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\" + Extension, throwOnMissingSubKey: false);
        }
        catch { }
        Notify();
        Log.Info($"Removed the {Extension} association");
    }

    /// <summary>If the association exists but points at another copy of the exe (or lacks the machine copy), refresh it.</summary>
    public static void RefreshIfRegistered()
    {
        try
        {
            bool user = IsRegisteredIn(Registry.CurrentUser), machine = IsRegisteredIn(Registry.LocalMachine);
            if (!user && !machine) return;
            bool current = PointsHere(Registry.CurrentUser) && machine && PointsHere(Registry.LocalMachine);
            Log.Info($"Association check: per-user={user} machine={machine} up-to-date={current}");
            if (!current) Register();
        }
        catch (Exception ex)
        {
            Log.Error("Refreshing the file association failed", ex);
        }
    }

    static bool PointsHere(RegistryKey root)
    {
        using var cmd = root.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command");
        return string.Equals(cmd?.GetValue("") as string, OpenCommand, StringComparison.OrdinalIgnoreCase);
    }

    static void Notify() => SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0x1000 /* SHCNF_FLUSH */, IntPtr.Zero, IntPtr.Zero);
}
