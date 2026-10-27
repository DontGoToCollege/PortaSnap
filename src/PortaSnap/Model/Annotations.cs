using System.Globalization;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PortaSnap.Imaging;

namespace PortaSnap.Model;

/// <summary>What an annotation needs from its document while rendering.</summary>
public sealed class RenderContext
{
    public required BitmapSource Base { get; init; }
    public Annotation? EditingText { get; init; }
    public Rect ImageRect => new(0, 0, Base.PixelWidth, Base.PixelHeight);
}

/// <summary>Annotations that carry editable text (text boxes and callouts).</summary>
public interface ITextAnnotation
{
    string Text { get; set; }
    string FontFamily { get; set; }
    double FontSize { get; set; }
    bool Bold { get; set; }
    bool Italic { get; set; }
    TextAlignment Align { get; set; }
    Color TextColor { get; }
    Rect TextRect { get; }
    void FitText();
    void SetDefaultSize(Point at);
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ArrowAnnotation), "arrow")]
[JsonDerivedType(typeof(ShapeAnnotation), "shape")]
[JsonDerivedType(typeof(TextAnnotation), "text")]
[JsonDerivedType(typeof(CalloutAnnotation), "callout")]
[JsonDerivedType(typeof(StepAnnotation), "step")]
[JsonDerivedType(typeof(HighlightAnnotation), "highlight")]
[JsonDerivedType(typeof(BlurAnnotation), "blur")]
[JsonDerivedType(typeof(PenAnnotation), "pen")]
[JsonDerivedType(typeof(MagnifyAnnotation), "magnify")]
[JsonDerivedType(typeof(SpotlightAnnotation), "spotlight")]
[JsonDerivedType(typeof(ImageAnnotation), "image")]
public abstract class Annotation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Color Stroke { get; set; } = Palette.Red;
    public Color Fill { get; set; } = Colors.Transparent;
    public double StrokeWidth { get; set; } = 4;
    public double Opacity { get; set; } = 1;
    public bool Shadow { get; set; }
    public DashKind Dash { get; set; }

    public abstract ToolKind Kind { get; }
    public abstract Rect Bounds { get; }
    public virtual IReadOnlyList<Point> Handles => Array.Empty<Point>();
    public virtual bool IsDegenerate => Bounds.Width < 4 && Bounds.Height < 4;

    public abstract void Render(DrawingContext dc, RenderContext ctx);
    public abstract void Offset(Vector v);
    public abstract void Scale(double sx, double sy);
    public abstract void UpdateCreate(Point start, Point current, bool constrain);
    public virtual void MoveHandle(int index, Point p, bool constrain) { }

    public virtual bool HitTest(Point p, double tolerance)
    {
        var b = Bounds;
        b.Inflate(tolerance, tolerance);
        return b.Contains(p);
    }

    public virtual void ApplyStyle(ToolStyle s)
    {
        Stroke = s.Stroke; Fill = s.Fill; StrokeWidth = s.StrokeWidth; Opacity = s.Opacity; Shadow = s.Shadow; Dash = s.Dash;
    }

    public virtual void CopyStyleTo(ToolStyle s)
    {
        s.Stroke = Stroke; s.Fill = Fill; s.StrokeWidth = StrokeWidth; s.Opacity = Opacity; s.Shadow = Shadow; s.Dash = Dash;
    }

    protected static Brush BrushOf(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    protected Pen MakePen(Color c, double width, PenLineCap cap = PenLineCap.Round)
    {
        var pen = new Pen(BrushOf(c), Math.Max(0.5, width)) { StartLineCap = cap, EndLineCap = cap, LineJoin = PenLineJoin.Round };
        if (Dash == DashKind.Dash) { pen.DashStyle = new DashStyle(new[] { 3.0, 2.0 }, 0); pen.DashCap = PenLineCap.Flat; }
        else if (Dash == DashKind.Dot) { pen.DashStyle = new DashStyle(new[] { 0.0, 2.0 }, 0); pen.DashCap = PenLineCap.Round; }
        pen.Freeze();
        return pen;
    }

    protected static bool Visible(Color c) => c.A > 0;
}

/// <summary>Base for annotations defined by a rectangle, with the usual eight resize handles.</summary>
public abstract class BoxAnnotation : Annotation
{
    public Rect Box { get; set; }

    /// <summary>Clockwise rotation in degrees around the box centre (used by imported Snagit objects).</summary>
    public double Rotation { get; set; }

