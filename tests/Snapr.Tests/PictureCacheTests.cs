using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapr.App.Rendering;
using Snapr.Core;

namespace Snapr.Tests;

[CollectionDefinition("Picture cache", DisableParallelization = true)]
public class PictureCacheCollection { }

// DocumentRenderer.Pictures and PictureDecodes are static, so these must not run alongside other rendering tests.
[Collection("Picture cache")]
public class PictureCacheTests
{
    static BitmapSource Solid(int w, int h, uint argb)
    {
        var b = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat(argb, w * h).ToArray(), w * 4);
        b.Freeze();
        return b;
    }

    static DocumentState With(params Annotation[] a) => DocumentState.Empty with { Annotations = a };

    [Fact]
    public void Many_image_objects_are_decoded_once_across_renders() => Sta.Run(() =>
    {
        var png = Convert.ToBase64String(ImageIo.EncodePng(Solid(2, 2, 0xFFFF0000)));
        var state = With(Enumerable.Range(0, 70).Select(i => (Annotation)new ImageAnnotation(new(i, 0, 1, 1), png)).ToArray());
        DocumentRenderer.Render(Solid(80, 10, 0xFFFFFFFF), state, false);
        var before = DocumentRenderer.PictureDecodes;
        DocumentRenderer.Render(Solid(80, 10, 0xFFFFFFFF), state, false);
        Assert.Equal(before, DocumentRenderer.PictureDecodes);
    });

    [Fact]
    public void Stamp_drag_previews_sharing_one_Id_decode_the_picture_once() => Sta.Run(() =>
    {
        var png = Convert.ToBase64String(ImageIo.EncodePng(Solid(3, 5, 0xFF00FF00)));
        var choice = new Snapr.App.Editor.StampChoice(null, png, 3, 5);
        var id = Guid.NewGuid();
        var style = ToolStyle.Default;
        DocumentRenderer.Render(Solid(80, 80, 0xFFFFFFFF), With(choice.Create(new(0, 0, 10, 10), style, id)), false);
        var before = DocumentRenderer.PictureDecodes;
        for (var i = 1; i <= 5; i++)
            DocumentRenderer.Render(Solid(80, 80, 0xFFFFFFFF), With(choice.Create(new(0, 0, 10 + i, 10 + i), style, id)), false);
        Assert.Equal(before, DocumentRenderer.PictureDecodes);
    });
}
