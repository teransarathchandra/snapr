using System.Text.Json;
using Snapr.Core;
using Snapr.Core.Stitching;

namespace Snapr.Tests;

public class PixelEditsTests
{
    const uint W = 0xFFFFFFFF, R = 0xFFFF0000, G = 0xFF00FF00, B = 0xFF0000FF;

    /// <summary>A 6 × 4 frame: white with a 2 × 2 red block at (1, 1).</summary>
    static Frame Sample()
    {
        var px = Enumerable.Repeat(W, 24).ToArray();
        foreach (var (x, y) in new[] { (1, 1), (2, 1), (1, 2), (2, 2) }) px[y * 6 + x] = R;
        return new Frame(6, 4, px);
    }

    static uint At(Frame f, int x, int y) => f.Pixels[y * f.Width + x];

    [Fact]
    public void Clear_fills_the_rect_only()
    {
        var f = PixelEdits.Clear(Sample(), new Rct(1, 1, 2, 2), G);
        Assert.Equal(G, At(f, 1, 1));
        Assert.Equal(G, At(f, 2, 2));
        Assert.Equal(W, At(f, 0, 0));
        Assert.Equal(W, At(f, 3, 1));
    }

    [Fact]
    public void Move_puts_pixels_at_the_offset_and_fills_the_hole()
    {
        var f = PixelEdits.Move(Sample(), new Rct(1, 1, 2, 2), 3, 0, B);
        Assert.Equal(R, At(f, 4, 1));
        Assert.Equal(R, At(f, 5, 2));
        Assert.Equal(B, At(f, 1, 1));
        Assert.Equal(B, At(f, 2, 2));
    }

    [Fact]
    public void Overlapping_move_uses_the_original_pixels()
    {
        var px = new uint[] { 1, 2, 3, 4 };
        var f = PixelEdits.Move(new Frame(4, 1, px), new Rct(0, 0, 3, 1), 1, 0, 9);
        Assert.Equal(new uint[] { 9, 1, 2, 3 }, f.Pixels);
    }

    [Fact]
    public void Move_off_the_image_is_clipped()
    {
        var f = PixelEdits.Move(Sample(), new Rct(1, 1, 2, 2), 4, 2, B);
        Assert.Equal(R, At(f, 5, 3));
        Assert.Equal(6 * 4, f.Pixels.Length);
    }

    [Fact]
    public void Edge_colour_is_the_most_frequent_border_colour()
    {
        Assert.Equal(W, PixelEdits.EdgeColour(Sample(), new Rct(0, 0, 6, 4)));
        Assert.Equal(R, PixelEdits.EdgeColour(Sample(), new Rct(1, 1, 3, 2)));   // border: R R W / R R W → R wins
    }

    [Fact]
    public void Edge_colour_ties_go_to_the_first_seen()
    {
        var f = new Frame(2, 1, [G, R]);
        Assert.Equal(G, PixelEdits.EdgeColour(f, new Rct(0, 0, 2, 1)));
    }

    [Fact]
    public void Edge_colour_of_a_tiny_rect() => Assert.Equal(R, PixelEdits.EdgeColour(Sample(), new Rct(1, 1, 2, 2)));

    [Fact]
    public void Auto_fill_is_resolved_when_applied()
    {
        var f = PixelEdits.Apply(Sample(), new ClearAreaEdit(new Rct(0, 0, 3, 3), null));
        Assert.Equal(W, At(f, 1, 1));   // border of (0,0,3,3) is mostly white
        var t = PixelEdits.Apply(Sample(), new ClearAreaEdit(new Rct(1, 1, 2, 2), "#00000000"));
        Assert.Equal(0u, At(t, 1, 1));
    }

