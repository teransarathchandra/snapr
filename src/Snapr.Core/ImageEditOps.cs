namespace Snapr.Core;

/// <summary>Applying an image edit to a document: the edit is appended and every object and the crop follow it (spec parity 4 §4).</summary>
public static class ImageEditOps
{
    /// <summary>Appends <paramref name="edit"/>; <paramref name="size"/> is the image size before it.
    /// Returns the same state for a no-op (same size, zero margins, fill outside the image), an invalid edit, a full edit list, or a result outside the size limits.</summary>
    public static DocumentState ApplyEdit(this DocumentState s, (int W, int H) size, ImageEdit edit)
    {
        var (w, h) = size;
        if (s.Edits.Count >= ImageEditRules.MaxEdits || !ImageEditRules.IsValid(edit, w, h)) return s;
        var (nw, nh) = ImageEditRules.SizeAfter(w, h, edit);
        var noOp = edit switch
        {
            ResizeEdit r => r.Width == w && r.Height == h,
            MarginsEdit m => m.Left == 0 && m.Top == 0 && m.Right == 0 && m.Bottom == 0,
            FillEdit f => f.X < 0 || f.Y < 0 || f.X >= w || f.Y >= h,
            _ => false,
        };
        if (noOp || !ImageEditRules.SizeOk(nw, nh)) return s;

        var map = Mapping(edit, w, h, nw, nh);
        var next = s with
        {
            Annotations = s.Annotations.Select(a => Transform(a, map)).ToArray(),
            Edits = [.. s.Edits, edit],
            Crop = null,
        };
        return s.Crop is { } c ? next.SetCrop(map.Area(c), nw, nh) : next;
    }

    /// <summary>How positions move, how lengths scale, and whether text/callout/image boxes keep their own size (on rotation).</summary>
    sealed record Map(Func<Pt, Pt> Point, double Scale, bool KeepBoxes)
    {
        public Rct Area(Rct r) => Rct.FromPoints(Point(new Pt(r.X, r.Y)), Point(new Pt(r.Right, r.Bottom)));

        public Rct Box(Rct r)
        {
            if (!KeepBoxes) return Area(r);
            var c = Point(r.Center);
            return new Rct(c.X - r.W / 2, c.Y - r.H / 2, r.W, r.H);
        }
    }

    static Map Mapping(ImageEdit e, int w, int h, int nw, int nh) => e switch
    {
        RotateEdit { QuarterTurns: 1 } => new(p => new Pt(h - p.Y, p.X), 1, true),
        RotateEdit { QuarterTurns: 2 } => new(p => new Pt(w - p.X, h - p.Y), 1, true),
        RotateEdit { QuarterTurns: 3 } => new(p => new Pt(p.Y, w - p.X), 1, true),
        FlipEdit { Horizontal: true } => new(p => new Pt(w - p.X, p.Y), 1, false),
        FlipEdit => new(p => new Pt(p.X, h - p.Y), 1, false),
        ResizeEdit => Scaled((double)nw / w, (double)nh / h),
        MarginsEdit m => new(p => new Pt(p.X + m.Left, p.Y + m.Top), 1, false),
        _ => new(p => p, 1, false),
    };

    static Map Scaled(double sx, double sy) => new(p => new Pt(p.X * sx, p.Y * sy), (sx + sy) / 2, false);

    static Annotation Transform(Annotation a, Map m)
    {
        var s = m.Scale;
        return a switch
        {
            ArrowAnnotation x => x with { Start = m.Point(x.Start), End = m.Point(x.End), Width = x.Width * s },
            ShapeAnnotation x => x with { Bounds = m.Area(x.Bounds), StrokeWidth = x.StrokeWidth * s, CornerRadius = x.CornerRadius * s },
            TextAnnotation x => x with { Position = TopLeft(m.Box(AnnotationGeometry.Bounds(x))), FontSize = x.FontSize * s },
            CalloutAnnotation x => x with { Bounds = m.Box(x.Bounds), TailPoint = m.Point(x.TailPoint), FontSize = x.FontSize * s },
            StepAnnotation x => x with { Center = m.Point(x.Center), Size = x.Size * s },
            HighlightAnnotation x => x with { Bounds = m.Area(x.Bounds) },
            PenAnnotation x => x with { Points = x.Points.Select(m.Point).ToArray(), Width = x.Width * s },
            BlurAnnotation x => x with { Bounds = m.Area(x.Bounds) },
            MagnifyAnnotation x => x with { SourceBounds = m.Area(x.SourceBounds), TargetCenter = m.Point(x.TargetCenter) },
            ImageAnnotation x => x with { Bounds = m.Box(x.Bounds) },
            _ => a,
        };
    }

    static Pt TopLeft(Rct r) => new(r.X, r.Y);
}