    public override Rect Bounds => Box;
    public override IReadOnlyList<Point> Handles => BoxHandles(Box);
    public override void MoveHandle(int index, Point p, bool constrain) => Box = MoveBoxHandle(Box, index, p, constrain);
    public override void Offset(Vector v) => Box = new Rect(Box.TopLeft + v, Box.Size);

    public override void Scale(double sx, double sy)
    {
        Box = new Rect(Box.X * sx, Box.Y * sy, Box.Width * sx, Box.Height * sy);
        StrokeWidth *= (sx + sy) / 2;
    }

    public override void UpdateCreate(Point start, Point current, bool constrain)
    {
        if (constrain)
        {
            double side = Math.Max(Math.Abs(current.X - start.X), Math.Abs(current.Y - start.Y));
            current = new Point(start.X + Math.Sign(current.X - start.X + 1e-9) * side, start.Y + Math.Sign(current.Y - start.Y + 1e-9) * side);
        }
        Box = new Rect(start, current);
    }

    internal static Point[] BoxHandles(Rect r) => new[]
    {
        r.TopLeft, new Point(r.Left + r.Width / 2, r.Top), r.TopRight, new Point(r.Right, r.Top + r.Height / 2),
        r.BottomRight, new Point(r.Left + r.Width / 2, r.Bottom), r.BottomLeft, new Point(r.Left, r.Top + r.Height / 2),
    };

    internal static Rect MoveBoxHandle(Rect r, int i, Point p, bool constrain)
    {
        double l = r.Left, t = r.Top, rr = r.Right, b = r.Bottom;
        switch (i)
        {
            case 0: l = p.X; t = p.Y; break;
            case 1: t = p.Y; break;
            case 2: rr = p.X; t = p.Y; break;
            case 3: rr = p.X; break;
            case 4: rr = p.X; b = p.Y; break;
            case 5: b = p.Y; break;
            case 6: l = p.X; b = p.Y; break;
            case 7: l = p.X; break;
        }
        if (constrain && i % 2 == 0 && r.Width > 0 && r.Height > 0)
        {
            // Keep the aspect ratio when dragging a corner with Shift.
            double ratio = r.Width / r.Height;
            double w = Math.Abs(rr - l), h = Math.Abs(b - t);
            if (w / ratio > h) h = w / ratio; else w = h * ratio;
            switch (i)
            {
                case 0: l = rr - w; t = b - h; break;
                case 2: rr = l + w; t = b - h; break;
                case 4: rr = l + w; b = t + h; break;
                case 6: l = rr - w; b = t + h; break;
            }
        }
        return new Rect(new Point(l, t), new Point(rr, b));
    }

    protected static Rect Deflate(Rect r, double d)
    {
        double w = Math.Max(1, r.Width - 2 * d), h = Math.Max(1, r.Height - 2 * d);
        return new Rect(r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h);
    }

    protected Geometry ShapeGeometry(Rect r, ShapeKind kind)
    {
        Geometry g = kind switch
        {
            ShapeKind.Ellipse => new EllipseGeometry(r),
            ShapeKind.RoundedRectangle => new RectangleGeometry(r, Radius(r), Radius(r)),
            _ => new RectangleGeometry(r),
        };
        g.Freeze();
        return g;
    }

    protected static double Radius(Rect r) => Math.Min(Math.Min(r.Width, r.Height) * 0.2, 18);
}

public sealed class ArrowAnnotation : Annotation
{
    public Point Start { get; set; }
    public Point End { get; set; }
    public ArrowHead StartHead { get; set; } = ArrowHead.None;
    public ArrowHead EndHead { get; set; } = ArrowHead.Arrow;
    public bool Tapered { get; set; }
    public bool IsLine { get; set; }

    public override ToolKind Kind => IsLine ? ToolKind.Line : ToolKind.Arrow;

    public override Rect Bounds
    {
        get
        {
            var r = new Rect(Start, End);
            r.Inflate(StrokeWidth / 2, StrokeWidth / 2);
            return r;
        }
    }

    public override IReadOnlyList<Point> Handles => new[] { Start, End };
    public override bool IsDegenerate => (End - Start).Length < 4;

    public override void MoveHandle(int index, Point p, bool constrain)
    {
        if (index == 0) Start = constrain ? GeometryUtil.SnapAngle(End, p) : p;
        else End = constrain ? GeometryUtil.SnapAngle(Start, p) : p;
    }

    public override void Offset(Vector v) { Start += v; End += v; }