    [Fact]
    public void Validity()
    {
        Assert.True(ImageEditRules.IsValid(new MovePixelsEdit(new Rct(1, 1, 2, 2), 3, 0, null), 6, 4));
        Assert.False(ImageEditRules.IsValid(new MovePixelsEdit(new Rct(1, 1, 2, 2), 0, 0, null), 6, 4));
        Assert.False(ImageEditRules.IsValid(new MovePixelsEdit(new Rct(1, 1, 2, 2), 40000, 0, null), 6, 4));
        Assert.False(ImageEditRules.IsValid(new ClearAreaEdit(new Rct(5, 3, 2, 2), null), 6, 4));
        Assert.False(ImageEditRules.IsValid(new ClearAreaEdit(new Rct(0.5, 0, 2, 2), null), 6, 4));
        Assert.False(ImageEditRules.IsValid(new ClearAreaEdit(new Rct(0, 0, 0, 2), null), 6, 4));
        Assert.False(ImageEditRules.IsValid(new ClearAreaEdit(new Rct(0, 0, 2, 2), "red"), 6, 4));
        Assert.Equal((6, 4), ImageEditRules.SizeAfter(6, 4, new ClearAreaEdit(new Rct(0, 0, 2, 2), null)));
    }

    [Fact]
    public void Invalid_pixel_edits_in_a_file_are_rejected() =>
        Assert.ThrowsAny<Exception>(() => ImageEditRules.Validate([new MovePixelsEdit(new Rct(4, 2, 5, 5), 1, 1, null)], 6, 4));

    [Fact]
    public void Edits_round_trip_and_leave_objects_and_crop_alone()
    {
        var arrow = new ArrowAnnotation(new(1, 1), new(5, 3), "#FFFF0000", 2);
        var s = DocumentState.Empty.Add(arrow).SetCrop(new Rct(0, 0, 5, 3), 6, 4)
            .ApplyEdit((6, 4), new MovePixelsEdit(new Rct(1, 1, 2, 2), 3, 0, "#FF00FF00"))
            .ApplyEdit((6, 4), new ClearAreaEdit(new Rct(0, 0, 1, 1), null));
        Assert.Equal(2, s.Edits.Count);
        Assert.Equal(arrow, s.Annotations[0]);
        Assert.Equal(new Rct(0, 0, 5, 3), s.Crop);
        var json = JsonSerializer.Serialize(s, SnaprJson.Options);
        Assert.Contains("movepixels", json);
        Assert.Contains("cleararea", json);
        var back = JsonSerializer.Deserialize<DocumentState>(json, SnaprJson.Options)!;
        Assert.Equal(s.Edits, back.Edits);
        var reverted = s.RevertEdits((6, 4));
        Assert.Empty(reverted.Edits);
        Assert.Equal(arrow, reverted.Annotations[0]);
    }

    [Theory]
    [InlineData(5, 5, true)]     // overlaps the source
    [InlineData(20, 20, false)]  // apart
    [InlineData(10, 0, false)]   // touching edges do not count
    public void Touches_objects_when_the_source_overlaps_one(double x, double y, bool expected)
    {
        var box = new BlurAnnotation(new Rct(x, y, 10, 10), BlurMode.Pixelate, 5);
        Assert.Equal(expected, PixelEdits.TouchesObjects(new Rct(0, 0, 10, 10), [box]));
    }

    [Theory]
    [InlineData(1.2, 1.7, 3.1, 2.9, 1, 1, 3, 2)]     // floor/ceil
    [InlineData(-5, -5, 3, 3, 0, 0, 3, 3)]           // clamped
    [InlineData(4, 3, 10, 10, 4, 3, 2, 1)]           // clamped to 6 × 4 → 2 × 1 → too small
    public void Selection_from_a_drag(double ax, double ay, double bx, double by, double x, double y, double w, double h)
    {
        var r = SelectionRect.FromDrag(new(ax, ay), new(bx, by), 6, 4);
        if (w < 2 || h < 2) Assert.Null(r);
        else Assert.Equal(new Rct(x, y, w, h), r);
    }
}
