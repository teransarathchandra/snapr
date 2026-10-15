using System.Text.Json.Serialization;

namespace Snapr.Core;

public enum ArrowHead { None, Triangle, Line }
public enum ShapeKind { Rectangle, Ellipse }
public enum BlurMode { Blur, Pixelate }
public enum MagnifyShape { Circle, Rectangle }

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ArrowAnnotation), "arrow")]
[JsonDerivedType(typeof(ShapeAnnotation), "shape")]
[JsonDerivedType(typeof(TextAnnotation), "text")]
[JsonDerivedType(typeof(CalloutAnnotation), "callout")]
[JsonDerivedType(typeof(StepAnnotation), "step")]
[JsonDerivedType(typeof(HighlightAnnotation), "highlight")]
[JsonDerivedType(typeof(PenAnnotation), "pen")]
[JsonDerivedType(typeof(BlurAnnotation), "blur")]
[JsonDerivedType(typeof(MagnifyAnnotation), "magnify")]
[JsonDerivedType(typeof(ImageAnnotation), "image")]
[JsonDerivedType(typeof(StampAnnotation), "stamp")]
public abstract record Annotation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public bool Shadow { get; init; }
    /// <summary>Objects with the same non-null GroupId select, move and copy as one. Null = not grouped (and omitted from JSON).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? GroupId { get; init; }
}

public sealed record ArrowAnnotation(Pt Start, Pt End, string Color, double Width,
    ArrowHead Head = ArrowHead.Triangle, ArrowHead Tail = ArrowHead.None) : Annotation;

public sealed record ShapeAnnotation(ShapeKind Kind, Rct Bounds, string Stroke, double StrokeWidth,
    string? Fill = null, double CornerRadius = 0) : Annotation;

public sealed record TextAnnotation(Pt Position, string Text, string FontFamily, double FontSize, bool Bold,
    string Color, string? Background = null) : Annotation;

public sealed record CalloutAnnotation(Rct Bounds, string Text, double FontSize, string Fill, string Stroke,
    string TextColor, Pt TailPoint) : Annotation;

public enum StepStyle { Numbers, Letters }

public sealed record StepAnnotation(Pt Center, int Number, double Size, string Fill, string TextColor,
    StepStyle Style = StepStyle.Numbers) : Annotation;

public sealed record HighlightAnnotation(Rct Bounds, string Color) : Annotation;

public sealed record PenAnnotation(IReadOnlyList<Pt> Points, string Color, double Width) : Annotation;

public sealed record BlurAnnotation(Rct Bounds, BlurMode Mode, int Strength) : Annotation;

public sealed record MagnifyAnnotation(Rct SourceBounds, Pt TargetCenter, double Zoom, MagnifyShape Shape) : Annotation;

/// <summary>A picture placed on the capture (for example a Snagit image object). Png is base64-encoded PNG bytes.</summary>
public sealed record ImageAnnotation(Rct Bounds, string Png) : Annotation;

/// <summary>A built-in stamp (spec parity 6 §2.2): the drawing comes from StampCatalog by Id, filled with Color.</summary>
public sealed record StampAnnotation(Rct Bounds, string StampId, string Color) : Annotation;
