using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using PortaSnap.Imaging;
using PortaSnap.Infrastructure;
using PortaSnap.Model;

namespace PortaSnap.Library;

/// <summary>File actions shared by the library window and the editor's Recent tray context menus.</summary>
public static class LibraryItemActions
{
    public static bool CopyImage(LibraryItem item)
    {
        try
        {
            return item.IsVideo
                ? ClipboardUtil.CopyFiles(item.Path)
                : ClipboardUtil.CopyImage(DocumentRenderer.Render(App.Current.Library.LoadDocument(item)));
        }
        catch (Exception ex)
        {
            Log.Error("Copy failed", ex);
            return false;
        }
    }

    public static void CopyPath(string path)
    {
        for (int i = 0; i < 5; i++)
        {
            try { Clipboard.SetText(path); return; }
            catch (COMException) { Thread.Sleep(60); }
        }
    }

    /// <summary>Exports a capture as an image (or copies a recording). Returns true when a file was written.</summary>
    public static bool Export(LibraryItem item, Window owner, App app)
    {
        try
        {
            if (item.IsVideo)
            {
                var dv = new SaveFileDialog { Filter = "MP4 video|*.mp4", FileName = item.FileName };
                if (dv.ShowDialog(owner) != true) return false;
                File.Copy(item.Path, dv.FileName, overwrite: true);
                return true;
            }
            var dlg = new SaveFileDialog
            {
                Filter = "JPEG image|*.jpg|PNG image|*.png|Bitmap|*.bmp|GIF image|*.gif|TIFF image|*.tif",
                FileName = Path.GetFileNameWithoutExtension(item.Path),
                InitialDirectory = Directory.Exists(app.Settings.LastExportFolder)
                    ? app.Settings.LastExportFolder
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            };
            if (dlg.ShowDialog(owner) != true) return false;
            ImageUtil.Save(DocumentRenderer.Render(app.Library.LoadDocument(item)), dlg.FileName, app.Settings.JpegQuality);
            app.Settings.LastExportFolder = Path.GetDirectoryName(dlg.FileName);
            app.SaveSettings();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Export failed", ex);
            MessageBox.Show(owner, "Export failed:\n\n" + ex.Message, "PortaSnap", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    // ---- Windows' own file Properties dialog ----

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string? lpVerb;
        public string? lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);

    const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;
    const int SW_SHOW = 5;

    public static void ShowProperties(string path, Window owner)
    {
        var info = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            fMask = SEE_MASK_INVOKEIDLIST,
            hwnd = new WindowInteropHelper(owner).Handle,
            lpVerb = "properties",
            lpFile = path,
            nShow = SW_SHOW,
        };
        if (!ShellExecuteEx(ref info)) Log.Error($"Properties dialog failed for {path} (error {Marshal.GetLastWin32Error()})");
    }
}
