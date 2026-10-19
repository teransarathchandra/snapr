using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapr.App.Rendering;
using Snapr.App.Services;
using Snapr.Core;

namespace Snapr.Tests;

public class CombineServiceTests
{
    const uint White = 0xFFFFFFFF, Red = 0xFFFF0000, Blue = 0xFF0000FF;

    static BitmapSource Solid(int w, int h, uint argb)
    {
        var b = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat(argb, w * h).ToArray(), w * 4);
        b.Freeze();
        return b;
    }

    static CaptureDocument Doc(BitmapSource image, DocumentState state) => new()
    {
        BasePng = ImageIo.EncodePng(image),
        PixelWidth = image.PixelWidth,
        PixelHeight = image.PixelHeight,
        Meta = new CaptureMeta(DateTimeOffset.Now, CaptureKind.Region, null),
        State = state,
    };

    [Fact]
    public void Flatten_includes_objects() => Sta.Run(() =>
    {
        var state = DocumentState.Empty.Add(new ShapeAnnotation(ShapeKind.Rectangle, new Rct(0, 0, 10, 10), "#FFFF0000", 1, Fill: "#FFFF0000"));
        var flat = CombineService.Flatten(Doc(Solid(20, 20, White), state));
        Assert.Equal(Red, ImageIo.PixelAt(flat, 5, 5));
        Assert.Equal(White, ImageIo.PixelAt(flat, 15, 15));
    });

    [Fact]
    public void Flatten_matches_export_with_crop() => Sta.Run(() =>
    {
        var flat = CombineService.Flatten(Doc(Solid(40, 30, White), DocumentState.Empty with { Crop = new Rct(5, 5, 12, 8) }));
        Assert.Equal((12, 8), (flat.PixelWidth, flat.PixelHeight));
    });

    [Fact]
    public void Flatten_replays_image_edits() => Sta.Run(() =>
    {
        var state = DocumentState.Empty with { Edits = [new ClearAreaEdit(new Rct(0, 0, 4, 4), "#FF0000FF")] };
        Assert.Equal(Blue, ImageIo.PixelAt(CombineService.Flatten(Doc(Solid(20, 20, White), state)), 1, 1));
    });

    [Fact]
    public void Build_places_objects_on_the_background() => Sta.Run(() =>
    {
        var output = CombineService.Build([Solid(10, 20, Red), Solid(30, 10, Blue)], CombineChoice.Default)!;
        Assert.Equal((100, 60), (output.Base.PixelWidth, output.Base.PixelHeight));
        Assert.Equal(White, ImageIo.PixelAt(output.Base, 1, 1));
        Assert.Equal([new Rct(20, 20, 10, 20), new Rct(50, 25, 30, 10)], output.Objects.Select(o => o.Bounds));
        var second = ImageIo.DecodePng(Convert.FromBase64String(output.Objects[1].Png));
        Assert.Equal((30, 10), (second.PixelWidth, second.PixelHeight));
        Assert.Equal(Blue, ImageIo.PixelAt(second, 3, 3));
    });

    [Fact]
    public void Transparent_background_is_alpha_zero_between_pictures() => Sta.Run(() =>
    {
        var output = CombineService.Build([Solid(10, 10, 0x80FF0000), Solid(10, 10, Red)], CombineChoice.Default with { Background = "#00000000" })!;
        Assert.Equal(0u, ImageIo.PixelAt(output.Base, 1, 1) >> 24);
        Assert.Equal(0x80u, ImageIo.PixelAt(ImageIo.DecodePng(Convert.FromBase64String(output.Objects[0].Png)), 2, 2) >> 24);
    });

    [Fact]
    public void Build_returns_null_when_too_large() => Sta.Run(() =>
        Assert.Null(CombineService.Build([Solid(20000, 1, Red), Solid(20000, 1, Red)], CombineChoice.Default with { Gap = 0 })));

    [Fact]
    public void Build_rejects_a_bad_background() => Sta.Run(() =>
        Assert.Throws<ArgumentException>(() => CombineService.Build([Solid(2, 2, Red), Solid(2, 2, Red)], CombineChoice.Default with { Background = "white" })));
}
