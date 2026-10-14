using System.Buffers.Binary;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapr.App.Rendering;
using Snapr.App.Services;
using Snapr.Core;
using Snapr.Core.Interop;

namespace Snapr.Tests;

public class ImageObjectsTests
{
    static BitmapSource Solid(int w, int h)
    {
        var px = Enumerable.Repeat(0xFF3B82F6u, w * h).ToArray();
        var b = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        b.Freeze();
        return b;
    }

    [Fact]
    public void A_small_image_is_centred_at_natural_size() => Sta.Run(() =>
    {
        var a = ImageObjects.Create(Solid(10, 10), new Pt(50, 50), new Rct(0, 0, 100, 100));
        Assert.Equal(new Rct(45, 45, 10, 10), a.Bounds);
        Assert.Equal((10, 10), PngInfo.Size(Convert.FromBase64String(a.Png)));
    });

    [Fact]
    public void A_large_image_is_shrunk_to_fit_the_view() => Sta.Run(() =>
    {
        var a = ImageObjects.Create(Solid(100, 50), new Pt(5, 5), new Rct(0, 0, 10, 10));
        Assert.Equal(new Rct(1, 3, 8, 4), a.Bounds);   // scale 0.08
    });

    [Theory]
    [InlineData("a.PNG", true)]
    [InlineData("b.jpeg", true)]
    [InlineData("c.gif", true)]
    [InlineData("d.snapr", false)]
    [InlineData("e.snagx", false)]
    [InlineData("f.txt", false)]
    public void Recognises_image_files(string path, bool expected) => Assert.Equal(expected, ImageObjects.IsImageFile(path));

    [Fact]
    public void Load_reads_a_png_file() => Sta.Run(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), $"snapr-test-{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(path, ImageIo.EncodePng(Solid(7, 3)));
            var b = ImageObjects.Load(path);
            Assert.Equal((7, 3), (b.PixelWidth, b.PixelHeight));
        }
        finally { File.Delete(path); }
    });

    [Fact]
    public void Load_refuses_a_png_declaring_more_than_100_megapixels() => Sta.Run(() =>
    {
        var png = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(png, 0);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(8), 13);
        "IHDR"u8.CopyTo(png.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), 20_000);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20), 20_000);
        var path = Path.Combine(Path.GetTempPath(), $"snapr-test-{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(path, png);
            Assert.Throws<InvalidDataException>(() => ImageObjects.Load(path));
        }
        finally { File.Delete(path); }
    });
}
