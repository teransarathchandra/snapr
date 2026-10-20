using Snapr.Core.Stitching;

namespace Snapr.Tests;

public class PanoramaStitcherTests
{
    const int W = 200, H = 150;

    // 600 × 400: a smooth gradient plus 40-px blocks of seeded random colour, so every region is distinct at every scale.
    static readonly Frame Source = MakeSource();

    static Frame MakeSource()
    {
        const int sw = 600, sh = 400;
        var rng = new Random(12345);
        var blocks = new uint[(sw / 40 + 1) * (sh / 40 + 1)];
        for (var i = 0; i < blocks.Length; i++) blocks[i] = (uint)rng.Next(0, 0x1000000);
        var px = new uint[sw * sh];
        for (var y = 0; y < sh; y++)
            for (var x = 0; x < sw; x++)
            {
                var b = blocks[(y / 40) * (sw / 40 + 1) + x / 40];
                int r = (int)((b >> 16) & 0xFF) / 2 + x * 127 / sw, g = (int)((b >> 8) & 0xFF) / 2 + y * 127 / sh, bl = (int)(b & 0xFF) / 2 + 64;
                px[y * sw + x] = 0xFF000000u | (uint)r << 16 | (uint)g << 8 | (uint)bl;
            }
        return new Frame(sw, sh, px);
    }

    static Frame Cut(int x0, int y0, int w = W, int h = H)
    {
        var px = new uint[w * h];
        for (var y = 0; y < h; y++) Array.Copy(Source.Pixels, (y0 + y) * Source.Width + x0, px, y * w, w);
        return new Frame(w, h, px);
    }

    static Frame Plain(uint argb) => new(W, H, Enumerable.Repeat(argb, W * H).ToArray());

    /// <summary>Adds frames cut at the given source positions and checks the canvas against the source over the covered union.</summary>
    static PanoramaStitcher StitchAndCheck(params (int X, int Y)[] at)
    {
        var s = new PanoramaStitcher();
        foreach (var (x, y) in at) Assert.Equal(PanoramaAdd.Added, s.Add(Cut(x, y)));
        int minX = at.Min(p => p.X), minY = at.Min(p => p.Y), maxX = at.Max(p => p.X) + W, maxY = at.Max(p => p.Y) + H;
        Assert.Equal((maxX - minX, maxY - minY), (s.Width, s.Height));
        var c = s.Compose();
        Assert.Equal((s.Width, s.Height), (c.Width, c.Height));
        for (var y = 0; y < c.Height; y++)
            for (var x = 0; x < c.Width; x++)
            {
                int sx = x + minX, sy = y + minY;
                var covered = at.Any(p => sx >= p.X && sx < p.X + W && sy >= p.Y && sy < p.Y + H);
                Assert.Equal(covered ? Source.Pixels[sy * Source.Width + sx] : 0u, c.Pixels[y * c.Width + x]);
            }
        Assert.False(s.LostParts);
        return s;
    }

    [Fact]
    public void Right_only_stitches() => StitchAndCheck((0, 0), (60, 0), (120, 0), (180, 0));

    [Fact]
    public void Down_only_stitches() => StitchAndCheck((0, 0), (0, 40), (0, 80), (0, 120));

    [Fact]
    public void Diagonal_stitches_and_leaves_uncovered_pixels_transparent() => StitchAndCheck((0, 0), (60, 40), (120, 80));

    [Fact]
    public void Back_and_forth_stitches() => StitchAndCheck((0, 0), (60, 0), (120, 0), (60, 0), (0, 0));

    [Fact]
    public void Content_moving_left_and_up_grows_the_canvas_the_other_way() => StitchAndCheck((200, 120), (140, 80), (80, 40));

    [Fact]
    public void Identical_frame_is_skipped()
    {
        var s = new PanoramaStitcher();
        s.Add(Cut(0, 0));
        Assert.Equal(PanoramaAdd.Skipped, s.Add(Cut(0, 0)));
        Assert.Equal(1, s.Frames);
        Assert.False(s.LostParts);
    }

