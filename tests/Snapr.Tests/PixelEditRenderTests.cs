using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapr.App.Rendering;
using Snapr.Core;

namespace Snapr.Tests;

public class PixelEditRenderTests
{
    const uint White = 0xFFFFFFFF, Red = 0xFFFF0000, Blue = 0xFF0000FF, Green = 0xFF00FF00;

    // 40 × 20 white image with a red 10 × 10 block at (5, 5).
    static BitmapSource Block()
    {
        var px = Enumerable.Repeat(White, 40 * 20).ToArray();
        for (var y = 5; y < 15; y++)
            for (var x = 5; x < 15; x++) px[y * 40 + x] = Red;
        var b = BitmapSource.Create(40, 20, 96, 96, PixelFormats.Bgra32, null, px, 160);
        b.Freeze();
        return b;
    }

    [Fact]
    public void Move_edit_is_replayed() => Sta.Run(() =>
    {
        var r = ImageEditRenderer.Apply(Block(), new MovePixelsEdit(new Rct(5, 5, 10, 10), 20, 0, "#FF0000FF"));
        Assert.Equal((40, 20), (r.PixelWidth, r.PixelHeight));
        Assert.Equal(Red, ImageIo.PixelAt(r, 27, 8));
        Assert.Equal(Blue, ImageIo.PixelAt(r, 8, 8));
    });

    [Fact]
    public void Clear_edit_with_auto_fill_uses_the_surroundings() => Sta.Run(() =>
        Assert.Equal(White, ImageIo.PixelAt(ImageEditRenderer.Apply(Block(), new ClearAreaEdit(new Rct(3, 3, 14, 14), null)), 10, 10)));

    [Fact]
    public void Transparent_fill_gives_alpha_zero() => Sta.Run(() =>
        Assert.Equal(0u, ImageIo.PixelAt(ImageEditRenderer.Apply(Block(), new ClearAreaEdit(new Rct(5, 5, 10, 10), "#00000000")), 10, 10) >> 24));

    [Fact]
    public void Edited_image_cache_replays_pixel_edits() => Sta.Run(() =>
        Assert.Equal(Green, ImageIo.PixelAt(new EditedImageCache().Get(Block(), [new ClearAreaEdit(new Rct(0, 0, 4, 4), "#FF00FF00")]), 1, 1)));
}
