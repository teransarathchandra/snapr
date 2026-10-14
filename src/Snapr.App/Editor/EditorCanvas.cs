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
    bool _dragging;            // moving the selection (or resizing _resizeOriginal)
    Annotation? _resizeOriginal;
    Rct? _box;                   // selection box being dragged
    bool _additive;              // Shift held when the box started
    Annotation? _clickHit;       // object under a plain mouse-down; a click without movement narrows the selection to it
    List<Pt>? _pen;
    Rct? _cropDrag;
    TextBox? _textEditor;
    Annotation? _textTarget;
    bool _textIsNew;

    /// <summary>A finished edit. A non-null merge key lets the window merge repeated edits (slider drags) into one undo step.</summary>
    public event Action<DocumentState, string?>? Committed;
    public event Action? SelectionChanged;

    public ToolKind Tool { get; set; } = ToolKind.Arrow;
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
        _selected = [];
        SetState(state);
        SelectionChanged?.Invoke();
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
        if (_box is { } box) dc.DrawRectangle(BoxFill, new Pen(Accent, 1 / Zoom) { DashStyle = DashStyles.Dash }, ToRect(box));
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
        }
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_base is null) return;
        var p = ToImage(e);
        Cursor = Tool == ToolKind.Move && OnHandle(p) ? Cursors.SizeNWSE : ToolCursor;
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
            case ToolKind.Move or ToolKind.Step or ToolKind.Eraser or ToolKind.Pen:
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
            case ToolKind.Crop:
                if (_cropDrag is { W: >= 4, H: >= 4 } c) Commit(_state.SetCrop(c, _base!.PixelWidth, _base.PixelHeight));
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

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        ResetDrag();   // e.g. Alt+Tab mid-drag: drop the unfinished edit
    }

    void ResetDrag()
    {
        _preview = _textEditor is null ? null : _preview;
        _dragging = false;
        _moved = false;
        _resizeOriginal = null;
        _clickHit = null;
        _box = null;
        _additive = false;
        _pen = null;
        _cropDrag = null;
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