    [Fact]
    public void Slight_change_without_movement_is_skipped_and_painted()
    {
        var s = new PanoramaStitcher();
        s.Add(Cut(0, 0));
        var changed = Cut(0, 0);
        for (var y = 50; y < 60; y++)
            for (var x = 50; x < 60; x++) changed.Pixels[y * W + x] = 0xFF123456;
        Assert.Equal(PanoramaAdd.Skipped, s.Add(changed));
        Assert.Equal(1, s.Frames);
        var c = s.Compose();
        Assert.Equal(0xFF123456u, c.Pixels[55 * W + 55]);
        Assert.Equal((W, H), (c.Width, c.Height));
    }

    [Fact]
    public void Plain_frame_is_dropped()
    {
        var s = new PanoramaStitcher();
        s.Add(Cut(0, 0));
        Assert.Equal(PanoramaAdd.Dropped, s.Add(Plain(0xFFFFFFFF)));
        Assert.True(s.LostParts);
        Assert.Equal((W, H), (s.Width, s.Height));
    }

    [Fact]
    public void Too_far_step_is_dropped_then_resumes()
    {
        var s = new PanoramaStitcher();
        s.Add(Cut(0, 0));
        Assert.Equal(PanoramaAdd.Dropped, s.Add(Cut(130, 0)));   // more than W/2 from the last accepted frame
        Assert.True(s.LostParts);
        Assert.Equal(PanoramaAdd.Added, s.Add(Cut(60, 0)));
        Assert.Equal((260, H), (s.Width, s.Height));
    }

    [Fact]
    public void Max_side_gives_full_and_keeps_the_canvas()
    {
        var s = new PanoramaStitcher(maxSide: 300);
        s.Add(Cut(0, 0));
        s.Add(Cut(60, 0));
        Assert.Equal(PanoramaAdd.Full, s.Add(Cut(120, 0)));   // 320 wide
        Assert.Equal((260, H, 2), (s.Width, s.Height, s.Frames));
        Assert.True(s.LostParts);
    }

    [Fact]
    public void Max_pixels_gives_full()
    {
        var s = new PanoramaStitcher(maxPixels: 40_000);
        s.Add(Cut(0, 0));
        s.Add(Cut(60, 0));                                    // 260 × 150 = 39 000
        Assert.Equal(PanoramaAdd.Full, s.Add(Cut(120, 0)));   // 320 × 150 = 48 000
        Assert.Equal(2, s.Frames);
    }

    [Fact]
    public void Max_frames_gives_full()
    {
        var s = new PanoramaStitcher(maxFrames: 2);
        s.Add(Cut(0, 0));
        s.Add(Cut(60, 0));
        Assert.Equal(PanoramaAdd.Full, s.Add(Cut(120, 0)));
        Assert.Equal(2, s.Frames);
    }

    [Fact]
    public void Wrong_frame_size_throws()
    {
        var s = new PanoramaStitcher();
        s.Add(Cut(0, 0));
        Assert.Throws<ArgumentException>(() => s.Add(Cut(0, 0, 100, 100)));
    }

    [Fact]
    public void Empty_stitcher_has_no_frames()
    {
        var s = new PanoramaStitcher();
        Assert.Equal((0, 0, 0), (s.Frames, s.Width, s.Height));
    }

    [Fact]
    public void Blank_first_frame_is_replaced_by_the_first_real_one()
    {
        var s = new PanoramaStitcher();
        Assert.Equal(PanoramaAdd.Added, s.Add(Plain(0xFFFFFFFF)));
        Assert.Equal(PanoramaAdd.Added, s.Add(Cut(0, 0)));
        Assert.Equal(PanoramaAdd.Added, s.Add(Cut(60, 0)));
        Assert.Equal(W + 60, s.Width);
        Assert.False(s.LostParts);
    }

    [Fact]
    public void Two_blank_frames_keep_waiting()
    {
        var s = new PanoramaStitcher();
        s.Add(Plain(0xFFFFFFFF));
        Assert.Equal(PanoramaAdd.Added, s.Add(Plain(0xFFF0F0F0)));
        Assert.False(s.LostParts);
        Assert.Equal(1, s.Frames);
    }
}
