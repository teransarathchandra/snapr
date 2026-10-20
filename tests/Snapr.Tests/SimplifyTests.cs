using Snapr.Core;
using Snapr.Core.Stitching;

namespace Snapr.Tests;

public class SimplifyTests
{
    const uint White = 0xFFFFFFFF;

    static Frame Canvas(int w, int h, uint c = White) => new(w, h, Enumerable.Repeat(c, w * h).ToArray());

    static void Fill(Frame f, int x, int y, int w, int h, uint c)
    {
        for (var yy = y; yy < y + h; yy++)
            for (var xx = x; xx < x + w; xx++) f.Pixels[yy * f.Width + xx] = c;
    }

    static Rct All(Frame f) => new(0, 0, f.Width, f.Height);

    [Fact]
    public void Plain_image_has_no_blocks() => Assert.Empty(BlockFinder.Find(Canvas(100, 80), new Rct(0, 0, 100, 80), []));

    [Fact]
    public void One_block_with_bounds_and_colour()
    {
        var f = Canvas(200, 150);
        Fill(f, 20, 30, 50, 40, 0xFF3366AA);
        var b = Assert.Single(BlockFinder.Find(f, All(f), []));
        Assert.Equal(new Block(new Rct(20, 30, 50, 40), 0xFF3366AA, 0), b);
    }

    [Fact]
    public void Two_blocks_largest_first()
    {
        var f = Canvas(200, 150);
        Fill(f, 10, 10, 40, 30, 0xFF00AA00);
        Fill(f, 100, 60, 60, 50, 0xFFAA0000);
        Assert.Equal([new Rct(100, 60, 60, 50), new Rct(10, 10, 40, 30)], BlockFinder.Find(f, All(f), []).Select(b => b.Bounds));
    }

    [Fact]
    public void Panel_with_buttons_nests_one_level()
    {
        var f = Canvas(200, 150);
        Fill(f, 20, 20, 120, 80, 0xFF3366AA);
        Fill(f, 30, 30, 30, 20, 0xFFEEEEEE);
        Fill(f, 80, 30, 30, 20, 0xFFEE0000);
        Assert.Equal(
            [
                new Block(new Rct(20, 20, 120, 80), 0xFF3366AA, 0),
                new Block(new Rct(30, 30, 30, 20), 0xFFEEEEEE, 1),
                new Block(new Rct(80, 30, 30, 20), 0xFFEE0000, 1),
            ],
            BlockFinder.Find(f, All(f), []));
    }

    [Fact]
    public void Small_noise_is_ignored()
    {
        var f = Canvas(100, 80);
        Fill(f, 10, 10, 5, 5, 0xFF000000);
        Assert.Empty(BlockFinder.Find(f, All(f), []));
    }

    [Fact]
    public void Outline_is_not_a_block()
    {
        var f = Canvas(100, 80);
        Fill(f, 10, 10, 20, 1, 0xFF000000);
        Fill(f, 10, 29, 20, 1, 0xFF000000);
        Fill(f, 10, 10, 1, 20, 0xFF000000);
        Fill(f, 29, 10, 1, 20, 0xFF000000);
        Assert.Empty(BlockFinder.Find(f, All(f), []));   // 76 of 400 px filled
    }

    [Fact]
    public void Block_inside_a_text_box_is_skipped()
    {
        var f = Canvas(100, 80);
        Fill(f, 10, 10, 40, 20, 0xFF000000);
        Assert.Empty(BlockFinder.Find(f, All(f), [new Rct(8, 8, 50, 26)]));
    }

    [Fact]
    public void Most_common_colour_wins_and_keeps_its_exact_value()
    {
        var f = Canvas(100, 80);
        Fill(f, 10, 10, 40, 40, 0xFF214181);     // bucket 20 40 80
        Fill(f, 40, 40, 10, 10, 0xFFFF0000);     // 100 px of another colour
        Fill(f, 10, 10, 1, 1, 0xFF204080);       // a different exact colour in the same bucket, found first
        var b = Assert.Single(BlockFinder.Find(f, All(f), []));
        Assert.Equal(0xFF204080u, b.Color);
    }

