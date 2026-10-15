using Snapr.Core;

namespace Snapr.App.Editor;

/// <summary>The stamp the Stamp tool places: a built-in Id, or a personal picture (base64 PNG and its pixel size).</summary>
public sealed record StampChoice(string? BuiltInId, string? Png, int PixelWidth, int PixelHeight)
{
    public static StampChoice Default { get; } = new("check", null, 0, 0);

    public double Aspect => BuiltInId is { } id
        ? StampCatalog.Find(id) is { } def ? StampCatalog.Aspect(def) : 1
        : (double)PixelWidth / Math.Max(1, PixelHeight);

    public Annotation Create(Rct box, ToolStyle style, Guid? annotationId = null) => BuiltInId is { } id
        ? new StampAnnotation(box, id, style.Color) { Shadow = style.Shadow, Id = annotationId ?? Guid.NewGuid() }
        : new ImageAnnotation(box, Png!) { Shadow = style.Shadow, Id = annotationId ?? Guid.NewGuid() };
}
