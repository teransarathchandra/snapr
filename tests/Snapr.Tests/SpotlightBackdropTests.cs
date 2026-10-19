using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapr.App.Rendering;

namespace Snapr.Tests;

public class SpotlightBackdropTests
{
    static BitmapSource Solid(int w, int h)
    {
        var b = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat(0xFF336699u, w * h).ToArray(), w * 4);
        b.Freeze();
        return b;
    }

    [Fact]
    public void Same_base_reuses_the_backdrop() => Sta.Run(() =>
    {
        var src = Solid(260, 130);
        var a = DocumentRenderer.SpotlightBackdrop(src);
        Assert.Same(a, DocumentRenderer.SpotlightBackdrop(src));
        Assert.Equal((20, 10), (a.PixelWidth, a.PixelHeight));   // shrunk by 13
        Assert.True(a.IsFrozen);
    });

    [Fact]
    public void New_base_gets_new_backdrop() => Sta.Run(() =>
        Assert.NotSame(DocumentRenderer.SpotlightBackdrop(Solid(260, 130)), DocumentRenderer.SpotlightBackdrop(Solid(260, 130))));

    [Fact]
    public void Tiny_base_gives_at_least_one_pixel() => Sta.Run(() =>
    {
        var a = DocumentRenderer.SpotlightBackdrop(Solid(5, 3));
        Assert.Equal((1, 1), (a.PixelWidth, a.PixelHeight));
    });
}
