using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapr.App.Services;
using Snapr.Core;

namespace Snapr.Tests;

public class SimplifyServiceTests
{
    static BitmapSource Picture()
    {
        const int w = 200, h = 150;
        var px = Enumerable.Repeat(0xFFFFFFFFu, w * h).ToArray();
        void Fill(int x, int y, int fw, int fh, uint c) { for (var yy = y; yy < y + fh; yy++) for (var xx = x; xx < x + fw; xx++) px[yy * w + xx] = c; }
        Fill(20, 20, 120, 80, 0xFF3366AA);
        Fill(30, 30, 30, 20, 0xFFEEEEEE);
        var b = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        b.Freeze();
        return b;
    }

    static readonly TextLine Line = new([new TextWord("Hi", new Rct(150, 110, 30, 10))]);

    [Fact]
    public void Notices()
    {
        var both = SimplifyOptions.Default;
        const string NoOcr = "Windows has no text recognition language, so text was left as is.";
        Assert.Null(SimplifyService.Notice(both, noOcr: false, found: true));
        Assert.Equal("Snapr found nothing to simplify here.", SimplifyService.Notice(both, false, false));
        Assert.Equal(NoOcr, SimplifyService.Notice(both, true, true));
        Assert.Equal("Windows has no text recognition language, so text was left as is, and Snapr found no blocks to simplify.", SimplifyService.Notice(both, true, false));
        Assert.Equal(NoOcr, SimplifyService.Notice(both with { Blocks = false }, true, false));
        Assert.Equal("Snapr found nothing to simplify here.", SimplifyService.Notice(both with { Text = false }, true, false));
        Assert.Null(SimplifyService.Notice(both with { Text = false }, true, true));
    }

    [Fact]
    public void Shapes_go_under_existing_objects() => Sta.Run(() =>
    {
        var existing = SimplifyService.Build(Picture(), new Rct(0, 0, 200, 150), [Line], SimplifyOptions.Default);
        var shapes = SimplifyService.Build(Picture(), new Rct(0, 0, 200, 150), [Line], SimplifyOptions.Default);
        var all = SimplifyService.PlaceUnder(existing, shapes);
        Assert.Equal(shapes.Concat(existing).Select(a => a.Id), all.Select(a => a.Id));
    });

    [Fact]
    public void Order_group_and_colours() => Sta.Run(() =>
    {
        var shapes = SimplifyService.Build(Picture(), new Rct(0, 0, 200, 150), [Line], SimplifyOptions.Default).Cast<ShapeAnnotation>().ToArray();
        Assert.Equal(4, shapes.Length);
        Assert.Equal((new Rct(20, 20, 120, 80), "#FF3366AA", 4.0), (shapes[0].Bounds, shapes[0].Fill, shapes[0].CornerRadius));
        Assert.Equal((new Rct(30, 30, 30, 20), "#FFEEEEEE"), (shapes[1].Bounds, shapes[1].Fill));
        Assert.Equal((new Rct(149, 109, 32, 12), "#FFFFFFFF", "#FFFFFFFF", 0.0), (shapes[2].Bounds, shapes[2].Fill, shapes[2].Stroke, shapes[2].CornerRadius));   // line cover: box + 1 px, colour under the bar
        Assert.Equal(new Rct(150, 112, 30, 6), shapes[3].Bounds);   // h = round(5.5) = 6, centred
        Assert.Equal(3.0, shapes[3].CornerRadius);
        Assert.Equal("#FF9CA3AF", shapes[3].Fill);                  // on the white page
        Assert.All(shapes, s => Assert.Equal(0.0, s.StrokeWidth));
        Assert.NotNull(shapes[0].GroupId);
        Assert.All(shapes, s => Assert.Equal(shapes[0].GroupId, s.GroupId));
    });

    [Fact]
    public void Plain_background_comes_first() => Sta.Run(() =>
    {
        var shapes = SimplifyService.Build(Picture(), new Rct(0, 0, 200, 150), [], SimplifyOptions.Default with { PlainBackground = true }).Cast<ShapeAnnotation>().ToArray();
        Assert.Equal((new Rct(0, 0, 200, 150), "#FFFFFFFF", 0.0), (shapes[0].Bounds, shapes[0].Fill, shapes[0].CornerRadius));
    });

