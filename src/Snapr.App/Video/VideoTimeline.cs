using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Snapr.Core.Video;

namespace Snapr.App.Video;

/// <summary>The video editor's timeline (spec parity 8 §5): kept and removed parts, trim handles, selection and playhead.</summary>
public sealed class VideoTimeline : FrameworkElement
{
    const double Pad = 8, HandleW = 8, HandleH = 40, Grab = 6, ClickSlop = 3;

    VideoCut _cut = VideoCut.For(1);
    double _position;
    TimeRange? _selection;
    int _drag;                  // 0 none, 1 start handle, 2 end handle, 3 selecting
    double _dragStart, _dragEnd, _pressX, _pressT, _curT;
    Brush? _hatch;

    public VideoTimeline()
    {
        Height = 56;
        Focusable = false;
    }

    public VideoCut Cut { get => _cut; set { _cut = value; InvalidateVisual(); } }
    public double Position { get => _position; set { _position = value; InvalidateVisual(); } }

    public TimeRange? Selection
    {
        get => _selection;
        set { _selection = value; InvalidateVisual(); }
    }

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
        var top = (h - HandleH) / 2;
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

        dc.DrawRectangle(Brushes.White, null, new Rect(X(_position) - 1, 2, 2, h - 4));
    }

    int HandleAt(double x)
    {
        if (Math.Abs(x - X(_cut.TrimStart)) <= Grab) return 1;
        if (Math.Abs(x - X(_cut.TrimEnd)) <= Grab) return 2;
        return 0;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var x = e.GetPosition(this).X;
        if (_drag == 0)
        {
            Cursor = HandleAt(x) != 0 ? Cursors.SizeWE : Cursors.Arrow;
            return;
        }
        _curT = T(x);
        if (_drag == 1) _dragStart = Math.Min(_curT, _dragEnd - VideoCut.MinKept);
        else if (_drag == 2) _dragEnd = Math.Max(_curT, _dragStart + VideoCut.MinKept);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var x = e.GetPosition(this).X;
        _pressX = x;
        _pressT = _curT = T(x);
        _drag = HandleAt(x) is var hd and not 0 ? hd : 3;
        _dragStart = _cut.TrimStart;
        _dragEnd = _cut.TrimEnd;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_drag == 0) return;
        var mode = _drag;
        _drag = 0;
        ReleaseMouseCapture();
        var x = e.GetPosition(this).X;
        if (mode is 1 or 2)
        {
            InvalidateVisual();
            TrimChanged?.Invoke(_dragStart, _dragEnd);
            return;
        }
        if (Math.Abs(x - _pressX) <= ClickSlop)
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
