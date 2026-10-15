namespace Snapr.Core;

public static class AnnotationFactory
{
    public const double MinDrag = 3;

    /// <summary>Creates the annotation for a drag-based tool, or null for a too-small drag or a non-drag tool.</summary>
    public static Annotation? FromDrag(ToolKind tool, Pt a, Pt b, ToolStyle s, ToolOptions o)
    {
        if (a.DistanceTo(b) < MinDrag) return null;
        var r = Rct.FromPoints(a, b);
        var isLine = tool == ToolKind.Arrow || (tool == ToolKind.Shape && o.Shape == ShapeOption.Line);
        if (!isLine && (r.W < 1 || r.H < 1)) return null;   // a perfectly straight drag would make an invisible box
        Annotation? made = tool switch
        {
            ToolKind.Arrow => new ArrowAnnotation(a, b, s.Color, s.Width),
            ToolKind.Shape when o.Shape == ShapeOption.Line => new ArrowAnnotation(a, b, s.Color, s.Width, ArrowHead.None),
            ToolKind.Shape => new ShapeAnnotation(o.Shape == ShapeOption.Ellipse ? ShapeKind.Ellipse : ShapeKind.Rectangle, r, s.Color, s.Width, s.Fill),
            ToolKind.Highlight => new HighlightAnnotation(r, ColorText.WithAlpha(s.Color, 0x66)),
            ToolKind.Blur => new BlurAnnotation(r, o.Blur, Math.Clamp(o.BlurStrength, 1, 20)),
            ToolKind.Spotlight => new SpotlightAnnotation(r, o.Spot, Math.Clamp(o.SpotDim, 0, 90), o.SpotBlur),
            ToolKind.Magnify => new MagnifyAnnotation(r, new Pt(r.Right + r.W * o.Zoom / 2 + 12, r.Y + r.H / 2), Math.Clamp(o.Zoom, 1.5, 4), o.Lens),
            ToolKind.Callout => new CalloutAnnotation(r, "", s.FontSize, s.Fill ?? "#FFFFFFFF", s.Color, "#FF111111",
                new Pt(r.X + r.W * 0.25, r.Bottom + Math.Max(24, r.H * 0.5))),
            _ => null,
        };
        return made is null ? null : made with { Shadow = s.Shadow && tool is not (ToolKind.Blur or ToolKind.Highlight or ToolKind.Spotlight) };
    }

    public static TextAnnotation Text(Pt at, ToolStyle s) =>
        new(at, "", "Segoe UI", s.FontSize, true, s.Color, s.Fill) { Shadow = s.Shadow };

    public static PenAnnotation Pen(IReadOnlyList<Pt> points, ToolStyle s) =>
        new(points, s.Color, s.Width) { Shadow = s.Shadow };
}
