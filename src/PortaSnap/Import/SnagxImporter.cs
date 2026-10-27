using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PortaSnap.Imaging;
using PortaSnap.Model;

namespace PortaSnap.Import;

public sealed class SnagxImportResult
{
    public required SnapDocument Document { get; init; }
    public int ImportedObjects { get; init; }
    public List<string> SkippedObjects { get; } = new();
}

/// <summary>
/// Reads Snagit .snagx files (a ZIP: index.json → page JSON + background PNG, metadata.json) into PortaSnap documents.
/// Format notes are in docs/SNAGX-FORMAT.md. Object geometry is in canvas units; on Windows these equal image
/// pixels, while Mac files use points, so everything is scaled by (PNG width / canvas width).
/// </summary>
public static class SnagxImporter
{
    public static SnagxImportResult Import(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        using var index = ReadJson(zip, "index.json") ?? throw new InvalidDataException("index.json missing (not a .snagx file?)");
        var pageName = index.RootElement.GetProperty("Pages")[0].GetString() ?? throw new InvalidDataException("No pages");
        using var pageDoc = ReadJson(zip, pageName) ?? throw new InvalidDataException($"{pageName} missing");
        using var metaDoc = ReadJson(zip, "metadata.json");
        var page = pageDoc.RootElement;

        double canvasW = Num(page, "CaptureCanvasWidth"), canvasH = Num(page, "CaptureCanvasHeight");
        var size = ParsePoint(Str(page, "BackgroundSize"));
        if (canvasW <= 0 && size is Point bs) { canvasW = bs.X; canvasH = bs.Y; }

        BitmapSource? background = null;
        var bgName = Str(page, "CaptureBackgroundImage");
        if (!string.IsNullOrEmpty(bgName) && ReadBytes(zip, bgName) is { } bgBytes) background = ImageUtil.Decode(bgBytes);

        double scale = background != null && canvasW > 0 ? background.PixelWidth / canvasW : 1.0;
        if (scale <= 0 || double.IsNaN(scale)) scale = 1.0;

        int pixelW = (int)Math.Round(Math.Max(1, canvasW) * scale), pixelH = (int)Math.Round(Math.Max(1, canvasH) * scale);
        BitmapSource baseImage;
        var bgLocation = ParsePoint(Str(page, "CaptureBackgroundLocation")) ?? new Point();
        if (background != null && background.PixelWidth == pixelW && background.PixelHeight == pixelH && bgLocation == new Point())
        {
            baseImage = background;
        }
        else
        {
            // Template pages or expanded canvases: compose onto a canvas filled with the page background colour.
            var fill = BackgroundColor(page);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(fill), null, new Rect(0, 0, pixelW, pixelH));
                if (background != null)
                    dc.DrawImage(background, new Rect(bgLocation.X * scale, bgLocation.Y * scale, background.PixelWidth, background.PixelHeight));
            }
            var rtb = new RenderTargetBitmap(pixelW, pixelH, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            baseImage = rtb;
        }

        var doc = new SnapDocument(baseImage)
        {
            Metadata = ReadMetadata(metaDoc?.RootElement, path),
        };

