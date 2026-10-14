using Snapr.Core;
using Snapr.Core.Stitching;

namespace Snapr.Tests;

public class FloodFillTests
{
    const uint A = 0xFF0000FF, B = 0xFFFF0000, Near = 0xFF0A00FF, Fill = 0xFF00FF00;

    // 5 × 3:  A A B A A
    //         A A B A A
    //         B B B Near A
    static Frame Grid() => new(5, 3, [A, A, B, A, A, A, A, B, A, A, B, B, B, Near, A]);

    [Fact]
    public void Fills_only_the_connected_area_of_exactly_that_colour()
    {
        var f = FloodFill.Fill(Grid(), 0, 0, Fill, 0);
        Assert.Equal([Fill, Fill, B, A, A, Fill, Fill, B, A, A, B, B, B, Near, A], f.Pixels);
    }

    [Fact]
    public void Tolerance_takes_in_similar_colours()
    {
        Assert.Equal(Near, FloodFill.Fill(Grid(), 4, 0, Fill, 0).Pixels[13]);              // Near (red differs by 10) stays at tolerance 0
        Assert.Equal(Fill, FloodFill.Fill(Grid(), 4, 0, Fill, 10).Pixels[13]);              // 10 % of 255 = 26 ≥ 10
    }

    [Fact]
    public void Tolerance_100_fills_everything() =>
        Assert.All(FloodFill.Fill(Grid(), 2, 0, Fill, 100).Pixels, p => Assert.Equal(Fill, p));

    [Fact]
    public void Diagonal_pixels_are_not_connected()
    {
        var f = new Frame(2, 2, [A, B, B, A]);
        Assert.Equal([Fill, B, B, A], FloodFill.Fill(f, 0, 0, Fill, 0).Pixels);
    }

    [Fact]
    public void Outside_point_returns_the_same_frame_and_input_is_never_changed()
    {
        var g = Grid();
        Assert.Same(g, FloodFill.Fill(g, 5, 0, Fill, 10));
        FloodFill.Fill(g, 0, 0, Fill, 10);
        Assert.Equal(Grid().Pixels, g.Pixels);
    }
}
