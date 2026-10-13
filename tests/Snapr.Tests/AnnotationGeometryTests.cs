using Snapr.Core;

namespace Snapr.Tests;

public class AnnotationGeometryTests
{
    [Fact]
    public void Arrow_hit_uses_distance_to_the_line()
    {
        var a = new ArrowAnnotation(new(0, 0), new(100, 0), "#FFFF0000", 4);
        Assert.True(AnnotationGeometry.Hit(a, new(50, 5), 6));
        Assert.False(AnnotationGeometry.Hit(a, new(50, 20), 6));
    }

    [Fact]
    public void Translate_moves_every_pen_point()
    {
        var p = new PenAnnotation([new(0, 0), new(10, 5)], "#FF000000", 2);
        var moved = (PenAnnotation)AnnotationGeometry.Translate(p, 3, 4);
        Assert.Equal([new Pt(3, 4), new Pt(13, 9)], moved.Points);
        Assert.Equal(p.Id, moved.Id);
    }

    [Fact]
    public void Resize_moves_the_bottom_right_corner()
    {
        var s = new ShapeAnnotation(ShapeKind.Rectangle, new(10, 10, 20, 20), "#FF000000", 2);
        Assert.Equal(new Pt(30, 30), AnnotationGeometry.ResizeHandle(s));
        Assert.Equal(new Rct(10, 10, 50, 40), ((ShapeAnnotation)AnnotationGeometry.ResizeTo(s, new(60, 50))).Bounds);
    }

    [Fact]
    public void Magnify_resize_changes_zoom_within_limits()
    {
        var m = new MagnifyAnnotation(new(0, 0, 20, 20), new(100, 100), 2, MagnifyShape.Circle);
        Assert.Equal(3, ((MagnifyAnnotation)AnnotationGeometry.ResizeTo(m, new(130, 130))).Zoom);
        Assert.Equal(4, ((MagnifyAnnotation)AnnotationGeometry.ResizeTo(m, new(900, 900))).Zoom);
        Assert.Equal(1.5, ((MagnifyAnnotation)AnnotationGeometry.ResizeTo(m, new(90, 90))).Zoom);
    }

    [Fact]
    public void WithStyle_then_StyleOf_round_trips()
    {
        var a = new ArrowAnnotation(new(0, 0), new(10, 10), "#FFFF0000", 4);
        var style = new ToolStyle("#FF3B82F6", 7, 20, null, true);
        var styled = AnnotationGeometry.WithStyle(a, style);

        Assert.Equal(style, AnnotationGeometry.StyleOf(styled, ToolStyle.Default with { FontSize = 20 }));
        Assert.True(styled.Shadow);
    }

    [Fact]
    public void WithOptions_changes_blur_mode_and_strength()
    {
        var b = new BlurAnnotation(new(0, 0, 10, 10), BlurMode.Blur, 3);
        var changed = (BlurAnnotation)AnnotationGeometry.WithOptions(b, ToolOptions.Default with { Blur = BlurMode.Pixelate, BlurStrength = 12 });
        Assert.Equal((BlurMode.Pixelate, 12), (changed.Mode, changed.Strength));
    }

    [Fact]
    public void Factory_ignores_tiny_drags() =>
        Assert.Null(AnnotationFactory.FromDrag(ToolKind.Arrow, new(0, 0), new(1, 1), ToolStyle.Default, ToolOptions.Default));

    [Fact]
    public void Shape_line_option_creates_a_headless_arrow()
    {
        var a = AnnotationFactory.FromDrag(ToolKind.Shape, new(0, 0), new(40, 40), ToolStyle.Default,
            ToolOptions.Default with { Shape = ShapeOption.Line });
        var arrow = Assert.IsType<ArrowAnnotation>(a);
        Assert.Equal((ArrowHead.None, ArrowHead.None), (arrow.Head, arrow.Tail));
    }

    [Fact]
    public void Highlight_uses_40_percent_alpha()
    {
        var h = AnnotationFactory.FromDrag(ToolKind.Highlight, new(0, 0), new(40, 10), ToolStyle.Default with { Color = "#FFFFCC00" }, ToolOptions.Default);
        Assert.Equal("#66FFCC00", Assert.IsType<HighlightAnnotation>(h).Color);
    }

    [Fact]
    public void WithAlpha_accepts_six_and_eight_digit_colours()
    {
        Assert.Equal("#40112233", ColorText.WithAlpha("#112233", 0x40));
        Assert.Equal("#40112233", ColorText.WithAlpha("#FF112233", 0x40));
    }

    [Theory]
    [InlineData(ToolKind.Highlight)]
    [InlineData(ToolKind.Blur)]
    [InlineData(ToolKind.Shape)]
    [InlineData(ToolKind.Callout)]
    [InlineData(ToolKind.Magnify)]
    public void Straight_drag_makes_no_zero_area_box(ToolKind tool) =>
        Assert.Null(AnnotationFactory.FromDrag(tool, new(0, 10), new(40, 10), ToolStyle.Default, ToolOptions.Default));

    [Fact]
    public void Straight_drag_still_draws_an_arrow() =>
        Assert.NotNull(AnnotationFactory.FromDrag(ToolKind.Arrow, new(0, 10), new(40, 10), ToolStyle.Default, ToolOptions.Default));
}
