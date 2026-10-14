using Snapr.Core;

namespace Snapr.Tests;

public class ImagePlacementTests
{
    static readonly Rct View = new(0, 0, 1000, 800);

    [Fact]
    public void A_small_image_keeps_its_size() => Assert.Equal((100.0, 50.0), ImagePlacement.Fit(100, 50, View));

    [Fact]
    public void A_large_image_shrinks_to_80_percent_of_the_view_keeping_its_shape() =>
        Assert.Equal((800.0, 400.0), ImagePlacement.Fit(2000, 1000, View));   // min(800/2000, 640/1000) = 0.4

    [Fact]
    public void An_empty_view_keeps_the_natural_size() => Assert.Equal((300.0, 200.0), ImagePlacement.Fit(300, 200, default));

    [Fact]
    public void Resizing_an_image_object_keeps_its_aspect_ratio()
    {
        var img = new ImageAnnotation(new Rct(10, 10, 20, 30), "iVBORw0KGgo=");
        Assert.Equal(new Rct(10, 10, 40, 60), ((ImageAnnotation)AnnotationGeometry.ResizeTo(img, new(50, 999))).Bounds);
        Assert.Equal(new Rct(10, 10, 4, 6), ((ImageAnnotation)AnnotationGeometry.ResizeTo(img, new(0, 0))).Bounds);
    }
}
