using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapr.App.Rendering;
using Snapr.Core;

namespace Snapr.Tests;

public class StampRenderTests
{
    public static IEnumerable<object[]> Ids() => StampCatalog.All.Select(s => new object[] { s.Id });

    static uint[] Draw(string id, string color)
    {
        var white = new uint[200 * 200];
        Array.Fill(white, 0xFFFFFFFFu);
        var src = BitmapSource.Create(200, 200, 96, 96, PixelFormats.Bgra32, null, white, 800);
        src.Freeze();
        var img = DocumentRenderer.Render(src,
            State(new StampAnnotation(new Rct(50, 50, 100, 100), id, color)), applyEffects: false);
        var c = new FormatConvertedBitmap(img, PixelFormats.Bgra32, null, 0);
        var px = new uint[200 * 200];
        c.CopyPixels(new Int32Rect(0, 0, 200, 200), px, 800, 0);
        return px;
    }

    static DocumentState State(Annotation a) => DocumentState.Empty with { Annotations = [a] };

    [Theory]
    [MemberData(nameof(Ids))]
    public void Every_builtin_stamp_draws_inside_its_box(string id) => Sta.Run(() =>
    {
        var px = Draw(id, "#FFFF0000");
        int inside = 0, outside = 0;
        for (var y = 0; y < 200; y++)
            for (var x = 0; x < 200; x++)
            {
                if (px[y * 200 + x] == 0xFFFFFFFFu) continue;
                if (x is >= 50 and < 150 && y is >= 50 and < 150) inside++;
                else if (!(x is >= 48 and < 152 && y is >= 48 and < 152)) outside++;
            }
        Assert.True(inside >= 500, $"{id}: only {inside} pixels drawn");
        Assert.True(outside == 0, $"{id}: {outside} pixels outside the box");
    });

    [Fact]
    public void Unknown_stamp_draws_placeholder() => Sta.Run(() =>
        Assert.Equal(0xFFD4D4D8u, Draw("from-the-future", "#FFFF0000")[100 * 200 + 100]));

    [Fact]
    public void Stamp_uses_its_colour() => Sta.Run(() =>
    {
        var px = Draw("check", "#FF0000FF");
        var top = px.Where((p, i) => p != 0xFFFFFFFFu && i % 200 is >= 50 and < 150 && i / 200 is >= 50 and < 150)
            .GroupBy(p => p).OrderByDescending(g => g.Count()).First().Key;
        Assert.Equal(0xFF0000FFu, top);
    });

    [Fact]
    public void Stamp_preview_is_frozen_and_sized() => Sta.Run(() =>
    {
        var img = DocumentRenderer.StampPreview("star", "#FFFF0000", 32);
        Assert.True(img.IsFrozen);
        Assert.Equal((32.0, 32.0), (img.Width, img.Height));
    });
}