    public override void Scale(double sx, double sy)
    {
        Start = new Point(Start.X * sx, Start.Y * sy);
        End = new Point(End.X * sx, End.Y * sy);
        StrokeWidth *= (sx + sy) / 2;
    }

    public override void UpdateCreate(Point start, Point current, bool constrain)
    {
        Start = start;
        End = constrain ? GeometryUtil.SnapAngle(start, current) : current;
    }

    public override bool HitTest(Point p, double tolerance)
        => GeometryUtil.DistanceToSegment(p, Start, End) <= StrokeWidth / 2 + tolerance + (EndHead != ArrowHead.None ? StrokeWidth : 0);

    public override void ApplyStyle(ToolStyle s)
    {
        base.ApplyStyle(s);
        StartHead = s.StartHead; EndHead = s.EndHead; Tapered = s.Tapered;
    }

    public override void CopyStyleTo(ToolStyle s)
    {
        base.CopyStyleTo(s);
        s.StartHead = StartHead; s.EndHead = EndHead; s.Tapered = Tapered;
    }

    public override void Render(DrawingContext dc, RenderContext ctx)
    {
        var d = End - Start;
        double len = d.Length;
        if (len < 0.5) return;
        var u = d / len;
        var n = new Vector(-u.Y, u.X);
        var brush = BrushOf(Stroke);
        double w = Math.Max(1, StrokeWidth);
        double headLen = Math.Min(len * 0.6, Math.Max(w * 3.0, 12));
        double headHalf = Math.Max(w * 1.6, 6);

        dc.PushOpacity(Opacity);
        Point s = Start, e = End;
        if (EndHead == ArrowHead.Arrow) e = End - u * headLen * 0.8;
        if (StartHead == ArrowHead.Arrow) s = Start + u * headLen * 0.8;

        if (Tapered && Dash == DashKind.Solid)
        {
            double w0 = Math.Max(1, w * 0.3), w1 = w;
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(s + n * w0 / 2, true, true);
                c.LineTo(e + n * w1 / 2, true, true);
                c.LineTo(e - n * w1 / 2, true, true);
                c.LineTo(s - n * w0 / 2, true, true);
            }
            g.Freeze();
            dc.DrawGeometry(brush, null, g);
            dc.DrawEllipse(brush, null, s, w0 / 2, w0 / 2);
            if (EndHead == ArrowHead.None) dc.DrawEllipse(brush, null, e, w1 / 2, w1 / 2);
        }
        else
        {
            dc.DrawLine(MakePen(Stroke, w, PenLineCap.Round), s, e);
        }

        DrawHead(dc, brush, End, u, EndHead, w, headLen, headHalf);
        DrawHead(dc, brush, Start, -u, StartHead, w, headLen, headHalf);
        dc.Pop();
    }

    void DrawHead(DrawingContext dc, Brush brush, Point tip, Vector u, ArrowHead kind, double w, double len, double half)
    {
        var n = new Vector(-u.Y, u.X);
        switch (kind)
        {
            case ArrowHead.Arrow:
            {
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(tip, true, true);
                    c.LineTo(tip - u * len + n * half, true, true);
                    c.LineTo(tip - u * len * 0.82, true, true);
                    c.LineTo(tip - u * len - n * half, true, true);
                }
                g.Freeze();
                dc.DrawGeometry(brush, new Pen(brush, 1) { LineJoin = PenLineJoin.Round }, g);
                break;
            }
            case ArrowHead.OpenArrow:
            {
                var pen = MakePen(Stroke, w, PenLineCap.Round);
                dc.DrawLine(pen, tip, tip - u * len + n * half);
                dc.DrawLine(pen, tip, tip - u * len - n * half);
                break;
            }
            case ArrowHead.Round:
            {
                double r = Math.Max(w * 1.2, 4);
                dc.DrawEllipse(brush, null, tip, r, r);
                break;
            }
            case ArrowHead.Square:
            {
                double h = Math.Max(w * 1.2, 4);
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(tip + u * h + n * h, true, true);
                    c.LineTo(tip + u * h - n * h, true, true);
                    c.LineTo(tip - u * h - n * h, true, true);
                    c.LineTo(tip - u * h + n * h, true, true);
                }
                g.Freeze();
                dc.DrawGeometry(brush, null, g);
                break;
            }
        }
    }
}

public sealed class ShapeAnnotation : BoxAnnotation
{
    public ShapeKind Shape { get; set; } = ShapeKind.Rectangle;

    public override ToolKind Kind => ToolKind.Shape;

