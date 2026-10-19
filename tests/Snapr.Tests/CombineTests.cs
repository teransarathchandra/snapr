using Snapr.Core;
using Snapr.Core.Interop;

namespace Snapr.Tests;

public class CombineTests
{
    static readonly (int, int)[] Two = [(10, 20), (30, 10)];

    [Fact]
    public void Side_by_side_centre_with_gap()
    {
        var r = CombineArrange.Arrange(Two, CombineLayout.SideBySide, CombineAlign.Centre, 20);
        Assert.Equal((100, 60), (r.Width, r.Height));
        Assert.Equal([new Rct(20, 20, 10, 20), new Rct(50, 25, 30, 10)], r.Places);
    }

    [Theory]
    [InlineData(CombineAlign.Start, 20)]
    [InlineData(CombineAlign.End, 30)]
    public void Side_by_side_aligns_vertically(CombineAlign align, double y) =>
        Assert.Equal(y, CombineArrange.Arrange(Two, CombineLayout.SideBySide, align, 20).Places[1].Y);

    [Fact]
    public void Stacked_centre_with_gap()
    {
        var r = CombineArrange.Arrange(Two, CombineLayout.Stacked, CombineAlign.Centre, 20);
        Assert.Equal((70, 90), (r.Width, r.Height));
        Assert.Equal([new Rct(30, 20, 10, 20), new Rct(20, 60, 30, 10)], r.Places);
    }

    [Fact]
    public void Stacked_end_aligns_right() =>
        Assert.Equal(40, CombineArrange.Arrange(Two, CombineLayout.Stacked, CombineAlign.End, 20).Places[0].X);

    [Fact]
    public void Grid_uses_column_widths_and_row_heights()
    {
        var r = CombineArrange.Arrange([(10, 10), (20, 30), (5, 5)], CombineLayout.Grid, CombineAlign.Start, 0);
        Assert.Equal((30, 35), (r.Width, r.Height));
        Assert.Equal([new Rct(0, 0, 10, 10), new Rct(10, 0, 20, 30), new Rct(0, 30, 5, 5)], r.Places);
    }

    [Fact]
    public void Grid_centre_rounds_down_on_both_axes()
    {
        var r = CombineArrange.Arrange([(5, 5), (20, 20)], CombineLayout.Grid, CombineAlign.Centre, 0);
        Assert.Equal(new Rct(0, 7, 5, 5), r.Places[0]);   // column 0 is 5 wide; row height 20 → (20-5)/2 = 7
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(10, 4)]
    public void Grid_column_count(int n, int cols)
    {
        var r = CombineArrange.Arrange(Enumerable.Repeat((10, 10), n).ToArray(), CombineLayout.Grid, CombineAlign.Start, 0);
        Assert.Equal(cols * 10, r.Width);
    }

    [Fact]
    public void Gap_zero_has_no_margin() =>
        Assert.Equal(new Rct(0, 0, 10, 20), CombineArrange.Arrange(Two, CombineLayout.SideBySide, CombineAlign.Start, 0).Places[0]);

    [Fact]
    public void Tiny_and_huge_mix()
    {
        var r = CombineArrange.Arrange([(1, 1), (3000, 2000)], CombineLayout.SideBySide, CombineAlign.Centre, 10);
        Assert.Equal((3031, 2020), (r.Width, r.Height));   // 1 + 3000 + 3 × 10
        Assert.Equal([new Rct(10, 1009, 1, 1), new Rct(21, 10, 3000, 2000)], r.Places);
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(21, 10)]
    [InlineData(2, -1)]
    [InlineData(2, 201)]
    public void Rejects_bad_count_or_gap(int n, int gap) =>
        Assert.Throws<ArgumentException>(() => CombineArrange.Arrange(Enumerable.Repeat((10, 10), n).ToArray(), CombineLayout.Grid, CombineAlign.Start, gap));

    [Fact]
    public void Rejects_zero_size() =>
        Assert.Throws<ArgumentException>(() => CombineArrange.Arrange([(10, 10), (0, 10)], CombineLayout.Grid, CombineAlign.Start, 0));

    [Fact]
    public void Too_large_at_and_over_the_cap()
    {
        Assert.Equal(100_000_000L, SnagxReader.MaxTotalPixels);
        Assert.False(CombineArrange.TooLarge(CombineArrange.Arrange([(10000, 5000), (10000, 5000)], CombineLayout.SideBySide, CombineAlign.Start, 0)));
        Assert.True(CombineArrange.TooLarge(CombineArrange.Arrange([(10000, 5000), (10000, 5000)], CombineLayout.SideBySide, CombineAlign.Start, 1)));
        Assert.True(CombineArrange.TooLarge(CombineArrange.Arrange([(20000, 1), (20000, 1)], CombineLayout.SideBySide, CombineAlign.Start, 0)));
    }

    [Fact]
    public void Toggle_adds_in_order_and_removes()
    {
        var p = new PickList();
        Assert.True(p.Toggle("a"));
        Assert.True(p.Toggle("b"));
        Assert.Equal((1, 2), (p.NumberOf("a"), p.NumberOf("b")));
        Assert.True(p.Toggle("a"));
        Assert.Equal(["b"], p.Paths);
        Assert.Equal(0, p.NumberOf("a"));
    }

    [Fact]
    public void Toggle_is_case_insensitive()
    {
        var p = new PickList();
        p.Toggle(@"C:\x\A.snapr");
        p.Toggle(@"c:\X\a.SNAPR");
        Assert.Equal(0, p.Count);
    }

    [Fact]
    public void Toggle_refuses_the_21st()
    {
        var p = new PickList();
        for (var i = 0; i < 20; i++) Assert.True(p.Toggle($"f{i}"));
        Assert.False(p.Toggle("f20"));
        Assert.Equal(20, p.Count);
        Assert.True(p.Toggle("f0"));   // removing still works when full
    }

    [Fact]
    public void Retain_drops_missing()
    {
        var p = new PickList();
        p.Toggle("a");
        p.Toggle("b");
        p.Toggle("c");
        p.Retain(["C", "a"]);
        Assert.Equal(["a", "c"], p.Paths);
        p.Clear();
        Assert.Equal(0, p.Count);
    }
}
