namespace Snapr.Core;

/// <summary>How covers look: solid blocks in Color, or strong pixelate.</summary>
public sealed record RedactLook(bool Pixelate, string Color);

public static class RedactOps
{
    public const double Padding = 2;

    /// <summary>Adds one cover per box, padded by 2 px and clamped to the image (spec parity 5 §4). Several covers share one new group.</summary>
    public static (DocumentState State, IReadOnlyList<Guid> Added) AddCovers(this DocumentState s, IReadOnlyList<Rct> boxes, RedactLook look, (int W, int H) imageSize)
    {
        var image = new Rct(0, 0, imageSize.W, imageSize.H);
        var areas = boxes.Select(b => b.Inflate(Padding).Intersect(image)).Where(b => !b.IsEmpty).ToArray();
        Guid? group = areas.Length > 1 ? Guid.NewGuid() : null;
        var covers = areas.Select(b => (look.Pixelate
            ? (Annotation)new BlurAnnotation(b, BlurMode.Pixelate, 20)
            : new ShapeAnnotation(ShapeKind.Rectangle, b, look.Color, 1, look.Color)) with { GroupId = group }).ToArray();
        return (s with { Annotations = [.. s.Annotations, .. covers] }, covers.Select(c => c.Id).ToArray());
    }
}