    public override void ApplyStyle(ToolStyle s) { base.ApplyStyle(s); Shape = s.Shape; }
    public override void CopyStyleTo(ToolStyle s) { base.CopyStyleTo(s); s.Shape = Shape; }

    public override void Render(DrawingContext dc, RenderContext ctx)
    {
        double w = Visible(Stroke) ? StrokeWidth : 0;
        var inner = Box;
        if (w > 0 && inner.Width > w && inner.Height > w) inner.Inflate(-w / 2, -w / 2);
        var fill = Visible(Fill) ? BrushOf(Fill) : null;
        var pen = w > 0 ? MakePen(Stroke, w) : null;
        if (fill == null && pen == null) return;
        dc.PushOpacity(Opacity);
        dc.DrawGeometry(fill, pen, ShapeGeometry(inner, Shape));
        dc.Pop();
    }
}

public sealed class TextAnnotation : BoxAnnotation, ITextAnnotation
{
    const double Pad = 6;

    public string Text { get; set; } = string.Empty;
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 24;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public TextAlignment Align { get; set; } = TextAlignment.Left;
    public bool Outline { get; set; }
    public bool CenterVertically { get; set; }

    public override ToolKind Kind => ToolKind.Text;
    public Color TextColor => Stroke;
    public Rect TextRect => Deflate(Box, Pad);
    public override bool IsDegenerate => Box.Width < 12 || Box.Height < 8;

    public void SetDefaultSize(Point at)
        => Box = new Rect(at.X, at.Y, Math.Max(220, FontSize * 9), FontSize * 1.4 + 2 * Pad);

    public void FitText()
    {
        var ft = TextLayout.Create(string.IsNullOrEmpty(Text) ? "Ag" : Text, FontFamily, FontSize, Bold, Italic, TextColor, TextRect.Width, Align);
        Box = new Rect(Box.X, Box.Y, Box.Width, Math.Max(ft.Height, FontSize * 1.2) + 2 * Pad);
    }

    public override void Scale(double sx, double sy) { base.Scale(sx, sy); FontSize *= (sx + sy) / 2; }

    public override void ApplyStyle(ToolStyle s)
    {
        base.ApplyStyle(s);
        FontFamily = s.FontFamily; FontSize = s.FontSize; Bold = s.Bold; Italic = s.Italic; Align = s.Align; Outline = s.TextOutline;
    }

    public override void CopyStyleTo(ToolStyle s)
    {
        base.CopyStyleTo(s);
        s.FontFamily = FontFamily; s.FontSize = FontSize; s.Bold = Bold; s.Italic = Italic; s.Align = Align; s.TextOutline = Outline;
    }

    public override void Render(DrawingContext dc, RenderContext ctx)
    {
        dc.PushOpacity(Opacity);
        if (Visible(Fill)) dc.DrawRoundedRectangle(BrushOf(Fill), null, Box, 4, 4);
        if (!ReferenceEquals(ctx.EditingText, this) && !string.IsNullOrEmpty(Text))
        {
            var ft = TextLayout.Create(Text, FontFamily, FontSize, Bold, Italic, TextColor, TextRect.Width, Align);
            var tr = TextRect;
            var origin = CenterVertically && ft.Height < tr.Height
                ? new Point(tr.Left, tr.Top + (tr.Height - ft.Height) / 2)
                : tr.TopLeft;
            if (Outline)
            {
                var geo = ft.BuildGeometry(origin);
                var outline = new Pen(BrushOf(Palette.Contrast(TextColor)), Math.Max(2, FontSize / 7)) { LineJoin = PenLineJoin.Round };
                dc.DrawGeometry(null, outline, geo);
                dc.DrawGeometry(BrushOf(TextColor), null, geo);
            }
            else
            {
                dc.DrawText(ft, origin);
            }
        }
        dc.Pop();
    }
}

public sealed class CalloutAnnotation : BoxAnnotation, ITextAnnotation
{
    const double Pad = 12;

    public Point Tail { get; set; }
    public string Text { get; set; } = string.Empty;
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 22;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public TextAlignment Align { get; set; } = TextAlignment.Center;
    public Color TextColor { get; set; } = Colors.White;
    public ShapeKind Shape { get; set; } = ShapeKind.RoundedRectangle;

    public override ToolKind Kind => ToolKind.Callout;
    public override Rect Bounds => Rect.Union(Box, new Rect(Tail, Tail));
    public override bool IsDegenerate => Box.Width < 12 || Box.Height < 8;

