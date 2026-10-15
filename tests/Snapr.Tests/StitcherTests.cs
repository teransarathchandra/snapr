using Snapr.Core.Stitching;

namespace Snapr.Tests;

public class StitcherTests
{
    const int W = 64, ViewH = 100, Header = 10, Footer = 6, Body = ViewH - Header - Footer;
    const uint HeaderColor = 0xFF2020F0, FooterColor = 0xFF20F020;

    static uint[] Page(int height, int seed = 3)
    {
        var r = new Random(seed);
        var px = new uint[W * height];
        for (var i = 0; i < px.Length; i++) px[i] = 0xFF000000u | (uint)r.Next(0, 0xFFFFFF);
        return px;
    }

    /// <summary>A viewport showing page rows [offset, offset+Body) between a fixed header and footer.</summary>
    static Frame View(uint[] page, int offset, Func<int, int, uint>? scrollbar = null)
    {
        var px = new uint[W * ViewH];
        for (var y = 0; y < ViewH; y++)
        for (var x = 0; x < W; x++)
        {
            px[y * W + x] = y < Header ? HeaderColor
                : y >= ViewH - Footer ? FooterColor
                : page[(offset + y - Header) * W + x];
            if (scrollbar is not null && x >= W - 4) px[y * W + x] = scrollbar(offset, y);
        }
        return new Frame(W, ViewH, px);
    }

    static uint[] Expected(uint[] page, int pageRows)
    {
        var px = new List<uint>();
        px.AddRange(Enumerable.Repeat(HeaderColor, W * Header));
        px.AddRange(page.AsSpan(0, W * pageRows).ToArray());
        px.AddRange(Enumerable.Repeat(FooterColor, W * Footer));
        return px.ToArray();
    }

    [Fact]
    public void Known_overlaps_rebuild_the_page()
    {
        var page = Page(300);
        var frames = new[] { 0, 30, 60, 90 }.Select(o => View(page, o)).ToArray();

        var result = Stitcher.Stitch(frames, ignoreRightColumns: 0);

        Assert.True(result.Complete);
        Assert.Equal(Header + 90 + Body + Footer, result.Image.Height);
        Assert.Equal(Expected(page, 90 + Body), result.Image.Pixels);
    }

    [Fact]
    public void Identical_frame_ends_the_sequence()
    {
        var page = Page(300);
        var frames = new[] { View(page, 0), View(page, 40), View(page, 40), View(page, 80) };

        var result = Stitcher.Stitch(frames, ignoreRightColumns: 0);

        Assert.True(result.Complete);
        Assert.Equal(Header + 40 + Body + Footer, result.Image.Height);
    }

    [Fact]
    public void No_overlap_stops_incomplete()
    {
        var page = Page(400);
        var frames = new[] { View(page, 0), View(page, 50), View(page, 250) };

        var result = Stitcher.Stitch(frames, ignoreRightColumns: 0);

        Assert.False(result.Complete);
        Assert.Equal(Header + 50 + Body + Footer, result.Image.Height);
    }

    [Fact]
    public void Moving_scrollbar_is_ignored()
    {
        var page = Page(300);
        uint Thumb(int offset, int y) => y >= Header + offset / 4 && y < Header + offset / 4 + 20 ? 0xFF888888 : 0xFFEEEEEE;
        var frames = new[] { 0, 30, 60 }.Select(o => View(page, o, Thumb)).ToArray();

        var result = Stitcher.Stitch(frames, ignoreRightColumns: 4);

        Assert.True(result.Complete);
        Assert.Equal(Header + 60 + Body + Footer, result.Image.Height);
    }

    [Fact]
    public void Max_height_stops_incomplete()
    {
        var page = Page(600);
        var frames = Enumerable.Range(0, 10).Select(i => View(page, i * 50)).ToArray();

        var result = Stitcher.Stitch(frames, ignoreRightColumns: 0, maxHeight: 250);

        Assert.False(result.Complete);
        Assert.True(result.Image.Height <= 250);
    }

