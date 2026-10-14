using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapr.App.Rendering;
using Snapr.Core;

namespace Snapr.Tests;

public class ImageEditRendererTests
{
    const uint Mark = 0xFFFF0000, Other = 0xFF0000FF;

    // 3 × 2 image, only the top-left pixel is Mark.
    static BitmapSource Marked()
    {
        var px = Enumerable.Repeat(Other, 6).ToArray();
        px[0] = Mark;
        var b = BitmapSource.Create(3, 2, 96, 96, PixelFormats.Bgra32, null, px, 12);
        b.Freeze();
        return b;
    }

    static BitmapSource Solid(int w, int h, uint argb)
    {
        var b = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat(argb, w * h).ToArray(), w * 4);
        b.Freeze();
        return b;
    }

    [Theory]
    [InlineData(1, 2, 3, 1, 0)]   // right: top-left -> top-right
    [InlineData(3, 2, 3, 0, 2)]   // left: top-left -> bottom-left
    [InlineData(2, 3, 2, 2, 1)]   // 180: top-left -> bottom-right
    public void Rotation_moves_the_marked_pixel(int turns, int w, int h, int x, int y) => Sta.Run(() =>
    {
        var r = ImageEditRenderer.Apply(Marked(), new RotateEdit(turns));
        Assert.Equal((w, h), (r.PixelWidth, r.PixelHeight));
        Assert.Equal(Mark, ImageIo.PixelAt(r, x, y));
    });

    [Theory]
    [InlineData(true, 2, 0)]
    [InlineData(false, 0, 1)]
    public void Flip_mirrors(bool horizontal, int x, int y) => Sta.Run(() =>
        Assert.Equal(Mark, ImageIo.PixelAt(ImageEditRenderer.Apply(Marked(), new FlipEdit(horizontal)), x, y)));

    [Fact]
    public void Resize_sets_the_size() => Sta.Run(() =>
    {
        var r = ImageEditRenderer.Apply(Solid(3, 2, Other), new ResizeEdit(6, 4));
        Assert.Equal((6, 4), (r.PixelWidth, r.PixelHeight));
        Assert.Equal(Other, ImageIo.PixelAt(r, 3, 2));
    });

    [Fact]
    public void Margins_grow_the_image_in_their_colour() => Sta.Run(() =>
    {
        var r = ImageEditRenderer.Apply(Solid(3, 2, Other), new MarginsEdit(1, 2, 3, 4, "#FF00FF00"));
        Assert.Equal((7, 8), (r.PixelWidth, r.PixelHeight));
        Assert.Equal(0xFF00FF00u, ImageIo.PixelAt(r, 0, 0));
        Assert.Equal(Other, ImageIo.PixelAt(r, 1, 2));
        Assert.Equal(0u, ImageIo.PixelAt(ImageEditRenderer.Apply(Solid(3, 2, Other), new MarginsEdit(1, 0, 0, 0, "#00000000")), 0, 0) >> 24);
    });

    [Fact]
    public void Fill_floods_the_image() => Sta.Run(() =>
    {
        var r = ImageEditRenderer.Apply(Marked(), new FillEdit(2, 1, "#FF00FF00", 0));
        Assert.Equal(0xFF00FF00u, ImageIo.PixelAt(r, 1, 0));
        Assert.Equal(Mark, ImageIo.PixelAt(r, 0, 0));
    });

    [Fact]
    public void Cache_returns_the_earlier_image_after_undo() => Sta.Run(() =>
    {
        var cache = new EditedImageCache();
        var original = Marked();
        Assert.Same(original, cache.Get(original, []));
        var one = cache.Get(original, [new RotateEdit(1)]);
        var two = cache.Get(original, [new RotateEdit(1), new FlipEdit(true)]);
        Assert.Same(one, cache.Get(original, [new RotateEdit(1)]));
        Assert.Same(two, cache.Get(original, [new RotateEdit(1), new FlipEdit(true)]));
        Assert.Equal((2, 3), (two.PixelWidth, two.PixelHeight));
    });

    [Fact]
    public void Cache_starts_over_for_a_different_original() => Sta.Run(() =>
    {
        var cache = new EditedImageCache();
        var a = cache.Get(Marked(), [new FlipEdit(true)]);
        var b = cache.Get(Solid(3, 2, Other), [new FlipEdit(true)]);
        Assert.NotSame(a, b);
        Assert.Equal(Other, ImageIo.PixelAt(b, 2, 0));
    });

    static ImageEdit[] Flips(int n) => Enumerable.Range(0, n).Select(i => (ImageEdit)new FlipEdit(i % 2 == 0)).ToArray();

    [Fact]
    public void Cache_exact_repeat_replays_nothing_and_one_new_edit_replays_one() => Sta.Run(() =>
    {
        var cache = new EditedImageCache();
        var original = Marked();
        var edits = Flips(5);
        cache.Get(original, edits);
        var before = cache.Replays;
        Assert.Equal(5, before);
        cache.Get(original, edits);
        Assert.Equal(before, cache.Replays);
        cache.Get(original, Flips(6));
        Assert.Equal(before + 1, cache.Replays);
    });

    [Fact]
    public void Cache_undo_far_back_replays_only_a_few_edits_per_step() => Sta.Run(() =>
    {
        var cache = new EditedImageCache();
        var original = Marked();
        var edits = Flips(24);
        cache.Get(original, edits);
        for (var n = 23; n >= 1; n--)
        {
            var before = cache.Replays;
            cache.Get(original, edits.Take(n).ToArray());
            Assert.True(cache.Replays - before <= 4, $"undo to {n} replayed {cache.Replays - before} edits");
        }
    });

    [Fact]
    public void Cache_stays_within_its_byte_budget_but_keeps_the_returned_image() => Sta.Run(() =>
    {
        var cache = new EditedImageCache(budgetBytes: 100);   // 3 x 2 x 4 = 24 bytes per image: room for four
        var original = Marked();
        var edits = Flips(12);
        var last = cache.Get(original, edits);
        Assert.Same(last, cache.Get(original, edits));
        var before = cache.Replays;
        cache.Get(original, edits.Take(11).ToArray());
        Assert.True(cache.Replays - before <= 4);
    });
}
