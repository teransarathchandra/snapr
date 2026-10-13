using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
}
