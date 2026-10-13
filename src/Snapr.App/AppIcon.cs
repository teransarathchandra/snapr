using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapr.App.Rendering;

namespace Snapr.App;

/// <summary>Snapr's original aperture glyph, drawn at runtime so no icon file is needed.</summary>
public static class AppIcon
{
    public static BitmapSource Render(int size)
    {
        var v = new DrawingVisual();
        using (var dc = v.RenderOpen())
        {
            var c = new Point(size / 2.0, size / 2.0);
            dc.DrawEllipse(ImageIo.Brush("#FF2563EB"), null, c, size * 0.48, size * 0.48);
            var pen = new Pen(Brushes.White, Math.Max(1, size / 14.0)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            for (var i = 0; i < 6; i++)
            {
                var a = i * Math.PI / 3;
                var p1 = c + new Vector(Math.Cos(a), Math.Sin(a)) * size * 0.3;
                var p2 = c + new Vector(Math.Cos(a + Math.PI / 2.2), Math.Sin(a + Math.PI / 2.2)) * size * 0.3;
                dc.DrawLine(pen, p1, p2);
            }
        }
        return ImageIo.Rasterize(v, size, size);
    }

    public static System.Drawing.Icon TrayIcon()
    {
        using var ms = new MemoryStream(ImageIo.EncodePng(Render(32)));
        using var bitmap = new System.Drawing.Bitmap(ms);
        return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
    }
}