    [Fact]
    public void Offsets_are_added() => Sta.Run(() =>
    {
        var shapes = SimplifyService.Build(Picture(), new Rct(500, 300, 200, 150), [Line], SimplifyOptions.Default).Cast<ShapeAnnotation>().ToArray();
        Assert.Equal(new Rct(520, 320, 120, 80), shapes[0].Bounds);
        Assert.Equal(new Rct(650, 412, 30, 6), shapes[^1].Bounds);
    });

    [Fact]
    public void Text_only_and_blocks_only() => Sta.Run(() =>
    {
        Assert.Equal(2, SimplifyService.Build(Picture(), new Rct(0, 0, 200, 150), [Line], SimplifyOptions.Default with { Blocks = false }).Count);   // cover + bar
        Assert.Equal(2, SimplifyService.Build(Picture(), new Rct(0, 0, 200, 150), [Line], SimplifyOptions.Default with { Text = false }).Count);
    });

    [Fact]
    public void Plain_background_has_no_line_covers() => Sta.Run(() =>
    {
        var shapes = SimplifyService.Build(Picture(), new Rct(0, 0, 200, 150), [Line], SimplifyOptions.Default with { PlainBackground = true }).Cast<ShapeAnnotation>().ToArray();
        Assert.Equal(4, shapes.Length);   // background, panel, button, bar
        Assert.Equal(new Rct(0, 0, 200, 150), shapes[0].Bounds);
        Assert.Equal(new Rct(150, 112, 30, 6), shapes[3].Bounds);
    });

    [Fact]
    public void Bar_colour_follows_the_block_under_it() => Sta.Run(() =>
    {
        var onPanel = new TextLine([new TextWord("x", new Rct(70, 60, 40, 12))]);
        var bar = (ShapeAnnotation)SimplifyService.Build(Picture(), new Rct(0, 0, 200, 150), [onPanel], SimplifyOptions.Default)[^1];
        Assert.Equal(SimplifyShapes.BarColor("auto", 0xFF3366AA), bar.Fill);
    });

    [Fact]
    public void Nothing_found_gives_an_empty_list() => Sta.Run(() =>
    {
        var white = BitmapSource.Create(50, 50, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat(0xFFFFFFFFu, 2500).ToArray(), 200);
        Assert.Empty(SimplifyService.Build(white, new Rct(0, 0, 50, 50), [], SimplifyOptions.Default));
    });

    [Fact]
    public void Bar_colour_comes_from_the_block_drawn_on_top() => Sta.Run(() =>
    {
        const int w = 200, h = 150;
        var px = Enumerable.Repeat(0xFFFFFFFFu, w * h).ToArray();
        void Fill(int x, int y, int fw, int fh, uint c) { for (var yy = y; yy < y + fh; yy++) for (var xx = x; xx < x + fw; xx++) px[yy * w + xx] = c; }
        Fill(20, 20, 80, 60, 0xFF222222);   // dark frame, 10 px thick
        Fill(30, 30, 60, 40, 0xFFFFFFFF);   // white hole
        Fill(50, 40, 20, 20, 0xFFEE0000);   // red block inside the hole
        var picture = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        picture.Freeze();
        // Centre (60, 50) is on the red block, inside the hole and inside the frame.
        var line = new TextLine([new TextWord("x", new Rct(45, 47, 30, 6))]);
        var shapes = SimplifyService.Build(picture, new Rct(0, 0, w, h), [line], SimplifyOptions.Default);
        var bar = (ShapeAnnotation)shapes[^1];
        Assert.Equal(SimplifyShapes.BarColor("auto", 0xFFEE0000), bar.Fill);
    });

    [Fact]
    public void Plain_background_is_opaque_even_with_transparent_edges() => Sta.Run(() =>
    {
        const int w = 200, h = 150;
        var px = Enumerable.Repeat(0x00FFFFFFu, w * h).ToArray();
        for (var yy = 20; yy < 100; yy++) for (var xx = 20; xx < 140; xx++) px[yy * w + xx] = 0xFF3366AA;
        var picture = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        picture.Freeze();
        var shapes = SimplifyService.Build(picture, new Rct(0, 0, w, h), [], SimplifyOptions.Default with { PlainBackground = true }).Cast<ShapeAnnotation>().ToArray();
        Assert.Equal(("#FFFFFFFF", "#FFFFFFFF"), (shapes[0].Stroke, shapes[0].Fill));
    });
}
