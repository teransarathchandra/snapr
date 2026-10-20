using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Snapr.Core;
using Snapr.Core.Video;

namespace Snapr.App.Video;

/// <summary>The video editor's timeline (spec parity 8 §5): kept and removed parts, trim handles, selection and playhead,
/// with the video's objects as bars on a lane under it (spec parity 17 §5).</summary>
public sealed class VideoTimeline : FrameworkElement
{
    const double Pad = 8, HandleW = 8, HandleH = 40, Grab = 6, ClickSlop = 3;
    const double CutH = 56, RowH = 10, RowGap = 2, LaneTop = CutH + 4;
    const int Rows = 4;

    VideoCut _cut = VideoCut.For(1);
    double _position;
    TimeRange? _selection;
    int _drag;                  // 0 none, 1 start handle, 2 end handle, 3 selecting, 4 object start, 5 object end, 6 object move, 7 empty lane
    double _dragStart, _dragEnd, _pressX, _pressT, _curT;
    Brush? _hatch;
    IReadOnlyList<VideoObject> _objects = [];
    int[] _rows = [];
    Guid? _selectedObject;
    int _dragObject = -1;
    double _objStart, _objEnd;  // the dragged object's live times

    public VideoTimeline()
    {
        Height = LaneTop + Rows * (RowH + RowGap) + 2;
        Focusable = false;
    }

    public VideoCut Cut { get => _cut; set { _cut = value; InvalidateVisual(); } }
    public double Position { get => _position; set { _position = value; InvalidateVisual(); } }

    public TimeRange? Selection
    {
        get => _selection;
        set { _selection = value; InvalidateVisual(); }
    }

    public IReadOnlyList<VideoObject> Objects
    {
        get => _objects;
        set { CancelDrag(); _objects = value; _rows = VideoProject.Rows(value, Rows); InvalidateVisual(); }
    }

    public Guid? SelectedObject { get => _selectedObject; set { _selectedObject = value; InvalidateVisual(); } }

    public event Action<Guid?>? ObjectSelected;
    public event Action<Guid, double, double>? ObjectRetimed;
    public event Action<double, double>? TrimChanged;
    public event Action<double>? Seek;
    public event Action? SelectionChanged;

    double X(double t) => Pad + t / _cut.Duration * Math.Max(1, ActualWidth - 2 * Pad);
    double T(double x) => Math.Clamp((x - Pad) / Math.Max(1, ActualWidth - 2 * Pad) * _cut.Duration, 0, _cut.Duration);

    Brush Res(string key) => (Brush)FindResource(key);

