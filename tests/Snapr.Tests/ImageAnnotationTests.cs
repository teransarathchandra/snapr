using System.Text.Json;
using Snapr.Core;

namespace Snapr.Tests;

public class ImageAnnotationTests
{
    static readonly ImageAnnotation Img = new(new Rct(10, 10, 20, 30), "iVBORw0KGgo=");

    [Fact]
    public void Round_trips_through_snapr_json()
    {
        var json = JsonSerializer.Serialize<Annotation>(Img, SnaprJson.Options);
        Assert.Contains("\"type\": \"image\"", json);
        Assert.Equal(Img, JsonSerializer.Deserialize<Annotation>(json, SnaprJson.Options));
    }

    [Fact]
    public void Moves_and_resizes_by_its_bounds()
    {
        Assert.Equal(new Rct(15, 15, 20, 30), ((ImageAnnotation)AnnotationGeometry.Translate(Img, 5, 5)).Bounds);
        Assert.Equal(new Pt(30, 40), AnnotationGeometry.ResizeHandle(Img));
        Assert.Equal(new Rct(10, 10, 40, 50), ((ImageAnnotation)AnnotationGeometry.ResizeTo(Img, new(50, 60))).Bounds);
    }

    [Fact]
    public void Is_hit_inside_its_bounds()
    {
        Assert.True(AnnotationGeometry.Hit(Img, new(20, 20), 2));
        Assert.False(AnnotationGeometry.Hit(Img, new(100, 100), 2));
    }

    [Fact]
    public void Keeps_its_shadow_through_style_changes()
    {
        Assert.True(AnnotationGeometry.StyleOf(Img with { Shadow = true }, ToolStyle.Default).Shadow);
        Assert.False(AnnotationGeometry.WithStyle(Img with { Shadow = true }, ToolStyle.Default with { Shadow = false }).Shadow);
    }

    [Fact]
    public void Imported_capture_kind_is_written_by_name() =>
        Assert.Equal("\"Imported\"", JsonSerializer.Serialize(CaptureKind.Imported, SnaprJson.Options));
}
