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

    BitmapSource? _base;
    DocumentState _state = DocumentState.Empty;
    DocumentState? _preview;
    Pt _down;
    Annotation? _dragOriginal;
    bool _resizing;
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
    public Guid? SelectedId { get; private set; }
    public Annotation? Selected => SelectedId is { } id ? _state.Annotations.FirstOrDefault(a => a.Id == id) : null;
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
        SelectedId = null;
        SetState(state);
        SelectionChanged?.Invoke();
    }

    public void SetState(DocumentState state)
    {
        _state = state;
        _preview = null;
        if (SelectedId is { } id && state.Annotations.All(a => a.Id != id))
        {
            SelectedId = null;
            SelectionChanged?.Invoke();
        }
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void Select(Annotation? a)
    {
        SelectedId = a?.Id;
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    public void Apply(Func<DocumentState, DocumentState> edit, string? mergeKey = null) => Commit(edit(_state), mergeKey);

    public void ApplyToSelection(Func<Annotation, Annotation> edit, string? mergeKey = null)
    {
        if (Selected is { } s) Commit(_state.Replace(edit(s)), mergeKey);
    }

    public void DeleteSelected()
    {
        if (SelectedId is not { } id) return;
        Commit(_state.Remove(id));
        Select(null);
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
        if (SelectedId is { } id && state.Annotations.FirstOrDefault(a => a.Id == id) is { } selected) DrawSelection(dc, selected);
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

    void DrawSelection(DrawingContext dc, Annotation a)
    {
        var b = AnnotationGeometry.Bounds(a).Inflate(4 / Zoom);
        dc.DrawRectangle(null, new Pen(Accent, 1.5 / Zoom) { DashStyle = DashStyles.Dash }, new Rect(b.X, b.Y, Math.Max(0, b.W), Math.Max(0, b.H)));
        if (AnnotationGeometry.ResizeHandle(a) is { } h)
            dc.DrawRectangle(Brushes.White, new Pen(Accent, 1.5 / Zoom), new Rect(h.X - HandleSize / 2, h.Y - HandleSize / 2, HandleSize, HandleSize));
    }

    bool OnHandle(Pt p) =>
        Selected is { } s && AnnotationGeometry.ResizeHandle(s) is { } h &&
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
                var hit = OnHandle(p) ? Selected : _state.HitTest(p, 6 / Zoom);
                if (e.ClickCount == 2 && hit is TextAnnotation or CalloutAnnotation)
                {
                    Select(hit);
                    BeginTextEdit(hit, isNew: false);
                    return;
                }
                _resizing = hit is not null && OnHandle(p);
                Select(hit);
                _dragOriginal = hit;
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
            case ToolKind.Move when _dragOriginal is not null:
                _preview = _state.Replace(_resizing
                    ? AnnotationGeometry.ResizeTo(_dragOriginal, p)
                    : AnnotationGeometry.Translate(_dragOriginal, p.X - _down.X, p.Y - _down.Y));
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
            case ToolKind.Move:
                if (_preview is not null) Commit(_preview);
                break;
            case ToolKind.Pen:
                if (_pen is { Count: > 1 }) Commit(_state.Add(AnnotationFactory.Pen(_pen.ToArray(), CurrentStyle)));
                break;
            case ToolKind.Step:
                Commit(_state.AddStep(p, CurrentStyle));
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
        _dragOriginal = null;
        _pen = null;
        _cropDrag = null;
        _resizing = false;
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