    public Rect TextRect
    {
        get
        {
            var r = Deflate(Box, Pad + (Visible(Stroke) ? StrokeWidth : 0));
            return Shape == ShapeKind.Ellipse ? Deflate(r, Math.Min(r.Width, r.Height) * 0.12) : r;
        }
    }

    public override IReadOnlyList<Point> Handles => BoxHandles(Box).Append(Tail).ToArray();

    public override void MoveHandle(int index, Point p, bool constrain)
    {
        if (index == 8) Tail = p;
        else base.MoveHandle(index, p, constrain);
    }

    public override void Offset(Vector v) { base.Offset(v); Tail += v; }

    public override void Scale(double sx, double sy)
    {
        base.Scale(sx, sy);
        Tail = new Point(Tail.X * sx, Tail.Y * sy);
        FontSize *= (sx + sy) / 2;
    }

    public override void UpdateCreate(Point start, Point current, bool constrain)
    {
        base.UpdateCreate(start, current, constrain);
        Tail = DefaultTail(Box);
    }

    public void SetDefaultSize(Point at)
    {
        Box = new Rect(at.X, at.Y, Math.Max(200, FontSize * 9), Math.Max(70, FontSize * 3));
        Tail = DefaultTail(Box);
    }

    static Point DefaultTail(Rect b) => new(b.Left + b.Width * 0.25, b.Bottom + Math.Max(28, b.Height * 0.5));

    public void FitText()
    {
        var tr = TextRect;
        var ft = TextLayout.Create(string.IsNullOrEmpty(Text) ? "Ag" : Text, FontFamily, FontSize, Bold, Italic, TextColor, tr.Width, Align);
        if (ft.Height > tr.Height)
            Box = new Rect(Box.X, Box.Y, Box.Width, Box.Height + (ft.Height - tr.Height) + 2);
    }

    public override void ApplyStyle(ToolStyle s)
    {
        base.ApplyStyle(s);
        FontFamily = s.FontFamily; FontSize = s.FontSize; Bold = s.Bold; Italic = s.Italic; Align = s.Align; TextColor = s.TextColor; Shape = s.Shape;
    }

    public override void CopyStyleTo(ToolStyle s)
    {
        base.CopyStyleTo(s);
        s.FontFamily = FontFamily; s.FontSize = FontSize; s.Bold = Bold; s.Italic = Italic; s.Align = Align; s.TextColor = TextColor; s.Shape = Shape;
    }

    public Geometry BuildGeometry()
    {
        var body = ShapeGeometry(Box, Shape);
        if (body.FillContains(Tail) || Box.Width < 2 || Box.Height < 2) return body;
        var c = new Point(Box.Left + Box.Width / 2, Box.Top + Box.Height / 2);
        var dir = Tail - c;
        if (dir.Length < 1) return body;
        dir.Normalize();
        var n = new Vector(-dir.Y, dir.X);
        double baseHalf = Math.Clamp(Math.Min(Box.Width, Box.Height) * 0.16, 6, 30);
        var tri = new StreamGeometry();
        using (var ctx = tri.Open())
        {
            ctx.BeginFigure(c + n * baseHalf, true, true);
            ctx.LineTo(Tail, true, true);
            ctx.LineTo(c - n * baseHalf, true, true);
        }
        tri.Freeze();
        var combined = new CombinedGeometry(GeometryCombineMode.Union, body, tri);
        combined.Freeze();
        return combined;
    }

    public override bool HitTest(Point p, double tolerance)
    {
        var b = Box;
        b.Inflate(tolerance, tolerance);
        return b.Contains(p) || BuildGeometry().FillContains(p, tolerance, ToleranceType.Absolute);
    }

    public override void Render(DrawingContext dc, RenderContext ctx)
    {
        dc.PushOpacity(Opacity);
        var geo = BuildGeometry();
        dc.DrawGeometry(Visible(Fill) ? BrushOf(Fill) : null, Visible(Stroke) && StrokeWidth > 0 ? MakePen(Stroke, StrokeWidth) : null, geo);
        if (!ReferenceEquals(ctx.EditingText, this) && !string.IsNullOrEmpty(Text))
        {
            var tr = TextRect;
            var ft = TextLayout.Create(Text, FontFamily, FontSize, Bold, Italic, TextColor, tr.Width, Align);
            double y = tr.Top + Math.Max(0, (tr.Height - ft.Height) / 2);
            dc.DrawText(ft, new Point(tr.Left, y));
        }
        dc.Pop();
    }
}

