using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Snapr.App.Rendering;
using Snapr.Core;

namespace Snapr.App.Editor;

/// <summary>
/// Draws the document with DocumentRenderer (the same code as export) and turns mouse input into edits.
/// Units are image pixels; the window zooms this element with a LayoutTransform.
/// It never changes the document itself: it raises Committed and the window applies the edit.
/// </summary>
public sealed class EditorCanvas : Canvas
{
    static readonly Brush Accent = ImageIo.Brush("#FF3B82F6");
    static readonly Brush CropDim = ImageIo.Brush("#99000000");
    static readonly Brush BoxFill = ImageIo.Brush("#1A3B82F6");

    BitmapSource? _base;
    DocumentState _state = DocumentState.Empty;
    DocumentState? _preview;
    Pt _down;
    IReadOnlyList<Guid> _selected = [];
    bool _moved;                 // a Move drag has passed the system drag distance (otherwise it is still a click)
    Guid _stampId = Guid.NewGuid();   // one Id for the whole Stamp drag, so the picture cache decodes once; fresh per placement
    bool _dragging;            // moving the selection (or resizing _resizeOriginal)
    Annotation? _resizeOriginal;
    Rct? _box;                   // selection box being dragged
    bool _additive;              // Shift held when the box started
    Annotation? _clickHit;       // object under a plain mouse-down; a click without movement narrows the selection to it
    List<Pt>? _pen;
    Rct? _cropDrag;
    TextBox? _textEditor;
    Annotation? _textTarget;
    (int Start, int End)? _cutDrag;
    bool _textIsNew;

    /// <summary>A finished edit. A non-null merge key lets the window merge repeated edits (slider drags) into one undo step.</summary>
    public event Action<DocumentState, string?>? Committed;
    public event Action? SelectionChanged;
    public event Action<string>? Message;
    /// <summary>A note for the editor's info banner.</summary>
    public event Action<string>? Notice;
    public bool CutVertical { get; set; }
    public CutJoin CutJoin { get; set; } = CutJoin.Torn;

    ToolKind _tool = ToolKind.Arrow;
    public ToolKind Tool
    {
        get => _tool;
        set
        {
            if (value != ToolKind.Selection) SetPixelSelection(null);
            _tool = value;
        }
    }

    Rct? _pixelSelection;
    bool _movingPixels;
    (int Dx, int Dy) _pixelOffset;
    uint _moveFillArgb;

    /// <summary>The Selection tool's pixel rectangle in image pixels, or null.</summary>
    public Rct? PixelSelection => _pixelSelection;
    /// <summary>Colour that fills the gap left by moving or clearing pixels; null = the surrounding colour.</summary>
    public string? GapFill { get; set; }
    public event Action? PixelSelectionChanged;

    void SetPixelSelection(Rct? r)
    {
        if (_pixelSelection == r) return;
        _pixelSelection = r;
        PixelSelectionChanged?.Invoke();
        InvalidateVisual();
    }

    public void ClearPixelSelection() => SetPixelSelection(null);

    /// <summary>Ctrl+A in the Selection tool: the whole view (crop, or the image) becomes the pixel selection.</summary>
    public void SelectAllPixels()
    {
        if (_base is null) return;
        var v = View;
        SetPixelSelection(SelectionRect.FromDrag(new Pt(v.X, v.Y), new Pt(v.Right, v.Bottom), _base.PixelWidth, _base.PixelHeight));
    }

    bool _ownEdit;   // committing our own pixel edit: the selection survives the image change

    void CommitOwn(DocumentState next)
    {
        _ownEdit = true;
        try { Commit(next); }
        finally { _ownEdit = false; }
    }

    (int W, int H) ImageSize => (_base!.PixelWidth, _base.PixelHeight);

    /// <summary>True, after showing the limit message, when the document can take no more image edits (spec parity 16 §3.5).</summary>
    public bool EditsFull()
    {
        if (_state.Edits.Count < ImageEditRules.MaxEdits) return false;
        Message?.Invoke(ImageEditRules.LimitMessage);
        return true;
    }

