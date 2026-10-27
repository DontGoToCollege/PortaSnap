using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using PortaSnap.Imaging;
using PortaSnap.Infrastructure;
using PortaSnap.Library;
using PortaSnap.Model;
using WinForms = System.Windows.Forms;

namespace PortaSnap.Editor;

public partial class EditorWindow : Window
{
    sealed record ToolDef(ToolKind Kind, string Label, string Icon, bool Filled, Key Key);

    static readonly ToolDef[] Tools =
    {
        new(ToolKind.Select, "Select", "M3,1 L3,14 L6.5,10.5 L9,15.5 L11,14.5 L8.6,9.6 L13,9.6 Z", false, Key.V),
        new(ToolKind.Arrow, "Arrow", "M2,14 L12.5,3.5 M7,3 L13,3 L13,9", false, Key.A),
        new(ToolKind.Line, "Line", "M2,14 L14,2", false, Key.L),
        new(ToolKind.Shape, "Shape", "M2,3.5 L14,3.5 L14,12.5 L2,12.5 Z", false, Key.R),
        new(ToolKind.Text, "Text", "M3,3 L13,3 M8,3 L8,14", false, Key.T),
        new(ToolKind.Callout, "Callout", "M2,2.5 L14,2.5 L14,10.5 L7,10.5 L4,14 L4,10.5 L2,10.5 Z", false, Key.O),
        new(ToolKind.Step, "Step", "M8,1.5 A6.5,6.5 0 1 1 7.99,1.5 Z M6.5,5.5 L8.5,4 L8.5,12", false, Key.S),
        new(ToolKind.Highlight, "Highlight", "M3,11 L10,4 L13,7 L6,14 L3,14 Z M1,15.5 L15,15.5", false, Key.H),
        new(ToolKind.Blur, "Blur", "M2,2 L6,2 L6,6 L2,6 Z M10,2 L14,2 L14,6 L10,6 Z M6,6 L10,6 L10,10 L6,10 Z M2,10 L6,10 L6,14 L2,14 Z M10,10 L14,10 L14,14 L10,14 Z", true, Key.B),
        new(ToolKind.Pen, "Pen", "M2,14 L3,10.5 L11,2.5 L13.5,5 L5.5,13 Z", false, Key.P),
        new(ToolKind.Marker, "Marker", "M2,13 C5,9 8,13 14,5", false, Key.K),
        new(ToolKind.Magnify, "Magnify", "M6.5,2 A4.5,4.5 0 1 1 6.49,2 Z M10,10 L14.5,14.5", false, Key.M),
        new(ToolKind.Spotlight, "Spotlight", "M8,1.5 A6.5,6.5 0 1 1 7.99,1.5 Z M8,5 A3,3 0 1 1 7.99,5 Z", false, Key.G),
        new(ToolKind.Crop, "Crop", "M4,1 L4,12 L15,12 M1,4 L12,4 L12,15", false, Key.C),
        new(ToolKind.Slice, "Slice", "M1,3 L5.5,3 L5.5,13 L1,13 M15,3 L10.5,3 L10.5,13 L15,13 M8,1 L8,15", false, Key.X),
    };

    readonly App _app;
    readonly Dictionary<ToolKind, ToggleButton> _toolButtons = new();
    SnapDocument? _doc;
    double _zoom = 1;
    enum FitMode { None, Fit, Border }
    FitMode _fitMode = FitMode.Border;
    bool _settingZoomCombo;
    const double BorderMargin = 6;
    bool _loadingProps;
    bool _draggingOut;
    DateTime _lastCheckpoint;
    object? _lastCheckpointTarget;
    TextBox? _textBox;
    Annotation? _textTarget;
    UndoStack.State? _textBefore;
    bool _textWasNew;
    Point _dragHandleStart;
    List<LibraryItem> _recent = new();
    LibraryItem? _contextItem;
    // Undo history per capture, kept while the editor stays open, so switching captures doesn't lose it.
    readonly Dictionary<string, UndoStack> _histories = new(StringComparer.OrdinalIgnoreCase);

    // Property panel controls
    TextBlock _propsTitle = null!, _propsHint = null!, _colorLabel = null!, _fillLabel = null!, _cropInfo = null!;
    StackPanel _secColor = null!, _secFill = null!, _secTextColor = null!, _secWidth = null!, _secOpacity = null!, _secShadow = null!,
        _secDash = null!, _secArrow = null!, _secShape = null!, _secText = null!, _secBlur = null!, _secStep = null!,
        _secMagnify = null!, _secCrop = null!, _secSelection = null!, _secEffects = null!;
    WrapPanel _strokeSwatches = null!, _fillSwatches = null!, _textSwatches = null!, _borderSwatches = null!;
    SliderRow _width = null!, _opacity = null!, _fontSize = null!, _intensity = null!, _stepSize = null!, _magnify = null!,
        _borderWidth = null!, _shadowBlur = null!, _tooth = null!;
    CheckBox _shadow = null!, _tapered = null!, _bold = null!, _italic = null!, _outline = null!,
        _effBorder = null!, _effShadow = null!, _effTorn = null!, _edgeTop = null!, _edgeRight = null!, _edgeBottom = null!, _edgeLeft = null!;
    StackPanel _secSlice = null!;
    ComboBox _sliceDirection = null!;
    CheckBox _sliceSnap = null!;
    ComboBox _dash = null!, _startHead = null!, _endHead = null!, _shape = null!, _font = null!, _align = null!, _blurMode = null!, _sequence = null!;

    public EditorWindow(App app)
    {
        _app = app;
        InitializeComponent();
        BuildToolStrip();
        BuildProperties();

        EditCanvas.CreateAnnotation = CreateAnnotation;
        EditCanvas.SelectionChanged += (_, _) => LoadProps();
        EditCanvas.DocumentChanged += (_, _) => { UpdateTitle(); UpdateUndo(); UpdateDocInfo(); };
        EditCanvas.HistoryChanged += (_, _) => UpdateUndo();
        EditCanvas.TextEditRequested += (_, a) => BeginTextEdit(a);
        EditCanvas.PointerMoved += (_, p) => PointerStatus.Text = _doc == null ? "" : $"{(int)p.X}, {(int)p.Y} px";
        EditCanvas.CropChanged += (_, _) => UpdateCropInfo();
        EditCanvas.SliceRequested += (_, r) => ApplySlice(r);
        EditCanvas.SliceHorizontalDefault = _app.Settings.SliceHorizontal;
        PreviewKeyUp += (_, e) =>
        {
            if (EditCanvas.Tool == ToolKind.Slice && (e.Key == Key.System ? e.SystemKey : e.Key) is Key.LeftAlt or Key.RightAlt)
            {
                EditCanvas.RefreshSlicePreview();
                e.Handled = true; // keep Alt from activating the window menu
            }
        };

        PreviewKeyDown += OnPreviewKeyDown;
        Drop += OnDrop;
        Deactivated += (_, _) => SaveIfDirty();
        Closing += (_, _) => { EndTextEdit(); SaveIfDirty(); };
        Closed += (_, _) => _app.Library.Changed -= OnLibraryChanged;
        DpiChanged += (_, _) => ApplyZoom();
        EditCanvas.DarkChecker = ThemeManager.IsDark;
        ThemeManager.Changed += OnThemeChanged;
        Closed += (_, _) => ThemeManager.Changed -= OnThemeChanged;
        _app.Library.Changed += OnLibraryChanged;

        SetupZoomDefaults();
        RefreshRecent();
        SetTool(ToolKind.Arrow);
        UpdateEnabled();
        UpdateTitle();
    }

    // ======================================================================
    // Documents
    // ======================================================================

    public void OpenDocument(SnapDocument doc)
    {
        EndTextEdit();
        if (!ReferenceEquals(doc, _doc)) SaveIfDirty();
        _doc = doc;
        if (EditCanvas.Tool == ToolKind.Crop) SetTool(ToolKind.Select);
        UndoStack? history = null;
        if (doc.LibraryPath != null && !_histories.TryGetValue(doc.LibraryPath, out history))
        {
            history = new UndoStack();
            _histories[doc.LibraryPath] = history;
        }
        EditCanvas.LoadDocument(doc, history);
        EmptyState.Visibility = Visibility.Collapsed;
        ApplyZoomTag(_app.Settings.EditorDefaultZoom, deferred: true);
        UpdateTitle();
        UpdateUndo();
        UpdateEnabled();
        LoadProps();
        HighlightRecent();
        UpdateDocInfo();
        Status(doc.Metadata.DisplayTitle);
    }