public sealed class StepAnnotation : Annotation
{
    public Point Center { get; set; }
    public double Size { get; set; } = 40;
    public int Number { get; set; } = 1;
    public StepSequence Sequence { get; set; } = StepSequence.Number;
    public ShapeKind Shape { get; set; } = ShapeKind.Ellipse;

    /// <summary>Number colour; transparent means "pick black or white automatically".</summary>
    public Color LabelColor { get; set; } = Colors.Transparent;

    /// <summary>Thin white ring around the badge.</summary>
    public bool Ring { get; set; } = true;

    public override ToolKind Kind => ToolKind.Step;
    public override Rect Bounds => new(Center.X - Size / 2, Center.Y - Size / 2, Size, Size);
    public override bool IsDegenerate => false;

    public string Label => Sequence switch
    {
        StepSequence.UpperLetter => ToLetters(Number),
        StepSequence.LowerLetter => ToLetters(Number).ToLowerInvariant(),
        _ => Number.ToString(CultureInfo.InvariantCulture),
    };

    static string ToLetters(int n)
    {
        n = Math.Max(1, n);
        var s = string.Empty;
        while (n > 0) { n--; s = (char)('A' + n % 26) + s; n /= 26; }
        return s;
    }

    public override void Offset(Vector v) => Center += v;

    public override void Scale(double sx, double sy)
    {
        Center = new Point(Center.X * sx, Center.Y * sy);
        Size *= (sx + sy) / 2;
    }

    public override void UpdateCreate(Point start, Point current, bool constrain) => Center = current;

    public override bool HitTest(Point p, double tolerance) => (p - Center).Length <= Size / 2 + tolerance;

    public override void ApplyStyle(ToolStyle s)
    {
        Stroke = s.Stroke; Opacity = s.Opacity; Shadow = s.Shadow; Size = s.StepSize; Sequence = s.Sequence;
        Shape = s.Shape == ShapeKind.Ellipse ? ShapeKind.Ellipse : ShapeKind.RoundedRectangle;
    }

    public override void CopyStyleTo(ToolStyle s)
    {
        s.Stroke = Stroke; s.Opacity = Opacity; s.Shadow = Shadow; s.StepSize = Size; s.Sequence = Sequence; s.Shape = Shape;
    }

    public override void Render(DrawingContext dc, RenderContext ctx)
    {
        dc.PushOpacity(Opacity);
        var r = Size / 2;
        var ring = Ring ? new Pen(BrushOf(Colors.White), Math.Max(1.5, Size * 0.06)) : null;
        if (Shape == ShapeKind.Ellipse) dc.DrawEllipse(BrushOf(Stroke), ring, Center, r, r);
        else dc.DrawRoundedRectangle(BrushOf(Stroke), ring, Bounds, Size * 0.2, Size * 0.2);
        var label = Label;
        double fontSize = Size * (label.Length > 2 ? 0.36 : label.Length > 1 ? 0.44 : 0.52);
        var textColor = Visible(LabelColor) ? LabelColor : Palette.Contrast(Stroke);
        var ft = TextLayout.Create(label, "Segoe UI", fontSize, true, false, textColor, double.NaN, TextAlignment.Left);
        dc.DrawText(ft, new Point(Center.X - ft.Width / 2, Center.Y - ft.Height / 2));
        dc.Pop();
    }
}

public sealed class HighlightAnnotation : BoxAnnotation
{
    public override ToolKind Kind => ToolKind.Highlight;

    public override void Render(DrawingContext dc, RenderContext ctx)
    {
        dc.PushOpacity(Opacity);
        dc.DrawRectangle(BrushOf(Stroke), null, Box);
        dc.Pop();
    }
}

public sealed class BlurAnnotation : BoxAnnotation
{
    BitmapSource? _cache;
    string? _cacheKey;

    public BlurMode Mode { get; set; } = BlurMode.Pixelate;
    public double Intensity { get; set; } = 12;

    public override ToolKind Kind => ToolKind.Blur;

    public override void ApplyStyle(ToolStyle s) { Mode = s.BlurMode; Intensity = s.Intensity; Stroke = s.Stroke; Opacity = 1; Shadow = false; }
    public override void CopyStyleTo(ToolStyle s) { s.BlurMode = Mode; s.Intensity = Intensity; s.Stroke = Stroke; }

