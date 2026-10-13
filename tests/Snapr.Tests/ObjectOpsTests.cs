using Snapr.Core;

namespace Snapr.Tests;

public class ObjectOpsTests
{
    [Fact]
    public void Flipping_an_arrow_horizontally_mirrors_its_ends()
    {
        var a = new ArrowAnnotation(new(10, 10), new(50, 30), "#FFFF0000", 4);
        var f = (ArrowAnnotation)AnnotationGeometry.Flip(a, horizontal: true);
        Assert.Equal((new Pt(50, 10), new Pt(10, 30)), (f.Start, f.End));
        Assert.Equal(a.Id, f.Id);
    }

    [Fact]
    public void Flipping_a_pen_vertically_mirrors_every_point()
    {
        var p = new PenAnnotation([new(0, 0), new(10, 20)], "#FF000000", 2);
        var f = (PenAnnotation)AnnotationGeometry.Flip(p, horizontal: false);
        Assert.Equal([new Pt(0, 20), new Pt(10, 0)], f.Points);
    }

    [Fact]
    public void Flipping_a_callout_moves_the_tail_to_the_other_side()
    {
        var c = new CalloutAnnotation(new(0, 0, 100, 40), "Hi", 18, "#FFFFFFFF", "#FFFF0000", "#FF111111", new(10, 80));
        var f = (CalloutAnnotation)AnnotationGeometry.Flip(c, horizontal: true);
        Assert.Equal(new Pt(90, 80), f.TailPoint);
        Assert.Equal(new Rct(0, 0, 100, 40), f.Bounds);
    }

    [Fact]
    public void Flipping_text_changes_nothing()
    {
        var t = new TextAnnotation(new(5, 5), "Hello", "Segoe UI", 20, true, "#FF000000");
        Assert.Equal(t, AnnotationGeometry.Flip(t, horizontal: true));
    }

    [Fact]
    public void PasteCopy_adds_an_offset_copy_with_a_new_id()
    {
        var a = new HighlightAnnotation(new(10, 10, 20, 20), "#66FFCC00");
        var (state, pasted) = DocumentState.Empty.Add(a).PasteCopy(a, 16);

        Assert.NotEqual(a.Id, pasted.Id);
        Assert.Equal(new Rct(26, 26, 20, 20), ((HighlightAnnotation)pasted).Bounds);
        Assert.Equal([a.Id, pasted.Id], state.Annotations.Select(x => x.Id));
    }

    [Theory]
    [InlineData(1, 1, 1.5)]
    [InlineData(1, -1, 0.67)]
    [InlineData(0.59, 1, 0.67)]
    [InlineData(0.59, -1, 0.5)]
    [InlineData(4, 1, 4)]
    [InlineData(0.1, -1, 0.1)]
    public void Zoom_moves_to_the_next_step(double current, int direction, double expected) =>
        Assert.Equal(expected, ZoomSteps.Next(current, direction));
}
