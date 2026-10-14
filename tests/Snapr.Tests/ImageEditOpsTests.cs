using Snapr.Core;

namespace Snapr.Tests;

public class ImageEditOpsTests
{
    static readonly (int, int) Size = (100, 50);
    static DocumentState Doc(params Annotation[] items) => DocumentState.Empty with { Annotations = items };
    static T Only<T>(DocumentState s) => Assert.IsType<T>(Assert.Single(s.Annotations));

    [Fact]
    public void Rotate_right_moves_points_and_swaps_areas()
    {
        var s = Doc(new ArrowAnnotation(new(10, 5), new(30, 20), "#FFFF0000", 4)).ApplyEdit(Size, new RotateEdit(1));
        var a = Only<ArrowAnnotation>(s);
        Assert.Equal((new Pt(45, 10), new Pt(30, 30)), (a.Start, a.End));   // (x, y) -> (H - y, x)
        Assert.Equal([new RotateEdit(1)], s.Edits);

        var shape = Only<ShapeAnnotation>(Doc(new ShapeAnnotation(ShapeKind.Rectangle, new(10, 5, 20, 10), "#FFFF0000", 2)).ApplyEdit(Size, new RotateEdit(1)));
        Assert.Equal(new Rct(35, 10, 10, 20), shape.Bounds);
    }

    [Fact]
    public void Rotate_right_keeps_text_and_image_sizes()
    {
        var img = Only<ImageAnnotation>(Doc(new ImageAnnotation(new(10, 5, 20, 10), "iVBORw0KGgo=")).ApplyEdit(Size, new RotateEdit(1)));
        Assert.Equal(new Rct(30, 15, 20, 10), img.Bounds);   // centre (20, 10) -> (40, 20); same width and height

        // "ab" at size 20: bounds 24 × 27 at (10, 5); centre (22, 18.5) -> (31.5, 22)
        var text = Only<TextAnnotation>(Doc(new TextAnnotation(new(10, 5), "ab", "Segoe UI", 20, false, "#FFFF0000")).ApplyEdit(Size, new RotateEdit(1)));
        Assert.Equal(new Pt(19.5, 8.5), text.Position);
        Assert.Equal(20, text.FontSize);
    }

    [Fact]
    public void Rotate_left_and_180_map_points()
    {
        var left = Only<StepAnnotation>(Doc(new StepAnnotation(new(10, 5), 1, 30, "#FFFF0000", "#FFFFFFFF")).ApplyEdit(Size, new RotateEdit(3)));
        Assert.Equal(new Pt(5, 90), left.Center);   // (x, y) -> (y, W - x)
        var half = Only<StepAnnotation>(Doc(new StepAnnotation(new(10, 5), 1, 30, "#FFFF0000", "#FFFFFFFF")).ApplyEdit(Size, new RotateEdit(2)));
        Assert.Equal(new Pt(90, 45), half.Center);
    }

    [Fact]
    public void Flips_mirror_points_and_areas()
    {
        var h = Doc(new PenAnnotation([new(10, 5), new(20, 15)], "#FF000000", 3), new HighlightAnnotation(new(10, 5, 20, 10), "#66FFFF00")).ApplyEdit(Size, new FlipEdit(true));
        Assert.Equal([new Pt(90, 5), new Pt(80, 15)], ((PenAnnotation)h.Annotations[0]).Points);
        Assert.Equal(new Rct(70, 5, 20, 10), ((HighlightAnnotation)h.Annotations[1]).Bounds);
        var v = Only<CalloutAnnotation>(Doc(new CalloutAnnotation(new(10, 5, 30, 10), "Hi", 18, "#FFFFFFFF", "#FFFF0000", "#FF111111", new(12, 30))).ApplyEdit(Size, new FlipEdit(false)));
        Assert.Equal(new Rct(10, 35, 30, 10), v.Bounds);
        Assert.Equal(new Pt(12, 20), v.TailPoint);
    }

    [Fact]
    public void Resize_scales_positions_and_lengths()
    {
        var s = Doc(
            new ArrowAnnotation(new(10, 5), new(30, 20), "#FFFF0000", 4),
            new TextAnnotation(new(10, 5), "ab", "Segoe UI", 20, false, "#FFFF0000"),
            new ShapeAnnotation(ShapeKind.Rectangle, new(10, 5, 20, 10), "#FFFF0000", 2, null, 6),
            new StepAnnotation(new(10, 5), 1, 30, "#FFFF0000", "#FFFFFFFF"),
            new MagnifyAnnotation(new(10, 5, 20, 10), new(50, 25), 2, MagnifyShape.Circle)).ApplyEdit(Size, new ResizeEdit(200, 100));
        var arrow = (ArrowAnnotation)s.Annotations[0];
        Assert.Equal((new Pt(20, 10), new Pt(60, 40), 8.0), (arrow.Start, arrow.End, arrow.Width));
        var text = (TextAnnotation)s.Annotations[1];
        Assert.Equal((new Pt(20, 10), 40.0), (text.Position, text.FontSize));
        var shape = (ShapeAnnotation)s.Annotations[2];
        Assert.Equal((new Rct(20, 10, 40, 20), 4.0, 12.0), (shape.Bounds, shape.StrokeWidth, shape.CornerRadius));
        Assert.Equal(60, ((StepAnnotation)s.Annotations[3]).Size);
        var mag = (MagnifyAnnotation)s.Annotations[4];
        Assert.Equal((new Rct(20, 10, 40, 20), new Pt(100, 50), 2.0), (mag.SourceBounds, mag.TargetCenter, mag.Zoom));
    }

