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

    /// <summary>Removes every image edit and moves objects and the crop back onto the original image, undoing each edit's
    /// geometry from last to first (spec parity 5 §5). Returns the same state when there are no edits.</summary>
    public static DocumentState RevertEdits(this DocumentState s, (int W, int H) originalSize)
    {
        if (s.Edits.Count == 0) return s;
        var sizes = new (int W, int H)[s.Edits.Count + 1];
        sizes[0] = originalSize;
        for (var i = 0; i < s.Edits.Count; i++) sizes[i + 1] = ImageEditRules.SizeAfter(sizes[i].W, sizes[i].H, s.Edits[i]);

        var annotations = s.Annotations;
        var crop = s.Crop;
        for (var i = s.Edits.Count - 1; i >= 0; i--)
        {
            var map = Inverse(s.Edits[i], sizes[i].W, sizes[i].H, sizes[i + 1].W, sizes[i + 1].H);
            annotations = annotations.Select(a => Transform(a, map)).ToArray();
            if (crop is { } c) crop = map.Area(c);
        }
        var next = s with { Annotations = annotations, Edits = [], Crop = null };
        return crop is { } nc ? next.SetCrop(nc, originalSize.W, originalSize.H) : next;
    }

    /// <summary>The mapping that undoes one edit: from the image after it (nw × nh) back to the image before it (w × h).</summary>
    static Map Inverse(ImageEdit e, int w, int h, int nw, int nh) => e switch
    {
        RotateEdit r => Mapping(new RotateEdit(4 - r.QuarterTurns), nw, nh, w, h),
        FlipEdit => Mapping(e, nw, nh, w, h),
        ResizeEdit => Scaled((double)w / nw, (double)h / nh) with { Scale = 1 / (((double)nw / w + (double)nh / h) / 2) },
        MarginsEdit m => new(p => new Pt(p.X - m.Left, p.Y - m.Top), 1, false),
        CutOutEdit c => new(p => Uncut(p, c), 1, true),
        _ => new(p => p, 1, false),
    };

    /// <summary>How positions move, how lengths scale, and whether text/callout/image/stamp boxes keep their own size (on rotation).</summary>
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
        CutOutEdit c => new(p => Cut(p, c), 1, true),
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
            SpotlightAnnotation x => x with { Bounds = m.Area(x.Bounds) },
            MagnifyAnnotation x => x with { SourceBounds = m.Area(x.SourceBounds), TargetCenter = m.Point(x.TargetCenter) },
            ImageAnnotation x => x with { Bounds = m.Box(x.Bounds) },
            StampAnnotation x => x with { Bounds = m.Box(x.Bounds) },
            _ => a,
        };
    }

    static Pt TopLeft(Rct r) => new(r.X, r.Y);

    /// <summary>Along the cut axis: before the strip unchanged, inside it onto the seam, after it moved back by the strip width.</summary>
    static Pt Cut(Pt p, CutOutEdit c) => c.Vertical ? p with { X = CutAxis(p.X, c) } : p with { Y = CutAxis(p.Y, c) };
    static double CutAxis(double v, CutOutEdit c) => v < c.Start ? v : v < c.End ? c.Start : v - (c.End - c.Start);

    static Pt Uncut(Pt p, CutOutEdit c) => c.Vertical ? p with { X = UncutAxis(p.X, c) } : p with { Y = UncutAxis(p.Y, c) };
    static double UncutAxis(double v, CutOutEdit c) => v < c.Start ? v : v + (c.End - c.Start);
}
