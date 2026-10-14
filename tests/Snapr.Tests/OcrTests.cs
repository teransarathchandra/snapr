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

    [Fact]
    public async Task Word_boxes_lie_on_the_drawn_words()
    {
        if (!OcrService.IsAvailable) return;   // machine without a Windows OCR language: manual check
        BitmapSource image = null!;
        Sta.Run(() =>
        {
            var v = new DrawingVisual();
            using (var dc = v.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 800, 160));
                dc.DrawText(new FormattedText("mail jane@example.com now", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 40, Brushes.Black, 1.0), new Point(40, 50));
            }
            image = ImageIo.Rasterize(v, 800, 160);
        });

        var lines = await OcrService.RecognizeLinesAsync(image);

        var word = lines.SelectMany(l => l.Words).Single(w => w.Text.Contains('@'));
        Assert.InRange(word.Bounds.X, 100, 400);
        Assert.InRange(word.Bounds.Y, 40, 110);
        Assert.InRange(word.Bounds.Bottom, 60, 130);
        Assert.True(word.Bounds.Right <= 800);
    }
}
