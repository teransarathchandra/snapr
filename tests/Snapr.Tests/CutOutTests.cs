using System.Text.Json;
using Snapr.Core;

namespace Snapr.Tests;

public class CutOutTests
{
    static readonly (int W, int H) Size = (200, 100);

    [Fact]
    public void Size_after_removes_the_strip()
    {
        Assert.Equal((200, 70), ImageEditRules.SizeAfter(200, 100, new CutOutEdit(false, 20, 50, CutJoin.Straight)));
        Assert.Equal((150, 100), ImageEditRules.SizeAfter(200, 100, new CutOutEdit(true, 20, 70, CutJoin.Torn)));
    }

    [Theory]
    [InlineData(false, -1, 10)]
    [InlineData(false, 10, 10)]
    [InlineData(false, 10, 101)]
    [InlineData(false, 0, 85)]     // leaves 15 px
    [InlineData(true, 0, 185)]
    public void Invalid_cuts(bool vertical, int start, int end) =>
        Assert.False(ImageEditRules.IsValid(new CutOutEdit(vertical, start, end, CutJoin.Straight), 200, 100));

    [Fact]
    public void Valid_cut_leaving_exactly_16px() =>
        Assert.True(ImageEditRules.IsValid(new CutOutEdit(false, 0, 84, CutJoin.Straight), 200, 100));

    [Fact]
    public void Cut_leaving_less_than_16px_is_refused()
    {
        var s = DocumentState.Empty;
        Assert.Same(s, s.ApplyEdit(Size, new CutOutEdit(false, 0, 90, CutJoin.Straight)));
    }

    [Fact]
    public void Cut_round_trips_through_json()
    {
        var s = DocumentState.Empty.ApplyEdit(Size, new CutOutEdit(true, 20, 70, CutJoin.Torn));
        var json = JsonSerializer.Serialize(s, SnaprJson.Options);
        Assert.Contains("cutout", json);
        Assert.Equal(s.Edits[0], JsonSerializer.Deserialize<DocumentState>(json, SnaprJson.Options)!.Edits[0]);
    }

    [Fact]
    public void Invalid_cut_in_a_file_is_rejected() =>
        Assert.ThrowsAny<Exception>(() => ImageEditRules.Validate([new CutOutEdit(false, 50, 500, CutJoin.Straight)], 200, 100));

    [Fact]
    public void Horizontal_cut_moves_points_after_it_up_and_collapses_points_inside()
    {
        var arrow = new ArrowAnnotation(new(10, 10), new(10, 80), "#FFFF0000", 4);
        var dot = new ArrowAnnotation(new(5, 30), new(6, 35), "#FFFF0000", 4);
        var s = DocumentState.Empty.Add(arrow).Add(dot).ApplyEdit(Size, new CutOutEdit(false, 20, 50, CutJoin.Straight));
        var a = (ArrowAnnotation)s.Annotations[0];
        Assert.Equal(new Pt(10, 10), a.Start);
        Assert.Equal(new Pt(10, 50), a.End);
        var d = (ArrowAnnotation)s.Annotations[1];
        Assert.Equal(new Pt(5, 20), d.Start);
        Assert.Equal(new Pt(6, 20), d.End);
        Assert.Equal(4, a.Width);
    }

    [Fact]
    public void Vertical_cut_moves_points_after_it_left()
    {
        var s = DocumentState.Empty.Add(new ArrowAnnotation(new(150, 10), new(10, 10), "#FFFF0000", 4))
            .ApplyEdit(Size, new CutOutEdit(true, 40, 100, CutJoin.Straight));
        var a = (ArrowAnnotation)s.Annotations[0];
        Assert.Equal(new Pt(90, 10), a.Start);
        Assert.Equal(new Pt(10, 10), a.End);
    }

    [Fact]
    public void Boxes_keep_their_size()
    {
        var img = new ImageAnnotation(new(10, 60, 30, 20), "iVBORw0KGgo=");
        var s = DocumentState.Empty.Add(img).ApplyEdit(Size, new CutOutEdit(false, 20, 50, CutJoin.Straight));
        Assert.Equal(new Rct(10, 30, 30, 20), ((ImageAnnotation)s.Annotations[0]).Bounds);
    }

    [Fact]
    public void Crop_spanning_the_cut_shrinks()
    {
        var s = DocumentState.Empty.SetCrop(new Rct(0, 10, 200, 80), 200, 100)
            .ApplyEdit(Size, new CutOutEdit(false, 20, 50, CutJoin.Straight));
        Assert.Equal(new Rct(0, 10, 200, 50), s.Crop);
    }

    [Fact]
    public void Revert_returns_objects_outside_the_strip_exactly_and_inside_ones_to_its_end()
    {
        var outside = new ArrowAnnotation(new(10, 10), new(10, 80), "#FFFF0000", 4);
        var inside = new ArrowAnnotation(new(5, 30), new(6, 35), "#FFFF0000", 4);
        var start = DocumentState.Empty.Add(outside).Add(inside);
        var back = start.ApplyEdit(Size, new CutOutEdit(false, 20, 50, CutJoin.Torn)).RevertEdits(Size);
        Assert.Empty(back.Edits);
        Assert.Equal(outside, back.Annotations[0]);
        Assert.Equal(new Pt(5, 50), ((ArrowAnnotation)back.Annotations[1]).Start);   // seam points go back to End
    }

    [Fact]
    public void Torn_band_spans_the_seam_and_stays_near_it()
    {
        var band = CutOutGeometry.TornBand(100);
        Assert.Equal(0, band.Min(p => p.X));
        Assert.Equal(100, band.Max(p => p.X));
        Assert.All(band, p => Assert.InRange(p.Y, -8.0, 8.0));
        Assert.Contains(band, p => p.Y < -6);
        Assert.Contains(band, p => p.Y > 6);
    }
}
