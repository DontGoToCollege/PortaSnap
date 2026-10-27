using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PortaSnap.Model;

namespace PortaSnap.Editor;

/// <summary>
/// The drawing surface: hosts one visual per annotation (so edits redraw only what changed), handles tool
/// interaction (create / select / move / resize / crop) and draws selection chrome in an overlay.
/// Coordinates: one document unit = one image pixel; the shadow padding offsets everything by <see cref="Pad"/>.
/// </summary>
public sealed class EditorCanvas : FrameworkElement
{
    enum DragMode { None, Create, Move, Handle, CropNew, CropMove, CropHandle }

    static readonly Brush LightCheckerBrush = CreateChecker(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xE5, 0xE7, 0xEB));
    static readonly Brush DarkCheckerBrush = CreateChecker(Color.FromRgb(0x2A, 0x2B, 0x2F), Color.FromRgb(0x33, 0x34, 0x39));
    static readonly Brush AccentBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)));
    static readonly Brush TailBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)));
    static readonly Brush DimBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x90, 0, 0, 0)));

    readonly VisualCollection _children;
    readonly ContainerVisual _frame = new();
    readonly ContainerVisual _content = new();
    readonly DrawingVisual _baseVisual = new();
    readonly ContainerVisual _annotationLayer = new();
    readonly DrawingVisual _borderVisual = new();
    readonly DrawingVisual _overlay = new();
    readonly Dictionary<Guid, DrawingVisual> _visuals = new();

    ToolKind _tool = ToolKind.Select;
    DragMode _drag;
    Point _dragStart;
    Point _last;
    int _handle;
    bool _checkpointed;
    Annotation? _creating;
    bool _cropActive;
    Rect _crop;
    Rect _cropStart;

    public EditorCanvas()
    {
        _children = new VisualCollection(this);
        _content.Children.Add(_baseVisual);
        _content.Children.Add(_annotationLayer);
        _frame.Children.Add(_content);
        _frame.Children.Add(_borderVisual);
        _children.Add(_frame);
        _children.Add(_overlay);
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = false;
    }

    public SnapDocument? Document { get; private set; }
    public UndoStack History { get; } = new();
    public Annotation? Selected { get; private set; }
    public double ViewScale { get; set; } = 1;

    bool _darkChecker;

    /// <summary>Use the dark transparency checkerboard (dark theme).</summary>
    public bool DarkChecker
    {
        get => _darkChecker;
        set { _darkChecker = value; InvalidateVisual(); }
    }
    public Func<ToolKind, Annotation?>? CreateAnnotation { get; set; }

    /// <summary>The annotation whose text is currently being edited in a TextBox (its own text is hidden).</summary>
    public Annotation? EditingText { get; set; }

    public event EventHandler? SelectionChanged;
    public event EventHandler? DocumentChanged;
    public event EventHandler? CropChanged;
    public event EventHandler<Annotation>? TextEditRequested;
    public event EventHandler<Point>? PointerMoved;

    public double Pad => Document?.Effects.Padding ?? 0;

    public ToolKind Tool
    {
        get => _tool;
        set
        {
            if (_tool == value) return;
            CancelInteraction();
            _tool = value;
            _cropActive = false;
            if (value == ToolKind.Crop && Document != null)
            {
                _cropActive = true;
                _crop = new Rect(0, 0, Document.Width, Document.Height);
                Select(null);
            }
            RefreshOverlay();
            CropChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Rect? CropRect => _cropActive ? _crop : null;

    // ---------------- document & visuals ----------------

    public void LoadDocument(SnapDocument? doc)
    {
        CancelInteraction();
        Document = doc;
        Selected = null;
        EditingText = null;
        History.Clear();
        _cropActive = false;
        if (_tool == ToolKind.Crop && doc != null)
        {
            _cropActive = true;
            _crop = new Rect(0, 0, doc.Width, doc.Height);
        }
        RebuildAll();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    RenderContext Ctx() => new() { Base = Document!.Base, EditingText = EditingText };

    public void RebuildAll()
    {
        _annotationLayer.Children.Clear();
        _visuals.Clear();
        if (Document == null)
        {
            using (_baseVisual.RenderOpen()) { }
            using (_borderVisual.RenderOpen()) { }
            using (_overlay.RenderOpen()) { }
            _frame.Effect = null;
            InvalidateMeasure();
            InvalidateVisual();
            return;
        }
        var d = Document;
        _frame.Offset = new Vector(Pad, Pad);
        _overlay.Offset = new Vector(Pad, Pad);
        _frame.Effect = DocumentRenderer.FrameEffect(d);
        _content.Clip = DocumentRenderer.ImageClip(d);
        DocumentRenderer.RenderBase(_baseVisual, d);
        var ctx = Ctx();
        foreach (var a in d.Annotations)
        {
            var v = new DrawingVisual();
            DocumentRenderer.RenderAnnotation(v, a, ctx);
            _visuals[a.Id] = v;
            _annotationLayer.Children.Add(v);
        }
        DocumentRenderer.RenderBorder(_borderVisual, d);
        if (Selected != null && !d.Annotations.Contains(Selected))
            Selected = d.Annotations.FirstOrDefault(a => a.Id == Selected.Id);
        InvalidateMeasure();
        InvalidateVisual();
        RefreshOverlay();
    }

    public void RefreshAnnotation(Annotation a)
    {
        if (Document == null) return;
        if (_visuals.TryGetValue(a.Id, out var v)) DocumentRenderer.RenderAnnotation(v, a, Ctx());
        else RebuildAll();
        RefreshOverlay();
    }

    void AddVisual(Annotation a)
    {
        var v = new DrawingVisual();
        DocumentRenderer.RenderAnnotation(v, a, Ctx());
        _visuals[a.Id] = v;
        _annotationLayer.Children.Add(v);
    }

    void RemoveVisual(Annotation a)
    {
        if (_visuals.Remove(a.Id, out var v)) _annotationLayer.Children.Remove(v);
    }

    public void MarkChanged()
    {
        if (Document == null) return;
        Document.IsDirty = true;
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Select(Annotation? a)
    {
        if (ReferenceEquals(Selected, a)) return;
        Selected = a;
        RefreshOverlay();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---------------- commands ----------------

    public void Checkpoint()
    {
        if (Document != null) History.Checkpoint(Document);
    }

    public bool Undo() => RestoreHistory(History.Undo);
    public bool Redo() => RestoreHistory(History.Redo);

    bool RestoreHistory(Func<SnapDocument, bool> op)
    {
        if (Document == null) return false;
        var selectedId = Selected?.Id;
        if (!op(Document)) return false;
        Selected = selectedId is Guid id ? Document.Annotations.FirstOrDefault(a => a.Id == id) : null;
        RebuildAll();
        MarkChanged();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void DeleteSelected()
    {
        if (Document == null || Selected == null) return;
        Checkpoint();
        Document.Annotations.Remove(Selected);
        RemoveVisual(Selected);
        Select(null);
        MarkChanged();
    }

    public void DuplicateSelected()
    {
        if (Document == null || Selected == null) return;
        Checkpoint();
        var copy = SnapJson.Clone(Selected);
        copy.Id = Guid.NewGuid();
        copy.Offset(new Vector(14, 14));
        if (copy is StepAnnotation step) step.Number = Document.NextStepNumber();
        Document.Annotations.Add(copy);
        AddVisual(copy);
        Select(copy);
        MarkChanged();
    }

    public void AddAnnotation(Annotation a)
    {
        if (Document == null) return;
        Checkpoint();
        Document.Annotations.Add(a);
        AddVisual(a);
        Select(a);
        MarkChanged();
    }

    public void BringToFront(bool front)
    {
        if (Document == null || Selected == null) return;
        Checkpoint();
        Document.Annotations.Remove(Selected);
        if (front) Document.Annotations.Add(Selected); else Document.Annotations.Insert(0, Selected);
        RebuildAll();
        MarkChanged();
    }

    public void Nudge(double dx, double dy)
    {
        if (Selected == null) return;
        Checkpoint();
        Selected.Offset(new Vector(dx, dy));
        RefreshAnnotation(Selected);
        MarkChanged();
    }

    public void CancelInteraction()
    {
        if (_drag != DragMode.None && IsMouseCaptured) ReleaseMouseCapture();
        _drag = DragMode.None;
        _creating = null;
    }

    // ---------------- layout & rendering ----------------

    protected override int VisualChildrenCount => _children.Count;
    protected override Visual GetVisualChild(int index) => _children[index];

    protected override Size MeasureOverride(Size availableSize)
        => Document == null ? new Size(0, 0) : new Size(Document.Width + 2 * Pad, Document.Height + 2 * Pad);

    protected override Size ArrangeOverride(Size finalSize) => MeasureOverride(finalSize);

    protected override void OnRender(DrawingContext dc)
    {
        if (Document == null) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Document.Width + 2 * Pad, Document.Height + 2 * Pad));
        dc.DrawRectangle(_darkChecker ? DarkCheckerBrush : LightCheckerBrush, null, new Rect(Pad, Pad, Document.Width, Document.Height));
    }

    public void RefreshOverlay()
    {
        using var dc = _overlay.RenderOpen();
        if (Document == null) return;
        double s = Math.Max(0.05, ViewScale);
        double hs = 4.5 / s;
        double lw = 1.2 / s;
        var accentPen = new Pen(AccentBrush, lw);
        var dashPen = new Pen(AccentBrush, lw) { DashStyle = new DashStyle(new[] { 4.0, 3.0 }, 0) };

        if (_cropActive)
        {
            var img = new Rect(0, 0, Document.Width, Document.Height);
            var dim = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(img), new RectangleGeometry(_crop));
            dc.DrawGeometry(DimBrush, null, dim);
            dc.DrawRectangle(null, new Pen(Brushes.White, lw * 2), _crop);
            dc.DrawRectangle(null, dashPen, _crop);
            // Rule-of-thirds guides
            var guide = new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), lw);
            for (int i = 1; i <= 2; i++)
            {
                double x = _crop.Left + _crop.Width * i / 3, y = _crop.Top + _crop.Height * i / 3;
                dc.DrawLine(guide, new Point(x, _crop.Top), new Point(x, _crop.Bottom));
                dc.DrawLine(guide, new Point(_crop.Left, y), new Point(_crop.Right, y));
            }
            foreach (var h in BoxAnnotation.BoxHandles(_crop))
                dc.DrawRectangle(Brushes.White, accentPen, new Rect(h.X - hs, h.Y - hs, hs * 2, hs * 2));
            return;
        }

        if (Selected != null)
        {
            if (Selected is not ArrowAnnotation)
            {
                var b = Selected.Bounds;
                if (!b.IsEmpty)
                {
                    b.Inflate(2 / s, 2 / s);
                    dc.DrawRectangle(null, dashPen, b);
                }
            }
            var handles = Selected.Handles;
            for (int i = 0; i < handles.Count; i++)
            {
                var h = handles[i];
                bool tail = Selected is CalloutAnnotation && i == 8;
                if (tail || Selected is ArrowAnnotation)
                    dc.DrawEllipse(tail ? TailBrush : Brushes.White, accentPen, h, hs * 1.1, hs * 1.1);
                else
                    dc.DrawRectangle(Brushes.White, accentPen, new Rect(h.X - hs, h.Y - hs, hs * 2, hs * 2));
            }
        }
    }

    // ---------------- hit testing ----------------

    Point ToDoc(Point p) => new(p.X - Pad, p.Y - Pad);

    double Tolerance => 5 / Math.Max(0.05, ViewScale);

    public Annotation? HitTestAnnotations(Point p)
    {
        if (Document == null) return null;
        for (int i = Document.Annotations.Count - 1; i >= 0; i--)
        {
            var a = Document.Annotations[i];
            if (a.HitTest(p, Tolerance)) return a;
        }
        return null;
    }

    int HitHandle(IReadOnlyList<Point> handles, Point p)
    {
        double r = 7 / Math.Max(0.05, ViewScale);
        for (int i = handles.Count - 1; i >= 0; i--)
            if (Math.Abs(handles[i].X - p.X) <= r && Math.Abs(handles[i].Y - p.Y) <= r) return i;
        return -1;
    }

    Rect ImageRect => Document == null ? Rect.Empty : new Rect(0, 0, Document.Width, Document.Height);

    Point ClampToImage(Point p) => new(Math.Clamp(p.X, 0, Document!.Width), Math.Clamp(p.Y, 0, Document.Height));

    // ---------------- mouse ----------------

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Document == null) return;
        Focus();
        var p = ToDoc(e.GetPosition(this));
        _dragStart = _last = p;
        _checkpointed = false;

        if (_cropActive)
        {
            int ch = HitHandle(BoxAnnotation.BoxHandles(_crop), p);
            if (ch >= 0) { _drag = DragMode.CropHandle; _handle = ch; }
            else if (_crop.Contains(p)) { _drag = DragMode.CropMove; _cropStart = _crop; }
            else { _drag = DragMode.CropNew; _crop = new Rect(ClampToImage(p), ClampToImage(p)); }
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (Selected != null)
        {
            int h = HitHandle(Selected.Handles, p);
            if (h >= 0)
            {
                _drag = DragMode.Handle;
                _handle = h;
                CaptureMouse();
                e.Handled = true;
                return;
            }
        }

        var hit = HitTestAnnotations(p);
        if (_tool == ToolKind.Select)
        {
            Select(hit);
            if (hit != null)
            {
                if (e.ClickCount == 2 && hit is ITextAnnotation)
                {
                    TextEditRequested?.Invoke(this, hit);
                }
                else
                {
                    _drag = DragMode.Move;
                    CaptureMouse();
                }
            }
            e.Handled = true;
            return;
        }

        bool textTool = _tool is ToolKind.Text or ToolKind.Callout;
        if (hit != null && (ReferenceEquals(hit, Selected) || (textTool && hit is ITextAnnotation)))
        {
            Select(hit);
            if (hit is ITextAnnotation && (e.ClickCount == 2 || textTool))
            {
                TextEditRequested?.Invoke(this, hit);
            }
            else
            {
                _drag = DragMode.Move;
                CaptureMouse();
            }
            e.Handled = true;
            return;
        }

        var created = CreateAnnotation?.Invoke(_tool);
        if (created == null) return;
        Checkpoint();
        _checkpointed = true;
        created.UpdateCreate(p, p, false);
        Document.Annotations.Add(created);
        AddVisual(created);
        _creating = created;
        Select(created);
        _drag = created is StepAnnotation ? DragMode.Move : DragMode.Create;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Document == null) return;
        var p = ToDoc(e.GetPosition(this));
        PointerMoved?.Invoke(this, p);
        if (e.LeftButton != MouseButtonState.Pressed || _drag == DragMode.None)
        {
            UpdateCursor(p);
            return;
        }
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (_drag)
        {
            case DragMode.Create when _creating != null:
                _creating.UpdateCreate(_dragStart, p, shift);
                RefreshAnnotation(_creating);
                break;
            case DragMode.Move when Selected != null:
                if (p != _last)
                {
                    EnsureCheckpoint();
                    Selected.Offset(p - _last);
                    RefreshAnnotation(Selected);
                }
                break;
            case DragMode.Handle when Selected != null:
                EnsureCheckpoint();
                Selected.MoveHandle(_handle, p, shift);
                RefreshAnnotation(Selected);
                break;
            case DragMode.CropNew:
                _crop = new Rect(ClampToImage(_dragStart), ClampToImage(p));
                CropChanged?.Invoke(this, EventArgs.Empty);
                break;
            case DragMode.CropMove:
            {
                var v = p - _dragStart;
                double x = Math.Clamp(_cropStart.X + v.X, 0, Document.Width - _cropStart.Width);
                double y = Math.Clamp(_cropStart.Y + v.Y, 0, Document.Height - _cropStart.Height);
                _crop = new Rect(x, y, _cropStart.Width, _cropStart.Height);
                CropChanged?.Invoke(this, EventArgs.Empty);
                break;
            }
            case DragMode.CropHandle:
            {
                var r = BoxAnnotation.MoveBoxHandle(_crop, _handle, ClampToImage(p), shift);
                r.Intersect(ImageRect);
                if (!r.IsEmpty) _crop = r;
                CropChanged?.Invoke(this, EventArgs.Empty);
                break;
            }
        }
        _last = p;
        RefreshOverlay();
    }

    void EnsureCheckpoint()
    {
        if (_checkpointed) return;
        Checkpoint();
        _checkpointed = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_drag == DragMode.None || Document == null) return;
        ReleaseMouseCapture();
        var mode = _drag;
        _drag = DragMode.None;

        if (mode == DragMode.Create && _creating != null)
        {
            var c = _creating;
            _creating = null;
            if (c.IsDegenerate)
            {
                if (c is ITextAnnotation text)
                {
                    text.SetDefaultSize(_dragStart);
                    RefreshAnnotation(c);
                }
                else
                {
                    Document.Annotations.Remove(c);
                    RemoveVisual(c);
                    History.DropLast();
                    Select(null);
                    RefreshOverlay();
                    return;
                }
            }
            MarkChanged();
            if (c is ITextAnnotation) TextEditRequested?.Invoke(this, c);
        }
        else if (mode is DragMode.Move or DragMode.Handle)
        {
            _creating = null;
            if (_checkpointed) MarkChanged();
        }
        else if (mode is DragMode.CropNew or DragMode.CropMove or DragMode.CropHandle)
        {
            if (_crop.Width < 2 || _crop.Height < 2) _crop = ImageRect;
            CropChanged?.Invoke(this, EventArgs.Empty);
        }
        RefreshOverlay();
        e.Handled = true;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (Document == null || _cropActive) return;
        var hit = HitTestAnnotations(ToDoc(e.GetPosition(this)));
        if (hit != null) Select(hit);
    }

    void UpdateCursor(Point p)
    {
        if (Document == null) { Cursor = Cursors.Arrow; return; }
        if (_cropActive)
        {
            int ch = HitHandle(BoxAnnotation.BoxHandles(_crop), p);
            Cursor = ch >= 0 ? HandleCursor(ch) : _crop.Contains(p) ? Cursors.SizeAll : Cursors.Cross;
            return;
        }
        if (Selected != null)
        {
            int h = HitHandle(Selected.Handles, p);
            if (h >= 0)
            {
                Cursor = Selected is ArrowAnnotation || (Selected is CalloutAnnotation && h == 8) ? Cursors.Hand : HandleCursor(h);
                return;
            }
        }
        var hit = HitTestAnnotations(p);
        if (hit != null && (_tool == ToolKind.Select || ReferenceEquals(hit, Selected)))
        {
            Cursor = Cursors.SizeAll;
            return;
        }
        Cursor = _tool switch
        {
            ToolKind.Select => Cursors.Arrow,
            ToolKind.Text or ToolKind.Callout when hit is ITextAnnotation => Cursors.IBeam,
            ToolKind.Text => Cursors.IBeam,
            _ => Cursors.Cross,
        };
    }

    static Cursor HandleCursor(int i) => i switch
    {
        0 or 4 => Cursors.SizeNWSE,
        2 or 6 => Cursors.SizeNESW,
        1 or 5 => Cursors.SizeNS,
        _ => Cursors.SizeWE,
    };

    static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }

    static Brush CreateChecker(Color a, Color b)
    {
        var light = new SolidColorBrush(a);
        var dark = new SolidColorBrush(b);
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(light, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        var squares = new GeometryGroup();
        squares.Children.Add(new RectangleGeometry(new Rect(0, 0, 8, 8)));
        squares.Children.Add(new RectangleGeometry(new Rect(8, 8, 8, 8)));
        group.Children.Add(new GeometryDrawing(dark, null, squares));
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 16, 16),
            ViewportUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }
}
