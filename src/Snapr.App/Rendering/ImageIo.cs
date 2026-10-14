using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapr.Core.Output;
using Snapr.Core.Stitching;

namespace Snapr.App.Rendering;

public static class ImageIo
{
    public static BitmapSource DecodePng(byte[] png)
    {
        using var ms = new MemoryStream(png);
        var frame = BitmapFrame.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }

    public static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    public static byte[] EncodeJpg(BitmapSource image, int quality)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) };
        encoder.Frames.Add(BitmapFrame.Create(FlattenOnWhite(image)));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    public static byte[] EncodeGif(BitmapSource image) => Encode(new GifBitmapEncoder(), FlattenOnWhite(image));

    public static byte[] EncodeBmp(BitmapSource image) => Encode(new BmpBitmapEncoder(), FlattenOnWhite(image));

    /// <summary>A one-page PDF of the image (transparent areas white), written by Snapr's own PDF writer.</summary>
    public static byte[] EncodePdf(BitmapSource image)
    {
        var frame = ToFrame(FlattenOnWhite(image));
        var rgb = new byte[frame.Width * frame.Height * 3];
        for (int i = 0, j = 0; i < frame.Pixels.Length; i++)
        {
            var p = frame.Pixels[i];
            rgb[j++] = (byte)(p >> 16);
            rgb[j++] = (byte)(p >> 8);
            rgb[j++] = (byte)p;
        }
        return PdfWriter.SingleImagePage(frame.Width, frame.Height, rgb);
    }

    static byte[] Encode(BitmapEncoder encoder, BitmapSource image)
    {
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>JPG has no alpha; transparent areas (torn edge, shadow) become white instead of black.</summary>
    public static BitmapSource FlattenOnWhite(BitmapSource image)
    {
        var rect = new Rect(0, 0, image.PixelWidth, image.PixelHeight);
        var v = new DrawingVisual();
        using (var dc = v.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, rect);
            dc.DrawImage(image, rect);
        }
        return Rasterize(v, image.PixelWidth, image.PixelHeight);
    }

    public static BitmapSource Rasterize(Visual visual, int width, int height)
    {
        var rtb = new RenderTargetBitmap(Math.Max(1, width), Math.Max(1, height), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    public static BitmapSource Thumbnail(BitmapSource image, int max = 256)
    {
        var s = Math.Min(1.0, (double)max / Math.Max(image.PixelWidth, image.PixelHeight));
        var t = new TransformedBitmap(image, new ScaleTransform(s, s));
        t.Freeze();
        return t;
    }

    public static SolidColorBrush Brush(string color)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        b.Freeze();
        return b;
    }

    /// <summary>Copies into a frozen Bgra32 bitmap at 96 DPI so pixel coordinates equal drawing units.</summary>
    public static BitmapSource ToBgra(BitmapSource image) => FromFrame(ToFrame(image));

    public static Frame ToFrame(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var px = new uint[image.PixelWidth * image.PixelHeight];
        converted.CopyPixels(px, image.PixelWidth * 4, 0);
        return new Frame(image.PixelWidth, image.PixelHeight, px);
    }

    public static BitmapSource FromFrame(Frame frame)
    {
        var b = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, frame.Pixels, frame.Width * 4);
        b.Freeze();
        return b;
    }

    /// <summary>One pixel as 0xAARRGGBB.</summary>
    public static uint PixelAt(BitmapSource image, int x, int y)
    {
        var px = new uint[1];
        new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(x, y, 1, 1), px, 4, 0);
        return px[0];
    }
}
