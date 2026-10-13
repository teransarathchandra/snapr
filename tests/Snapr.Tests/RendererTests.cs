using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapr.App.Rendering;
using Snapr.Core;

namespace Snapr.Tests;

public class RendererTests
{
    static BitmapSource Noise(int w, int h)
    {
        var r = new Random(1);
        var px = new uint[w * h];
        for (var i = 0; i < px.Length; i++) px[i] = 0xFF000000u | (uint)r.Next(0, 0xFFFFFF);
        return Make(w, h, px);
    }

    static BitmapSource Solid(int w, int h, uint argb) => Make(w, h, Enumerable.Repeat(argb, w * h).ToArray());

    static BitmapSource Make(int w, int h, uint[] px)
    {
        var b = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        b.Freeze();
        return b;
    }

    static uint Px(BitmapSource b, int x, int y)
    {
        var c = new FormatConvertedBitmap(b, PixelFormats.Bgra32, null, 0);
        var one = new uint[1];
        c.CopyPixels(new Int32Rect(x, y, 1, 1), one, 4, 0);
        return one[0];
    }

    static DocumentState With(params Annotation[] a) => DocumentState.Empty with { Annotations = a };

    [Fact]
    public void Arrow_is_drawn_in_its_colour() => Sta.Run(() =>
    {
        var img = DocumentRenderer.Render(Solid(200, 100, 0xFFFFFFFF),
            With(new ArrowAnnotation(new(20, 50), new(180, 50), "#FFFF0000", 6)), applyEffects: false);
        Assert.Equal(0xFFFF0000u, Px(img, 100, 50));
        Assert.Equal(0xFFFFFFFFu, Px(img, 100, 10));
    });

    [Fact]
    public void Pixelate_makes_uniform_blocks_and_leaves_the_rest_alone() => Sta.Run(() =>
    {
        var src = Noise(64, 64);
        var img = DocumentRenderer.Render(src, With(new BlurAnnotation(new(0, 0, 40, 40), BlurMode.Pixelate, 10)), false);
        Assert.Equal(Px(img, 2, 2), Px(img, 17, 17));
        Assert.Equal(Px(src, 50, 50), Px(img, 50, 50));
    });

    [Fact]
    public void Blur_changes_pixels_inside_only() => Sta.Run(() =>
    {
        var src = Noise(64, 64);
        var img = DocumentRenderer.Render(src, With(new BlurAnnotation(new(0, 0, 40, 40), BlurMode.Blur, 5)), false);
        Assert.NotEqual(Px(src, 20, 20), Px(img, 20, 20));
        Assert.Equal(Px(src, 55, 55), Px(img, 55, 55));
    });

    [Fact]
    public void Crop_sets_size_and_origin() => Sta.Run(() =>
    {
        var src = Noise(100, 100);
        var img = DocumentRenderer.Render(src, DocumentState.Empty with { Crop = new Rct(10, 10, 50, 30) }, false);
        Assert.Equal((50, 30), (img.PixelWidth, img.PixelHeight));
        Assert.Equal(Px(src, 10, 10), Px(img, 0, 0));
    });

    [Fact]
    public void Border_effect_grows_the_image_and_paints_the_edge() => Sta.Run(() =>
    {
        var state = DocumentState.Empty with { Effects = new DocumentEffects(new BorderEffect("#FF00FF00", 5)) };
        var img = DocumentRenderer.Render(Solid(20, 10, 0xFFFFFFFF), state, applyEffects: true);
        Assert.Equal((30, 20), (img.PixelWidth, img.PixelHeight));
        Assert.Equal(0xFF00FF00u, Px(img, 0, 0));
        Assert.Equal(0xFFFFFFFFu, Px(img, 15, 10));
    });

    [Fact]
    public void Shadow_effect_adds_margin_and_casts_down_right() => Sta.Run(() =>
    {
        var img = EffectsRenderer.Apply(Solid(40, 40, 0xFFFFFFFF), new DocumentEffects(Shadow: true));
        Assert.Equal((88, 88), (img.PixelWidth, img.PixelHeight));
        Assert.True((Px(img, 24 + 40 + 3, 24 + 40 + 3) >> 24) > 0, "expected shadow below-right of the image");
        Assert.Equal(0u, Px(img, 1, 1) >> 24);
    });

    [Fact]
    public void Torn_edge_makes_the_bottom_transparent() => Sta.Run(() =>
    {
        var img = EffectsRenderer.Apply(Solid(100, 60, 0xFFFFFFFF), new DocumentEffects(TornEdge: true));
        Assert.Equal(0u, Px(img, 50, 59) >> 24);
        Assert.Equal(0xFFFFFFFFu, Px(img, 50, 5));
    });

    [Fact]
    public void Jpg_flattens_transparency_to_white() => Sta.Run(() =>
    {
        var jpg = ImageIo.EncodeJpg(Solid(16, 16, 0x00000000), 90);
        var back = BitmapFrame.Create(new MemoryStream(jpg), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var px = Px(back, 8, 8);
        Assert.True((px & 0xFF) > 0xF0 && ((px >> 8) & 0xFF) > 0xF0 && ((px >> 16) & 0xFF) > 0xF0, $"expected white, got {px:X8}");
    });

    [Fact]
    public void Png_round_trips_pixels() => Sta.Run(() =>
    {
        var src = Noise(30, 20);
        var back = ImageIo.DecodePng(ImageIo.EncodePng(src));
        Assert.Equal(Px(src, 7, 9), Px(back, 7, 9));
    });

    [Fact]
    public void Every_annotation_type_renders_without_throwing() => Sta.Run(() =>
    {
        var img = DocumentRenderer.Render(Noise(400, 300), ModelJsonTests.SampleState() with { Crop = null }, applyEffects: true);
        Assert.True(img.PixelWidth > 400);
    });

    [Fact]
    public void Frame_conversion_round_trips() => Sta.Run(() =>
    {
        var src = Noise(12, 7);
        var frame = ImageIo.ToFrame(src);
        Assert.Equal((12, 7), (frame.Width, frame.Height));
        Assert.Equal(Px(src, 3, 4), Px(ImageIo.FromFrame(frame), 3, 4));
    });

    [Fact]
    public void Arrow_has_a_filled_head_wider_than_the_shaft() => Sta.Run(() =>
    {
        var img = DocumentRenderer.Render(Solid(200, 100, 0xFFFFFFFF),
            With(new ArrowAnnotation(new(20, 50), new(180, 50), "#FFFF0000", 6)), applyEffects: false);
        Assert.Equal(0xFFFF0000u, Px(img, 165, 56));
    });

    [Fact]
    public void Annotation_shadow_darkens_pixels_below_right() => Sta.Run(() =>
    {
        var shape = new ShapeAnnotation(ShapeKind.Rectangle, new(20, 20, 40, 40), "#FFFF0000", 4) { Shadow = true };
        var img = DocumentRenderer.Render(Solid(100, 100, 0xFFFFFFFF), With(shape), applyEffects: false);
        Assert.NotEqual(0xFFFFFFFFu, Px(img, 64, 64));
    });
}
