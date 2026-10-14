using Snapr.Core;

namespace Snapr.Tests;

public class RevertEditsTests
{
    static readonly (int W, int H) Original = (100, 50);

    static DocumentState Start() => (DocumentState.Empty with
    {
        Annotations =
        [
            new ArrowAnnotation(new(10, 5), new(30, 20), "#FFFF0000", 4),
            new ShapeAnnotation(ShapeKind.Rectangle, new(10, 5, 20, 10), "#FFFF0000", 2, null, 6),
            new TextAnnotation(new(10, 5), "ab", "Segoe UI", 20, false, "#FFFF0000"),
            new ImageAnnotation(new(40, 10, 20, 10), "iVBORw0KGgo="),
            new StepAnnotation(new(60, 30), 1, 30, "#FFFF0000", "#FFFFFFFF"),
        ],
        Crop = new Rct(2, 4, 60, 40),
    });

    static DocumentState Apply(DocumentState s, params ImageEdit[] edits)
    {
        var size = Original;
        foreach (var e in edits)
        {
            s = s.ApplyEdit(size, e);
            size = ImageEditRules.SizeAfter(size.W, size.H, e);
        }
        return s;
    }

    [Fact]
    public void Revert_after_mixed_edits_returns_objects_and_crop()
    {
        var start = Start();
        var edited = Apply(start, new RotateEdit(1), new ResizeEdit(100, 200), new MarginsEdit(4, 6, 0, 0, "#FFFFFFFF"), new FlipEdit(true), new FillEdit(3, 3, "#FF00FF00", 10));
        Assert.Equal(5, edited.Edits.Count);

        var back = edited.RevertEdits(Original);
        Assert.Empty(back.Edits);
        Assert.Equal(start.Annotations, back.Annotations);
        Assert.Equal(start.Crop, back.Crop);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Revert_after_each_rotation(int turns)
    {
        var start = Start();
        var back = Apply(start, new RotateEdit(turns)).RevertEdits(Original);
        Assert.Equal(start.Annotations, back.Annotations);
        Assert.Equal(start.Crop, back.Crop);
    }

    [Fact]
    public void Revert_keeps_ids_groups_and_effects()
    {
        var g = Guid.NewGuid();
        var arrow = new ArrowAnnotation(new(10, 5), new(30, 20), "#FFFF0000", 4) { GroupId = g, Shadow = true };
        var start = DocumentState.Empty with { Annotations = [arrow], Effects = new DocumentEffects(null, true, false) };
        var back = Apply(start, new FlipEdit(false)).RevertEdits(Original);
        Assert.Equal(arrow, Assert.Single(back.Annotations));
        Assert.Equal(start.Effects, back.Effects);
    }

    [Fact]
    public void Revert_after_non_uniform_resize_restores_lengths()
    {
        var start = Start();
        var back = Apply(start, new ResizeEdit(100, 100)).RevertEdits(Original);

        var arrow = Assert.IsType<ArrowAnnotation>(back.Annotations[0]);
        Assert.Equal(4, arrow.Width, 9);
        Assert.Equal(10, arrow.Start.X, 9);
        Assert.Equal(5, arrow.Start.Y, 9);
        Assert.Equal(30, arrow.End.X, 9);
        Assert.Equal(20, arrow.End.Y, 9);

        var shape = Assert.IsType<ShapeAnnotation>(back.Annotations[1]);
        Assert.Equal(2, shape.StrokeWidth, 9);
        Assert.Equal(6, shape.CornerRadius, 9);
        Assert.Equal(10, shape.Bounds.X, 9);
        Assert.Equal(5, shape.Bounds.Y, 9);
        Assert.Equal(20, shape.Bounds.W, 9);
        Assert.Equal(10, shape.Bounds.H, 9);

        var text = Assert.IsType<TextAnnotation>(back.Annotations[2]);
        Assert.Equal(20, text.FontSize, 9);
        Assert.Equal(10, text.Position.X, 9);
        Assert.Equal(5, text.Position.Y, 9);

        var step = Assert.IsType<StepAnnotation>(back.Annotations[4]);
        Assert.Equal(30, step.Size, 9);
        Assert.Equal(60, step.Center.X, 9);
        Assert.Equal(30, step.Center.Y, 9);
    }

    [Fact]
    public void Without_edits_nothing_changes()
    {
        var s = Start();
        Assert.Same(s, s.RevertEdits(Original));
    }
}