    /// <summary>Closes the current capture without saving (used after it is deleted).</summary>
    void CloseDocument()
    {
        EndTextEdit();
        _doc = null;
        EditCanvas.LoadDocument(null);
        UpdateTitle();
        UpdateUndo();
        UpdateEnabled();
        LoadProps();
        UpdateDocInfo();
    }

    /// <summary>Status-bar summary: size, when the capture was taken and when its file was last saved.</summary>
    void UpdateDocInfo()
    {
        if (_doc == null)
        {
            DocInfoText.Text = string.Empty;
            return;
        }
        var text = $"{_doc.Width} × {_doc.Height} px   ·   Created {_doc.Metadata.CapturedAt:g}";
        try
        {
            if (_doc.LibraryPath != null && File.Exists(_doc.LibraryPath))
                text += $"   ·   Updated {File.GetLastWriteTime(_doc.LibraryPath):g}";
        }
        catch { }
        if (_doc.IsDirty) text += "  (unsaved changes)";
        DocInfoText.Text = text;
        RevertButton.IsEnabled = _doc.HasOriginal;
    }

    public bool HasDocument => _doc != null;

    void OnThemeChanged(object? sender, EventArgs e)
    {
        EditCanvas.DarkChecker = ThemeManager.IsDark;
        LoadProps(); // re-marks swatches with the new accent colour
    }

    /// <summary>Opens the newest capture in the library (the first item in the Recent tray), if there is one.</summary>
    public void OpenMostRecent()
    {
        var newest = _app.Library.Scan().FirstOrDefault(i => !i.IsVideo);
        if (newest != null) OpenLibraryItem(newest);
    }

    public void SaveIfDirty()
    {
        if (_doc != null && _doc.IsDirty) SaveToLibrary(false);
    }

    void SaveToLibrary(bool announce)
    {
        if (_doc == null) return;
        EndTextEdit();
        try
        {
            _app.Library.Save(_doc);
            UpdateTitle();
            UpdateDocInfo();
            if (announce) Status("Saved to the library");
        }
        catch (Exception ex)
        {
            Log.Error("Save failed", ex);
            Status("Save failed: " + ex.Message);
        }
    }

    Annotation? CreateAnnotation(ToolKind kind)
    {
        if (_doc == null) return null;
        Annotation? a = kind switch
        {
            ToolKind.Arrow => new ArrowAnnotation(),
            ToolKind.Line => new ArrowAnnotation { IsLine = true },
            ToolKind.Shape => new ShapeAnnotation(),
            ToolKind.Text => new TextAnnotation(),
            ToolKind.Callout => new CalloutAnnotation(),
            ToolKind.Step => new StepAnnotation { Number = _doc.NextStepNumber() },
            ToolKind.Highlight => new HighlightAnnotation(),
            ToolKind.Blur => new BlurAnnotation(),
            ToolKind.Pen => new PenAnnotation(),
            ToolKind.Marker => new PenAnnotation { IsMarker = true },
            ToolKind.Magnify => new MagnifyAnnotation(),
            ToolKind.Spotlight => new SpotlightAnnotation(),
            _ => null,
        };
        a?.ApplyStyle(_app.Settings.StyleFor(kind));
        return a;
    }

    void UpdateTitle()
    {
        Title = _doc == null
            ? "PortaSnap Editor"
            : $"{(_doc.IsDirty ? "• " : "")}{_doc.Metadata.DisplayTitle} — {_doc.Width} × {_doc.Height} — PortaSnap Editor";
    }

    void UpdateUndo()
    {
        UndoButton.IsEnabled = EditCanvas.History.CanUndo;
        RedoButton.IsEnabled = EditCanvas.History.CanRedo;
    }

    void UpdateEnabled()
    {
        bool has = _doc != null;
        SaveButton.IsEnabled = has;
        ExportButton.IsEnabled = has;
        CopyButton.IsEnabled = has;
        ResizeButton.IsEnabled = has;
        CanvasButton.IsEnabled = has;
        RevertButton.IsEnabled = has && _doc!.HasOriginal;
        DragHandle.IsEnabled = has;
        DragHandle.Opacity = has ? 1 : 0.4;
        EmptyState.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
    }

    void Status(string text) => StatusText.Text = text;

    string SafeName()
    {
        if (_doc == null) return "PortaSnap";
        var title = _doc.Metadata.DisplayTitle;
        if (title.Length > 40) title = title[..40];
        var name = $"{_doc.Metadata.CapturedAt:yyyy-MM-dd_HH-mm-ss} {title}".Trim();
        return string.Concat(name.Split(System.IO.Path.GetInvalidFileNameChars()));
    }

    // ======================================================================
    // Tools
    // ======================================================================

    void BuildToolStrip()
    {
        foreach (var t in Tools)
        {
            var path = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(t.Icon),
                Width = 16,
                Height = 16,
                Stretch = Stretch.None,
                StrokeThickness = t.Kind == ToolKind.Marker ? 3 : 1.5,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            path.SetResourceReference(Shape.StrokeProperty, "TextBrush");
            if (t.Filled) path.SetResourceReference(Shape.FillProperty, "TextBrush");
            var stack = new StackPanel();
            stack.Children.Add(path);
            stack.Children.Add(new TextBlock { Text = t.Label, FontSize = 10.5, Margin = new Thickness(0, 3, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
            var b = new ToggleButton { Style = (Style)FindResource("ToolToggle"), Content = stack, ToolTip = $"{t.Label}  ({t.Key})" };
            var kind = t.Kind;
            b.Click += (_, _) => SetTool(kind);
            _toolButtons[t.Kind] = b;
            ToolStrip.Children.Add(b);
            if (t.Kind is ToolKind.Select or ToolKind.Spotlight)
            {
                var sep = new Rectangle { Width = 1, Margin = new Thickness(6, 4, 6, 4) };
                sep.SetResourceReference(Shape.FillProperty, "LineBrush");
                ToolStrip.Children.Add(sep);
            }
        }
    }

    void SetTool(ToolKind kind)
    {
        EndTextEdit();
        if (kind != ToolKind.Select) EditCanvas.Select(null);
        EditCanvas.Tool = kind;
        foreach (var (k, b) in _toolButtons) b.IsChecked = k == kind;
        LoadProps();
        if (kind == ToolKind.Crop) UpdateCropInfo();
    }

    static string KindName(ToolKind k) => k switch
    {
        ToolKind.Select => "Image",
        ToolKind.Shape => "Shape",
        ToolKind.Step => "Step",
        _ => k.ToString(),
    };

    // ======================================================================
    // Properties panel
    // ======================================================================

    sealed class SliderRow
    {
        public required StackPanel Panel { get; init; }
        public required Slider Slider { get; init; }
        public required TextBlock Value { get; init; }
        public required Func<double, string> Format { get; init; }

        public void Set(double v)
        {
            Slider.Value = Math.Clamp(v, Slider.Minimum, Slider.Maximum);
            Value.Text = Format(Slider.Value);
        }
    }

    StackPanel AddSection(string? header = null, Panel? parent = null)
    {
        var s = new StackPanel();
        if (header != null) s.Children.Add(new TextBlock { Text = header.ToUpperInvariant(), Style = (Style)FindResource("SectionHeader") });
        (parent ?? PropsPanel).Children.Add(s);
        return s;
    }

    TextBlock FieldLabel(string text) => new() { Text = text, Style = (Style)FindResource("FieldLabel") };

    SliderRow MakeSlider(string label, double min, double max, double tick, Func<double, string> fmt, Action<double> onChange)
    {
        var header = new DockPanel { Margin = new Thickness(0, 8, 0, 2) };
        var val = new TextBlock();
        val.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        DockPanel.SetDock(val, Dock.Right);
        header.Children.Add(val);
        header.Children.Add(new TextBlock { Text = label });
        var slider = new Slider { Minimum = min, Maximum = max, TickFrequency = tick, IsSnapToTickEnabled = true, SmallChange = tick, LargeChange = tick * 5 };
        slider.ValueChanged += (_, e) =>
        {
            val.Text = fmt(e.NewValue);
            if (!_loadingProps) onChange(e.NewValue);
        };
        var panel = new StackPanel();
        panel.Children.Add(header);
        panel.Children.Add(slider);
        return new SliderRow { Panel = panel, Slider = slider, Value = val, Format = fmt };
    }

    CheckBox MakeCheck(string text, Action<bool> onChange)
    {
        var c = new CheckBox { Content = text, Margin = new Thickness(0, 6, 0, 0) };
        c.Click += (_, _) => { if (!_loadingProps) onChange(c.IsChecked == true); };
        return c;
    }

    ComboBox MakeCombo(IEnumerable<string> items, Action<int> onChange)
    {
        var c = new ComboBox { Margin = new Thickness(0, 0, 0, 2) };
        foreach (var i in items) c.Items.Add(i);
        c.SelectionChanged += (_, _) => { if (!_loadingProps && c.SelectedIndex >= 0) onChange(c.SelectedIndex); };
        return c;
    }

    WrapPanel MakeSwatches(Action<Color> pick, bool allowNone, Func<Color> current)
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
        if (allowNone) wrap.Children.Add(SwatchButton(Colors.Transparent, pick, true));
        foreach (var c in Palette.Swatches) wrap.Children.Add(SwatchButton(c, pick, false));
        var more = new Button
        {
            Style = (Style)FindResource("FlatButton"),
            Content = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 13 },
            Padding = new Thickness(3),
            Margin = new Thickness(0, 0, 2, 2),
            ToolTip = "More colours…",
        };
        more.Click += (_, _) =>
        {
            var c = PickCustomColor(current());
            if (c != null) pick(c.Value);
        };
        wrap.Children.Add(more);
        return wrap;
    }

    Button SwatchButton(Color c, Action<Color> pick, bool none)
    {
        var inner = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Background = none ? Brushes.White : new SolidColorBrush(c),
        };
        inner.SetResourceReference(Border.BorderBrushProperty, "InputBorderBrush");
        if (none) inner.Child = new Line { X1 = 2, Y1 = 14, X2 = 14, Y2 = 2, Stroke = Brushes.Red, StrokeThickness = 1.5 };
        var b = new Button
        {
            Style = (Style)FindResource("FlatButton"),
            Padding = new Thickness(2),
            Margin = new Thickness(0, 0, 2, 2),
            Content = inner,
            Tag = c,
            ToolTip = none ? "None" : $"#{c.R:X2}{c.G:X2}{c.B:X2}",
        };
        b.Click += (_, _) => pick(c);
        return b;
    }

