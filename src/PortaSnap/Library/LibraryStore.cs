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

    public LibraryItem(string path, string? projectPath, DateTime date)
    {
        Path = path;
        ProjectPath = projectPath;
        Date = date;
        IsVideo = System.IO.Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase);
        _title = IsVideo ? "Video" : System.IO.Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>The user-facing file: the capture's image (JPG by default) or a recording (.mp4).</summary>
    public string Path { get; }

    /// <summary>The editable project behind an image (Library\.projects\name.psnap); may not exist yet. Null for videos.</summary>
    public string? ProjectPath { get; }

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
/// The capture library. Every capture is a normal image file in the Library folder (JPG by default) — that is the
/// file you see in Explorer, share and open. Its editable project (.psnap: original pixels + annotations) lives in
/// the hidden Library\.projects folder; saving in the editor updates the project and rewrites the image.
/// Recordings are .mp4 files in the Library folder. Thumbnails are cached in .thumbs.
/// </summary>
public sealed class LibraryStore
{
    const string StampFormat = "yyyy-MM-dd_HH-mm-ss";
    public const string ProjectsFolderName = ".projects";

    static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };

    readonly Dictionary<string, LibraryItem> _items = new(StringComparer.OrdinalIgnoreCase);

    public LibraryStore(string root)
    {
        Root = root;
        ProjectsDir = Path.Combine(root, ProjectsFolderName);
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(ProjectsDir);
        try { File.SetAttributes(ProjectsDir, File.GetAttributes(ProjectsDir) | FileAttributes.Hidden); } catch { }
    }

    public string Root { get; }
    public string ProjectsDir { get; }

    /// <summary>Extension (with dot) for new capture images, e.g. ".jpg".</summary>
    public string ImageExtension { get; set; } = ".jpg";
    public int JpegQuality { get; set; } = 100;

    public event EventHandler? Changed;

    public static bool IsImageFile(string path) => ImageExtensions.Contains(Path.GetExtension(path));

    public string NewVideoPath(DateTime when) => UniqueBase(when, ".mp4") + ".mp4";

    /// <summary>A capture name (date stamp) that is free for both the image and its project.</summary>
    string UniqueBase(DateTime when, string ext)
    {
        var stamp = when.ToString(StampFormat, CultureInfo.InvariantCulture);
        var name = stamp;
        int i = 2;
        while (File.Exists(Path.Combine(Root, name + ext)) || File.Exists(Path.Combine(ProjectsDir, name + SnapFile.Extension))
               || ImageExtensions.Any(e => File.Exists(Path.Combine(Root, name + e))))
            name = $"{stamp}_{i++}";
        return Path.Combine(Root, name);
    }

    /// <summary>
    /// The image that belongs to a project: for library projects (in .projects) the same-named image in the Library
    /// folder — existing one first, keeping its format — otherwise a new name in the configured format. Projects
    /// outside the library have no companion image.
    /// </summary>
    public string? ImagePathFor(string? projectPath)
    {
        if (projectPath == null) return null;
        var dir = Path.GetDirectoryName(projectPath);
        if (dir == null || !string.Equals(Path.GetFileName(dir), ProjectsFolderName, StringComparison.OrdinalIgnoreCase)) return null;
        var parent = Path.GetDirectoryName(dir)!;
        var name = Path.GetFileNameWithoutExtension(projectPath);
        foreach (var ext in ImageExtensions)
        {
            var candidate = Path.Combine(parent, name + ext);
            if (File.Exists(candidate)) return candidate;
        }
        return Path.Combine(parent, name + ImageExtension);
    }

    static string ThumbPath(string itemPath) => Path.Combine(PortablePaths.ThumbDir, Path.GetFileName(itemPath) + ".png");

    /// <summary>Saves a capture: project (.psnap) plus its image file (overwritten in place), and refreshes the thumbnail.</summary>
    public string Save(SnapDocument doc, BitmapSource? rendered = null)
    {
        if (doc.LibraryPath == null)
        {
            var baseName = Path.GetFileName(UniqueBase(doc.Metadata.CapturedAt, ImageExtension));
            doc.LibraryPath = Path.Combine(ProjectsDir, baseName + SnapFile.Extension);
        }
        rendered ??= DocumentRenderer.Render(doc);
        var thumb = SnapFile.Save(doc, doc.LibraryPath, rendered);
        var image = ImagePathFor(doc.LibraryPath);
        if (image != null)
        {
            ImageUtil.Save(rendered, image, JpegQuality);
            WriteThumb(image, thumb);
            if (_items.TryGetValue(image, out var item))
            {
                item.Thumbnail = DecodeThumb(thumb);
                item.Title = doc.Metadata.DisplayTitle;
            }
        }
        doc.IsDirty = false;
        Changed?.Invoke(this, EventArgs.Empty);
        return image ?? doc.LibraryPath;
    }

    /// <summary>Opens a library image for editing: its project if there is one, otherwise the image itself.</summary>
    public SnapDocument LoadDocument(LibraryItem item)
    {
        if (item.ProjectPath != null && File.Exists(item.ProjectPath)) return SnapFile.Load(item.ProjectPath);
        return new SnapDocument(ImageUtil.LoadFile(item.Path))
        {
            LibraryPath = item.ProjectPath,
            Metadata = new CaptureMetadata { CapturedAt = item.Date, WindowTitle = Path.GetFileNameWithoutExtension(item.Path), Source = "file", SourcePath = item.Path },
        };
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
                bool video = Path.GetExtension(f).Equals(".mp4", StringComparison.OrdinalIgnoreCase);
                if (!video && !IsImageFile(f)) continue;
                seen.Add(f);
                if (!_items.TryGetValue(f, out var item))
                {
                    var project = video ? null : Path.Combine(ProjectsDir, Path.GetFileNameWithoutExtension(f) + SnapFile.Extension);
                    item = new LibraryItem(f, project, DateFromName(f));
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
            BitmapSource? direct = null;
            try
            {
                var cache = ThumbPath(item.Path);
                if (File.Exists(cache)) png = File.ReadAllBytes(cache);
                if (!item.IsVideo)
                {
                    bool hasProject = item.ProjectPath != null && File.Exists(item.ProjectPath);
                    if (png == null && hasProject)
                    {
                        png = SnapFile.ReadThumbnailBytes(item.ProjectPath!);
                        if (png != null) WriteThumb(item.Path, png);
                    }
                    if (png == null)
                    {
                        // A plain image (e.g. dropped into the Library folder): thumbnail straight from the file.
                        direct = ImageUtil.Decode(File.ReadAllBytes(item.Path), 240);
                        WriteThumb(item.Path, ImageUtil.EncodePng(direct));
                    }
                    if (hasProject) title = SnapFile.ReadMetadata(item.ProjectPath!)?.DisplayTitle;
                }
            }
            catch (Exception ex) { Log.Error($"Library item load failed: {item.Path}", ex); }
            var thumb = direct ?? DecodeThumb(png);
            dispatcher.BeginInvoke(() =>
            {
                if (thumb != null) item.Thumbnail = thumb;
                if (title != null) item.Title = title;
            });
        });
    }

    /// <summary>Moves items (image + project, or recording) to the Recycle Bin and drops their cached thumbnails.</summary>
    public void Delete(IEnumerable<LibraryItem> items)
    {
        foreach (var item in items.ToList())
        {
            try
            {
                Recycle(item.Path);
                if (item.ProjectPath != null) Recycle(item.ProjectPath);
                var thumb = ThumbPath(item.Path);
                if (File.Exists(thumb)) File.Delete(thumb);
                _items.Remove(item.Path);
            }
            catch (Exception ex) { Log.Error($"Delete failed: {item.Path}", ex); }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    static void Recycle(string path)
    {
        if (!File.Exists(path)) return;
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
    }

    /// <summary>
    /// One-time move from the 0.4 layout (.psnap files directly in the Library folder) to image files with hidden
    /// projects: renders each project's image (unless one already exists) and moves the project into .projects.
    /// </summary>
    public int MigrateLegacyProjects()
    {
        int moved = 0;
        foreach (var legacy in Directory.EnumerateFiles(Root, "*" + SnapFile.Extension).ToList())
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(legacy);
                var target = Path.Combine(ProjectsDir, name + SnapFile.Extension);
                if (File.Exists(target)) continue;
                bool hasImage = ImageExtensions.Any(e => File.Exists(Path.Combine(Root, name + e)));
                if (!hasImage)
                {
                    var doc = SnapFile.Load(legacy);
                    ImageUtil.Save(DocumentRenderer.Render(doc), Path.Combine(Root, name + ImageExtension), JpegQuality);
                }
                File.Move(legacy, target);
                var oldThumb = ThumbPath(legacy);
                if (File.Exists(oldThumb)) File.Move(oldThumb, ThumbPath(Path.Combine(Root, name + ImageExtension)), overwrite: true);
                moved++;
            }
            catch (Exception ex) { Log.Error($"Library migration failed for {legacy}", ex); }
        }
        if (moved > 0) Log.Info($"Library migrated: {moved} capture(s) now saved as image files with projects in {ProjectsFolderName}");
        return moved;
    }

    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