    [Fact]
    public void Single_frame_is_returned_as_is()
    {
        var f = View(Page(200), 0);
        var result = Stitcher.Stitch([f]);
        Assert.True(result.Complete);
        Assert.Equal(f.Pixels, result.Image.Pixels);
    }

    [Fact]
    public void Frames_of_different_sizes_are_rejected() =>
        Assert.Throws<ArgumentException>(() => Stitcher.Stitch([new Frame(2, 2, new uint[4]), new Frame(3, 2, new uint[6])]));

    [Fact]
    public void Window_border_blending_with_the_background_is_ignored()
    {
        // A window's 1 px edge is semi-transparent: it shows the (unscrolled) background, so it differs per row
        // in a way that does not move with the content.
        var page = Page(300);
        var frames = new[] { 0, 30, 60 }.Select((o, i) => View(page, o, null)).ToArray();
        var r = new Random(9);
        foreach (var f in frames)
            for (var y = 0; y < ViewH; y++) f.Pixels[y * W] = 0xFF000000u | (uint)r.Next(0, 0xFFFFFF);

        var result = Stitcher.Stitch(frames, ignoreRightColumns: 0);

        Assert.True(result.Complete);
        Assert.Equal(Header + 60 + Body + Footer, result.Image.Height);
    }

    [Fact]
    public void Window_fixed_edge_shading_is_ignored()
    {
        // Browsers such as Edge draw a shaded strip down the content's left edge: it changes from row to row but
        // belongs to the window, so it is the same in every frame and must not take part in matching.
        var page = Page(300);
        var frames = new[] { 0, 30, 60 }.Select(o => View(page, o)).ToArray();
        foreach (var f in frames)
            for (var y = 0; y < ViewH; y++)
            for (var x = 0; x < 9; x++)
                f.Pixels[y * W + x] = 0xFF000000u | (uint)(y * 0x020202);

        var result = Stitcher.Stitch(frames, ignoreRightColumns: 0);

        Assert.True(result.Complete);
        Assert.Equal(Header + 60 + Body + Footer, result.Image.Height);
    }

    [Fact]
    public void Edge_row_blended_by_one_level_still_matches()
    {
        // The last content row sits under the window's semi-transparent bottom edge, which shifts colours by 1.
        var page = Page(300);
        var frames = new[] { 0, 30, 60 }.Select(o => View(page, o)).ToArray();
        foreach (var f in frames)
            for (var x = 0; x < W; x++)
                f.Pixels[(ViewH - Footer - 1) * W + x] ^= 1;

        var result = Stitcher.Stitch(frames, ignoreRightColumns: 0);

        Assert.True(result.Complete);
        Assert.Equal(Header + 60 + Body + Footer, result.Image.Height);
    }

    [Fact]
    public void Strongly_blended_edge_row_is_replaced_by_the_next_frame()
    {
        // Edge blends its last content row with the window edge by up to ~10 levels: too much for the tolerance, so
        // that row must be left out of matching and taken from the next frame, leaving no line inside the image.
        var page = Page(300);
        var frames = new[] { 0, 30, 60 }.Select(o => View(page, o)).ToArray();
        foreach (var f in frames)
            for (var x = 0; x < W; x++)
            {
                var i = (ViewH - Footer - 1) * W + x;
                f.Pixels[i] = (f.Pixels[i] & 0xFF000000u) | ((f.Pixels[i] & 0x00F0F0F0u) >> 1) | 0x00080808u;
            }

        var result = Stitcher.Stitch(frames, ignoreRightColumns: 0);

        Assert.True(result.Complete);
        Assert.Equal(Header + 60 + Body + Footer, result.Image.Height);
        Assert.Equal(page.AsSpan((Body - 1) * W, W).ToArray(), result.Image.Row(Header + Body - 1).ToArray());
    }
}