        var skipped = new List<string>();
        int imported = 0;
        if (page.TryGetProperty("CaptureObjects", out var objects) && objects.ValueKind == JsonValueKind.Array)
        {
            foreach (var obj in objects.EnumerateArray())
            {
                var mode = Str(obj, "ToolMode") ?? "?";
                try
                {
                    var a = Convert(obj, mode, scale);
                    if (a != null)
                    {
                        doc.Annotations.Add(a);
                        imported++;
                    }
                    else
                    {
                        skipped.Add(mode);
                    }
                }
                catch
                {
                    skipped.Add(mode);
                }
            }
        }
        var result = new SnagxImportResult { Document = doc, ImportedObjects = imported };
        result.SkippedObjects.AddRange(skipped);
        return result;
    }

    static CaptureMetadata ReadMetadata(JsonElement? meta, string path)
    {
        var m = new CaptureMetadata { Source = "snagx-import", SourcePath = path, CapturedAt = File.GetLastWriteTime(path) };
        if (meta is not JsonElement e) return m;
        m.WindowTitle = Str(e, "WindowName");
        m.AppName = Str(e, "AppName");
        var url = Str(e, "WebURL");
        if (!string.IsNullOrWhiteSpace(url)) m.Url = url;
        // CaptureDate is stored in UTC ("2026-03-21 21:49:30").
        if (DateTime.TryParseExact(Str(e, "CaptureDate"), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc))
            m.CapturedAt = utc.ToLocalTime();
        return m;
    }

    static Annotation? Convert(JsonElement o, string mode, double k)
    {
        var a = ConvertCore(o, mode, k);
        if (a is BoxAnnotation box) box.Rotation = Num(o, "RotationAngle");
        return a;
    }

    static Annotation? ConvertCore(JsonElement o, string mode, double k)
    {
        var pts = Points(o, k);
        if (pts.Count == 0)
        {
            var s = ParsePoint(Str(o, "StartPoint"));
            var e = ParsePoint(Str(o, "EndPoint"));
            if (s is Point sp && e is Point ep) { pts.Add(Scale(sp, k)); pts.Add(Scale(ep, k)); }
        }
        if (pts.Count == 0) return null;
        var p0 = pts[0];
        var p1 = pts[^1];
        var box = new Rect(p0, p1);

        var fg = Col(o, "ForegroundColor") ?? Colors.Red;
        var bg = Col(o, "BackgroundColor") ?? Colors.Transparent;
        double sw = Num(o, "StrokeWidth", 3) * k;
        double opacity = Math.Clamp(Num(o, "Opacity", 100) / 100.0, 0, 1);
        bool shadow = Bool(o, "DropShadowEnabled");
        var dash = (Str(o, "DashType") ?? "Solid").Contains("Dash", StringComparison.OrdinalIgnoreCase) ? DashKind.Dash
                 : (Str(o, "DashType") ?? "").Contains("Dot", StringComparison.OrdinalIgnoreCase) ? DashKind.Dot : DashKind.Solid;

        switch (mode)
        {
            case "Arrow":
            case "Line":
            {
                var start = MapHead(Str(o, "ArrowStart"));
                var end = MapHead(Str(o, "ArrowEnd"));
                return new ArrowAnnotation
                {
                    Start = p0, End = p1, Stroke = fg, StrokeWidth = sw, Opacity = opacity, Shadow = shadow, Dash = dash,
                    StartHead = start, EndHead = end,
                    Tapered = (Str(o, "ArrowEnd") ?? "").Contains("Taper", StringComparison.OrdinalIgnoreCase),
                    IsLine = start == ArrowHead.None && end == ArrowHead.None,
                };
            }
            case "Shape":
                return new ShapeAnnotation
                {
                    Box = box, Shape = MapShape(Str(o, "ToolShape")), Stroke = fg, Fill = bg, StrokeWidth = sw,
                    Opacity = opacity, Shadow = shadow, Dash = dash,
                };
            case "Text":
            {
                var rtf = ParseRtf(Str(o, "RTFEncodedText"));
                if (string.IsNullOrWhiteSpace(rtf.Text)) return null;
                return new TextAnnotation
                {
                    Box = box, Text = rtf.Text,
                    Stroke = rtf.Color ?? fg,
                    FontFamily = rtf.Font ?? Str(o, "FontFamily") ?? "Segoe UI",
                    FontSize = Num(o, "FontSize", 24) * k,
                    Bold = rtf.Bold || Bool(o, "TextSelectionBold"),
                    Italic = rtf.Italic || Bool(o, "TextSelectionItalic"),
                    Align = MapAlign(Str(o, "ToolHorizontalAlign")),
                    CenterVertically = string.Equals(Str(o, "ToolVerticalAlign"), "Center", StringComparison.OrdinalIgnoreCase),
                    Outline = (Col(o, "TextOutlineColor")?.A ?? 0) > 0 && Num(o, "TextOutlineWidth") > 0,
                    Opacity = opacity, Shadow = shadow,
                };
            }
            case "Callout":
            {
                var rtf = ParseRtf(Str(o, "RTFEncodedText"));
                var tail = TailPoint(o, k) ?? new Point(box.Left + box.Width * 0.25, box.Bottom + 30);
                return new CalloutAnnotation
                {
                    Box = box, Tail = tail, Text = rtf.Text ?? string.Empty,
                    Fill = bg, Stroke = fg, StrokeWidth = sw,
                    TextColor = rtf.Color ?? Palette.Contrast(bg),
                    FontFamily = rtf.Font ?? Str(o, "FontFamily") ?? "Segoe UI",
                    FontSize = Num(o, "FontSize", 22) * k,
                    Bold = rtf.Bold, Italic = rtf.Italic,
                    Align = MapAlign(Str(o, "ToolHorizontalAlign")),
                    Shape = MapCalloutShape(Str(o, "CalloutShape")),
                    Opacity = opacity, Shadow = shadow,
                };
            }
            case "Step":
            {
                var label = Str(o, "PlainText") ?? "1";
                var seq = Str(o, "StepSequenceType") ?? "Number";
                int number = int.TryParse(label, out var n) ? n
                    : label.Length == 1 && char.IsLetter(label[0]) ? char.ToUpperInvariant(label[0]) - 'A' + 1 : 1;
                return new StepAnnotation
                {
                    Center = new Point(box.Left + box.Width / 2, box.Top + box.Height / 2),
                    Size = Math.Max(box.Width, box.Height),
                    Number = number,
                    Sequence = seq.Contains("Lower", StringComparison.OrdinalIgnoreCase) ? StepSequence.LowerLetter
                             : seq.Contains("Letter", StringComparison.OrdinalIgnoreCase) || seq.Contains("Upper", StringComparison.OrdinalIgnoreCase) ? StepSequence.UpperLetter
                             : StepSequence.Number,
                    // Snagit: BackgroundColor fills the badge, ForegroundColor is the number.
                    Stroke = bg.A > 0 ? bg : fg,
                    LabelColor = bg.A > 0 ? fg : Colors.Transparent,
                    Ring = false,
                    Shape = (Str(o, "StepStyle") ?? "Circle").Contains("Circle", StringComparison.OrdinalIgnoreCase) ? ShapeKind.Ellipse : ShapeKind.RoundedRectangle,
                    Opacity = opacity, Shadow = shadow,
                };
            }
            case "Highlight":
                return new HighlightAnnotation { Box = box, Stroke = fg, Opacity = opacity >= 1 ? 0.45 : opacity };
            case "Magnify":
                return new MagnifyAnnotation
                {
                    Box = box, Stroke = fg, StrokeWidth = sw, Zoom = Math.Max(1.1, Num(o, "MagnifyScale", 200) / 100.0),
                    Shape = MapShape(Str(o, "ToolShape")), Shadow = shadow,
                };
            case "Pen":
            case "Highlighter":
            case "Marker":
                return new PenAnnotation
                {
                    Points = pts, Stroke = fg, StrokeWidth = sw, Opacity = mode == "Pen" ? opacity : Math.Min(opacity, 0.5),
                    IsMarker = mode != "Pen", Shadow = shadow,
                };
            case "Blur":
            case "Pixelate":
            case "Redact":
                return new BlurAnnotation { Box = box, Mode = mode == "Redact" ? BlurMode.Solid : mode == "Blur" ? BlurMode.Blur : BlurMode.Pixelate, Intensity = 12, Stroke = Colors.Black };
            case "Spotlight":
                return new SpotlightAnnotation { Box = box, Opacity = 0.55, Shape = MapShape(Str(o, "ToolShape")) };
            case "Image":
            case "DropZone":
            case "Stamp":
            {
                var data = Base64(Str(o, "Image"));
                if (data == null || !ImageUtil.LooksLikeImage(data)) return null;
                return new ImageAnnotation { Box = box, ImageData = data, Opacity = opacity, Shadow = shadow };
            }
            default:
                return null;
        }
    }

    // ---------- RTF ----------

    sealed record RtfInfo(string Text, Color? Color, string? Font, bool Bold, bool Italic);

    /// <summary>Uses WPF's RTF reader to get plain text plus the first run's colour/font/weight.</summary>
    static RtfInfo ParseRtf(string? base64)
    {
        var bytes = Base64(base64);
        if (bytes == null) return new RtfInfo(string.Empty, null, null, false, false);
        try
        {
            var flow = new FlowDocument();
            var range = new TextRange(flow.ContentStart, flow.ContentEnd);
            using (var ms = new MemoryStream(bytes)) range.Load(ms, DataFormats.Rtf);
            var text = range.Text.Replace("\r\n", "\n").TrimEnd('\n', '\r', ' ');
            var run = flow.Blocks.OfType<Paragraph>().SelectMany(p => Runs(p.Inlines)).FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.Text));
            Color? color = (run?.Foreground as SolidColorBrush)?.Color;
            var font = run?.FontFamily?.Source;
            return new RtfInfo(text, color, font, run?.FontWeight >= FontWeights.Bold, run?.FontStyle == FontStyles.Italic);
        }
        catch
        {
            // Fall back to stripping control words.
            var raw = Encoding.Latin1.GetString(bytes);
            var plain = System.Text.RegularExpressions.Regex.Replace(raw, @"\\[a-z]+-?\d* ?|[{}]", string.Empty).Trim();
            return new RtfInfo(plain, null, null, false, false);
        }
    }

    /// <summary>All runs, including those nested in Bold/Italic/Span elements.</summary>
    static IEnumerable<Run> Runs(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Run r) yield return r;
            else if (inline is Span s)
                foreach (var nested in Runs(s.Inlines)) yield return nested;
        }
    }

    // ---------- helpers ----------

    static JsonDocument? ReadJson(ZipArchive zip, string name)
    {
        var bytes = ReadBytes(zip, name);
        return bytes == null ? null : JsonDocument.Parse(bytes);
    }

    static byte[]? ReadBytes(ZipArchive zip, string name)
    {
        var e = zip.GetEntry(name);
        if (e == null) return null;
        using var s = e.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.Array when v.GetArrayLength() > 0 && v[0].ValueKind == JsonValueKind.String => v[0].GetString(),
            _ => null,
        } : null;

    static double Num(JsonElement e, string name, double fallback = 0)
    {
        if (!e.TryGetProperty(name, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
        return fallback;
    }

    static bool Bool(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.Number && v.GetDouble() != 0));

    static Color? Col(JsonElement e, string name) => ColorJsonConverter.Parse(Str(e, name));

    static Point? ParsePoint(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var parts = s.Split(',');
        if (parts.Length < 2) return null;
        if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return new Point(x, y);
        return null;
    }

    static Point Scale(Point p, double k) => new(p.X * k, p.Y * k);

    static List<Point> Points(JsonElement o, double k)
    {
        var list = new List<Point>();
        if (o.TryGetProperty("PointsArray", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var item in arr.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String && ParsePoint(item.GetString()) is Point p)
                    list.Add(Scale(p, k));
        return list;
    }

    static Point? TailPoint(JsonElement o, double k)
    {
        if (!o.TryGetProperty("CalloutTails", out var t)) return null;
        string? s = t.ValueKind switch
        {
            JsonValueKind.String => t.GetString(),
            JsonValueKind.Array when t.GetArrayLength() > 0 => t[0].ValueKind == JsonValueKind.String ? t[0].GetString() : null,
            _ => null,
        };
        return ParsePoint(s) is Point p ? Scale(p, k) : null;
    }

    static byte[]? Base64(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        try { return System.Convert.FromBase64String(s); } catch { return null; }
    }

    static Color BackgroundColor(JsonElement page)
    {
        if (page.TryGetProperty("GradientBackgroundColor", out var g) && g.TryGetProperty("GradientColors", out var cols)
            && cols.ValueKind == JsonValueKind.Array && cols.GetArrayLength() > 0
            && ColorJsonConverter.Parse(cols[0].GetString()) is Color c && c.A > 0)
            return c;
        return ColorJsonConverter.Parse(Str(page, "CaptureBackgroundColor")) is Color bc && bc.A > 0 ? bc : Colors.White;
    }

    static ArrowHead MapHead(string? s)
    {
        if (string.IsNullOrEmpty(s)) return ArrowHead.None;
        if (s.Contains("Arrow", StringComparison.OrdinalIgnoreCase)) return s.Contains("Open", StringComparison.OrdinalIgnoreCase) || s.Contains("Line", StringComparison.OrdinalIgnoreCase) ? ArrowHead.OpenArrow : ArrowHead.Arrow;
        if (s.Contains("Circle", StringComparison.OrdinalIgnoreCase) || s.Contains("Dot", StringComparison.OrdinalIgnoreCase) || s.Contains("Ball", StringComparison.OrdinalIgnoreCase)) return ArrowHead.Round;
        if (s.Contains("Square", StringComparison.OrdinalIgnoreCase) || s.Contains("Diamond", StringComparison.OrdinalIgnoreCase)) return ArrowHead.Square;
        return ArrowHead.None; // "Flat", "Round" (a rounded line cap), "None"
    }

    static ShapeKind MapShape(string? s) =>
        s == null ? ShapeKind.Rectangle
        : s.Contains("Ellipse", StringComparison.OrdinalIgnoreCase) || s.Contains("Circle", StringComparison.OrdinalIgnoreCase) || s.Contains("Oval", StringComparison.OrdinalIgnoreCase) ? ShapeKind.Ellipse
        : s.Contains("Round", StringComparison.OrdinalIgnoreCase) ? ShapeKind.RoundedRectangle
        : ShapeKind.Rectangle;

    static ShapeKind MapCalloutShape(string? s) =>
        s == null ? ShapeKind.RoundedRectangle
        : s.Contains("Oval", StringComparison.OrdinalIgnoreCase) || s.Contains("Ellipse", StringComparison.OrdinalIgnoreCase) || s.Contains("Cloud", StringComparison.OrdinalIgnoreCase) || s.Contains("Thought", StringComparison.OrdinalIgnoreCase) ? ShapeKind.Ellipse
        : s.Contains("Rect", StringComparison.OrdinalIgnoreCase) && !s.Contains("Round", StringComparison.OrdinalIgnoreCase) ? ShapeKind.Rectangle
        : ShapeKind.RoundedRectangle;

    static TextAlignment MapAlign(string? s) =>
        s?.Equals("Center", StringComparison.OrdinalIgnoreCase) == true ? TextAlignment.Center
        : s?.Equals("Right", StringComparison.OrdinalIgnoreCase) == true ? TextAlignment.Right
        : TextAlignment.Left;
}
