using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapr.App.Rendering;

namespace Snapr.Tests;

public class ExportTests
{
    static BitmapSource Transparent(int w, int h)
    {
        var b = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, new uint[w * h], w * 4);
        b.Freeze();
        return b;
    }

    [Theory]
    [InlineData("gif")]
    [InlineData("bmp")]
    public void Gif_and_bmp_are_flattened_on_white(string format) => Sta.Run(() =>
    {
        var bytes = format == "gif" ? ImageIo.EncodeGif(Transparent(6, 4)) : ImageIo.EncodeBmp(Transparent(6, 4));
        var back = BitmapFrame.Create(new MemoryStream(bytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Assert.Equal((6, 4), (back.PixelWidth, back.PixelHeight));
        var px = new uint[1];
        new FormatConvertedBitmap(back, PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(3, 2, 1, 1), px, 4, 0);
        Assert.Equal(0xFFFFFFFFu, px[0]);
    });

    [Fact]
    public void Pdf_contains_the_flattened_pixels() => Sta.Run(() =>
    {
        var pixels = PdfAndPrintTests.ImageStream(ImageIo.EncodePdf(Transparent(4, 2)));
        Assert.Equal(4 * 2 * 3, pixels.Length);
        Assert.All(pixels, b => Assert.Equal(255, b));
    });
}
