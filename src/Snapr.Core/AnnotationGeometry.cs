namespace Snapr.Core;

public static class AnnotationGeometry
{
    public static Rct Bounds(Annotation a) => a switch
    {
        ArrowAnnotation x => Rct.FromPoints(x.Start, x.End).Inflate(x.Width * 2),
        ShapeAnnotation x => x.Bounds,
        TextAnnotation x => TextBounds(x),
        CalloutAnnotation x => x.Bounds.Union(new Rct(x.TailPoint.X, x.TailPoint.Y, 1, 1)),
        StepAnnotation x => new Rct(x.Center.X - x.Size / 2, x.Center.Y - x.Size / 2, x.Size, x.Size),
        HighlightAnnotation x => x.Bounds,
        PenAnnotation x => PointsBounds(x.Points).Inflate(x.Width),
        BlurAnnotation x => x.Bounds,
        MagnifyAnnotation x => MagnifyTarget(x),
        _ => default,
    };

    public static Rct MagnifyTarget(MagnifyAnnotation m)
    {
        var w = m.SourceBounds.W * m.Zoom;
        var h = m.SourceBounds.H * m.Zoom;
        return new(m.TargetCenter.X - w / 2, m.TargetCenter.Y - h / 2, w, h);
    }

    /// <summary>Approximate text box; good enough for hit-testing and selection outlines.</summary>
    static Rct TextBounds(TextAnnotation t)
    {
        var lines = t.Text.Split('\n');
        var longest = Math.Max(1, lines.Max(l => l.Length));
        return new(t.Position.X, t.Position.Y, longest * t.FontSize * 0.6, lines.Length * t.FontSize * 1.35);
    }

    static Rct PointsBounds(IReadOnlyList<Pt> pts)
    {
        if (pts.Count == 0) return default;
        double minX = pts.Min(p => p.X), minY = pts.Min(p => p.Y);
        return new(minX, minY, pts.Max(p => p.X) - minX, pts.Max(p => p.Y) - minY);
    }

    public static double DistanceToSegment(Pt p, Pt a, Pt b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
        if (len2 == 0) return p.DistanceTo(a);
        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        return p.DistanceTo(new Pt(a.X + t * dx, a.Y + t * dy));
    }

    public static bool Hit(Annotation a, Pt p, double tol) => a switch
    {
        ArrowAnnotation x => DistanceToSegment(p, x.Start, x.End) <= tol + x.Width / 2,
        PenAnnotation x => x.Points.Count == 1
            ? x.Points[0].DistanceTo(p) <= tol + x.Width / 2
            : x.Points.Zip(x.Points.Skip(1)).Any(s => DistanceToSegment(p, s.First, s.Second) <= tol + x.Width / 2),
        StepAnnotation x => x.Center.DistanceTo(p) <= x.Size / 2 + tol,
        _ => Bounds(a).Contains(p, tol),
    };

    public static Annotation Translate(Annotation a, double dx, double dy) => a switch
    {
        ArrowAnnotation x => x with { Start = x.Start.Offset(dx, dy), End = x.End.Offset(dx, dy) },
        ShapeAnnotation x => x with { Bounds = x.Bounds.Offset(dx, dy) },
        TextAnnotation x => x with { Position = x.Position.Offset(dx, dy) },
        CalloutAnnotation x => x with { Bounds = x.Bounds.Offset(dx, dy), TailPoint = x.TailPoint.Offset(dx, dy) },
        StepAnnotation x => x with { Center = x.Center.Offset(dx, dy) },
        HighlightAnnotation x => x with { Bounds = x.Bounds.Offset(dx, dy) },
        PenAnnotation x => x with { Points = x.Points.Select(q => q.Offset(dx, dy)).ToArray() },
        BlurAnnotation x => x with { Bounds = x.Bounds.Offset(dx, dy) },
        MagnifyAnnotation x => x with { TargetCenter = x.TargetCenter.Offset(dx, dy) },
        _ => a,
    };

    public static Pt? ResizeHandle(Annotation a) => a switch
    {
        ArrowAnnotation x => x.End,
        ShapeAnnotation x => BottomRight(x.Bounds),
        CalloutAnnotation x => BottomRight(x.Bounds),
        HighlightAnnotation x => BottomRight(x.Bounds),
        BlurAnnotation x => BottomRight(x.Bounds),
        MagnifyAnnotation x => BottomRight(MagnifyTarget(x)),
        _ => null,
    };

    static Pt BottomRight(Rct r) => new(r.Right, r.Bottom);
    static Rct Grow(Rct r, Pt p) => Rct.FromPoints(new Pt(r.X, r.Y), p);