    void MarkSwatch(WrapPanel wrap, Color c)
    {
        var accent = (Brush)FindResource("AccentBrush");
        foreach (var b in wrap.Children.OfType<Button>())
            b.BorderBrush = b.Tag is Color bc && bc == c ? accent : Brushes.Transparent;
    }

    static Color? PickCustomColor(Color initial)
    {
        using var dlg = new WinForms.ColorDialog { FullOpen = true, AnyColor = true, Color = System.Drawing.Color.FromArgb(255, initial.R, initial.G, initial.B) };
        return dlg.ShowDialog() == WinForms.DialogResult.OK ? Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B) : null;
    }

    Button MakeButton(string glyph, string text, RoutedEventHandler click)
    {
        var b = new Button { Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(8, 4, 8, 4) };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
        sp.Children.Add(new TextBlock { Text = text });
        b.Content = sp;
        b.Click += click;
        return b;
    }

    void BuildProperties()
    {
        _propsTitle = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) };
        PropsPanel.Children.Add(_propsTitle);
        _propsHint = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        _propsHint.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        PropsPanel.Children.Add(_propsHint);

        _secColor = AddSection();
        _colorLabel = FieldLabel("Color");
        _secColor.Children.Add(_colorLabel);
        _strokeSwatches = MakeSwatches(c => ApplyStyleChange(s => s.Stroke = c), false, () => CurrentStyle().Stroke);
        _secColor.Children.Add(_strokeSwatches);

        _secFill = AddSection();
        _fillLabel = FieldLabel("Fill");
        _secFill.Children.Add(_fillLabel);
        _fillSwatches = MakeSwatches(c => ApplyStyleChange(s => s.Fill = c), true, () => CurrentStyle().Fill);
        _secFill.Children.Add(_fillSwatches);

        _secTextColor = AddSection();
        _secTextColor.Children.Add(FieldLabel("Text color"));
        _textSwatches = MakeSwatches(c => ApplyStyleChange(s => s.TextColor = c), false, () => CurrentStyle().TextColor);
        _secTextColor.Children.Add(_textSwatches);

        _secWidth = AddSection();
        _width = MakeSlider("Thickness", 1, 40, 1, v => $"{v:0} px", v => ApplyStyleChange(s => s.StrokeWidth = v));
        _secWidth.Children.Add(_width.Panel);

        _secOpacity = AddSection();
        _opacity = MakeSlider("Opacity", 5, 100, 5, v => $"{v:0}%", v => ApplyStyleChange(s => s.Opacity = v / 100));
        _secOpacity.Children.Add(_opacity.Panel);

        _secShadow = AddSection();
        _shadow = MakeCheck("Drop shadow", v => ApplyStyleChange(s => s.Shadow = v));
        _secShadow.Children.Add(_shadow);

        _secDash = AddSection();
        _secDash.Children.Add(FieldLabel("Line style"));
        _dash = MakeCombo(new[] { "Solid", "Dashed", "Dotted" }, i => ApplyStyleChange(s => s.Dash = (DashKind)i));
        _secDash.Children.Add(_dash);

        var heads = new[] { "None", "Arrow", "Open arrow", "Round", "Square" };
        _secArrow = AddSection();
        _secArrow.Children.Add(FieldLabel("Start"));
        _startHead = MakeCombo(heads, i => ApplyStyleChange(s => s.StartHead = (ArrowHead)i));
        _secArrow.Children.Add(_startHead);
        _secArrow.Children.Add(FieldLabel("End"));
        _endHead = MakeCombo(heads, i => ApplyStyleChange(s => s.EndHead = (ArrowHead)i));
        _secArrow.Children.Add(_endHead);
        _tapered = MakeCheck("Tapered shaft", v => ApplyStyleChange(s => s.Tapered = v));
        _secArrow.Children.Add(_tapered);

        _secShape = AddSection();
        _secShape.Children.Add(FieldLabel("Shape"));
        _shape = MakeCombo(new[] { "Rectangle", "Rounded rectangle", "Ellipse" }, i => ApplyStyleChange(s => s.Shape = (ShapeKind)i));
        _secShape.Children.Add(_shape);

        _secText = AddSection();
        _secText.Children.Add(FieldLabel("Font"));
        _font = MakeCombo(Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().OrderBy(f => f), i => ApplyStyleChange(s => s.FontFamily = (string)_font.Items[i]));
        _secText.Children.Add(_font);
        _fontSize = MakeSlider("Size", 8, 120, 1, v => $"{v:0}", v => ApplyStyleChange(s => s.FontSize = v));
        _secText.Children.Add(_fontSize.Panel);
        var styleRow = new WrapPanel();
        _bold = MakeCheck("Bold", v => ApplyStyleChange(s => s.Bold = v));
        _italic = MakeCheck("Italic", v => ApplyStyleChange(s => s.Italic = v));
        _outline = MakeCheck("Outline", v => ApplyStyleChange(s => s.TextOutline = v));
        _bold.Margin = _italic.Margin = _outline.Margin = new Thickness(0, 6, 12, 0);
        styleRow.Children.Add(_bold);
        styleRow.Children.Add(_italic);
        styleRow.Children.Add(_outline);
        _secText.Children.Add(styleRow);
        _secText.Children.Add(FieldLabel("Alignment"));
        _align = MakeCombo(new[] { "Left", "Right", "Center" }, i => ApplyStyleChange(s => s.Align = (TextAlignment)i));
        _secText.Children.Add(_align);

        _secBlur = AddSection();
        _secBlur.Children.Add(FieldLabel("Effect"));
        _blurMode = MakeCombo(new[] { "Pixelate", "Blur", "Solid (redact)" }, i => ApplyStyleChange(s => s.BlurMode = (BlurMode)i));
        _secBlur.Children.Add(_blurMode);
        _intensity = MakeSlider("Strength", 2, 40, 1, v => $"{v:0}", v => ApplyStyleChange(s => s.Intensity = v));
        _secBlur.Children.Add(_intensity.Panel);

        _secStep = AddSection();
        _secStep.Children.Add(FieldLabel("Sequence"));
        _sequence = MakeCombo(new[] { "1, 2, 3", "A, B, C", "a, b, c" }, i => ApplyStyleChange(s => s.Sequence = (StepSequence)i));
        _secStep.Children.Add(_sequence);
        _stepSize = MakeSlider("Size", 16, 140, 2, v => $"{v:0} px", v => ApplyStyleChange(s => s.StepSize = v));
        _secStep.Children.Add(_stepSize.Panel);

        _secMagnify = AddSection();
        _magnify = MakeSlider("Magnification", 1.25, 6, 0.25, v => $"{v:0.##}×", v => ApplyStyleChange(s => s.MagnifyZoom = v));
        _secMagnify.Children.Add(_magnify.Panel);

        _secCrop = AddSection();
        _cropInfo = new TextBlock { Margin = new Thickness(0, 10, 0, 8) };
        _cropInfo.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        _secCrop.Children.Add(_cropInfo);
        var cropButtons = new WrapPanel();
        var apply = new Button { Style = (Style)FindResource("PrimaryButton"), Content = "Apply crop", Margin = new Thickness(0, 0, 6, 0) };
        apply.Click += (_, _) => ApplyCrop();
        cropButtons.Children.Add(apply);
        var cancel = new Button { Style = (Style)FindResource("SecondaryButton"), Content = "Cancel" };
        cancel.Click += (_, _) => SetTool(ToolKind.Select);
        cropButtons.Children.Add(cancel);
        _secCrop.Children.Add(cropButtons);

        _secSlice = AddSection();
        _secSlice.Children.Add(FieldLabel("Direction"));
        _sliceDirection = MakeCombo(new[] { "Vertical cut (removes columns)", "Horizontal cut (removes rows)" }, i =>
        {
            _app.Settings.SliceHorizontal = i == 1;
            EditCanvas.SliceHorizontalDefault = i == 1;
            EditCanvas.RefreshSlicePreview();
            _app.SaveSettings();
        });
        _secSlice.Children.Add(_sliceDirection);
        _sliceSnap = MakeCheck("Snap the pieces together", v =>
        {
            _app.Settings.SliceSnap = v;
            _app.SaveSettings();
        });
        _sliceSnap.ToolTip = "On: the gap closes and the image gets smaller. Off: the cut-out area becomes transparent and the pieces stay apart.";
        _secSlice.Children.Add(_sliceSnap);

        _secSelection = AddSection("Selected object");
        var selButtons = new WrapPanel();
        selButtons.Children.Add(MakeButton("", "Delete", (_, _) => EditCanvas.DeleteSelected()));
        selButtons.Children.Add(MakeButton("", "Duplicate", (_, _) => EditCanvas.DuplicateSelected()));
        selButtons.Children.Add(MakeButton("", "To front", (_, _) => EditCanvas.BringToFront(true)));
        selButtons.Children.Add(MakeButton("", "To back", (_, _) => EditCanvas.BringToFront(false)));
        _secSelection.Children.Add(selButtons);

        _secEffects = AddSection("Image effects");
        _effBorder = MakeCheck("Border", v => ChangeEffects(e => e.Border = v));
        _secEffects.Children.Add(_effBorder);
        _borderSwatches = MakeSwatches(c => ChangeEffects(e => { e.BorderColor = c; e.Border = true; }), false, () => _doc?.Effects.BorderColor ?? Colors.Black);
        _borderSwatches.Margin = new Thickness(20, 4, 0, 0);
        _secEffects.Children.Add(_borderSwatches);
        _borderWidth = MakeSlider("Border width", 1, 30, 1, v => $"{v:0} px", v => ChangeEffects(e => { e.BorderWidth = v; e.Border = true; }));
        _borderWidth.Panel.Margin = new Thickness(20, 0, 0, 0);
        _secEffects.Children.Add(_borderWidth.Panel);

        _effShadow = MakeCheck("Drop shadow", v => ChangeEffects(e => e.Shadow = v));
        _effShadow.Margin = new Thickness(0, 12, 0, 0);
        _secEffects.Children.Add(_effShadow);
        _shadowBlur = MakeSlider("Shadow softness", 4, 40, 1, v => $"{v:0}", v => ChangeEffects(e => { e.ShadowBlur = v; e.Shadow = true; }));
        _shadowBlur.Panel.Margin = new Thickness(20, 0, 0, 0);
        _secEffects.Children.Add(_shadowBlur.Panel);

        _effTorn = MakeCheck("Torn edge", v => ChangeEffects(e => e.TornEdge = v));
        _effTorn.Margin = new Thickness(0, 12, 0, 0);
        _secEffects.Children.Add(_effTorn);
        var edges = new WrapPanel { Margin = new Thickness(20, 0, 0, 0) };
        _edgeTop = MakeCheck("Top", v => ChangeEdge(TornEdges.Top, v));
        _edgeRight = MakeCheck("Right", v => ChangeEdge(TornEdges.Right, v));
        _edgeBottom = MakeCheck("Bottom", v => ChangeEdge(TornEdges.Bottom, v));
        _edgeLeft = MakeCheck("Left", v => ChangeEdge(TornEdges.Left, v));
        foreach (var c in new[] { _edgeTop, _edgeRight, _edgeBottom, _edgeLeft })
        {
            c.Margin = new Thickness(0, 6, 10, 0);
            edges.Children.Add(c);
        }
        _secEffects.Children.Add(edges);
        _tooth = MakeSlider("Tear size", 4, 40, 1, v => $"{v:0} px", v => ChangeEffects(e => { e.ToothSize = v; e.TornEdge = true; }));
        _tooth.Panel.Margin = new Thickness(20, 0, 0, 0);
        _secEffects.Children.Add(_tooth.Panel);
    }

    ToolKind StyleKind() => EditCanvas.Selected?.Kind ?? EditCanvas.Tool;

    ToolStyle CurrentStyle()
    {
        var style = _app.Settings.StyleFor(StyleKind()).Clone();
        EditCanvas.Selected?.CopyStyleTo(style);
        return style;
    }

    void LoadProps()
    {
        _loadingProps = true;
        try
        {
            var sel = EditCanvas.Selected;
            var tool = EditCanvas.Tool;
            var kind = sel?.Kind ?? tool;
            bool styleVisible = sel != null || (tool != ToolKind.Select && tool != ToolKind.Crop && tool != ToolKind.Slice);
            var style = CurrentStyle();

            _propsTitle.Text = sel is ImageAnnotation ? "Image — selected"
                : sel != null ? $"{KindName(kind)} — selected"
                : tool == ToolKind.Select ? "Select"
                : tool == ToolKind.Crop ? "Crop"
                : tool == ToolKind.Slice ? "Slice" : KindName(kind);

            _propsHint.Text = tool == ToolKind.Crop
                ? "Drag on the image to choose the area to keep, then press Enter or Apply crop. Esc cancels."
                : sel == null && tool == ToolKind.Select
                    ? "Click an object to edit it. Drag to move it, drag its handles to resize, double-click text to edit it."
                    : sel == null ? ToolHint(tool) : string.Empty;
            _propsHint.Visibility = string.IsNullOrEmpty(_propsHint.Text) ? Visibility.Collapsed : Visibility.Visible;

            var visible = new HashSet<StackPanel>();
            if (styleVisible)
            {
                switch (kind)
                {
                    case ToolKind.Arrow: visible.UnionWith(new[] { _secColor, _secWidth, _secOpacity, _secShadow, _secDash, _secArrow }); break;
                    case ToolKind.Line: visible.UnionWith(new[] { _secColor, _secWidth, _secOpacity, _secShadow, _secDash, _secArrow }); break;
                    case ToolKind.Shape: visible.UnionWith(new[] { _secColor, _secFill, _secWidth, _secOpacity, _secShadow, _secDash, _secShape }); break;
                    case ToolKind.Text: visible.UnionWith(new[] { _secColor, _secFill, _secText, _secOpacity, _secShadow }); break;
                    case ToolKind.Callout: visible.UnionWith(new[] { _secFill, _secTextColor, _secColor, _secWidth, _secText, _secShape, _secShadow, _secOpacity }); break;
                    case ToolKind.Step: visible.UnionWith(new[] { _secColor, _secStep, _secShadow, _secOpacity }); break;
                    case ToolKind.Highlight: visible.UnionWith(new[] { _secColor, _secOpacity }); break;
                    case ToolKind.Blur: visible.UnionWith(new[] { _secBlur }); if (style.BlurMode == BlurMode.Solid) visible.Add(_secColor); break;
                    case ToolKind.Pen: visible.UnionWith(new[] { _secColor, _secWidth, _secOpacity, _secShadow }); break;
                    case ToolKind.Marker: visible.UnionWith(new[] { _secColor, _secWidth, _secOpacity }); break;
                    case ToolKind.Magnify: visible.UnionWith(new[] { _secColor, _secWidth, _secMagnify, _secShape, _secShadow }); break;
                    case ToolKind.Spotlight: visible.UnionWith(new[] { _secOpacity, _secShape }); break;
                    case ToolKind.Select when sel is ImageAnnotation: visible.UnionWith(new[] { _secOpacity, _secShadow }); break;
                }
            }
            if (tool == ToolKind.Crop) visible.Add(_secCrop);
            if (tool == ToolKind.Slice) visible.Add(_secSlice);
            if (sel != null) visible.Add(_secSelection);
            if (_doc != null && tool is not (ToolKind.Crop or ToolKind.Slice)) visible.Add(_secEffects);
            foreach (var s in new[] { _secColor, _secFill, _secTextColor, _secWidth, _secOpacity, _secShadow, _secDash, _secArrow, _secShape, _secText, _secBlur, _secStep, _secMagnify, _secCrop, _secSlice, _secSelection, _secEffects })
                s.Visibility = visible.Contains(s) ? Visibility.Visible : Visibility.Collapsed;

            _colorLabel.Text = kind switch
            {
                ToolKind.Text => "Text color",
                ToolKind.Shape or ToolKind.Callout => "Outline",
                ToolKind.Step => "Badge color",
                ToolKind.Magnify => "Border",
                ToolKind.Blur => "Fill color",
                _ => "Color",
            };
            _fillLabel.Text = kind == ToolKind.Text ? "Background" : "Fill";
            ((TextBlock)((DockPanel)_opacity.Panel.Children[0]).Children[1]).Text = kind == ToolKind.Spotlight ? "Dim amount" : "Opacity";

            MarkSwatch(_strokeSwatches, style.Stroke);
            MarkSwatch(_fillSwatches, style.Fill);
            MarkSwatch(_textSwatches, style.TextColor);
            _width.Set(style.StrokeWidth);
            _opacity.Set(style.Opacity * 100);
            _shadow.IsChecked = style.Shadow;
            _dash.SelectedIndex = (int)style.Dash;
            _startHead.SelectedIndex = (int)style.StartHead;
            _endHead.SelectedIndex = (int)style.EndHead;
            _tapered.IsChecked = style.Tapered;
            _shape.SelectedIndex = (int)style.Shape;
            if (!_font.Items.Contains(style.FontFamily)) _font.Items.Add(style.FontFamily);
            _font.SelectedItem = style.FontFamily;
            _fontSize.Set(style.FontSize);
            _bold.IsChecked = style.Bold;
            _italic.IsChecked = style.Italic;
            _outline.IsChecked = style.TextOutline;
            _outline.Visibility = kind == ToolKind.Text ? Visibility.Visible : Visibility.Collapsed;
            _align.SelectedIndex = (int)style.Align;
            _blurMode.SelectedIndex = (int)style.BlurMode;
            _intensity.Set(style.Intensity);
            _sequence.SelectedIndex = (int)style.Sequence;
            _stepSize.Set(style.StepSize);
            _magnify.Set(style.MagnifyZoom);
            _sliceDirection.SelectedIndex = _app.Settings.SliceHorizontal ? 1 : 0;
            _sliceSnap.IsChecked = _app.Settings.SliceSnap;

            if (_doc != null)
            {
                var fx = _doc.Effects;
                _effBorder.IsChecked = fx.Border;
                MarkSwatch(_borderSwatches, fx.BorderColor);
                _borderWidth.Set(fx.BorderWidth);
                _effShadow.IsChecked = fx.Shadow;
                _shadowBlur.Set(fx.ShadowBlur);
                _effTorn.IsChecked = fx.TornEdge;
                _edgeTop.IsChecked = fx.Edges.HasFlag(TornEdges.Top);
                _edgeRight.IsChecked = fx.Edges.HasFlag(TornEdges.Right);
                _edgeBottom.IsChecked = fx.Edges.HasFlag(TornEdges.Bottom);
                _edgeLeft.IsChecked = fx.Edges.HasFlag(TornEdges.Left);
                _tooth.Set(fx.ToothSize);
            }
        }
        finally
        {
            _loadingProps = false;
        }
    }

    static string ToolHint(ToolKind t) => t switch
    {
        ToolKind.Arrow => "Drag to draw an arrow. Hold Shift to snap to 45°. Click an existing arrow or line to select it, then drag its ends to resize.",
        ToolKind.Line => "Drag to draw a line. Hold Shift to snap to 45°. Click an existing line or arrow to select it, then drag its ends to resize.",
        ToolKind.Shape => "Drag to draw a shape. Hold Shift for a square or circle.",
        ToolKind.Text => "Click or drag to add a text box, then type. Ctrl+Enter or Esc to finish.",
        ToolKind.Callout => "Drag to add a speech bubble; drag the orange handle to aim its tail.",
        ToolKind.Step => "Click to place numbered steps; numbers count up automatically. Click an existing step to select it.",
        ToolKind.Highlight => "Drag over an area to highlight it.",
        ToolKind.Blur => "Drag over sensitive information to pixelate, blur or redact it.",
        ToolKind.Pen => "Draw freehand.",
        ToolKind.Marker => "Draw with a translucent highlighter.",
        ToolKind.Magnify => "Drag to add a magnifying lens over the area under it.",
        ToolKind.Spotlight => "Drag to spotlight an area; everything else is dimmed.",
        ToolKind.Slice => "Drag across the part of the image to cut out. Hold Alt to switch between a vertical and a horizontal cut.",
        _ => string.Empty,
    };

    /// <summary>Applies a style edit to the selection (if any) and remembers it as the tool's default.</summary>
    void ApplyStyleChange(Action<ToolStyle> change)
    {
        if (_loadingProps) return;
        var kind = StyleKind();
        change(_app.Settings.StyleFor(kind));
        var sel = EditCanvas.Selected;
        if (sel != null && _doc != null)
        {
            CheckpointThrottled(sel);
            var temp = _app.Settings.StyleFor(kind).Clone();
            sel.CopyStyleTo(temp);
            change(temp);
            sel.ApplyStyle(temp);
            if (sel is ITextAnnotation t) t.FitText();
            EditCanvas.RefreshAnnotation(sel);
            EditCanvas.MarkChanged();
        }
        _app.SaveSettings();
        LoadProps();
    }

    void CheckpointThrottled(object target)
    {
        if (!ReferenceEquals(_lastCheckpointTarget, target) || (DateTime.Now - _lastCheckpoint).TotalMilliseconds > 900)
            EditCanvas.Checkpoint();
        _lastCheckpoint = DateTime.Now;
        _lastCheckpointTarget = target;
    }

    void ChangeEffects(Action<DocumentEffects> change)
    {
        if (_loadingProps || _doc == null) return;
        CheckpointThrottled(_doc.Effects);
        change(_doc.Effects);
        _lastCheckpointTarget = _doc.Effects;
        EditCanvas.RebuildAll();
        EditCanvas.MarkChanged();
        ApplyZoom();
        LoadProps();
    }

    void ChangeEdge(TornEdges edge, bool on) => ChangeEffects(e =>
    {
        e.Edges = on ? e.Edges | edge : e.Edges & ~edge;
        e.TornEdge = e.Edges != TornEdges.None;
    });

    // ======================================================================
    // Text editing
    // ======================================================================

    void BeginTextEdit(Annotation a)
    {
        if (a is not ITextAnnotation t || _doc == null) return;
        EndTextEdit();
        _textTarget = a;
        _textWasNew = string.IsNullOrEmpty(t.Text);
        _textBefore = UndoStack.Snapshot(_doc);
        EditCanvas.EditingText = a;
        EditCanvas.RefreshAnnotation(a);

        var r = t.TextRect;
        double pad = EditCanvas.Pad;
        var color = t.TextColor;
        var tb = new TextBox
        {
            Text = t.Text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)FindResource("AccentBrush"),
            Padding = new Thickness(-2, -1, -2, 0),
            Foreground = new SolidColorBrush(color),
            CaretBrush = new SolidColorBrush(color),
            SelectionBrush = (Brush)FindResource("AccentBrush"),
            FontFamily = new FontFamily(t.FontFamily),
            FontSize = t.FontSize,
            FontWeight = t.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = t.Italic ? FontStyles.Italic : FontStyles.Normal,
            TextAlignment = t.Align,
            Width = Math.Max(40, r.Width + 2),
            MinHeight = Math.Max(t.FontSize * 1.35, r.Height + 2),
        };
        Canvas.SetLeft(tb, r.X + pad - 1);
        Canvas.SetTop(tb, r.Y + pad - 1);
        tb.LostKeyboardFocus += (_, _) => EndTextEdit();
        tb.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape || (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
            {
                e.Handled = true;
                EndTextEdit();
                EditCanvas.Focus();
            }
        };
        TextLayer.Children.Add(tb);
        _textBox = tb;
        Dispatcher.BeginInvoke(() =>
        {
            tb.Focus();
            Keyboard.Focus(tb);
            tb.SelectAll();
        }, DispatcherPriority.Input);
    }

    void EndTextEdit()
    {
        if (_textBox == null || _textTarget == null || _doc == null) return;
        var tb = _textBox;
        var a = _textTarget;
        _textBox = null;
        _textTarget = null;
        TextLayer.Children.Remove(tb);
        EditCanvas.EditingText = null;

        var t = (ITextAnnotation)a;
        var newText = tb.Text.TrimEnd();
        bool removeEmpty = string.IsNullOrWhiteSpace(newText) && a is TextAnnotation && _doc.Annotations.Contains(a);
        if (removeEmpty)
        {
            if (!_textWasNew && _textBefore != null) EditCanvas.History.Push(_textBefore);
            _doc.Annotations.Remove(a);
            if (_textWasNew) EditCanvas.MarkChanged();
            EditCanvas.Select(null);
            EditCanvas.RebuildAll();
            EditCanvas.MarkChanged();
            return;
        }
        if (newText != t.Text)
        {
            if (!_textWasNew && _textBefore != null) EditCanvas.History.Push(_textBefore);
            t.Text = newText;
            t.FitText();
            EditCanvas.MarkChanged();
        }
        EditCanvas.RefreshAnnotation(a);
    }

    // ======================================================================
    // Zoom
    // ======================================================================

    double DpiScale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    double FitZoom(FitMode mode)
    {
        if (_doc == null) return 1;
        double pad = _doc.Effects.Padding;
        // "Fit to border" fills the viewport edge to edge (enlarging small captures too);
        // "Fit" keeps a roomy margin and never enlarges past 100%.
        double margin = mode == FitMode.Border ? BorderMargin * 2 + 4 : 90;
        double availW = (Scroller.ViewportWidth > 0 ? Scroller.ViewportWidth : Scroller.ActualWidth) - margin;
        double availH = (Scroller.ViewportHeight > 0 ? Scroller.ViewportHeight : Scroller.ActualHeight) - margin;
        if (availW <= 0 || availH <= 0) return 1;
        double z = Math.Min(availW / (_doc.Width + 2 * pad), availH / (_doc.Height + 2 * pad)) * DpiScale;
        return mode == FitMode.Border ? Math.Clamp(z, 0.05, 8) : Math.Min(1.0, z);
    }

    double CurrentZoom() => _fitMode == FitMode.None ? _zoom : FitZoom(_fitMode);

    void ApplyZoom()
    {
        if (_doc == null) return;
        ZoomHost.Margin = new Thickness(_fitMode == FitMode.Border ? BorderMargin : 40);
        double z = CurrentZoom();
        double layout = z / DpiScale;
        ZoomTransform.ScaleX = ZoomTransform.ScaleY = layout;
        EditCanvas.ViewScale = layout;
        RenderOptions.SetBitmapScalingMode(EditCanvas, z >= 2 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        EditCanvas.RefreshOverlay();
        ZoomStatus.Text = $"{z * 100:0}%";
    }

    void SetZoom(double z)
    {
        _fitMode = FitMode.None;
        _zoom = Math.Clamp(z, 0.05, 8);
        ApplyZoom();
    }

    /// <summary>Applies a zoom tag ("border", "fit" or a factor) and shows it in the combo.</summary>
    void ApplyZoomTag(string tag, bool deferred = false)
    {
        var item = ZoomCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag)
                   ?? ZoomCombo.Items.OfType<ComboBoxItem>().First();
        tag = (string)item.Tag;
        _settingZoomCombo = true;
        ZoomCombo.SelectedItem = item;
        _settingZoomCombo = false;
        if (tag == "border") _fitMode = FitMode.Border;
        else if (tag == "fit") _fitMode = FitMode.Fit;
        else
        {
            _fitMode = FitMode.None;
            _zoom = double.Parse(tag, System.Globalization.CultureInfo.InvariantCulture);
        }
        if (deferred) Dispatcher.BeginInvoke(ApplyZoom, DispatcherPriority.Loaded);
        else ApplyZoom();
    }

    void ZoomCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingZoomCombo || ZoomCombo.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        ApplyZoomTag(tag);
    }

    /// <summary>Right-clicking a zoom level in the list makes it the default for every capture the editor opens.</summary>
    void SetupZoomDefaults()
    {
        foreach (var item in ZoomCombo.Items.OfType<ComboBoxItem>())
        {
            item.Uid = (string)item.Content; // remember the plain label
            item.ToolTip = "Right-click to make this the default zoom";
            item.MouseRightButtonUp += (_, e) =>
            {
                _app.Settings.EditorDefaultZoom = (string)item.Tag;
                _app.SaveSettings();
                UpdateZoomLabels();
                Status($"\"{item.Uid}\" is now the default zoom when the editor opens");
                e.Handled = true;
            };
        }
        UpdateZoomLabels();
    }

    void UpdateZoomLabels()
    {
        foreach (var item in ZoomCombo.Items.OfType<ComboBoxItem>())
            item.Content = (string)item.Tag == _app.Settings.EditorDefaultZoom ? item.Uid + "  ★" : item.Uid;
    }

    void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || _doc == null) return;
        SetZoom(e.Delta > 0 ? CurrentZoom() * 1.2 : CurrentZoom() / 1.2);
        ZoomCombo.SelectedIndex = -1;
        e.Handled = true;
    }

    void Scroller_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_fitMode != FitMode.None) ApplyZoom();
    }

    // ======================================================================
    // Commands
    // ======================================================================

    void Capture_Click(object sender, RoutedEventArgs e) => _app.Run(() => _app.Capture.CaptureImageAsync(_app.Settings.ImageType));
    void Library_Click(object sender, RoutedEventArgs e) => _app.ShowLibrary();
    void Settings_Click(object sender, RoutedEventArgs e) => _app.ShowSettings();
    void Save_Click(object sender, RoutedEventArgs e) => SaveToLibrary(true);
    void Undo_Click(object sender, RoutedEventArgs e) => UndoRedo(undo: true);
    void Redo_Click(object sender, RoutedEventArgs e) => UndoRedo(undo: false);

    void UndoRedo(bool undo)
    {
        EndTextEdit();
        if (EditCanvas.Tool is ToolKind.Crop or ToolKind.Slice) SetTool(ToolKind.Select);
        int w = _doc?.Width ?? 0, h = _doc?.Height ?? 0;
        if (!(undo ? EditCanvas.Undo() : EditCanvas.Redo())) return;
        // A crop/resize/slice changes the image size: refit the view so the restored image is shown properly.
        if (_doc != null && (_doc.Width != w || _doc.Height != h)) ApplyZoomTag(_app.Settings.EditorDefaultZoom);
        else ApplyZoom();
        LoadProps();
        UpdateTitle();
        UpdateDocInfo();
        Status(undo ? "Undone" : "Redone");
    }

    void Revert_Click(object sender, RoutedEventArgs e)
    {
        if (_doc?.OriginalPng == null) return;
        EndTextEdit();
        if (MessageBox.Show(this, "Revert to the original capture, from before it was first cropped, resized, sliced or had its canvas changed?\n\nAnnotations go back to how they were at that point. You can undo this.",
                "Revert to original", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        EditCanvas.Checkpoint();
        _doc.Base = ImageUtil.Decode(_doc.OriginalPng);
        _doc.Annotations.Clear();
        _doc.Annotations.AddRange(SnapJson.DeserializeAnnotations(_doc.OriginalAnnotationsJson ?? "[]"));
        AfterGeometryChange();
        Status("Reverted to the original capture");
    }

    void Open_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "All supported|*.psnap;*.snagx;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.mp4|PortaSnap projects|*.psnap|Snagit files|*.snagx|Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|Videos|*.mp4",
            InitialDirectory = _app.Library.Root,
        };
        if (dlg.ShowDialog(this) == true) _app.OpenFile(dlg.FileName);
    }

    void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_doc == null) return;
        EndTextEdit();
        var formats = new[] { "png", "jpg", "bmp", "gif", "tif", "psnap" };
        var dlg = new SaveFileDialog
        {
            Filter = "PNG image|*.png|JPEG image|*.jpg|Bitmap|*.bmp|GIF image|*.gif|TIFF image|*.tif|PortaSnap project (editable)|*.psnap",
            FilterIndex = Math.Max(0, Array.IndexOf(formats, _app.Settings.ExportFormat)) + 1,
            FileName = SafeName(),
            AddExtension = true,
            InitialDirectory = Directory.Exists(_app.Settings.LastExportFolder)
                ? _app.Settings.LastExportFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var ext = System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant();
            if (ext == SnapFile.Extension) SnapFile.Save(_doc, dlg.FileName);
            else ImageUtil.Save(DocumentRenderer.Render(_doc), dlg.FileName, _app.Settings.JpegQuality);
            _app.Settings.LastExportFolder = System.IO.Path.GetDirectoryName(dlg.FileName);
            _app.Settings.ExportFormat = ext.TrimStart('.') switch { "jpeg" => "jpg", "tiff" => "tif", var x => x };
            _app.SaveSettings();
            Status($"Exported {System.IO.Path.GetFileName(dlg.FileName)}");
        }
        catch (Exception ex)
        {
            Log.Error("Export failed", ex);
            MessageBox.Show(this, $"Export failed:\n\n{ex.Message}", "PortaSnap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void Copy_Click(object sender, RoutedEventArgs e) => CopyImage();

    void CopyImage()
    {
        if (_doc == null) return;
        EndTextEdit();
        Status(ClipboardUtil.CopyImage(DocumentRenderer.Render(_doc)) ? "Copied to the clipboard" : "Clipboard is busy — try again");
    }

    void Resize_Click(object sender, RoutedEventArgs e)
    {
        if (_doc == null) return;
        EndTextEdit();
        var dlg = new ResizeDialog(_doc.Width, _doc.Height) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        int w = dlg.ResultWidth, h = dlg.ResultHeight;
        if (w == _doc.Width && h == _doc.Height) return;
        EditCanvas.Checkpoint();
        _doc.RememberOriginal();
        double sx = (double)w / _doc.Width, sy = (double)h / _doc.Height;
        _doc.Base = ImageUtil.Resample(_doc.Base, w, h);
        foreach (var a in _doc.Annotations) a.Scale(sx, sy);
        AfterGeometryChange();
    }

    void Canvas_Click(object sender, RoutedEventArgs e)
    {
        if (_doc == null) return;
        EndTextEdit();
        var dlg = new CanvasDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;
        int l = dlg.PadLeft, t = dlg.PadTop, r = dlg.PadRight, b = dlg.PadBottom;
        if (l + t + r + b == 0) return;
        EditCanvas.Checkpoint();
        _doc.RememberOriginal();
        int w = _doc.Width + l + r, h = _doc.Height + t + b;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(dlg.Fill), null, new Rect(0, 0, w, h));
            dc.DrawImage(_doc.Base, new Rect(l, t, _doc.Width, _doc.Height));
        }
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        _doc.Base = rtb;
        foreach (var a in _doc.Annotations) a.Offset(new Vector(l, t));
        AfterGeometryChange();
    }

    void ApplyCrop()
    {
        if (_doc == null || EditCanvas.CropRect is not Rect r) return;
        var ir = new Int32Rect((int)Math.Round(r.X), (int)Math.Round(r.Y), (int)Math.Round(r.Width), (int)Math.Round(r.Height));
        if (ir.Width < 1 || ir.Height < 1) return;
        if (ir.X == 0 && ir.Y == 0 && ir.Width == _doc.Width && ir.Height == _doc.Height)
        {
            SetTool(ToolKind.Select);
            return;
        }
        EditCanvas.Checkpoint();
        _doc.RememberOriginal();
        _doc.Base = ImageUtil.Crop(_doc.Base, ir);
        foreach (var a in _doc.Annotations) a.Offset(new Vector(-ir.X, -ir.Y));
        SetTool(ToolKind.Select);
        AfterGeometryChange();
    }

    void AfterGeometryChange()
    {
        EditCanvas.RebuildAll();
        EditCanvas.MarkChanged();
        ApplyZoomTag(_app.Settings.EditorDefaultZoom);
        UpdateTitle();
        UpdateDocInfo();
        Status($"{_doc!.Width} × {_doc.Height}  ·  Ctrl+Z undoes it; Revert restores the original capture");
    }

    void ApplySlice(SliceRequest r)
    {
        if (_doc == null) return;
        int length = r.Horizontal ? _doc.Height : _doc.Width;
        int from = Math.Clamp((int)Math.Round(r.From), 0, length);
        int to = Math.Clamp((int)Math.Round(r.To), 0, length);
        if (to - from < 1) return;
        bool join = _app.Settings.SliceSnap;
        if (join && to - from >= length)
        {
            Status("That would cut out the whole image.");
            return;
        }
        EditCanvas.Checkpoint();
        _doc.RememberOriginal();
        _doc.Base = ImageFx.Slice(_doc.Base, r.Horizontal, from, to, join);
        if (join)
        {
            // Objects past the cut move with their piece; ones inside the removed band land on the seam.
            int cut = to - from;
            foreach (var a in _doc.Annotations)
            {
                var b = a.Bounds;
                if (b.IsEmpty) continue;
                double c = r.Horizontal ? b.Top + b.Height / 2 : b.Left + b.Width / 2;
                double shift = c >= to ? cut : c > from ? c - from : 0;
                if (shift > 0) a.Offset(r.Horizontal ? new Vector(0, -shift) : new Vector(-shift, 0));
            }
        }
        AfterGeometryChange();
        Status($"Cut out {to - from} px {(r.Horizontal ? "rows" : "columns")}" + (join ? " — pieces joined" : " — gap left transparent") + $"  ·  {_doc.Width} × {_doc.Height}");
    }

    void UpdateCropInfo()
    {
        if (EditCanvas.CropRect is Rect r)
            _cropInfo.Text = $"Keep {Math.Round(r.Width)} × {Math.Round(r.Height)} px at {Math.Round(r.X)}, {Math.Round(r.Y)}";
    }

    void Paste()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                if (files.Count > 0 && File.Exists(files[0])) _app.OpenFile(files[0]!);
                return;
            }
            if (!Clipboard.ContainsImage()) return;
            var img = Clipboard.GetImage();
            if (img == null) return;
            var bmp = ImageUtil.Detach(img);
            if (_doc == null)
            {
                var doc = new SnapDocument(bmp) { Metadata = new CaptureMetadata { WindowTitle = "Pasted image", Source = "clipboard" } };
                _app.Library.Save(doc);
                OpenDocument(doc);
                return;
            }
            double w = Math.Min(bmp.PixelWidth, _doc.Width * 0.8);
            double h = w * bmp.PixelHeight / bmp.PixelWidth;
            EditCanvas.AddAnnotation(new ImageAnnotation
            {
                Box = new Rect((_doc.Width - w) / 2, (_doc.Height - h) / 2, w, h),
                ImageData = ImageUtil.EncodePng(bmp),
            });
        }
        catch (Exception ex)
        {
            Log.Error("Paste failed", ex);
        }
    }

    // ======================================================================
    // Drag out / drop in
    // ======================================================================

    void DragHandle_MouseDown(object sender, MouseButtonEventArgs e) => _dragHandleStart = e.GetPosition(this);

    void DragHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _doc == null || _draggingOut) return;
        if ((e.GetPosition(this) - _dragHandleStart).Length < 6) return;
        EndTextEdit();
        try
        {
            _draggingOut = true;
            var img = DocumentRenderer.Render(_doc);
            var file = ClipboardUtil.WriteTempPng(img, SafeName());
            var data = new DataObject();
            data.SetFileDropList(new StringCollection { file });
            data.SetImage(img);
            DragDrop.DoDragDrop(DragHandle, data, DragDropEffects.Copy);
        }
        catch (Exception ex)
        {
            Log.Error("Drag out failed", ex);
        }
        finally
        {
            _draggingOut = false;
        }
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        if (_draggingOut) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            _app.OpenFile(files[0]);
    }

    // ======================================================================
    // Recent tray
    // ======================================================================

    void OnLibraryChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshRecent);

    void RefreshRecent()
    {
        var all = _app.Library.Scan(); // newest capture first
        if (!_app.Settings.RecentNewestFirst) all.Reverse();
        _recent = all.Take(300).ToList();
        SortText.Text = _app.Settings.RecentNewestFirst ? "Newest" : "Oldest";
        SortButton.ToolTip = _app.Settings.RecentNewestFirst
            ? "Showing newest captures first — click to show oldest first"
            : "Showing oldest captures first — click to show newest first";
        RecentList.ItemsSource = _recent;
        foreach (var i in _recent) _app.Library.EnsureLoaded(i);
        HighlightRecent();
    }

    void HighlightRecent()
    {
        RecentList.SelectedItem = _doc?.LibraryPath == null
            ? null
            : _recent.FirstOrDefault(i => string.Equals(i.Path, _doc.LibraryPath, StringComparison.OrdinalIgnoreCase));
    }

    void Sort_Click(object sender, RoutedEventArgs e)
    {
        _app.Settings.RecentNewestFirst = !_app.Settings.RecentNewestFirst;
        _app.SaveSettings();
        RefreshRecent();
        if (RecentList.SelectedItem != null) RecentList.ScrollIntoView(RecentList.SelectedItem);
        else if (_recent.Count > 0) RecentList.ScrollIntoView(_recent[0]);
    }

    /// <summary>Opens the previous/next capture in the Recent tray (Left/Right arrow keys).</summary>
    void NavigateRecent(int delta)
    {
        if (_recent.Count == 0) return;
        int index = _doc?.LibraryPath == null ? -1
            : _recent.FindIndex(i => string.Equals(i.Path, _doc.LibraryPath, StringComparison.OrdinalIgnoreCase));
        int next = index < 0 ? (delta > 0 ? 0 : _recent.Count - 1) : index + delta;
        // Skip recordings; the arrows page through images.
        while (next >= 0 && next < _recent.Count && _recent[next].IsVideo) next += Math.Sign(delta);
        if (next < 0 || next >= _recent.Count || next == index) return;
        var item = _recent[next];
        OpenLibraryItem(item);
        RecentList.ScrollIntoView(item);
    }

    void DeleteCurrentCapture()
    {
        if (_doc?.LibraryPath == null) return;
        var item = _recent.FirstOrDefault(i => string.Equals(i.Path, _doc.LibraryPath, StringComparison.OrdinalIgnoreCase))
                   ?? _app.Library.Scan().FirstOrDefault(i => string.Equals(i.Path, _doc.LibraryPath, StringComparison.OrdinalIgnoreCase));
        if (item != null) DeleteItems(new[] { item });
    }

    /// <summary>Moves captures to the Recycle Bin; if the open one goes, the next capture in the tray opens.</summary>
    void DeleteItems(IReadOnlyList<LibraryItem> items)
    {
        if (items.Count == 0) return;
        var msg = items.Count == 1
            ? $"Move \"{items[0].Title}\" ({items[0].DateText}) to the Recycle Bin?"
            : $"Move {items.Count} captures to the Recycle Bin?";
        if (!ConfirmDeleteDialog.Confirm(this, _app, msg)) return;

        bool deletingOpen = _doc?.LibraryPath != null && items.Any(i => string.Equals(i.Path, _doc.LibraryPath, StringComparison.OrdinalIgnoreCase));
        LibraryItem? next = null;
        if (deletingOpen)
        {
            int index = _recent.FindIndex(i => string.Equals(i.Path, _doc!.LibraryPath, StringComparison.OrdinalIgnoreCase));
            var remaining = _recent.Where(i => !i.IsVideo && !items.Contains(i)).ToList();
            next = remaining.FirstOrDefault(i => _recent.IndexOf(i) > index) ?? remaining.LastOrDefault();
            _doc!.IsDirty = false; // don't write it back after deleting
            _histories.Remove(_doc.LibraryPath!);
            CloseDocument();
        }
        _app.Library.Delete(items);
        if (next != null) OpenLibraryItem(next);
        Status(items.Count == 1 ? "Moved to the Recycle Bin" : $"Moved {items.Count} captures to the Recycle Bin");
    }

    void RecentList_PreviewRightDown(object sender, MouseButtonEventArgs e)
        => _contextItem = (e.OriginalSource as FrameworkElement)?.DataContext as LibraryItem;

    void RecentMenu_Opening(object sender, ContextMenuEventArgs e)
    {
        if (_contextItem == null) e.Handled = true; // right-clicked empty space
    }

    /// <summary>Gives keyboard focus back to the editor once the menu closes (otherwise keys like Delete go nowhere).</summary>
    void RecentMenu_Closing(object sender, ContextMenuEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            Activate();
            EditCanvas.Focus();
            HighlightRecent();
        }, DispatcherPriority.Input);
    }

    void RecentMenu_Open(object sender, RoutedEventArgs e) { if (_contextItem != null) OpenLibraryItem(_contextItem); }
    void RecentMenu_Reveal(object sender, RoutedEventArgs e) { if (_contextItem != null) App.RevealFile(_contextItem.Path); }
    void RecentMenu_Properties(object sender, RoutedEventArgs e) { if (_contextItem != null) LibraryItemActions.ShowProperties(_contextItem.Path, this); }
    void RecentMenu_CopyPath(object sender, RoutedEventArgs e) { if (_contextItem != null) LibraryItemActions.CopyPath(_contextItem.Path); }
    void RecentMenu_Delete(object sender, RoutedEventArgs e) { if (_contextItem != null) DeleteItems(new[] { _contextItem }); }

    void RecentMenu_Copy(object sender, RoutedEventArgs e)
    {
        if (_contextItem == null) return;
        Status(LibraryItemActions.CopyImage(_contextItem) ? "Copied to the clipboard" : "Copy failed");
    }

    void RecentMenu_Export(object sender, RoutedEventArgs e)
    {
        if (_contextItem != null && LibraryItemActions.Export(_contextItem, this, _app)) Status("Exported " + _contextItem.FileName);
    }

    void RecentList_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is LibraryItem item) OpenLibraryItem(item);
    }

    public void OpenLibraryItem(LibraryItem item)
    {
        if (item.IsVideo)
        {
            _app.ShowVideo(item.Path);
            return;
        }
        if (_doc?.LibraryPath != null && string.Equals(_doc.LibraryPath, item.Path, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            SaveIfDirty();
            OpenDocument(SnapFile.Load(item.Path));
        }
        catch (Exception ex)
        {
            Log.Error($"Open failed: {item.Path}", ex);
            Status("Could not open " + item.FileName + ": " + ex.Message);
        }
    }

    // ======================================================================
    // Keyboard
    // ======================================================================

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_textBox != null && _textBox.IsKeyboardFocusWithin) return;
        if (EditCanvas.Tool == ToolKind.Slice && (e.Key == Key.System ? e.SystemKey : e.Key) is Key.LeftAlt or Key.RightAlt)
        {
            EditCanvas.RefreshSlicePreview();
            e.Handled = true;
            return;
        }
        var focused = Keyboard.FocusedElement;
        bool inInput = focused is TextBox or ComboBox or ComboBoxItem or Slider;
        var mods = Keyboard.Modifiers;
        bool ctrl = mods.HasFlag(ModifierKeys.Control);
        bool shift = mods.HasFlag(ModifierKeys.Shift);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (ctrl)
        {
            switch (key)
            {
                case Key.Z: if (shift) Redo_Click(this, e); else Undo_Click(this, e); break;
                case Key.Y: Redo_Click(this, e); break;
                case Key.S: SaveToLibrary(true); break;
                case Key.E: Export_Click(this, e); break;
                case Key.O: Open_Click(this, e); break;
                case Key.C when shift || !inInput: CopyImage(); break;
                case Key.V when !inInput: Paste(); break;
                case Key.D: EditCanvas.DuplicateSelected(); break;
                case Key.OemPlus:
                case Key.Add: SetZoom(CurrentZoom() * 1.25); ZoomCombo.SelectedIndex = -1; break;
                case Key.OemMinus:
                case Key.Subtract: SetZoom(CurrentZoom() / 1.25); ZoomCombo.SelectedIndex = -1; break;
                case Key.D0:
                case Key.NumPad0: ApplyZoomTag(_app.Settings.EditorDefaultZoom); break;
                case Key.D1:
                case Key.NumPad1: ApplyZoomTag("1"); break;
                default: return;
            }
            e.Handled = true;
            return;
        }
        if (inInput || _doc == null) return;

        double step = shift ? 10 : 1;
        switch (key)
        {
            case Key.Delete when EditCanvas.Selected == null:
                DeleteCurrentCapture();
                break;
            case Key.Delete:
            case Key.Back:
                EditCanvas.DeleteSelected();
                break;
            case Key.Escape:
                if (EditCanvas.Tool == ToolKind.Crop) SetTool(ToolKind.Select);
                else EditCanvas.Select(null);
                break;
            case Key.Enter:
                if (EditCanvas.Tool == ToolKind.Crop) ApplyCrop();
                else if (EditCanvas.Selected is ITextAnnotation) BeginTextEdit(EditCanvas.Selected);
                else return;
                break;
            case Key.Left when EditCanvas.Selected == null: NavigateRecent(-1); break;
            case Key.Right when EditCanvas.Selected == null: NavigateRecent(1); break;
            case Key.Left when EditCanvas.Selected != null: EditCanvas.Nudge(-step, 0); break;
            case Key.Right when EditCanvas.Selected != null: EditCanvas.Nudge(step, 0); break;
            case Key.Up when EditCanvas.Selected != null: EditCanvas.Nudge(0, -step); break;
            case Key.Down when EditCanvas.Selected != null: EditCanvas.Nudge(0, step); break;
            default:
                if (mods == ModifierKeys.None && Tools.FirstOrDefault(t => t.Key == key) is { } tool)
                {
                    SetTool(tool.Kind);
                    break;
                }
                return;
        }
        e.Handled = true;
    }
}