    Brush Hatch()
    {
        if (_hatch is not null) return _hatch;
        var pen = new Pen(Res("TextSecondaryBrush"), 1);
        var drawing = new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 8), new Point(8, 0)));
        var back = new GeometryDrawing(new SolidColorBrush(Color.FromArgb(200, 17, 17, 19)), null, new RectangleGeometry(new Rect(0, 0, 8, 8)));
        var brush = new DrawingBrush(new DrawingGroup { Children = { back, drawing } })
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 8, 8),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 8, 8),
            ViewboxUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return _hatch = brush;
    }

    (double Start, double End) Trim => _drag is 1 or 2 ? (_dragStart, _dragEnd) : (_cut.TrimStart, _cut.TrimEnd);

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        var top = (CutH - HandleH) / 2;
        var track = new Rect(Pad, top, Math.Max(1, w - 2 * Pad), HandleH);
        dc.DrawRectangle(Res("CanvasBrush"), new Pen(Res("PanelBorderBrush"), 1), track);

        var (ts, te) = Trim;
        var accent = (SolidColorBrush)Res("AccentBrush");
        var fill = new SolidColorBrush(Color.FromArgb(89, accent.Color.R, accent.Color.G, accent.Color.B));
        var live = _cut with { TrimStart = ts, TrimEnd = te };

        var from = 0.0;
        foreach (var k in live.Kept)
        {
            if (k.Start > from) dc.DrawRectangle(Hatch(), null, new Rect(X(from), top, X(k.Start) - X(from), HandleH));
            dc.DrawRectangle(fill, null, new Rect(X(k.Start), top, Math.Max(0, X(k.End) - X(k.Start)), HandleH));
            from = k.End;
        }
        if (from < _cut.Duration) dc.DrawRectangle(Hatch(), null, new Rect(X(from), top, X(_cut.Duration) - X(from), HandleH));

        if (_drag == 3 || _selection is not null)
        {
            var s = _drag == 3 ? new TimeRange(Math.Min(_pressT, _curT), Math.Max(_pressT, _curT)) : _selection!.Value;
            dc.DrawRectangle(null, new Pen(accent, 2), new Rect(X(s.Start), top, Math.Max(1, X(s.End) - X(s.Start)), HandleH));
        }

        foreach (var t in new[] { ts, te })
            dc.DrawRoundedRectangle(accent, null, new Rect(X(t) - HandleW / 2, top, HandleW, HandleH), 3, 3);

        for (var i = 0; i < _objects.Count; i++)
        {
            var (s, e) = Times(i);
            var bar = new Rect(X(s), RowTop(_rows[i]), Math.Max(2, X(e) - X(s)), RowH);
            var outline = _objects[i].Item.Id == _selectedObject ? new Pen(Brushes.White, 2) : null;
            dc.DrawRoundedRectangle(BarBrush(_objects[i].Item, accent), outline, bar, 2, 2);
        }

        dc.DrawRectangle(Brushes.White, null, new Rect(X(_position) - 1, 2, 2, h - 4));
    }

    static double RowTop(int row) => LaneTop + row * (RowH + RowGap);

    (double Start, double End) Times(int i) => i == _dragObject && _drag is 4 or 5 or 6 ? (_objStart, _objEnd) : (_objects[i].Start, _objects[i].End);

    static Brush BarBrush(Annotation a, Brush fallback)
    {
        if (!ColorText.TryArgb(MainColor(a), out var argb)) return fallback;
        var b = new SolidColorBrush(Color.FromRgb((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));   // opaque, so highlights show
        b.Freeze();
        return b;
    }

    /// <summary>The colour an object's bar is drawn in: the colour it was drawn with; null for kinds without one.</summary>
    public static string? MainColor(Annotation a) => a switch
    {
        ArrowAnnotation x => x.Color,
        ShapeAnnotation x => x.Stroke,
        TextAnnotation x => x.Color,
        CalloutAnnotation x => x.Stroke,
        StepAnnotation x => x.Fill,
        HighlightAnnotation x => x.Color,
        PenAnnotation x => x.Color,
        StampAnnotation x => x.Color,
        _ => null,
    };

    public static string KindName(Annotation a) => a switch
    {
        ArrowAnnotation => "Arrow",
        TextAnnotation => "Text",
        CalloutAnnotation => "Callout",
        ShapeAnnotation => "Shape",
        PenAnnotation => "Pen",
        HighlightAnnotation => "Highlight",
        StepAnnotation => "Step",
        StampAnnotation => "Stamp",
        _ => "Object",
    };

    /// <summary>The bar's tooltip, e.g. "Arrow, 0:03.2 – 0:06.2".</summary>
    public static string Tip(VideoObject o) => $"{KindName(o.Item)}, {VideoEditorWindow.Fmt(o.Start)} – {VideoEditorWindow.Fmt(o.End)}";

    /// <summary>The bar under <paramref name="x"/> on <paramref name="row"/> (later bars are on top) and the part: 1 its start,
    /// 2 its end (within 6 px, less on short bars so their middle stays grabbable), 3 the middle. (-1, 0) when none.</summary>
    public static (int Index, int Part) HitBar(IReadOnlyList<(double X0, double X1, int Row)> bars, double x, int row)
    {
        for (var i = bars.Count - 1; i >= 0; i--)
        {
            var (x0, x1, r) = bars[i];
            var grab = Math.Min(Grab, (x1 - x0) / 3);
            if (r != row || x < x0 - grab || x > x1 + grab) continue;
            if (x <= x0 + grab) return (i, 1);
            if (x >= x1 - grab) return (i, 2);
            return (i, 3);
        }
        return (-1, 0);
    }

    (int Index, int Part) HitObject(Point p)
    {
        if (p.Y < LaneTop) return (-1, 0);
        var row = Math.Min(Rows - 1, (int)((p.Y - LaneTop) / (RowH + RowGap)));
        return HitBar(_objects.Select((o, i) => (X(o.Start), X(o.End), _rows[i])).ToArray(), p.X, row);
    }

    int HandleAt(double x)
    {
        if (Math.Abs(x - X(_cut.TrimStart)) <= Grab) return 1;
        if (Math.Abs(x - X(_cut.TrimEnd)) <= Grab) return 2;
        return 0;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        var x = p.X;
        if (_drag == 0)
        {
            var (i, part) = HitObject(p);
            Cursor = p.Y < LaneTop ? HandleAt(x) != 0 ? Cursors.SizeWE : Cursors.Arrow
                : part is 1 or 2 ? Cursors.SizeWE : part == 3 ? Cursors.SizeAll : Cursors.Arrow;
            var tip = i >= 0 ? Tip(_objects[i]) : null;
            if (!Equals(ToolTip, tip)) ToolTip = tip;
            return;
        }
        _curT = T(x);
        if (_drag is 4 or 5 or 6)
        {
            var o = _objects[_dragObject];
            var d = _curT - _pressT;
            var dur = _cut.Duration;
            if (_drag == 4) (_objStart, _objEnd) = (Math.Clamp(o.Start + d, 0, Math.Max(0, o.End - VideoProject.MinLength)), o.End);
            else if (_drag == 5) (_objStart, _objEnd) = (o.Start, Math.Clamp(o.End + d, Math.Min(dur, o.Start + VideoProject.MinLength), dur));
            else
            {
                var len = o.End - o.Start;
                _objStart = Math.Clamp(o.Start + d, 0, Math.Max(0, dur - len));
                _objEnd = _objStart + len;
            }
            InvalidateVisual();
            return;
        }
        if (_drag == 1) _dragStart = Math.Min(_curT, _dragEnd - VideoCut.MinKept);
        else if (_drag == 2) _dragEnd = Math.Max(_curT, _dragStart + VideoCut.MinKept);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        var x = p.X;
        _pressX = x;
        _pressT = _curT = T(x);
        if (p.Y >= LaneTop)
        {
            var (i, part) = HitObject(p);
            _dragObject = i;
            _drag = i < 0 ? 7 : part + 3;
            if (i >= 0) (_objStart, _objEnd) = (_objects[i].Start, _objects[i].End);
            CaptureMouse();
            e.Handled = true;
            return;
        }
        _drag = HandleAt(x) is var hd and not 0 ? hd : 3;
        _dragStart = _cut.TrimStart;
        _dragEnd = _cut.TrimEnd;
        CaptureMouse();
        e.Handled = true;
    }

    /// <summary>Drops a drag in progress without raising anything: its object index may no longer be valid.</summary>
    void CancelDrag()
    {
        if (_drag == 0) return;
        _drag = 0;
        _dragObject = -1;
        if (IsMouseCaptured) ReleaseMouseCapture();
        InvalidateVisual();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        CancelDrag();   // a normal release has already reset _drag, so this only cancels a drag cut short
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_drag == 0) return;
        var mode = _drag;
        _drag = 0;
        ReleaseMouseCapture();
        var x = e.GetPosition(this).X;
        var click = Math.Abs(x - _pressX) <= ClickSlop;
        if (mode == 7)
        {
            if (click) ObjectSelected?.Invoke(null);
            return;
        }
        if (mode is 4 or 5 or 6)
        {
            var o = _objects[_dragObject];
            _dragObject = -1;
            InvalidateVisual();
            ObjectSelected?.Invoke(o.Item.Id);
            if (click) Seek?.Invoke(o.Start);
            else ObjectRetimed?.Invoke(o.Item.Id, _objStart, _objEnd);
            return;
        }
        if (mode is 1 or 2)
        {
            InvalidateVisual();
            TrimChanged?.Invoke(_dragStart, _dragEnd);
            return;
        }
        if (click)
        {
            _selection = null;
            InvalidateVisual();
            SelectionChanged?.Invoke();
            Seek?.Invoke(T(x));
            return;
        }
        var t = T(x);
        _selection = new TimeRange(Math.Min(_pressT, t), Math.Max(_pressT, t));
        InvalidateVisual();
        SelectionChanged?.Invoke();
    }
}
