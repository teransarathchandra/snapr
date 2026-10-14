using System.IO;
using System.Windows.Media.Imaging;
using Snapr.App.Rendering;
using Snapr.Core;
using Snapr.Core.Interop;

namespace Snapr.App.Services;

/// <summary>Turns pictures (clipboard or dropped files) into Image objects on a capture (spec parity 3 §5).</summary>
public static class ImageObjects
{
    const long MaxFileBytes = 200L * 1024 * 1024;

    public static IReadOnlyList<string> Extensions { get; } = [".png", ".jpg", ".jpeg", ".bmp", ".gif"];

    public static bool IsImageFile(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>An Image object centred on <paramref name="center"/>, sized by ImagePlacement.Fit, stored as PNG.</summary>
    public static ImageAnnotation Create(BitmapSource image, Pt center, Rct view)
    {
        if ((long)image.PixelWidth * image.PixelHeight > SnagxReader.MaxTotalPixels) throw new InvalidDataException("The image is too large.");
        var (w, h) = ImagePlacement.Fit(image.PixelWidth, image.PixelHeight, view);
        var png = ImageIo.EncodePng(ImageIo.ToBgra(image));
        return new ImageAnnotation(new Rct(center.X - w / 2, center.Y - h / 2, w, h), Convert.ToBase64String(png));
    }

    /// <summary>Reads an image file. Checks the file size and the pixel count before the pixels are decoded.</summary>
    public static BitmapSource Load(string path)
    {
        if (new FileInfo(path).Length > MaxFileBytes) throw new InvalidDataException("The file is larger than 200 MB.");
        var bytes = File.ReadAllBytes(path);
        if (PngInfo.Size(bytes) is { } size && (long)size.Width * size.Height > SnagxReader.MaxTotalPixels)
            throw new InvalidDataException("The image is too large.");
        // Default cache: the size comes from the header; pixels are decoded later (ToBgra), after the check below.
        var frame = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default).Frames[0];
        if ((long)frame.PixelWidth * frame.PixelHeight > SnagxReader.MaxTotalPixels) throw new InvalidDataException("The image is too large.");
        return frame;
    }
}
