using System.Text.Json;
using Snapr.Core;

namespace Snapr.Tests;

public class SpotlightTests
{
    static SpotlightAnnotation Spot() => new(new Rct(10, 10, 40, 30), SpotlightShape.Ellipse, 60, true);

    [Fact]
    public void Round_trips_through_json()
    {
        var s = DocumentState.Empty.Add(Spot());
        var json = JsonSerializer.Serialize(s, SnaprJson.Options);
        Assert.Contains("spotlight", json);
        Assert.Equal(s.Annotations[0], JsonSerializer.Deserialize<DocumentState>(json, SnaprJson.Options)!.Annotations[0]);
    }

    [Fact]
    public void Drag_creates_a_spotlight_with_the_tool_options()
    {
        var o = ToolOptions.Default with { Spot = SpotlightShape.Ellipse, SpotDim = 75, SpotBlur = true };
        var a = Assert.IsType<SpotlightAnnotation>(AnnotationFactory.FromDrag(ToolKind.Spotlight, new(10, 10), new(60, 40), ToolStyle.Default with { Shadow = true }, o));
        Assert.Equal(new Rct(10, 10, 50, 30), a.Bounds);
        Assert.Equal(SpotlightShape.Ellipse, a.Shape);
        Assert.Equal(75, a.Dim);
        Assert.True(a.Blur);
        Assert.False(a.Shadow);
    }

    [Fact]
    public void Defaults_are_rectangle_60_no_blur()
    {
        var a = (SpotlightAnnotation)AnnotationFactory.FromDrag(ToolKind.Spotlight, new(0, 0), new(20, 20), ToolStyle.Default, ToolOptions.Default)!;
        Assert.Equal(SpotlightShape.Rectangle, a.Shape);
        Assert.Equal(60, a.Dim);
        Assert.False(a.Blur);
    }

    [Fact]
    public void Options_apply_and_read_back_with_dim_clamped()
    {
        var a = (SpotlightAnnotation)AnnotationGeometry.WithOptions(Spot(), ToolOptions.Default with { Spot = SpotlightShape.Rectangle, SpotDim = 120, SpotBlur = false });
        Assert.Equal(90, a.Dim);
        Assert.Equal(SpotlightShape.Rectangle, a.Shape);
        var o = AnnotationGeometry.OptionsOf(a, ToolOptions.Default);
        Assert.Equal((SpotlightShape.Rectangle, 90, false), (o.Spot, o.SpotDim, o.SpotBlur));
    }

    [Fact]
    public void Moves_resizes_and_follows_image_edits_as_an_area()
    {
        var a = Spot();
        Assert.Equal(new Rct(15, 5, 40, 30), ((SpotlightAnnotation)AnnotationGeometry.Translate(a, 5, -5)).Bounds);
        Assert.Equal(new Pt(50, 40), AnnotationGeometry.ResizeHandle(a));
        Assert.Equal(new Rct(10, 10, 90, 10), ((SpotlightAnnotation)AnnotationGeometry.ResizeTo(a, new(100, 20))).Bounds);
        var resized = DocumentState.Empty.Add(a).ApplyEdit((100, 100), new ResizeEdit(200, 100));
        Assert.Equal(new Rct(20, 10, 80, 30), ((SpotlightAnnotation)resized.Annotations[0]).Bounds);
    }

    [Fact]
    public void Style_never_adds_a_shadow() =>
        Assert.False(AnnotationGeometry.WithStyle(Spot(), ToolStyle.Default with { Shadow = true }).Shadow);
}
