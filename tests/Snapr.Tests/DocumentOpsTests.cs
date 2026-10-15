using Snapr.Core;

namespace Snapr.Tests;

public class DocumentOpsTests
{
    static readonly ToolStyle Red = ToolStyle.Default;

    [Fact]
    public void Steps_number_themselves_and_restart()
    {
        var s = DocumentState.Empty.AddStep(new(10, 10), Red).AddStep(new(20, 20), Red);
        s = s.RestartSteps().AddStep(new(30, 30), Red);

        Assert.Equal([1, 2, 1], s.Annotations.OfType<StepAnnotation>().Select(x => x.Number));
        Assert.Equal(2, s.NextStepNumber);
    }

    [Fact]
    public void Deleting_a_step_does_not_renumber()
    {
        var s = DocumentState.Empty.AddStep(new(10, 10), Red).AddStep(new(20, 20), Red);
        s = s.Remove(s.Annotations[0].Id).AddStep(new(30, 30), Red);

        Assert.Equal([2, 3], s.Annotations.OfType<StepAnnotation>().Select(x => x.Number));
    }

    [Fact]
    public void Empty_text_is_removed_on_commit()
    {
        var t = AnnotationFactory.Text(new(5, 5), Red);
        var s = DocumentState.Empty.Add(t).CommitText(t.Id, "   ");
        Assert.Empty(s.Annotations);
    }

    [Fact]
    public void Text_commit_sets_text_and_keeps_empty_callouts()
    {
        var t = AnnotationFactory.Text(new(5, 5), Red);
        var c = (CalloutAnnotation)AnnotationFactory.FromDrag(ToolKind.Callout, new(0, 0), new(80, 40), Red, ToolOptions.Default)!;
        var s = DocumentState.Empty.Add(t).Add(c).CommitText(t.Id, "Hi").CommitText(c.Id, "");

        Assert.Equal("Hi", Assert.IsType<TextAnnotation>(s.Annotations[0]).Text);
        Assert.IsType<CalloutAnnotation>(s.Annotations[1]);
    }

    [Fact]
    public void Crop_is_clamped_rounded_and_reset_restores()
    {
        var s = DocumentState.Empty.SetCrop(new Rct(-10, 5, 500, 50.6), 100, 80);
        Assert.Equal(new Rct(0, 5, 100, 51), s.Crop);
        Assert.Null(s.ResetCrop().Crop);
    }

    [Fact]
    public void Crop_outside_the_image_is_ignored() =>
        Assert.Null(DocumentState.Empty.SetCrop(new Rct(200, 200, 10, 10), 100, 80).Crop);

    [Fact]
    public void Bring_to_front_and_send_to_back_reorder()
    {
        var a = new HighlightAnnotation(new(0, 0, 10, 10), "#66FFCC00");
        var b = new HighlightAnnotation(new(0, 0, 10, 10), "#66FFCC00");
        var s = DocumentState.Empty.Add(a).Add(b);

        Assert.Equal(a.Id, s.BringToFront(a.Id).Annotations[^1].Id);
        Assert.Equal(b.Id, s.SendToBack(b.Id).Annotations[0].Id);
    }

    [Fact]
    public void HitTest_returns_the_topmost_annotation()
    {
        var below = new ShapeAnnotation(ShapeKind.Rectangle, new(0, 0, 50, 50), "#FF000000", 2);
        var above = new ShapeAnnotation(ShapeKind.Rectangle, new(10, 10, 50, 50), "#FF000000", 2);
        var s = DocumentState.Empty.Add(below).Add(above);

        Assert.Equal(above.Id, s.HitTest(new(20, 20), 2)!.Id);
        Assert.Null(s.HitTest(new(200, 200), 2));
    }

    [Fact]
    public void HitTest_prefers_other_objects_over_a_spotlight_above_them()
    {
        var arrow = new ShapeAnnotation(ShapeKind.Rectangle, new(0, 0, 50, 50), "#FF000000", 2, "#FF000000");
        var spot = new SpotlightAnnotation(new(0, 0, 100, 100), SpotlightShape.Rectangle, 50, false);
        var s = DocumentState.Empty.Add(arrow).Add(spot);

        Assert.Equal(arrow.Id, s.HitTest(new(20, 20), 2)!.Id);
    }

    [Fact]
    public void HitTest_returns_a_spotlight_when_nothing_else_is_there()
    {
        var spot = new SpotlightAnnotation(new(0, 0, 100, 100), SpotlightShape.Rectangle, 50, false);
        var s = DocumentState.Empty.Add(spot);

        Assert.Equal(spot.Id, s.HitTest(new(20, 20), 2)!.Id);
    }

    [Fact]
    public void SameAs_detects_no_op_edits()
    {
        var s = DocumentState.Empty.AddStep(new(1, 1), Red);
        Assert.True(s.SameAs(s with { }));
        Assert.False(s.SameAs(s.RestartSteps()));
    }
}
