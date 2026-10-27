using System.IO.Compression;
using System.Text.Json;
using System.Windows.Media.Imaging;
using PortaSnap.Imaging;

namespace PortaSnap.Model;

/// <summary>
/// PortaSnap's editable project format (.psnap): a ZIP containing document.json (annotations, effects, metadata),
/// base.png (the untouched capture) and thumbnail.png (flattened preview). Open and documented on purpose.
/// </summary>
public static class SnapFile
{
    public const string Extension = ".psnap";

    sealed class DocumentFile
    {
        public string Format { get; set; } = "portasnap";
        public int Version { get; set; } = 1;
        public string? App { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public CaptureMetadata? Metadata { get; set; }
        public DocumentEffects? Effects { get; set; }
        public List<Annotation> Annotations { get; set; } = new();
    }

    /// <summary>Saves atomically and returns the PNG thumbnail bytes that were written.</summary>
    public static byte[] Save(SnapDocument d, string path, BitmapSource? rendered = null)
    {
        rendered ??= DocumentRenderer.Render(d);
        var thumb = ImageUtil.EncodePng(ImageUtil.Thumbnail(rendered, 360));
        var model = new DocumentFile
        {
            App = $"PortaSnap {typeof(SnapFile).Assembly.GetName().Version}",
            Width = d.Width,
            Height = d.Height,
            Metadata = d.Metadata,
            Effects = d.Effects,
            Annotations = d.Annotations,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "document.json", JsonSerializer.SerializeToUtf8Bytes(model, SnapJson.Indented), CompressionLevel.Optimal);
            WriteEntry(zip, "base.png", d.GetBasePng(), CompressionLevel.NoCompression);
            WriteEntry(zip, "thumbnail.png", thumb, CompressionLevel.NoCompression);
        }
        File.Move(tmp, path, overwrite: true);
        return thumb;
    }

    static void WriteEntry(ZipArchive zip, string name, byte[] data, CompressionLevel level)
    {
        var e = zip.CreateEntry(name, level);
        using var s = e.Open();
        s.Write(data, 0, data.Length);
    }

    static byte[]? ReadEntry(ZipArchive zip, string name)
    {
        var e = zip.GetEntry(name);
        if (e == null) return null;
        using var s = e.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    public static SnapDocument Load(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var basePng = ReadEntry(zip, "base.png") ?? throw new InvalidDataException("base.png missing");
        var json = ReadEntry(zip, "document.json") ?? throw new InvalidDataException("document.json missing");
        var model = JsonSerializer.Deserialize<DocumentFile>(json, SnapJson.Compact) ?? new DocumentFile();
        var doc = new SnapDocument(ImageUtil.Decode(basePng))
        {
            Effects = model.Effects ?? new DocumentEffects(),
            Metadata = model.Metadata ?? new CaptureMetadata(),
            LibraryPath = path,
        };
        doc.SetBasePngCache(basePng);
        doc.Annotations.AddRange(model.Annotations);
        return doc;
    }

    public static byte[]? ReadThumbnailBytes(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return ReadEntry(zip, "thumbnail.png");
        }
        catch { return null; }
    }

    public static CaptureMetadata? ReadMetadata(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var json = ReadEntry(zip, "document.json");
            if (json == null) return null;
            using var jd = JsonDocument.Parse(json);
            return jd.RootElement.TryGetProperty("metadata", out var m)
                ? m.Deserialize<CaptureMetadata>(SnapJson.Compact)
                : null;
        }
        catch { return null; }
    }
}