    [Fact]
    public void At_most_300_blocks_keeping_the_largest()
    {
        var f = Canvas(1000, 600);
        for (var y = 0; y + 25 <= 600; y += 25)
            for (var x = 0; x + 25 <= 1000; x += 25)
                Fill(f, x + 5, y + 5, 12, 12, 0xFF000000);   // 960 small blocks
        Fill(f, 0, 0, 1000, 25, White);
        Fill(f, 5, 3, 40, 20, 0xFF0000FF);                     // one bigger block in the cleared first row
        var blocks = BlockFinder.Find(f, All(f), []);
        Assert.Equal(300, blocks.Count);
        Assert.Equal(new Rct(5, 3, 40, 20), blocks[0].Bounds);
    }

    [Fact]
    public void Large_area_uses_the_downscaled_path()
    {
        var f = Canvas(2200, 2000);                             // 4 400 000 px
        Fill(f, 100, 100, 400, 300, 0xFF336699);
        var b = Assert.Single(BlockFinder.Find(f, All(f), []));
        Assert.InRange(b.Bounds.X, 98, 102);
        Assert.InRange(b.Bounds.Y, 98, 102);
        Assert.InRange(b.Bounds.W, 396, 404);
        Assert.InRange(b.Bounds.H, 296, 304);
    }

    [Fact]
    public void Downscaled_area_with_an_odd_origin()
    {
        var f = Canvas(2101, 2001);                             // 4 204 101 px
        Fill(f, 203, 305, 400, 300, 0xFF336699);
        var b = Assert.Single(BlockFinder.Find(f, new Rct(3, 5, 2097, 1995), []));
        Assert.InRange(b.Bounds.X, 201, 205);
        Assert.InRange(b.Bounds.Y, 303, 307);
        Assert.InRange(b.Bounds.W, 396, 404);
        Assert.InRange(b.Bounds.H, 296, 304);
    }

    [Fact]
    public void Colour_ties_go_to_the_colour_found_first_row_by_row()
    {
        var f = Canvas(100, 80);
        Fill(f, 10, 10, 40, 20, 0xFF3366AA);   // two buckets within tolerance, so no inner block
        Fill(f, 10, 30, 40, 20, 0xFF2F5EA0);
        var b = Assert.Single(BlockFinder.Find(f, All(f), []));
        Assert.Equal(0xFF3366AAu, b.Color);
    }

    [Fact]
    public void Block_inside_a_hole_is_drawn_after_the_hole()
    {
        var f = Canvas(200, 150);
        Fill(f, 10, 10, 80, 60, 0xFF222222);
        Fill(f, 20, 20, 60, 40, White);
        Fill(f, 40, 30, 20, 20, 0xFFFF0000);
        Assert.Equal(
            [
                new Block(new Rct(10, 10, 80, 60), 0xFF222222, 0),
                new Block(new Rct(20, 20, 60, 40), White, 1),
                new Block(new Rct(40, 30, 20, 20), 0xFFFF0000, 0),
            ],
            BlockFinder.Find(f, All(f), []));
    }

    [Fact]
    public void Area_limits_the_search()
    {
        var f = Canvas(200, 100);
        Fill(f, 10, 10, 30, 30, 0xFF000000);
        Fill(f, 120, 10, 30, 30, 0xFF000000);
        Assert.Equal([new Rct(120, 10, 30, 30)], BlockFinder.Find(f, new Rct(100, 0, 100, 100), []).Select(b => b.Bounds));
    }

    [Fact]
    public void Bar_is_centred_on_the_line()
    {
        var line = new TextLine([new TextWord("Hello", new Rct(10, 20, 30, 10)), new TextWord("world", new Rct(45, 18, 20, 14))]);
        Assert.Equal(new Rct(10, 21, 55, 8), SimplifyShapes.Bar(line));   // union (10,18,55,14); h = round(7.7) = 8
        Assert.Null(SimplifyShapes.Bar(new TextLine([])));
    }

    [Fact]
    public void Auto_bar_colour_on_light_and_fallback()
    {
        Assert.Equal("#FF9CA3AF", SimplifyShapes.BarColor("auto", 0xFFFFFFFF));
        Assert.Equal("#FF111111", SimplifyShapes.BarColor("auto", 0xFFAAAAAA));   // grey too close to #AAAAAA
    }

    [Fact]
    public void Auto_bar_colour_on_dark_and_fallback()
    {
        Assert.Equal("#FF6B7280", SimplifyShapes.BarColor("auto", 0xFF111111));
        Assert.Equal("#FFE5E7EB", SimplifyShapes.BarColor("auto", 0xFF707070));
    }

    [Fact]
    public void Chosen_bar_colour_passes_through() => Assert.Equal("#FF3B82F6", SimplifyShapes.BarColor("#FF3B82F6", 0xFF3B82F6));
}