    public bool ClearSelectedPixels()
    {
        if (_base is null || _pixelSelection is not { } sel) return false;
        if (EditsFull()) return true;
        CommitOwn(_state.ApplyEdit(ImageSize, new ClearAreaEdit(sel, GapFill)));
        return true;
    }

    /// <summary>What is visible inside the selection (image plus objects such as redaction covers, no effects), or null with no selection.</summary>
    public BitmapSource? SelectedPixels() =>
        _base is null || _pixelSelection is not { } s ? null : DocumentRenderer.Render(_base, _state with { Crop = s }, applyEffects: false);

    public void CropToPixelSelection()
    {
        if (_base is null || _pixelSelection is not { } sel) return;
        Commit(_state.SetCrop(sel, _base.PixelWidth, _base.PixelHeight));
        SetPixelSelection(null);
    }

    uint MoveFill(Rct sel) =>
        GapFill is not null && ColorText.TryArgb(GapFill, out var argb) ? argb : PixelEdits.EdgeColour(ImageIo.ToFrame(_base!), sel);
    public StampChoice Stamp { get; set; } = StampChoice.Default;
    public ToolStyle CurrentStyle { get; set; } = ToolStyle.Default;
    public ToolOptions Options { get; set; } = ToolOptions.Default;
    public double Zoom { get; set; } = 1;
    public DocumentState State => _state;
    /// <summary>Selected object IDs, always including whole groups, in document order.</summary>
    public IReadOnlyList<Guid> SelectedIds => _selected;
    public IReadOnlyList<Annotation> SelectedObjects => _state.Annotations.Where(a => _selected.Contains(a.Id)).ToArray();
    /// <summary>The selected object when exactly one is selected; null when none or several are.</summary>
    public Annotation? Selected => _selected.Count == 1 ? _state.Annotations.FirstOrDefault(a => a.Id == _selected[0]) : null;
    public Rct View => _state.Crop ?? (_base is null ? default : new Rct(0, 0, _base.PixelWidth, _base.PixelHeight));

