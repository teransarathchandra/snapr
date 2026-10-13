using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Snapr.Core;

namespace Snapr.App.Rendering;

/// <summary>Whole-image effects, applied at export and copy (not on the editor canvas).</summary>
public static class EffectsRenderer
{
    const int ShadowMargin = 24;

    public static BitmapSource Apply(BitmapSource image, DocumentEffects fx)
    {
        if (fx.Border is { Width: > 0 } border) image = Border(image, border);
        if (fx.TornEdge) image = TornEdge(image);
        if (fx.Shadow) image = Shadow(image);
        return image;
    }

    static BitmapSource Border(BitmapSource image, BorderEffect b)
    {
        int w = image.PixelWidth + 2 * b.Width, h = image.PixelHeight + 2 * b.Width;
        var v = new DrawingVisual();
        using (var dc = v.RenderOpen())
        {
            dc.DrawRectangle(ImageIo.Brush(b.Color), null, new Rect(0, 0, w, h));
            dc.DrawImage(image, new Rect(b.Width, b.Width, image.PixelWidth, image.PixelHeight));
        }
        return ImageIo.Rasterize(v, w, h);
    }

    static BitmapSource TornEdge(BitmapSource image)
    {
        int w = image.PixelWidth, h = image.PixelHeight;
        const double amplitude = 8, step = 12;
        var rnd = new Random(7); // fixed seed: the same document always exports the same edge
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(0, 0), true, true);
            c.LineTo(new Point(w, 0), true, false);
            for (double x = w; x > 0; x -= step)
                c.LineTo(new Point(x, h - amplitude - rnd.NextDouble() * amplitude), true, false);
            c.LineTo(new Point(0, h - amplitude - rnd.NextDouble() * amplitude), true, false);
        }
        g.Freeze();
        var v = new DrawingVisual();
        using (var dc = v.RenderOpen())
        {
            dc.PushClip(g);
            dc.DrawImage(image, new Rect(0, 0, w, h));
            dc.Pop();
        }
        return ImageIo.Rasterize(v, w, h);
    }

    static BitmapSource Shadow(BitmapSource image)
    {
        int w = image.PixelWidth + 2 * ShadowMargin, h = image.PixelHeight + 2 * ShadowMargin;
        var v = new DrawingVisual
        {
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 6, Opacity = 0.45, Direction = 315, Color = Colors.Black },
        };
        using (var dc = v.RenderOpen())
            dc.DrawImage(image, new Rect(ShadowMargin, ShadowMargin, image.PixelWidth, image.PixelHeight));
        return ImageIo.Rasterize(v, w, h);
    }
}
