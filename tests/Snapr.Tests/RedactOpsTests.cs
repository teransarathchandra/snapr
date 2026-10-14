using Snapr.Core;

namespace Snapr.Tests;

public class RedactOpsTests
{
    static readonly RedactLook Black = new(false, "#FF000000");
    static readonly (int, int) Size = (200, 100);

    [Fact]
    public void Covers_are_padded_and_clamped_to_the_image()
    {
        var (s, _) = DocumentState.Empty.AddCovers([new Rct(10, 10, 20, 10), new Rct(0, 95, 10, 5)], Black, Size);
        Assert.Equal(new Rct(8, 8, 24, 14), ((ShapeAnnotation)s.Annotations[0]).Bounds);
        Assert.Equal(new Rct(0, 93, 12, 7), ((ShapeAnnotation)s.Annotations[1]).Bounds);
    }

    [Fact]
    public void Solid_block_uses_the_colour_for_fill_and_stroke()
    {
        var (s, added) = DocumentState.Empty.AddCovers([new Rct(10, 10, 20, 10)], new RedactLook(false, "#FFFF3B30"), Size);
        var shape = Assert.IsType<ShapeAnnotation>(Assert.Single(s.Annotations));
        Assert.Equal((ShapeKind.Rectangle, "#FFFF3B30", "#FFFF3B30", 1.0), (shape.Kind, shape.Stroke, shape.Fill, shape.StrokeWidth));
        Assert.Null(shape.GroupId);
        Assert.Equal([shape.Id], added);
    }

    [Fact]
    public void Pixelate_look_adds_strong_pixelate_areas()
    {
        var (s, _) = DocumentState.Empty.AddCovers([new Rct(10, 10, 20, 10)], new RedactLook(true, "#FF000000"), Size);
        var blur = Assert.IsType<BlurAnnotation>(Assert.Single(s.Annotations));
        Assert.Equal((BlurMode.Pixelate, 20), (blur.Mode, blur.Strength));
    }

    [Fact]
    public void Several_covers_share_one_new_group_and_keep_existing_objects()
    {
        var existing = new ArrowAnnotation(new(1, 1), new(5, 5), "#FFFF0000", 2);
        var (s, added) = (DocumentState.Empty with { Annotations = [existing] }).AddCovers([new Rct(10, 10, 5, 5), new Rct(50, 50, 5, 5)], Black, Size);
        Assert.Equal(3, s.Annotations.Count);
        Assert.Equal(existing, s.Annotations[0]);
        var covers = s.Annotations.Skip(1).ToArray();
        Assert.NotNull(covers[0].GroupId);
        Assert.Equal(covers[0].GroupId, covers[1].GroupId);
        Assert.Equal(covers.Select(c => c.Id), added);
    }

    [Fact]
    public void Boxes_outside_the_image_are_skipped()
    {
        var (s, added) = DocumentState.Empty.AddCovers([new Rct(500, 500, 10, 10)], Black, Size);
        Assert.Empty(s.Annotations);
        Assert.Empty(added);
    }
}
