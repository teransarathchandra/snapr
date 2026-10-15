using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapr.App.Rendering;
using Snapr.Core;

namespace Snapr.Tests;

public class SpotlightRenderTests
{
    const uint Grey = 0xFFC8C8C8;

    static BitmapSource Make(int w, int h, uint[] px)
    {
        var b = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        b.Freeze();
        return b;
    }

    static BitmapSource Solid(int w, int h, uint argb) => Make(w, h, Enumerable.Repeat(argb, w * h).ToArray());

    static BitmapSource Noise(int w, int h)
    {
        var r = new Random(1);
        return Make(w, h, Enumerable.Range(0, w * h).Select(_ => 0xFF000000u | (uint)r.Next(0, 0xFFFFFF)).ToArray());
    }

    static uint Px(BitmapSource b, int x, int y)
    {
        var one = new uint[1];
        new FormatConvertedBitmap(b, PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(x, y, 1, 1), one, 4, 0);
        return one[0];
    }

    static void AssertGrey(BitmapSource img, int x, int y, int expected)
    {
        var p = Px(img, x, y);
        foreach (var shift in new[] { 0, 8, 16 })
            Assert.InRange((int)((p >> shift) & 0xFF), expected - 2, expected + 2);
    }

    static SpotlightAnnotation Spot(Rct b, int dim = 50, bool blur = false, SpotlightShape shape = SpotlightShape.Rectangle) =>
        new(b, shape, dim, blur);

    static BitmapSource Render(BitmapSource src, params Annotation[] a) =>
        DocumentRenderer.Render(src, DocumentState.Empty with { Annotations = a }, applyEffects: false);

    [Fact]
    public void Outside_is_dimmed_inside_is_unchanged() => Sta.Run(() =>
    {
        var img = Render(Solid(100, 100, Grey), Spot(new(20, 20, 30, 30)));
        Assert.Equal(Grey, Px(img, 30, 30));
        AssertGrey(img, 80, 80, 100);
    });

    [Fact]
    public void Largest_dim_wins() => Sta.Run(() =>
    {
        var img = Render(Solid(100, 100, Grey), Spot(new(0, 0, 10, 10), 20), Spot(new(10, 0, 10, 10), 80));
        AssertGrey(img, 90, 90, 40);
    });

    [Fact]
    public void Overlapping_spotlights_stay_bright() => Sta.Run(() =>
    {
        var img = Render(Solid(100, 100, Grey), Spot(new(10, 10, 40, 40)), Spot(new(30, 30, 40, 40)));
        Assert.Equal(Grey, Px(img, 35, 35));
        Assert.Equal(Grey, Px(img, 15, 15));
        Assert.Equal(Grey, Px(img, 65, 65));
        AssertGrey(img, 90, 10, 100);
    });

    [Fact]
    public void Overlapping_rectangle_and_ellipse_stay_bright() => Sta.Run(() =>
    {
        var img = Render(Solid(100, 100, Grey), Spot(new(10, 10, 40, 40), 60),
            Spot(new(30, 30, 40, 40), 60, shape: SpotlightShape.Ellipse));
        Assert.Equal(Grey, Px(img, 45, 45));
    });

    [Fact]
    public void Ellipse_corner_is_dimmed() => Sta.Run(() =>
    {
        var img = Render(Solid(100, 100, Grey), Spot(new(20, 20, 40, 40), shape: SpotlightShape.Ellipse));
        Assert.Equal(Grey, Px(img, 40, 40));
        AssertGrey(img, 21, 21, 100);
    });

    [Fact]
    public void Objects_on_top_stay_bright() => Sta.Run(() =>
    {
        var img = Render(Solid(100, 100, Grey), Spot(new(0, 0, 10, 10), 80),
            new ShapeAnnotation(ShapeKind.Rectangle, new(60, 60, 20, 20), "#FFFF0000", 1, "#FFFF0000"));
        Assert.Equal(0xFFFF0000u, Px(img, 70, 70));
    });

    [Fact]
    public void Blur_changes_the_outside() => Sta.Run(() =>
    {
        var src = Noise(100, 100);
        var img = Render(src, Spot(new(0, 0, 20, 20), 0, blur: true));
        Assert.NotEqual(Px(src, 60, 60), Px(img, 60, 60));
        Assert.Equal(Px(src, 10, 10), Px(img, 10, 10));
    });

    [Fact]
    public void Spotlight_partly_outside_the_image_renders() => Sta.Run(() =>
    {
        var img = Render(Solid(100, 100, Grey), Spot(new(80, 80, 100, 100)));
        Assert.Equal(Grey, Px(img, 90, 90));
        AssertGrey(img, 10, 10, 100);
    });

    [Fact]
    public void Zero_area_spotlight_draws_no_backdrop() => Sta.Run(() =>
    {
        var img = Render(Solid(100, 100, Grey), Spot(new(20, 20, 0, 30), 60));
        Assert.Equal(Grey, Px(img, 80, 80));
    });

    [Fact]
    public void No_spotlight_no_change() => Sta.Run(() =>
    {
        Assert.Equal(Grey, Px(Render(Solid(100, 100, Grey)), 50, 50));
    });
}