    public override void Render(DrawingContext dc, RenderContext ctx)
    {
        var r = Rect.Intersect(Box, ctx.ImageRect);
        if (r.IsEmpty || r.Width < 1 || r.Height < 1) return;
        if (Mode == BlurMode.Solid)
        {
            dc.DrawRectangle(BrushOf(Visible(Stroke) ? Stroke : Colors.Black), null, r);
            return;
        }
        int x = (int)Math.Floor(r.X), y = (int)Math.Floor(r.Y);
        int w = Math.Min(ctx.Base.PixelWidth - x, (int)Math.Ceiling(r.Right) - x);
        int h = Math.Min(ctx.Base.PixelHeight - y, (int)Math.Ceiling(r.Bottom) - y);
        if (w <= 0 || h <= 0) return;
        var key = $"{x},{y},{w},{h},{Mode},{Intensity},{ctx.Base.GetHashCode()}";
        if (_cacheKey != key || _cache == null)
        {
            var area = new Int32Rect(x, y, w, h);
            _cache = Mode == BlurMode.Pixelate
                ? ImageFx.Pixelate(ctx.Base, area, (int)Math.Max(2, Intensity))
                : ImageFx.Blur(ctx.Base, area, (int)Math.Max(1, Intensity));
            _cacheKey = key;
        }
        dc.DrawImage(_cache, new Rect(x, y, w, h));
    }
}

public sealed class PenAnnotation : Annotation
{
    public List<Point> Points { get; set; } = new();
    public bool IsMarker { get; set; }

    public override ToolKind Kind => IsMarker ? ToolKind.Marker : ToolKind.Pen;
    public override bool IsDegenerate => Points.Count < 2;

    public override Rect Bounds
    {
        get
        {
            if (Points.Count == 0) return Rect.Empty;
            double l = Points.Min(p => p.X), t = Points.Min(p => p.Y), r = Points.Max(p => p.X), b = Points.Max(p => p.Y);
            var rect = new Rect(new Point(l, t), new Point(r, b));
            rect.Inflate(StrokeWidth / 2, StrokeWidth / 2);
            return rect;
        }
    }

    public override void Offset(Vector v)
    {
        for (int i = 0; i < Points.Count; i++) Points[i] += v;
    }

    public override void Scale(double sx, double sy)
    {
        for (int i = 0; i < Points.Count; i++) Points[i] = new Point(Points[i].X * sx, Points[i].Y * sy);
        StrokeWidth *= (sx + sy) / 2;
    }

    public override void UpdateCreate(Point start, Point current, bool constrain)
    {
        if (Points.Count == 0) Points.Add(start);
        if ((current - Points[^1]).Length >= 1.5) Points.Add(current);
    }

    public override bool HitTest(Point p, double tolerance)
    {
        double limit = StrokeWidth / 2 + tolerance;
        for (int i = 1; i < Points.Count; i++)
            if (GeometryUtil.DistanceToSegment(p, Points[i - 1], Points[i]) <= limit) return true;
        return Points.Count == 1 && (p - Points[0]).Length <= limit;
    }

    public override void Render(DrawingContext dc, RenderContext ctx)
    {
        if (Points.Count == 0) return;
        dc.PushOpacity(Opacity);
        var pen = MakePen(Stroke, StrokeWidth, IsMarker ? PenLineCap.Square : PenLineCap.Round);
        if (Points.Count == 1)
        {
            dc.DrawEllipse(BrushOf(Stroke), null, Points[0], StrokeWidth / 2, StrokeWidth / 2);
        }
        else
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(Points[0], false, false);
                for (int i = 1; i < Points.Count - 1; i++)
                {
                    var mid = new Point((Points[i].X + Points[i + 1].X) / 2, (Points[i].Y + Points[i + 1].Y) / 2);
                    c.QuadraticBezierTo(Points[i], mid, true, true);
                }
                c.LineTo(Points[^1], true, true);
            }
            g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }
        dc.Pop();
    }
}

public sealed class MagnifyAnnotation : BoxAnnotation
{
    public double Zoom { get; set; } = 2;
    public ShapeKind Shape { get; set; } = ShapeKind.Ellipse;

    public override ToolKind Kind => ToolKind.Magnify;

    public override void ApplyStyle(ToolStyle s) { base.ApplyStyle(s); Zoom = s.MagnifyZoom; Shape = s.Shape; }
    public override void CopyStyleTo(ToolStyle s) { base.CopyStyleTo(s); s.MagnifyZoom = Zoom; s.Shape = Shape; }

