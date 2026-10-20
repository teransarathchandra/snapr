using System.Windows.Media.Imaging;
using Snapr.App.Rendering;
using Snapr.App.Video;
using Snapr.Core;
using Snapr.Core.Video;

namespace Snapr.Tests;

public sealed class VideoOverlaysTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("snapr-overlays").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    static ShapeAnnotation Red(double x) => new(ShapeKind.Rectangle, new Rct(x, 10, 20, 20), "#FFFF0000", 1, Fill: "#FFFF0000");

    [Fact]
    public void Render_is_transparent_except_the_objects() => Sta.Run(() =>
    {
        var img = VideoOverlays.Render([Red(10)], 64, 48);
        Assert.Equal((64, 48), (img.PixelWidth, img.PixelHeight));
        var px = ImageIo.PixelAt(img, 20, 20);
        Assert.Equal(0xFFu, px >> 24);
        Assert.InRange((px >> 16) & 0xFF, 253u, 255u);
        Assert.InRange((px >> 8) & 0xFF, 0u, 2u);
        Assert.InRange(px & 0xFF, 0u, 2u);
        Assert.Equal(0u, ImageIo.PixelAt(img, 2, 2) >> 24);
    });

    [Fact]
    public void Write_makes_one_png_per_time_range() => Sta.Run(() =>
    {
        var p = VideoProject.For(10).SetObjects([new(Red(10), 1, 2), new(Red(20), 3, 4), new(Red(30), 1, 2)]);
        var layers = VideoOverlays.Write(p, 64, 48, _dir);
        Assert.Equal([(1.0, 2.0), (3.0, 4.0)], layers.Select(l => (l.Start, l.End)));
        foreach (var l in layers)
        {
            Assert.True(File.Exists(l.Png));
            var img = ImageIo.DecodePng(File.ReadAllBytes(l.Png));
            Assert.Equal((64, 48), (img.PixelWidth, img.PixelHeight));
        }
    });
}
