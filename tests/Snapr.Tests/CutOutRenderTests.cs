using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapr.App.Rendering;
using Snapr.Core;

namespace Snapr.Tests;

public class CutOutRenderTests
{
    static uint Row(int y) => 0xFF000000 | (uint)(y << 8) | 0x10;
    static uint Col(int x) => 0xFF000000 | (uint)(x << 16) | 0x10;

    // 200 × 100; every pixel of row y is Row(y).
    static BitmapSource Rows()
    {
        var px = new uint[200 * 100];
        for (int y = 0; y < 100; y++) Array.Fill(px, Row(y), y * 200, 200);
        var b = BitmapSource.Create(200, 100, 96, 96, PixelFormats.Bgra32, null, px, 800);
        b.Freeze();
        return b;
    }

    // 200 × 100; every pixel of column x is Col(x).
    static BitmapSource Cols()
    {
        var px = new uint[200 * 100];
        for (int i = 0; i < px.Length; i++) px[i] = Col(i % 200);
        var b = BitmapSource.Create(200, 100, 96, 96, PixelFormats.Bgra32, null, px, 800);
        b.Freeze();
        return b;
    }

    [Fact]
    public void Horizontal_cut_joins_the_rows_around_the_strip() => Sta.Run(() =>
    {
        var r = ImageEditRenderer.Apply(Rows(), new CutOutEdit(false, 20, 50, CutJoin.Straight));
        Assert.Equal((200, 70), (r.PixelWidth, r.PixelHeight));
        Assert.Equal(Row(19), ImageIo.PixelAt(r, 100, 19));
        Assert.Equal(Row(50), ImageIo.PixelAt(r, 100, 20));
        Assert.Equal(Row(99), ImageIo.PixelAt(r, 100, 69));
    });

    [Fact]
    public void Vertical_cut_joins_the_columns_around_the_strip() => Sta.Run(() =>
    {
        var r = ImageEditRenderer.Apply(Cols(), new CutOutEdit(true, 40, 100, CutJoin.Straight));
        Assert.Equal((140, 100), (r.PixelWidth, r.PixelHeight));
        Assert.Equal(Col(100), ImageIo.PixelAt(r, 40, 50));
    });

    [Fact]
    public void Torn_join_draws_a_white_seam() => Sta.Run(() =>
    {
        var r = ImageEditRenderer.Apply(Rows(), new CutOutEdit(false, 20, 50, CutJoin.Torn));
        Assert.Equal(0xFFFFFFFFu, ImageIo.PixelAt(r, 4, 20));
        Assert.Equal(Row(90), ImageIo.PixelAt(r, 100, 60));
    });

    [Fact]
    public void Straight_join_has_no_seam() => Sta.Run(() =>
    {
        var r = ImageEditRenderer.Apply(Rows(), new CutOutEdit(false, 20, 50, CutJoin.Straight));
        Assert.Equal(Row(50), ImageIo.PixelAt(r, 4, 20));
    });
}
