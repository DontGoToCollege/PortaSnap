using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace PortaSnap.Model;

/// <summary>
/// Builds the visual tree for a document. The editor and export share these helpers so what you see is exactly
/// what gets saved: frame (drop shadow) → content (torn-edge clip) → base image + annotations, then the border.
/// </summary>
public static class DocumentRenderer
{
    static readonly Effect AnnotationShadowEffect = CreateAnnotationShadow();

    static Effect CreateAnnotationShadow()
    {
        var e = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 3, Direction = 315, Opacity = 0.45, Color = Colors.Black, RenderingBias = RenderingBias.Quality };
        e.Freeze();
        return e;
    }

    public static void RenderAnnotation(DrawingVisual v, Annotation a, RenderContext ctx)
    {
        using (var dc = v.RenderOpen())
        {
            if (a is BoxAnnotation { Rotation: not 0 } box)
            {
                var c = new Point(box.Box.Left + box.Box.Width / 2, box.Box.Top + box.Box.Height / 2);
                dc.PushTransform(new RotateTransform(box.Rotation, c.X, c.Y));
                a.Render(dc, ctx);
                dc.Pop();
            }
            else
            {
                a.Render(dc, ctx);
            }
        }
        v.Effect = a.Shadow ? AnnotationShadowEffect : null;
    }

    public static Effect? FrameEffect(SnapDocument d)
    {
        if (!d.Effects.Shadow) return null;
        var e = new DropShadowEffect
        {
            BlurRadius = d.Effects.ShadowBlur,
            ShadowDepth = d.Effects.ShadowDepth,
            Direction = 315,
            Opacity = 0.55,
            Color = d.Effects.ShadowColor,
            RenderingBias = RenderingBias.Quality,
        };
        e.Freeze();
        return e;
    }

    /// <summary>The outline of the image: a plain rectangle, or a jagged polygon when torn edges are on.</summary>
    public static Geometry ImageClip(SnapDocument d)
    {
        double w = d.Width, h = d.Height;
        if (!d.Effects.TornEdge || d.Effects.Edges == TornEdges.None)
        {
            var rg = new RectangleGeometry(new Rect(0, 0, w, h));
            rg.Freeze();
            return rg;
        }

        double amp = Math.Max(3, d.Effects.ToothSize);
        double step = amp * 1.1;
        var rng = new Random((int)(w * 31 + h * 17));
        var e = d.Effects.Edges;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(0, 0), true, true);
            if (e.HasFlag(TornEdges.Top))
                for (double x = 0; x < w; x += step) c.LineTo(new Point(x, rng.NextDouble() * amp), true, false);
            c.LineTo(new Point(w, e.HasFlag(TornEdges.Top) ? rng.NextDouble() * amp : 0), true, false);
            if (e.HasFlag(TornEdges.Right))
                for (double y = 0; y < h; y += step) c.LineTo(new Point(w - rng.NextDouble() * amp, y), true, false);
            c.LineTo(new Point(w, h), true, false);
            if (e.HasFlag(TornEdges.Bottom))
                for (double x = w; x > 0; x -= step) c.LineTo(new Point(x, h - rng.NextDouble() * amp), true, false);
            c.LineTo(new Point(0, h), true, false);
            if (e.HasFlag(TornEdges.Left))
                for (double y = h; y > 0; y -= step) c.LineTo(new Point(rng.NextDouble() * amp, y), true, false);
        }
        g.Freeze();
        return g;
    }

    public static void RenderBorder(DrawingVisual v, SnapDocument d)
    {
        using var dc = v.RenderOpen();
        if (!d.Effects.Border || d.Effects.BorderWidth <= 0) return;
        var geo = ImageClip(d);
        var brush = new SolidColorBrush(d.Effects.BorderColor);
        brush.Freeze();
        // Stroke twice the width and clip to the image so the border sits entirely inside it.
        dc.PushClip(geo);
        dc.DrawGeometry(null, new Pen(brush, d.Effects.BorderWidth * 2) { LineJoin = PenLineJoin.Miter }, geo);
        dc.Pop();
    }

    public static void RenderBase(DrawingVisual v, SnapDocument d)
    {
        using var dc = v.RenderOpen();
        dc.DrawImage(d.Base, new Rect(0, 0, d.Width, d.Height));
    }

    /// <summary>Complete off-screen visual for export (no selection chrome).</summary>
    public static ContainerVisual BuildFrame(SnapDocument d)
    {
        double pad = d.Effects.Padding;
        var ctx = new RenderContext { Base = d.Base };
        var frame = new ContainerVisual { Offset = new Vector(pad, pad), Effect = FrameEffect(d) };
        var content = new ContainerVisual { Clip = ImageClip(d) };
        var baseVisual = new DrawingVisual();
        RenderBase(baseVisual, d);
        content.Children.Add(baseVisual);
        foreach (var a in d.Annotations)
        {
            var v = new DrawingVisual();
            RenderAnnotation(v, a, ctx);
            content.Children.Add(v);
        }
        frame.Children.Add(content);
        var border = new DrawingVisual();
        RenderBorder(border, d);
        frame.Children.Add(border);
        return frame;
    }

    /// <summary>Flattens the document (image + annotations + effects) to a bitmap.</summary>
    public static BitmapSource Render(SnapDocument d)
    {
        double pad = d.Effects.Padding;
        int w = (int)Math.Ceiling(d.Width + 2 * pad), h = (int)Math.Ceiling(d.Height + 2 * pad);
        var frame = BuildFrame(d);
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(frame);
        rtb.Freeze();
        return rtb;
    }
}
