using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapr.App.Rendering;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Snapr.App.Services;

public sealed class OcrUnavailableException() : Exception("No Windows OCR language is installed.");

/// <summary>Offline OCR using the engine built into Windows.</summary>
public static class OcrService
{
    public static bool IsAvailable => OcrEngine.AvailableRecognizerLanguages.Count > 0;

    public static async Task<string> RecognizeAsync(BitmapSource image)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                     ?? (OcrEngine.IsLanguageSupported(new Language("en-US")) ? OcrEngine.TryCreateFromLanguage(new Language("en-US")) : null)
                     ?? throw new OcrUnavailableException();
        using var bitmap = await ToSoftwareBitmap(Prepare(image));
        var result = await engine.RecognizeAsync(bitmap);
        return string.Join(Environment.NewLine, result.Lines.Select(l => l.Text));
    }

    /// <summary>Small captures are upscaled 2× (much better accuracy); huge ones are shrunk to the engine's limit.</summary>
    static BitmapSource Prepare(BitmapSource image)
    {
        double longest = Math.Max(image.PixelWidth, image.PixelHeight);
        double max = OcrEngine.MaxImageDimension;
        var scale = longest > max ? max / longest : longest * 2 <= Math.Min(max, 1600) ? 2.0 : 1.0;
        if (scale == 1.0) return image;
        var scaled = new TransformedBitmap(image, new ScaleTransform(scale, scale));
        scaled.Freeze();
        return scaled;
    }

    static async Task<SoftwareBitmap> ToSoftwareBitmap(BitmapSource image)
    {
        var png = ImageIo.EncodePng(image);
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(png);
            await writer.StoreAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        var decoder = await global::Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }
}
