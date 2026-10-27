using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using PortaSnap.Imaging;

namespace PortaSnap.Infrastructure;

public static class ClipboardUtil
{
    /// <summary>Puts an image on the clipboard as both a bitmap and PNG (PNG keeps transparency for apps that read it).</summary>
    public static bool CopyImage(BitmapSource image)
    {
        var data = new DataObject();
        data.SetImage(image);
        data.SetData("PNG", new MemoryStream(ImageUtil.EncodePng(image)), false);
        return SetWithRetry(data);
    }

    public static bool CopyFiles(params string[] paths)
    {
        var list = new StringCollection();
        list.AddRange(paths);
        var data = new DataObject();
        data.SetFileDropList(list);
        return SetWithRetry(data);
    }

    static bool SetWithRetry(DataObject data)
    {
        for (int i = 0; i < 6; i++)
        {
            try
            {
                Clipboard.SetDataObject(data, true);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(60); // another app has the clipboard open
            }
        }
        Log.Error("Clipboard is busy; copy failed");
        return false;
    }

    /// <summary>Writes a PNG into the temp folder for drag-and-drop or file copies.</summary>
    public static string WriteTempPng(BitmapSource image, string baseName)
    {
        Directory.CreateDirectory(PortablePaths.TempDir);
        var safe = string.Concat(baseName.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safe)) safe = "PortaSnap";
        var path = Path.Combine(PortablePaths.TempDir, safe + ".png");
        File.WriteAllBytes(path, ImageUtil.EncodePng(image));
        return path;
    }
}