    public static Annotation ResizeTo(Annotation a, Pt p) => a switch
    {
        ArrowAnnotation x => x with { End = p },
        ShapeAnnotation x => x with { Bounds = Grow(x.Bounds, p) },
        CalloutAnnotation x => x with { Bounds = Grow(x.Bounds, p) },
        HighlightAnnotation x => x with { Bounds = Grow(x.Bounds, p) },
        BlurAnnotation x => x with { Bounds = Grow(x.Bounds, p) },
        MagnifyAnnotation x => x with { Zoom = Math.Clamp(2 * (p.X - x.TargetCenter.X) / Math.Max(1, x.SourceBounds.W), 1.5, 4) },
        _ => a,
    };

    /// <summary>Mirrors an object within its own bounds. Shapes, text, steps, highlight, blur and magnify are left as they are.</summary>
    public static Annotation Flip(Annotation a, bool horizontal)
    {
        var c = Bounds(a).Center;
        Pt M(Pt p) => horizontal ? new(2 * c.X - p.X, p.Y) : new(p.X, 2 * c.Y - p.Y);
        Rct MR(Rct r) => horizontal ? r with { X = 2 * c.X - r.Right } : r with { Y = 2 * c.Y - r.Bottom };
        return a switch
        {
            ArrowAnnotation x => x with { Start = M(x.Start), End = M(x.End) },
            PenAnnotation x => x with { Points = x.Points.Select(M).ToArray() },
            CalloutAnnotation x => x with { Bounds = MR(x.Bounds), TailPoint = M(x.TailPoint) },
            _ => a,
        };
    }

    public static Annotation WithStyle(Annotation a, ToolStyle s) => (a switch
    {
        ArrowAnnotation x => x with { Color = s.Color, Width = s.Width },
        ShapeAnnotation x => x with { Stroke = s.Color, StrokeWidth = s.Width, Fill = s.Fill },
        TextAnnotation x => x with { Color = s.Color, FontSize = s.FontSize, Background = s.Fill },
        CalloutAnnotation x => x with { Stroke = s.Color, FontSize = s.FontSize, Fill = s.Fill ?? x.Fill },
        StepAnnotation x => x with { Fill = s.Color, Size = StepSize(s.FontSize) },
        HighlightAnnotation x => x with { Color = ColorText.WithAlpha(s.Color, 0x66) },
        PenAnnotation x => x with { Color = s.Color, Width = s.Width },
        _ => a,
    }) with { Shadow = a is BlurAnnotation or HighlightAnnotation ? false : s.Shadow };

    public static double StepSize(double fontSize) => Math.Max(24, fontSize * 1.6);

    public static ToolStyle StyleOf(Annotation a, ToolStyle fallback) => a switch
    {
        ArrowAnnotation x => fallback with { Color = x.Color, Width = x.Width, Shadow = x.Shadow },
        ShapeAnnotation x => fallback with { Color = x.Stroke, Width = x.StrokeWidth, Fill = x.Fill, Shadow = x.Shadow },
        TextAnnotation x => fallback with { Color = x.Color, FontSize = x.FontSize, Fill = x.Background, Shadow = x.Shadow },
        CalloutAnnotation x => fallback with { Color = x.Stroke, FontSize = x.FontSize, Fill = x.Fill, Shadow = x.Shadow },
        StepAnnotation x => fallback with { Color = x.Fill, FontSize = x.Size / 1.6, Shadow = x.Shadow },
        HighlightAnnotation x => fallback with { Color = ColorText.WithAlpha(x.Color, 0xFF), Shadow = false },
        PenAnnotation x => fallback with { Color = x.Color, Width = x.Width, Shadow = x.Shadow },
        _ => fallback,
    };

    public static Annotation WithOptions(Annotation a, ToolOptions o) => a switch
    {
        BlurAnnotation x => x with { Mode = o.Blur, Strength = Math.Clamp(o.BlurStrength, 1, 20) },
        MagnifyAnnotation x => x with { Zoom = Math.Clamp(o.Zoom, 1.5, 4), Shape = o.Lens },
        StepAnnotation x => x with { Style = o.Steps },
        _ => a,
    };

    public static ToolOptions OptionsOf(Annotation a, ToolOptions fallback) => a switch
    {
        BlurAnnotation x => fallback with { Blur = x.Mode, BlurStrength = x.Strength },
        MagnifyAnnotation x => fallback with { Zoom = x.Zoom, Lens = x.Shape },
        StepAnnotation x => fallback with { Steps = x.Style },
        _ => fallback,
    };
}
