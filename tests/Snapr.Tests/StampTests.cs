using System.Text.Json;
using Snapr.Core;

namespace Snapr.Tests;

public class StampTests
{
    [Fact]
    public void Catalogue_has_25_unique_stamps()
    {
        Assert.Equal(25, StampCatalog.All.Count);
        Assert.Equal(25, StampCatalog.All.Select(s => s.Id).Distinct().Count());
        Assert.All(StampCatalog.All, s => Assert.False(string.IsNullOrWhiteSpace(s.Name)));
        Assert.All(StampCatalog.All.Where(s => s.Shape == StampShape.Path), s => Assert.StartsWith("F0 ", s.Data));
        Assert.Equal("check", StampCatalog.All[0].Id);
    }

    [Fact]
    public void Find_returns_definition_or_null()
    {
        Assert.Equal("Star", StampCatalog.Find("star")!.Name);
        Assert.Null(StampCatalog.Find("nope"));
    }

    [Fact]
    public void Aspect_is_square_for_paths_and_single_letter_badges_wide_for_words()
    {
        Assert.Equal(1, StampCatalog.Aspect(StampCatalog.Find("check")!));
        Assert.Equal(1, StampCatalog.Aspect(StampCatalog.Find("badge-1")!));
        Assert.True(StampCatalog.Aspect(StampCatalog.Find("todo")!) > 2);
    }

    [Fact]
    public void Centered_click_is_64_on_the_longest_side()
    {
        Assert.Equal(new Rct(68, 68, 64, 64), StampPlacement.Centered(new(100, 100), 1));
        var wide = StampPlacement.Centered(new(100, 100), 2);
        Assert.Equal(new Rct(68, 84, 64, 32), wide);
        var tall = StampPlacement.Centered(new(100, 100), 0.5);
        Assert.Equal(new Rct(84, 68, 32, 64), tall);
    }

    [Fact]
    public void Drag_uses_larger_side_in_the_drag_direction()
    {
        Assert.Equal(new Rct(10, 10, 50, 50), StampPlacement.FromDrag(new(10, 10), new(60, 30), 1));
        Assert.Equal(new Rct(-40, -40, 50, 50), StampPlacement.FromDrag(new(10, 10), new(-40, 0), 1));
        Assert.Equal(new Rct(10, 10, 40, 20), StampPlacement.FromDrag(new(10, 10), new(20, 50), 2));
    }

    [Fact]
    public void Stamp_round_trips_through_json()
    {
        var s = DocumentState.Empty.Add(new StampAnnotation(new(1, 2, 64, 64), "star", "#FF3B82F6") { Shadow = true });
        var json = JsonSerializer.Serialize(s, SnaprJson.Options);
        Assert.Contains("\"type\": \"stamp\"", json);
        var back = JsonSerializer.Deserialize<DocumentState>(json, SnaprJson.Options)!;
        Assert.Equal(s.Annotations[0], back.Annotations[0]);
    }

    [Fact]
    public void Stamp_moves_and_reports_bounds()
    {
        var a = new StampAnnotation(new(10, 10, 64, 64), "check", "#FFFF0000");
        Assert.Equal(new Rct(10, 10, 64, 64), AnnotationGeometry.Bounds(a));
        Assert.Equal(new Rct(15, 7, 64, 64), ((StampAnnotation)AnnotationGeometry.Translate(a, 5, -3)).Bounds);
        Assert.Equal(new Pt(74, 74), AnnotationGeometry.ResizeHandle(a));
    }

    [Fact]
    public void Resize_handle_keeps_stamp_square()
    {
        var a = new StampAnnotation(new(10, 10, 64, 64), "check", "#FFFF0000");
        var big = (StampAnnotation)AnnotationGeometry.ResizeTo(a, new(110, 20));
        Assert.Equal(new Rct(10, 10, 100, 100), big.Bounds);
        var tiny = (StampAnnotation)AnnotationGeometry.ResizeTo(a, new(0, 0));
        Assert.Equal(4, tiny.Bounds.W);
        Assert.Equal(4, tiny.Bounds.H);
    }

    [Fact]
    public void Style_sets_and_reads_colour_and_shadow()
    {
        var a = new StampAnnotation(new(0, 0, 64, 64), "check", "#FFFF0000");
        var styled = (StampAnnotation)AnnotationGeometry.WithStyle(a, ToolStyle.Default with { Color = "#FF34C759", Shadow = true });
        Assert.Equal("#FF34C759", styled.Color);
        Assert.True(styled.Shadow);
        var read = AnnotationGeometry.StyleOf(styled, ToolStyle.Default);
        Assert.Equal("#FF34C759", read.Color);
        Assert.True(read.Shadow);
    }

    [Fact]
    public void Rotation_keeps_stamp_size_and_revert_returns_it()
    {
        var start = DocumentState.Empty.Add(new StampAnnotation(new(10, 5, 20, 20), "check", "#FFFF0000"));
        var rotated = start.ApplyEdit((100, 50), new RotateEdit(1));
        var r = ((StampAnnotation)rotated.Annotations[0]).Bounds;
        Assert.Equal(20, r.W);
        Assert.Equal(20, r.H);
        var back = rotated.RevertEdits((100, 50));
        Assert.Equal(start.Annotations[0], back.Annotations[0]);
    }

    [Fact]
    public void Resize_edit_scales_stamp()
    {
        var start = DocumentState.Empty.Add(new StampAnnotation(new(10, 10, 20, 20), "check", "#FFFF0000"));
        var r = ((StampAnnotation)start.ApplyEdit((100, 100), new ResizeEdit(200, 200)).Annotations[0]).Bounds;
        Assert.Equal(new Rct(20, 20, 40, 40), r);
    }
}