    [Fact]
    public void Margins_move_everything_by_left_and_top()
    {
        var s = Doc(new BlurAnnotation(new(10, 5, 20, 10), BlurMode.Pixelate, 8)).ApplyEdit(Size, new MarginsEdit(5, 7, 1, 1, "#FFFFFFFF"));
        Assert.Equal(new Rct(15, 12, 20, 10), Only<BlurAnnotation>(s).Bounds);
    }

    [Fact]
    public void Fill_keeps_objects_and_adds_the_edit()
    {
        var a = new ArrowAnnotation(new(10, 5), new(30, 20), "#FFFF0000", 4);
        var s = Doc(a).ApplyEdit(Size, new FillEdit(3, 4, "#FF00FF00", 10));
        Assert.Equal(a, Only<ArrowAnnotation>(s));
        Assert.Equal([new FillEdit(3, 4, "#FF00FF00", 10)], s.Edits);
    }

    [Fact]
    public void Crop_follows_rotation_and_stays_inside()
    {
        var s = (DocumentState.Empty with { Crop = new Rct(0, 0, 40, 50) }).ApplyEdit(Size, new RotateEdit(1));
        Assert.Equal(new Rct(0, 0, 50, 40), s.Crop);
        var shrunk = (DocumentState.Empty with { Crop = new Rct(10, 10, 80, 30) }).ApplyEdit(Size, new ResizeEdit(10, 5));
        Assert.Equal(new Rct(1, 1, 8, 3), shrunk.Crop);
    }

    [Fact]
    public void Four_right_turns_and_two_flips_return_every_object()
    {
        var start = Doc(
            new ArrowAnnotation(new(10, 5), new(30, 20), "#FFFF0000", 4),
            new ShapeAnnotation(ShapeKind.Ellipse, new(10, 5, 20, 10), "#FFFF0000", 2),
            new TextAnnotation(new(10, 5), "ab", "Segoe UI", 20, false, "#FFFF0000"),
            new ImageAnnotation(new(10, 5, 20, 10), "iVBORw0KGgo="),
            new CalloutAnnotation(new(10, 5, 30, 10), "Hi", 18, "#FFFFFFFF", "#FFFF0000", "#FF111111", new(12, 30))) with { Crop = new Rct(2, 3, 40, 30) };
        var s = start;
        var size = Size;
        for (var i = 0; i < 4; i++)
        {
            s = s.ApplyEdit(size, new RotateEdit(1));
            size = ImageEditRules.SizeAfter(size.Item1, size.Item2, new RotateEdit(1));
        }
        Assert.Equal(start.Annotations, s.Annotations);
        Assert.Equal(start.Crop, s.Crop);

        var flipped = start.ApplyEdit(Size, new FlipEdit(true)).ApplyEdit(Size, new FlipEdit(true)).ApplyEdit(Size, new FlipEdit(false)).ApplyEdit(Size, new FlipEdit(false));
        Assert.Equal(start.Annotations, flipped.Annotations);
    }

    [Fact]
    public void No_op_and_oversized_edits_change_nothing()
    {
        var s = Doc(new ArrowAnnotation(new(10, 5), new(30, 20), "#FFFF0000", 4));
        Assert.Same(s, s.ApplyEdit(Size, new ResizeEdit(100, 50)));
        Assert.Same(s, s.ApplyEdit(Size, new MarginsEdit(0, 0, 0, 0, "#FFFFFFFF")));
        Assert.Same(s, s.ApplyEdit(Size, new FillEdit(100, 0, "#FFFFFFFF", 10)));
        Assert.Same(s, s.ApplyEdit(Size, new ResizeEdit(40_000, 10)));
    }

    [Fact]
    public void Object_ids_and_groups_are_kept()
    {
        var g = Guid.NewGuid();
        var a = new ArrowAnnotation(new(10, 5), new(30, 20), "#FFFF0000", 4) { GroupId = g, Shadow = true };
        var r = Only<ArrowAnnotation>(Doc(a).ApplyEdit(Size, new RotateEdit(2)));
        Assert.Equal((a.Id, g, true), (r.Id, r.GroupId, r.Shadow));
    }

    [Fact]
    public void Invalid_edits_and_the_1001st_edit_change_nothing()
    {
        var s = Doc(new ArrowAnnotation(new(10, 5), new(30, 20), "#FFFF0000", 4));
        Assert.Same(s, s.ApplyEdit(Size, new RotateEdit(4)));
        Assert.Same(s, s.ApplyEdit(Size, new MarginsEdit(-1, 0, 0, 0, "#FFFFFFFF")));
        Assert.Same(s, s.ApplyEdit(Size, new FillEdit(1, 1, "#FFFFFFFF", 101)));
        Assert.Same(s, s.ApplyEdit(Size, new FillEdit(1, 1, "red", 10)));

        var full = DocumentState.Empty with { Edits = Enumerable.Repeat<ImageEdit>(new FlipEdit(true), 1000).ToArray() };
        Assert.Same(full, full.ApplyEdit(Size, new FlipEdit(true)));
    }
}
