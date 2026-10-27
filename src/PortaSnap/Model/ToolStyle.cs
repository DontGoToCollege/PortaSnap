using System.Windows;
using System.Windows.Media;

namespace PortaSnap.Model;

public enum ToolKind { Select, Arrow, Line, Shape, Text, Callout, Step, Highlight, Blur, Pen, Marker, Magnify, Spotlight, Crop }
public enum ArrowHead { None, Arrow, OpenArrow, Round, Square }
public enum ShapeKind { Rectangle, RoundedRectangle, Ellipse }
public enum BlurMode { Pixelate, Blur, Solid }
public enum StepSequence { Number, UpperLetter, LowerLetter }
public enum DashKind { Solid, Dash, Dot }

/// <summary>The remembered style for one tool; new annotations start from it and edits flow back into it.</summary>
public sealed class ToolStyle
{
    public Color Stroke { get; set; } = Palette.Red;
    public Color Fill { get; set; } = Colors.Transparent;
    public Color TextColor { get; set; } = Colors.White;
    public double StrokeWidth { get; set; } = 4;
    public double Opacity { get; set; } = 1;
    public bool Shadow { get; set; }
    public DashKind Dash { get; set; }
    public ArrowHead StartHead { get; set; } = ArrowHead.None;
    public ArrowHead EndHead { get; set; } = ArrowHead.Arrow;
    public bool Tapered { get; set; } = true;
    public ShapeKind Shape { get; set; } = ShapeKind.Rectangle;
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 24;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public TextAlignment Align { get; set; } = TextAlignment.Left;
    public bool TextOutline { get; set; }
    public BlurMode BlurMode { get; set; } = BlurMode.Pixelate;
    public double Intensity { get; set; } = 12;
    public StepSequence Sequence { get; set; } = StepSequence.Number;
    public double StepSize { get; set; } = 40;
    public double MagnifyZoom { get; set; } = 2;

    public ToolStyle Clone() => (ToolStyle)MemberwiseClone();

    public static Dictionary<ToolKind, ToolStyle> Defaults() => new()
    {
        [ToolKind.Arrow] = new() { Stroke = Palette.Red, StrokeWidth = 8, Shadow = true, EndHead = ArrowHead.Arrow, Tapered = true },
        [ToolKind.Line] = new() { Stroke = Palette.Red, StrokeWidth = 4, Shadow = false, StartHead = ArrowHead.None, EndHead = ArrowHead.None, Tapered = false },
        [ToolKind.Shape] = new() { Stroke = Palette.Red, Fill = Colors.Transparent, StrokeWidth = 4, Shadow = true, Shape = ShapeKind.RoundedRectangle },
        [ToolKind.Text] = new() { Stroke = Palette.Red, Fill = Colors.Transparent, FontSize = 28, Bold = true, TextOutline = true },
        [ToolKind.Callout] = new() { Stroke = Colors.Transparent, Fill = Palette.Blue, TextColor = Colors.White, StrokeWidth = 2, FontSize = 22, Bold = true, Shadow = true, Shape = ShapeKind.RoundedRectangle, Align = TextAlignment.Center },
        [ToolKind.Step] = new() { Stroke = Palette.Red, StepSize = 40, Shadow = true, Sequence = StepSequence.Number, Shape = ShapeKind.Ellipse },
        [ToolKind.Highlight] = new() { Stroke = Palette.Yellow, Opacity = 0.45 },
        [ToolKind.Blur] = new() { BlurMode = BlurMode.Pixelate, Intensity = 12, Stroke = Colors.Black },
        [ToolKind.Pen] = new() { Stroke = Palette.Red, StrokeWidth = 4 },
        [ToolKind.Marker] = new() { Stroke = Palette.Yellow, StrokeWidth = 18, Opacity = 0.45 },
        [ToolKind.Magnify] = new() { Stroke = Palette.Gray, StrokeWidth = 3, Shadow = true, Shape = ShapeKind.Ellipse, MagnifyZoom = 2 },
        [ToolKind.Spotlight] = new() { Opacity = 0.55, Shape = ShapeKind.RoundedRectangle },
    };
}

public static class Palette
{
    public static readonly Color Red = Color.FromRgb(0xE5, 0x39, 0x35);
    public static readonly Color Orange = Color.FromRgb(0xFB, 0x8C, 0x00);
    public static readonly Color Yellow = Color.FromRgb(0xFD, 0xD8, 0x35);
    public static readonly Color Green = Color.FromRgb(0x43, 0xA0, 0x47);
    public static readonly Color Teal = Color.FromRgb(0x00, 0xAC, 0xC1);
    public static readonly Color Blue = Color.FromRgb(0x1E, 0x88, 0xE5);
    public static readonly Color Purple = Color.FromRgb(0x8E, 0x24, 0xAA);
    public static readonly Color Pink = Color.FromRgb(0xD8, 0x1B, 0x60);
    public static readonly Color Black = Color.FromRgb(0x11, 0x18, 0x27);
    public static readonly Color Gray = Color.FromRgb(0x6B, 0x72, 0x80);
    public static readonly Color White = Colors.White;

    public static readonly Color[] Swatches = { Red, Orange, Yellow, Green, Teal, Blue, Purple, Pink, Black, Gray, White };

    /// <summary>Black or white, whichever reads better on the given background.</summary>
    public static Color Contrast(Color bg)
    {
        double lum = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
        return lum > 0.6 ? Black : Colors.White;
    }
}
