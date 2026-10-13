using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapr.App.Rendering;
using Snapr.App.Services;

namespace Snapr.Tests;

public class OcrTests
{
    [Fact]
    public async Task Recognizes_rendered_text()
    {
        if (!OcrService.IsAvailable) return;   // machine without a Windows OCR language: covered by the manual check in Task 14
        BitmapSource image = null!;
        Sta.Run(() =>
        {
            var v = new DrawingVisual();
            using (var dc = v.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 640, 120));
                dc.DrawText(new FormattedText("Snapr reads this text", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 40, Brushes.Black, 1.0), new Point(20, 30));
            }
            image = ImageIo.Rasterize(v, 640, 120);
        });

        var text = await OcrService.RecognizeAsync(image);

        Assert.Contains("Snapr reads this text", text);
    }
}
