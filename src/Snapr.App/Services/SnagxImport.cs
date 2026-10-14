using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapr.App.Rendering;
using Snapr.Core.Interop;

namespace Snapr.App.Services;

public static class SnagxImport
{
    /// <summary>The canvas-sized base image: the background colour, then the page picture at its place and size.</summary>
    public static BitmapSource ComposeBase(SnagxPage page)
    {
        var picture = ImageIo.DecodePng(page.BasePng);
        var r = page.BaseRect;
        double w = r.W > 0 ? r.W : picture.PixelWidth, h = r.H > 0 ? r.H : picture.PixelHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(ImageIo.Brush(page.BackgroundColor), null, new Rect(0, 0, page.Width, page.Height));
            dc.DrawImage(picture, new Rect(r.X, r.Y, w, h));
        }
        return ImageIo.Rasterize(visual, page.Width, page.Height);
    }
}
