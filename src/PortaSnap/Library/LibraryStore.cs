using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.Imaging;
using PortaSnap.Imaging;
using PortaSnap.Infrastructure;
using PortaSnap.Model;

namespace PortaSnap.Library;

public sealed class LibraryItem : INotifyPropertyChanged
{
    BitmapSource? _thumbnail;
    string _title;

    public LibraryItem(string path, DateTime date)
    {
        Path = path;
        Date = date;
        IsVideo = System.IO.Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase);
        _title = IsVideo ? "Video" : "Capture";
    }

    public string Path { get; }
    public DateTime Date { get; }
    public bool IsVideo { get; }
    public string FileName => System.IO.Path.GetFileName(Path);
    public string DateText => Date.ToString("g", CultureInfo.CurrentCulture);
    public Visibility VideoBadge => IsVideo ? Visibility.Visible : Visibility.Collapsed;
    internal bool LoadStarted { get; set; }

    public BitmapSource? Thumbnail
    {
        get => _thumbnail;
        set { _thumbnail = value; OnChanged(); }
    }

    public string Title
    {
        get => _title;
        set { _title = value; OnChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// The capture library: a flat folder of .psnap documents and .mp4 recordings inside the portable data folder,
/// with cached thumbnails in .thumbs. Files are plain and can be copied/backed up directly.
/// </summary>
public sealed class LibraryStore
{
    const string StampFormat = "yyyy-MM-dd_HH-mm-ss";
    readonly Dictionary<string, LibraryItem> _items = new(StringComparer.OrdinalIgnoreCase);

    public LibraryStore(string root)
    {
        Root = root;
        Directory.CreateDirectory(root);
    }

    public string Root { get; }
    public event EventHandler? Changed;

    public string NewDocumentPath(DateTime when) => Unique(Path.Combine(Root, when.ToString(StampFormat, CultureInfo.InvariantCulture)), SnapFile.Extension);
    public string NewVideoPath(DateTime when) => Unique(Path.Combine(Root, when.ToString(StampFormat, CultureInfo.InvariantCulture)), ".mp4");

    static string Unique(string basePath, string ext)
    {
        var p = basePath + ext;
        int i = 2;
        while (File.Exists(p)) p = $"{basePath}_{i++}{ext}";
        return p;
    }

    static string ThumbPath(string itemPath) => Path.Combine(PortablePaths.ThumbDir, Path.GetFileName(itemPath) + ".png");

    /// <summary>Saves a document into the library (allocating a file name the first time) and refreshes its thumbnail.</summary>
    public string Save(SnapDocument doc, BitmapSource? rendered = null)
    {
        doc.LibraryPath ??= NewDocumentPath(doc.Metadata.CapturedAt);
        var path = doc.LibraryPath;
        var thumb = SnapFile.Save(doc, path, rendered);
        WriteThumb(path, thumb);
        doc.IsDirty = false;
        if (_items.TryGetValue(path, out var item))
        {
            item.Thumbnail = DecodeThumb(thumb);
            item.Title = doc.Metadata.DisplayTitle;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return path;
    }

    public void AddVideo(string path, BitmapSource? firstFrame)
    {
        if (firstFrame != null) WriteThumb(path, ImageUtil.EncodePng(ImageUtil.Thumbnail(firstFrame, 360)));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    static void WriteThumb(string itemPath, byte[] png)
    {
        try
        {
            Directory.CreateDirectory(PortablePaths.ThumbDir);
            File.WriteAllBytes(ThumbPath(itemPath), png);
        }
        catch (Exception ex) { Log.Error("Thumbnail write failed", ex); }
    }

    static BitmapSource? DecodeThumb(byte[]? png)
    {
        if (png == null) return null;
        try { return ImageUtil.Decode(png, 240); } catch { return null; }
    }

    public List<LibraryItem> Scan()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<LibraryItem>();
        if (Directory.Exists(Root))
        {
            foreach (var f in Directory.EnumerateFiles(Root))
            {
                var ext = Path.GetExtension(f);
                if (!ext.Equals(SnapFile.Extension, StringComparison.OrdinalIgnoreCase) && !ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase)) continue;
                seen.Add(f);
                if (!_items.TryGetValue(f, out var item))
                {
                    item = new LibraryItem(f, DateFromName(f));
                    _items[f] = item;
                }
                list.Add(item);
            }
        }
        foreach (var stale in _items.Keys.Where(k => !seen.Contains(k)).ToList()) _items.Remove(stale);
        list.Sort((a, b) => b.Date.CompareTo(a.Date));
        return list;
    }

    static DateTime DateFromName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (name.Length >= StampFormat.Length &&
            DateTime.TryParseExact(name[..StampFormat.Length], StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        try { return File.GetLastWriteTime(path); } catch { return DateTime.MinValue; }
    }

    /// <summary>Loads thumbnail and title in the background (once per item).</summary>
    public void EnsureLoaded(LibraryItem item)
    {
        if (item.LoadStarted) return;
        item.LoadStarted = true;
        var dispatcher = Application.Current.Dispatcher;
        Task.Run(() =>
        {
            byte[]? png = null;
            string? title = null;
            try
            {
                var cache = ThumbPath(item.Path);
                if (File.Exists(cache)) png = File.ReadAllBytes(cache);
                if (!item.IsVideo)
                {
                    if (png == null)
                    {
                        png = SnapFile.ReadThumbnailBytes(item.Path);
                        if (png != null) WriteThumb(item.Path, png);
                    }
                    title = SnapFile.ReadMetadata(item.Path)?.DisplayTitle;
                }
            }
            catch (Exception ex) { Log.Error($"Library item load failed: {item.Path}", ex); }
            var thumb = DecodeThumb(png);
            dispatcher.BeginInvoke(() =>
            {
                if (thumb != null) item.Thumbnail = thumb;
                if (title != null) item.Title = title;
            });
        });
    }

    /// <summary>Moves items to the Recycle Bin (recoverable) and drops their cached thumbnails.</summary>
    public void Delete(IEnumerable<LibraryItem> items)
    {
        foreach (var item in items.ToList())
        {
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(item.Path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                var thumb = ThumbPath(item.Path);
                if (File.Exists(thumb)) File.Delete(thumb);
                _items.Remove(item.Path);
            }
            catch (Exception ex) { Log.Error($"Delete failed: {item.Path}", ex); }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
