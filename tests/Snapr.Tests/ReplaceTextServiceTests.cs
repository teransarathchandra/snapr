using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapr.App.Rendering;
using Snapr.App.Services;
using Snapr.Core;
using Snapr.Core.Stitching;

namespace Snapr.Tests;

public class ReplaceTextServiceTests
{
    static (Frame Frame, TextLine Line) Render(string text, double size, bool bold, Point at)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
            size, Brushes.Black, 1.0);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 400, 100));
            dc.DrawText(ft, at);
        }
        var bmp = new RenderTargetBitmap(400, 100, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var b = ft.BuildGeometry(at).Bounds;
        var line = new TextLine([new TextWord(text, new Rct(b.X - 1, b.Y - 1, b.Width + 2, b.Height + 2))]);
        return (ImageIo.ToFrame(bmp), line);
    }

    static TextAnnotation Check(string text, double size, bool bold, Point at, bool full)
    {
        TextAnnotation? t = null;
        Sta.Run(() =>
        {
            var (f, line) = Render(text, size, bold, at);
            var (cover, tx) = ReplaceTextService.Build(f, line);
            t = tx;
            Assert.InRange(tx.FontSize, size - 1, size + 1);
            Assert.Equal(bold, tx.Bold);
            if (!full) return;
            Assert.Equal("#FF000000", tx.Color);
            Assert.Equal("Segoe UI", tx.FontFamily);
            Assert.Equal(text, tx.Text);
            Assert.InRange(tx.Position.X, at.X - 1.5, at.X + 1.5);
            Assert.InRange(tx.Position.Y, at.Y - 1.5, at.Y + 1.5);
            Assert.Equal("#FFFFFFFF", cover.Fill);
            Assert.Equal(cover.GroupId, tx.GroupId);
        });
        return t!;
    }

    [Fact] public void Regular_text_matches() => Check("Hello World", 24, false, new Point(20, 10), true);
    [Fact] public void Bold_text_matches() => Check("Hello World", 24, true, new Point(20, 10), false);
    [Fact] public void Small_text_size() => Check("Settings", 11, false, new Point(5, 5), false);
}
