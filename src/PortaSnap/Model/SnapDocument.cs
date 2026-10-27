using System.Windows.Media;
using System.Windows.Media.Imaging;
using PortaSnap.Imaging;

namespace PortaSnap.Model;

public sealed class CaptureMetadata
{
    public DateTime CapturedAt { get; set; } = DateTime.Now;
    public string? WindowTitle { get; set; }
    public string? AppName { get; set; }
    public string? Source { get; set; }
    public string? SourcePath { get; set; }
    public string? Url { get; set; }

    public string DisplayTitle => !string.IsNullOrWhiteSpace(WindowTitle) ? WindowTitle!
        : !string.IsNullOrWhiteSpace(AppName) ? AppName! : "Capture";
}

[Flags]
public enum TornEdges { None = 0, Top = 1, Right = 2, Bottom = 4, Left = 8 }

/// <summary>Whole-image effects (Snagit's "Effects" panel): border, drop shadow, torn edge.</summary>
public sealed class DocumentEffects
{
    public bool Border { get; set; }
    public Color BorderColor { get; set; } = Color.FromRgb(0x37, 0x41, 0x51);
    public double BorderWidth { get; set; } = 3;
    public bool Shadow { get; set; }
    public Color ShadowColor { get; set; } = Colors.Black;
    public double ShadowBlur { get; set; } = 18;
    public double ShadowDepth { get; set; } = 6;
    public bool TornEdge { get; set; }
    public TornEdges Edges { get; set; } = TornEdges.Bottom;
    public double ToothSize { get; set; } = 12;

    /// <summary>Extra space around the image needed so the shadow is not clipped.</summary>
    public double Padding => Shadow ? Math.Ceiling(ShadowBlur + ShadowDepth + 2) : 0;

    public DocumentEffects Clone() => (DocumentEffects)MemberwiseClone();
}

public sealed class SnapDocument
{
    BitmapSource _base;
    byte[]? _basePng;

    public SnapDocument(BitmapSource baseImage)
    {
        _base = baseImage;
    }

    /// <summary>The captured pixels. One canvas unit equals one pixel of this image.</summary>
    public BitmapSource Base
    {
        get => _base;
        set { _base = value; _basePng = null; }
    }

    public List<Annotation> Annotations { get; } = new();
    public DocumentEffects Effects { get; set; } = new();
    public CaptureMetadata Metadata { get; set; } = new();
    public string? LibraryPath { get; set; }
    public bool IsDirty { get; set; }

    public int Width => _base.PixelWidth;
    public int Height => _base.PixelHeight;

    public byte[] GetBasePng() => _basePng ??= ImageUtil.EncodePng(_base);
    internal void SetBasePngCache(byte[] png) => _basePng = png;

    /// <summary>
    /// The capture as it was before the first crop / resize / canvas / slice (PNG + annotations JSON), kept in the
    /// .psnap so destructive edits can always be reverted, even after the undo history is gone.
    /// </summary>
    public byte[]? OriginalPng { get; set; }
    public string? OriginalAnnotationsJson { get; set; }
    public bool HasOriginal => OriginalPng != null;

    public void RememberOriginal()
    {
        if (OriginalPng != null) return;
        OriginalPng = GetBasePng();
        OriginalAnnotationsJson = SnapJson.SerializeAnnotations(Annotations);
    }

    public int NextStepNumber() => Annotations.OfType<StepAnnotation>().Select(s => s.Number).DefaultIfEmpty(0).Max() + 1;
}
