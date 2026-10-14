using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapr.App.Rendering;
using Snapr.App.Services;
using Snapr.Core;
using Snapr.Core.Interop;

namespace Snapr.Tests;

public class SnagxImportTests
{
    static BitmapSource Solid(int w, int h, uint argb)
    {
        var b = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat(argb, w * h).ToArray(), w * 4);
        b.Freeze();
        return b;
    }

    static uint Px(BitmapSource b, int x, int y)
    {
        var one = new uint[1];
        new FormatConvertedBitmap(b, PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(x, y, 1, 1), one, 4, 0);
        return one[0];
    }

    static SnagxPage Page(Rct baseRect) => new(40, 30, "#FF0000FF", ImageIo.EncodePng(Solid(10, 10, 0xFFFF0000)), baseRect,
        [], null, new ImportReport(new Dictionary<string, int>()));

    [Fact]
    public void Base_image_is_canvas_sized_with_the_picture_at_its_place() => Sta.Run(() =>
    {
        var image = SnagxImport.ComposeBase(Page(new Rct(5, 5, 20, 20)));
        Assert.Equal((40, 30), (image.PixelWidth, image.PixelHeight));
        Assert.Equal(0xFFFF0000u, Px(image, 15, 15));
        Assert.Equal(0xFF0000FFu, Px(image, 2, 2));
    });

    [Fact]
    public void Missing_background_size_uses_the_picture_size() => Sta.Run(() =>
    {
        var image = SnagxImport.ComposeBase(Page(new Rct(0, 0, 0, 0)));
        Assert.Equal(0xFFFF0000u, Px(image, 9, 9));
        Assert.Equal(0xFF0000FFu, Px(image, 11, 11));
    });
}