    public EditorCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        Background = Brushes.Transparent;
    }

    public void Load(BitmapSource baseImage, DocumentState state)
    {
        CancelTextEditor();
        _base = baseImage;
        SetPixelSelection(null);
        _selected = [];
        SetState(state);
        SelectionChanged?.Invoke();
    }

    /// <summary>Replaces the image under the objects (after an image edit, undo or redo) without touching the selection.</summary>
    public void SetImage(BitmapSource image)
    {
        if (!ReferenceEquals(_base, image) && !_ownEdit) SetPixelSelection(null);   // undo/redo clear it from the window
        _base = image;
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void SetState(DocumentState state)
    {
        _state = state;
        _preview = null;
        var kept = state.ExpandToGroups(_selected);
        if (!kept.SequenceEqual(_selected))
        {
            _selected = kept;
            SelectionChanged?.Invoke();
        }
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void Select(Annotation? a) => SelectIds(a is null ? Array.Empty<Guid>() : new[] { a.Id });

    /// <summary>Selects these objects and every member of their groups.</summary>
    public void SelectIds(IEnumerable<Guid> ids)
    {
        _selected = _state.ExpandToGroups(ids);
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    public void SelectAll() => SelectIds(_state.HitInBox(View));

    public void Apply(Func<DocumentState, DocumentState> edit, string? mergeKey = null) => Commit(edit(_state), mergeKey);

    /// <summary>Applies the edit to every selected object as one undo step.</summary>
    public void ApplyToSelection(Func<Annotation, Annotation> edit, string? mergeKey = null)
    {
        if (_selected.Count == 0) return;
        Commit(_state with { Annotations = _state.Annotations.Select(a => _selected.Contains(a.Id) ? edit(a) : a).ToArray() }, mergeKey);
    }

    public void DeleteSelected()
    {
        if (_selected.Count == 0) return;
        Commit(_state.RemoveMany(_selected));
        Select(null);
    }

    /// <summary>Moves the selection; repeated nudges merge into one undo step.</summary>
    public void Nudge(double dx, double dy)
    {
        if (_selected.Count > 0) Commit(_state.TranslateMany(_selected, dx, dy), "nudge");
    }

    void Commit(DocumentState next, string? mergeKey = null)
    {
        if (!next.SameAs(_state)) Committed?.Invoke(next, mergeKey);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        base.MeasureOverride(constraint);
        return new Size(View.W, View.H);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        base.ArrangeOverride(arrangeSize);
        return new Size(View.W, View.H);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_base is null) return;
        var v = View;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, v.W, v.H)));
        dc.PushTransform(new TranslateTransform(-v.X, -v.Y));
        var state = _preview ?? _state;
        DocumentRenderer.DrawScene(dc, _base, state);
        if (_cropDrag is { } c) DrawCropPreview(dc, c);
        if (_cutDrag is { } cut) DrawCutPreview(dc, cut);
        if (_box is { } box) dc.DrawRectangle(BoxFill, new Pen(Accent, 1 / Zoom) { DashStyle = DashStyles.Dash }, ToRect(box));
        DrawPixelSelection(dc);
        DrawSelection(dc, state);
        dc.Pop();
        dc.Pop();
    }

    void DrawCropPreview(DrawingContext dc, Rct c)
    {
        var v = View;
        var outside = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(v.X, v.Y, v.W, v.H)), new RectangleGeometry(new Rect(c.X, c.Y, c.W, c.H)));
        dc.DrawGeometry(CropDim, null, outside);
        dc.DrawRectangle(null, new Pen(Accent, 2 / Zoom), new Rect(c.X, c.Y, c.W, c.H));
    }

    void DrawPixelSelection(DrawingContext dc)
    {
        if (Tool != ToolKind.Selection || _pixelSelection is not { } sel) return;
        var shown = sel;
        if (_movingPixels)
        {
            // Gap fill over the source, then the selected pixels at the new spot. A transparent fill shows as grey here.
            var fill = _moveFillArgb >> 24 == 0 ? Color.FromArgb(0x80, 0x80, 0x80, 0x80)
                : Color.FromArgb((byte)(_moveFillArgb >> 24), (byte)(_moveFillArgb >> 16), (byte)(_moveFillArgb >> 8), (byte)_moveFillArgb);
            dc.DrawRectangle(new SolidColorBrush(fill), null, ToRect(sel));
            shown = sel.Offset(_pixelOffset.Dx, _pixelOffset.Dy);
            var piece = new CroppedBitmap(_base!, new Int32Rect((int)sel.X, (int)sel.Y, (int)sel.W, (int)sel.H));
            dc.DrawImage(piece, ToRect(shown));
        }
        var r = ToRect(shown);
        dc.DrawRectangle(null, new Pen(Brushes.White, 1 / Zoom), r);
        dc.DrawRectangle(null, new Pen(Brushes.Black, 1 / Zoom) { DashStyle = DashStyles.Dash }, r);
        var label = new FormattedText($"{(int)shown.W} × {(int)shown.H}", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 12 / Zoom, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var at = new Point(r.X, Math.Max(View.Y, r.Y - label.Height - 2 / Zoom));
        dc.DrawRectangle(CropDim, null, new Rect(at, new Size(label.Width, label.Height)));
        dc.DrawText(label, at);
    }

    void DrawCutPreview(DrawingContext dc, (int Start, int End) cut)
    {
        var v = _base is null ? default : new Rct(0, 0, _base.PixelWidth, _base.PixelHeight);
        var band = CutVertical ? new Rect(cut.Start, v.Y, cut.End - cut.Start, v.H) : new Rect(v.X, cut.Start, v.W, cut.End - cut.Start);
        dc.DrawRectangle(CutFill, null, band);
        var hatch = new Pen(CutLine, 1 / Zoom);
        foreach (var (a, b) in CutHatch.Lines(band, 8 / Zoom)) dc.DrawLine(hatch, a, b);
        var edge = new Pen(CutLine, 1.5 / Zoom);
        if (CutVertical)
        {
            dc.DrawLine(edge, band.TopLeft, band.BottomLeft);
            dc.DrawLine(edge, band.TopRight, band.BottomRight);
        }
        else
        {
            dc.DrawLine(edge, band.TopLeft, band.TopRight);
            dc.DrawLine(edge, band.BottomLeft, band.BottomRight);
        }
    }

    static readonly Brush CutFill = Frozen(new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0x3B, 0x30)));
    static readonly Brush CutLine = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30)));
    static Brush Frozen(Brush b) { b.Freeze(); return b; }

    double HandleSize => 10 / Zoom;

    void DrawSelection(DrawingContext dc, DocumentState state)
    {
        var selected = state.Annotations.Where(a => _selected.Contains(a.Id)).ToArray();
        if (selected.Length == 0) return;
        var dashed = new Pen(Accent, 1.5 / Zoom) { DashStyle = DashStyles.Dash };
        var all = default(Rct);
        foreach (var a in selected)
        {
            var b = AnnotationGeometry.Bounds(a).Inflate(4 / Zoom);
            all = all.Union(b);
            dc.DrawRectangle(null, dashed, ToRect(b));
        }
        if (selected.Length > 1) dc.DrawRectangle(null, new Pen(Accent, 1 / Zoom), ToRect(all.Inflate(4 / Zoom)));
        if (ResizeTarget(state) is { } r && AnnotationGeometry.ResizeHandle(r) is { } h)
            dc.DrawRectangle(Brushes.White, new Pen(Accent, 1.5 / Zoom), new Rect(h.X - HandleSize / 2, h.Y - HandleSize / 2, HandleSize, HandleSize));
    }

    static Rect ToRect(Rct r) => new(r.X, r.Y, Math.Max(0, r.W), Math.Max(0, r.H));

    /// <summary>The object whose resize handle is shown: only when the selection is exactly one ungrouped object.</summary>
    Annotation? ResizeTarget(DocumentState state) =>
        _selected.Count == 1 && state.Annotations.FirstOrDefault(a => a.Id == _selected[0]) is { GroupId: null } a ? a : null;

    bool OnHandle(Pt p) =>
        ResizeTarget(_state) is { } s && AnnotationGeometry.ResizeHandle(s) is { } h &&
        Math.Abs(p.X - h.X) <= HandleSize && Math.Abs(p.Y - h.Y) <= HandleSize;

    Pt ToImage(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        return new Pt(p.X + View.X, p.Y + View.Y);
    }

    Cursor ToolCursor => Tool switch
    {
        ToolKind.Move => Cursors.Arrow,
        ToolKind.Text => Cursors.IBeam,
        ToolKind.Eraser => Cursors.Hand,
        _ => Cursors.Cross,
    };

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_base is null || _textEditor is { IsMouseOver: true }) return;
        CommitTextEditor();
        Focus();
        var p = ToImage(e);
        _down = p;
        e.Handled = true;
        switch (Tool)
        {
            case ToolKind.Move:
                var onHandle = OnHandle(p);
                var hit = onHandle ? ResizeTarget(_state) : _state.HitTest(p, 6 / Zoom);
                if (e.ClickCount == 2 && hit is TextAnnotation or CalloutAnnotation)
                {
                    Select(hit);
                    BeginTextEdit(hit, isNew: false);
                    return;
                }
                var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                if (hit is null)
                {
                    if (!shift) Select(null);
                    _additive = shift;
                    _box = new Rct(p.X, p.Y, 0, 0);
                    break;
                }
                if (shift)
                {
                    SelectIds(_state.Toggle(_selected, hit.Id));
                    return;
                }
                if (!_selected.Contains(hit.Id)) Select(hit);
                _resizeOriginal = onHandle ? hit : null;
                _clickHit = onHandle ? null : hit;
                _dragging = true;
                break;
            case ToolKind.Text:
                BeginTextEdit(AnnotationFactory.Text(p, CurrentStyle), isNew: true);
                return;
            case ToolKind.Pen:
                _pen = [p];
                break;
            case ToolKind.Selection:
                if (_pixelSelection is { } sel && sel.Contains(p))
                {
                    _movingPixels = true;
                    _pixelOffset = (0, 0);
                    _moveFillArgb = MoveFill(sel);
                }
                else SetPixelSelection(null);
                break;
        }
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_base is null) return;
        var p = ToImage(e);
        Cursor = Tool == ToolKind.Move && OnHandle(p) ? Cursors.SizeNWSE
            : Tool == ToolKind.Selection && (_movingPixels || !IsMouseCaptured && _pixelSelection is { } over && over.Contains(p)) ? Cursors.SizeAll
            : ToolCursor;
        if (!IsMouseCaptured) return;
        switch (Tool)
        {
            case ToolKind.Move when _box is not null:
                _box = Rct.FromPoints(_down, p);
                break;
            case ToolKind.Move when _dragging:
                if (_resizeOriginal is null && !_moved)
                {
                    if (Math.Abs(p.X - _down.X) < SystemParameters.MinimumHorizontalDragDistance / Zoom
                        && Math.Abs(p.Y - _down.Y) < SystemParameters.MinimumVerticalDragDistance / Zoom) break;   // still a click
                    _moved = true;
                }
                _preview =_resizeOriginal is { } r
                    ? _state.Replace(AnnotationGeometry.ResizeTo(r, p))
                    : _state.TranslateMany(_selected, p.X - _down.X, p.Y - _down.Y);
                break;
            case ToolKind.Pen when _pen is not null:
                _pen.Add(p);
                _preview = _state.Add(AnnotationFactory.Pen(_pen.ToArray(), CurrentStyle));
                break;
            case ToolKind.Crop:
                _cropDrag = Rct.FromPoints(_down, p);
                break;
            case ToolKind.CutOut:
                _cutDrag = IsClick(p) ? null : Strip(_down, p);
                break;
            case ToolKind.Selection when _movingPixels:
                int dx = (int)Math.Round(p.X - _down.X), dy = (int)Math.Round(p.Y - _down.Y);
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { if (Math.Abs(dx) >= Math.Abs(dy)) dy = 0; else dx = 0; }
                _pixelOffset = (dx, dy);
                break;
            case ToolKind.Selection:
                var drag = SelectionRect.FromDrag(_down, p, _base.PixelWidth, _base.PixelHeight)?.Intersect(View);   // only what is visible
                SetPixelSelection(drag is { W: >= 2, H: >= 2 } ? drag : null);
                break;
            case ToolKind.Move or ToolKind.Step or ToolKind.Eraser or ToolKind.Pen or ToolKind.Fill:
                break;
            case ToolKind.Stamp:
                _preview = IsClick(p) ? null : _state.Add(Stamp.Create(StampPlacement.FromDrag(_down, p, Stamp.Aspect), CurrentStyle, _stampId));
                break;
            default:
                _preview = AnnotationFactory.FromDrag(Tool, _down, p, CurrentStyle, Options) is { } a ? _state.Add(a) : null;
                break;
        }
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured) return;
        var p = ToImage(e);
        switch (Tool)
        {
            case ToolKind.Move when _box is { } box:
                var visible = box.Intersect(View);
                if (!visible.IsEmpty && (box.W * Zoom >= 3 || box.H * Zoom >= 3))   // smaller than 3 × 3 screen px = a click on empty space
                {
                    var hits = _state.HitInBox(visible);
                    SelectIds(_additive ? _selected.Concat(hits) : hits);
                }
                break;
            case ToolKind.Move:
                if (_preview is not null) Commit(_preview);
                else if (_clickHit is not null && !_moved) Select(_clickHit);
                break;
            case ToolKind.Pen:
                if (_pen is { Count: > 1 }) Commit(_state.Add(AnnotationFactory.Pen(_pen.ToArray(), CurrentStyle)));
                break;
            case ToolKind.Step:
                Commit(_state.AddStep(p, CurrentStyle, Options.Steps));
                break;
            case ToolKind.Eraser:
                if (_state.HitTest(p, 6 / Zoom) is { } hit) Commit(_state.Remove(hit.Id));
                break;
            case ToolKind.Fill:
                if (IsClick(p)) TryFill(p);
                break;
            case ToolKind.Stamp:
                var stamp = Stamp.Create(IsClick(p) ? StampPlacement.Centered(_down, Stamp.Aspect) : StampPlacement.FromDrag(_down, p, Stamp.Aspect), CurrentStyle, _stampId);
                Commit(_state.Add(stamp));
                Select(stamp);   // the Stamp tool stays active (spec parity 6 §2.4)
                break;
            case ToolKind.Crop:
                if (_cropDrag is { W: >= 4, H: >= 4 } c) Commit(_state.SetCrop(c, _base!.PixelWidth, _base.PixelHeight));
                break;
            case ToolKind.CutOut:
                if (!IsClick(p) && Strip(_down, p) is { } strip && strip.End - strip.Start >= 2)
                {
                    if (EditsFull()) break;
                    var size = (_base!.PixelWidth, _base.PixelHeight);
                    var next = _state.ApplyEdit(size, new CutOutEdit(CutVertical, strip.Start, strip.End, CutJoin));
                    if (ReferenceEquals(next, _state)) Message?.Invoke("That would leave the image too small.");
                    else Commit(next);
                }
                break;
            case ToolKind.Selection:
                if (_movingPixels && _pixelSelection is { } sel && _pixelOffset != (0, 0))
                {
                    var (dx, dy) = _pixelOffset;
                    _movingPixels = false;   // before Commit: the window's ShowState clears the selection, which is then re-set below
                    if (EditsFull()) break;
                    var next = _state.ApplyEdit(ImageSize, new MovePixelsEdit(sel, dx, dy, GapFill));
                    if (ReferenceEquals(next, _state)) break;   // refused (e.g. the edit cap): nothing moved, the selection stays
                    CommitOwn(next);
                    var moved = sel.Offset(dx, dy).Intersect(new Rct(0, 0, _base!.PixelWidth, _base.PixelHeight));
                    SetPixelSelection(moved.IsEmpty ? null : moved);
                    if (PixelEdits.TouchesObjects(sel, next.Annotations))
                        Notice?.Invoke("Moved pixels don't take objects with them. Check that redaction covers and arrows still cover what they should.");
                }
                break;
            default:
                if (AnnotationFactory.FromDrag(Tool, _down, p, CurrentStyle, Options) is { } a)
                {
                    Commit(_state.Add(a));
                    Select(a);
                    if (a is CalloutAnnotation) BeginTextEdit(a, isNew: false);
                }
                break;
        }
        ResetDrag();
        ReleaseMouseCapture();
    }

    /// <summary>The strip on the chosen axis, rounded to whole pixels and clamped to the image.</summary>
    (int Start, int End)? Strip(Pt a, Pt b)
    {
        if (_base is null) return null;
        var max = CutVertical ? _base.PixelWidth : _base.PixelHeight;
        double u = CutVertical ? a.X : a.Y, v = CutVertical ? b.X : b.Y;
        int s = (int)Math.Clamp(Math.Floor(Math.Min(u, v)), 0, max), e = (int)Math.Clamp(Math.Ceiling(Math.Max(u, v)), 0, max);
        return (s, e);
    }

    /// <summary>True when the press and release are within the system drag distance (in screen pixels).</summary>
    bool IsClick(Pt p) =>
        Math.Abs(p.X - _down.X) * Zoom <= SystemParameters.MinimumHorizontalDragDistance
        && Math.Abs(p.Y - _down.Y) * Zoom <= SystemParameters.MinimumVerticalDragDistance;

    /// <summary>Fill tool click (spec parity 4 §5): adds a fill edit unless the point is outside the image or already that colour.</summary>
    void TryFill(Pt p)
    {
        int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y);
        if (_base is null || x < 0 || y < 0 || x >= _base.PixelWidth || y >= _base.PixelHeight) return;
        if (!ColorText.TryArgb(CurrentStyle.Color, out var argb) || ImageIo.PixelAt(_base, x, y) == argb) return;
        if (EditsFull()) return;
        Commit(_state.ApplyEdit((_base.PixelWidth, _base.PixelHeight), new FillEdit(x, y, CurrentStyle.Color, Options.FillTolerance)));
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        ResetDrag();   // e.g. Alt+Tab mid-drag: drop the unfinished edit
    }

    void ResetDrag()
    {
        _preview = _textEditor is null ? null : _preview;
        _stampId = Guid.NewGuid();
        _dragging = false;
        _moved = false;
        _resizeOriginal = null;
        _clickHit = null;
        _box = null;
        _additive = false;
        _pen = null;
        _cropDrag = null;
        _cutDrag = null;
        _movingPixels = false;
        InvalidateVisual();
    }

    void BeginTextEdit(Annotation a, bool isNew)
    {
        var (pos, width, size, color, bold, text) = a switch
        {
            TextAnnotation t => (t.Position, double.NaN, t.FontSize, t.Color, t.Bold, t.Text),
            CalloutAnnotation c => (new Pt(c.Bounds.X + 8, c.Bounds.Y + 6), Math.Max(20, c.Bounds.W - 16), c.FontSize, c.TextColor, false, c.Text),
            _ => throw new ArgumentException("Only text and callouts can be edited as text.", nameof(a)),
        };
        _textTarget = a;
        _textIsNew = isNew;
        var box = new TextBox
        {
            Text = text,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = size,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Foreground = ImageIo.Brush(color),
            CaretBrush = ImageIo.Brush(color),
            Background = Brushes.Transparent,
            BorderBrush = Accent,
            BorderThickness = new Thickness(1 / Zoom),
            Padding = new Thickness(0),
            MinWidth = 40,
            AcceptsReturn = true,
            TextWrapping = double.IsNaN(width) ? TextWrapping.NoWrap : TextWrapping.Wrap,
        };
        if (!double.IsNaN(width)) box.Width = width;
        // The TextBox draws its text about 3 px right and 1 px down of its origin; offset so the text does not jump on commit.
        SetLeft(box, pos.X - View.X - 3);
        SetTop(box, pos.Y - View.Y - 1);
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape || (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift))
            {
                e.Handled = true;
                CommitTextEditor();
                Focus();
            }
        };
        box.LostKeyboardFocus += (_, _) => CommitTextEditor();
        _textEditor = box;
        Children.Add(box);

        Annotation blank = a switch
        {
            TextAnnotation t => t with { Text = "" },
            CalloutAnnotation c => c with { Text = "" },
            _ => a,
        };
        _preview = isNew ? _state.Add(blank) : _state.Replace(blank);
        InvalidateVisual();
        Dispatcher.BeginInvoke(() => { box.Focus(); box.SelectAll(); }, DispatcherPriority.Input);
    }

    public void CommitTextEditor()
    {
        if (_textEditor is not { } box || _textTarget is not { } target) return;
        _textEditor = null;
        _textTarget = null;
        Children.Remove(box);
        _preview = null;
        Commit((_textIsNew ? _state.Add(target) : _state).CommitText(target.Id, box.Text));
        InvalidateVisual();
    }

    void CancelTextEditor()
    {
        if (_textEditor is not { } box) return;
        _textEditor = null;
        _textTarget = null;
        Children.Remove(box);
        _preview = null;
    }
}
