using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapr.App.Rendering;
using Snapr.Core;
using Snapr.Core.Stitching;

namespace Snapr.App.Services;

/// <summary>Replace text (spec parity 22 §3): builds the cover and a text object matching an OCR line's size, weight, colour and position.</summary>
public static class ReplaceTextService
{
    const string Family = "Segoe UI";

    public static (ShapeAnnotation Cover, TextAnnotation Text) Build(Frame frame, TextLine line)
    {
        var box = ReplaceText.LineBox(line);
        var bg = ReplaceText.Background(frame, box);
        var ink = ReplaceText.Ink(frame, box, bg);
        var inkBox = ReplaceText.InkBox(frame, box, bg, ink);
        var text = ReplaceText.LineText(line);

        var refHeight = string.IsNullOrWhiteSpace(text) ? 0 : Measure(text, 100, false).Height;
        var size = refHeight > 0 ? Math.Max(4, Math.Round(2 * 100 * inkBox.H / refHeight) / 2) : 12;
        var bold = false;
        var bounds = Measure(text, size, false);
        if (!string.IsNullOrWhiteSpace(text) && refHeight > 0)
        {
            var target = ReplaceText.InkCount(frame, inkBox, bg, ink);
            var regular = InkPixels(text, size, false);
            var heavy = InkPixels(text, size, true);
            bold = Math.Abs(heavy - target) < Math.Abs(regular - target);
            if (bold) bounds = Measure(text, size, true);
        }
        var position = new Pt(inkBox.X - bounds.X, inkBox.Y - bounds.Y);
        return ReplaceText.Objects(ReplaceText.CoverRect(box, frame.Width, frame.Height), bg,
            new TextAnnotation(position, text, Family, size, bold, ReplaceText.Hex(ink)));
    }

    static FormattedText Format(string text, double size, bool bold) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(Family), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
            size, Brushes.Black, 1.0);

    /// <summary>Ink extent of the text drawn at the origin.</summary>
    static Rect Measure(string text, double size, bool bold) =>
        Format(text, size, bold).BuildGeometry(new Point(0, 0))?.Bounds is { IsEmpty: false } b ? b : new Rect();

    static int InkPixels(string text, double size, bool bold)
    {
        var ft = Format(text, size, bold);
        var w = Math.Max(1, (int)Math.Ceiling(ft.WidthIncludingTrailingWhitespace) + 4);
        var h = Math.Max(1, (int)Math.Ceiling(ft.Height) + 4);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawText(ft, new Point(2, 2));
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var px = new byte[w * h * 4];
        bmp.CopyPixels(px, w * 4, 0);
        var n = 0;
        for (var i = 3; i < px.Length; i += 4) if (px[i] > 127) n++;
        return n;
    }
}