    public override void Render(DrawingContext dc, RenderContext ctx)
    {
        if (Box.Width < 2 || Box.Height < 2) return;
        double zoom = Math.Max(1, Zoom);
        var c = new Point(Box.Left + Box.Width / 2, Box.Top + Box.Height / 2);
        var src = new Rect(c.X - Box.Width / zoom / 2, c.Y - Box.Height / zoom / 2, Box.Width / zoom, Box.Height / zoom);
        src.Intersect(ctx.ImageRect);
        var geo = ShapeGeometry(Box, Shape);
        if (!src.IsEmpty && src.Width >= 1 && src.Height >= 1)
        {
            var ir = new Int32Rect((int)src.X, (int)src.Y, Math.Max(1, (int)src.Width), Math.Max(1, (int)src.Height));
            ir.Width = Math.Min(ir.Width, ctx.Base.PixelWidth - ir.X);
            ir.Height = Math.Min(ir.Height, ctx.Base.PixelHeight - ir.Y);
            if (ir.Width > 0 && ir.Height > 0)
            {
                var crop = new CroppedBitmap(ctx.Base, ir);
                crop.Freeze();
                var dest = new Rect(c.X + (ir.X - c.X) * zoom, c.Y + (ir.Y - c.Y) * zoom, ir.Width * zoom, ir.Height * zoom);
                dc.PushClip(geo);
                dc.DrawImage(crop, dest);
                dc.Pop();
            }
        }
        if (Visible(Stroke) && StrokeWidth > 0) dc.DrawGeometry(null, MakePen(Stroke, StrokeWidth), geo);
    }
}

public sealed class SpotlightAnnotation : BoxAnnotation
{
    public ShapeKind Shape { get; set; } = ShapeKind.RoundedRectangle;

    public override ToolKind Kind => ToolKind.Spotlight;

    public override void ApplyStyle(ToolStyle s) { Opacity = s.Opacity; Shape = s.Shape; Shadow = false; }
    public override void CopyStyleTo(ToolStyle s) { s.Opacity = Opacity; s.Shape = Shape; }

    public override void Render(DrawingContext dc, RenderContext ctx)
    {
        var outer = new RectangleGeometry(ctx.ImageRect);
        var dim = new CombinedGeometry(GeometryCombineMode.Exclude, outer, ShapeGeometry(Box, Shape));
        dim.Freeze();
        dc.DrawGeometry(BrushOf(Color.FromArgb((byte)Math.Clamp(Opacity * 255, 0, 255), 0, 0, 0)), null, dim);
    }
}

public sealed class ImageAnnotation : BoxAnnotation
{
    BitmapSource? _bitmap;

    public byte[] ImageData { get; set; } = Array.Empty<byte>();

    public override ToolKind Kind => ToolKind.Select;

    public BitmapSource? Bitmap => _bitmap ??= ImageUtil.TryDecode(ImageData);

    public override void ApplyStyle(ToolStyle s) { Opacity = s.Opacity; Shadow = s.Shadow; }
    public override void CopyStyleTo(ToolStyle s) { s.Opacity = Opacity; s.Shadow = Shadow; }

    public override void Render(DrawingContext dc, RenderContext ctx)
    {
        if (Bitmap == null) return;
        dc.PushOpacity(Opacity);
        dc.DrawImage(Bitmap, Box);
        dc.Pop();
    }
}

public static class TextLayout
{
    public static FormattedText Create(string text, string family, double size, bool bold, bool italic, Color color, double maxWidth, TextAlignment align)
    {
        var typeface = new Typeface(new FontFamily(family), italic ? FontStyles.Italic : FontStyles.Normal,
            bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, Math.Max(1, size), brush, 1.0)
        {
            TextAlignment = align,
        };
        if (!double.IsNaN(maxWidth) && maxWidth > 1) ft.MaxTextWidth = maxWidth;
        return ft;
    }
}

public static class GeometryUtil
{
    public static double DistanceToSegment(Point p, Point a, Point b)
    {
        var ab = b - a;
        double len2 = ab.LengthSquared;
        if (len2 < 1e-9) return (p - a).Length;
        double t = Math.Clamp(Vector.Multiply(p - a, ab) / len2, 0, 1);
        return (p - (a + ab * t)).Length;
    }

    /// <summary>Snaps the direction origin→p to the nearest 45° step.</summary>
    public static Point SnapAngle(Point origin, Point p)
    {
        var d = p - origin;
        double len = d.Length;
        if (len < 1e-6) return p;
        double angle = Math.Atan2(d.Y, d.X);
        double step = Math.PI / 4;
        angle = Math.Round(angle / step) * step;
        return new Point(origin.X + Math.Cos(angle) * len, origin.Y + Math.Sin(angle) * len);
    }
}
