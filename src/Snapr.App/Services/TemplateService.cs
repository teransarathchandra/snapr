using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapr.App.Rendering;
using Snapr.Core;
using Snapr.Core.Stitching;

namespace Snapr.App.Services;

public sealed record TemplateOutput(BitmapSource Base, IReadOnlyList<Annotation> Objects, Guid TitleId);

/// <summary>Templates (spec parity 14 §3): pictures as Image objects, placeholders as Text objects, Step objects for step by step.</summary>
public static class TemplateService
{
    const string Font = "Segoe UI";

    /// <summary>Relative luminance (simple, unlinearised) under 0.5; transparent counts as light.</summary>
    public static bool IsDark(string background)
    {
        if (!ColorText.TryArgb(background, out var c)) throw new ArgumentException("The background must be #AARRGGBB.", nameof(background));
        if (c >> 24 == 0) return false;
        double r = (c >> 16) & 0xFF, g = (c >> 8) & 0xFF, b = c & 0xFF;
        return (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255 < 0.5;
    }

    /// <summary>Text width in DIPs, drawn the way <c>DocumentRenderer</c> draws a TextAnnotation (pixelsPerDip 1).</summary>
    static double Measure(string s, double size, bool bold) => new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface(new FontFamily(Font), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
        size, Brushes.Black, 1.0).WidthIncludingTrailingWhitespace;

    public static TemplateOutput? Build(TemplateKind kind, IReadOnlyList<BitmapSource> pictures, IReadOnlyList<string?> titles, string background)
    {
        var dark = IsDark(background);
        ColorText.TryArgb(background, out var argb);
        var layout = TemplateLayout.Arrange(kind, pictures.Select(p => (p.PixelWidth, p.PixelHeight)).ToArray());
        if (TemplateLayout.TooLarge(layout)) return null;
        var px = new uint[layout.Width * layout.Height];
        Array.Fill(px, argb);
        var canvas = ImageIo.FromFrame(new Frame(layout.Width, layout.Height, px));
        var colour = dark ? "#FFFFFFFF" : "#FF111111";
        var objects = new List<Annotation>();
        objects.AddRange(pictures.Select((p, i) => new ImageAnnotation(layout.Pictures[i], Convert.ToBase64String(ImageIo.EncodePng(ImageIo.ToBgra(p))))));
        TextAnnotation? title = null;
        foreach (var slot in layout.Texts)
        {
            var isTitle = slot.Role == TextRole.Title;
            var size = isTitle ? TemplateLayout.TitleSize : TemplateLayout.SmallSize;
            var limit = slot.Role switch
            {
                TextRole.Label or TextRole.Caption when kind != TemplateKind.StepByStep => layout.Pictures[slot.Index].W,
                TextRole.Caption => layout.Width - (TemplateLayout.Margin + 48) - TemplateLayout.Margin,
                _ => layout.Width - 2 * TemplateLayout.Margin,
            };
            var text = new TextAnnotation(slot.Position, TemplateTexts.Fit(TemplateTexts.For(kind, slot, titles), limit, s => Measure(s, size, isTitle)), Font,
                size, isTitle, colour);
            title ??= isTitle ? text : null;
            objects.Add(text);
        }
        objects.AddRange(layout.Steps.Select(s => new StepAnnotation(s.Center, s.Number, TemplateLayout.BadgeSize, dark ? "#FFFFFFFF" : "#FF3B82F6", dark ? "#FF111111" : "#FFFFFFFF")));
        return new TemplateOutput(canvas, objects, title!.Id);
    }
}
