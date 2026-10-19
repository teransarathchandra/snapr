using System.Windows;
using Snapr.App.Editor;

namespace Snapr.Tests;

public class CutHatchTests
{
    [Fact]
    public void Lines_stay_inside_the_band()
    {
        var band = new Rect(10, 50, 100, 20);
        var lines = CutHatch.Lines(band, 8);
        Assert.True(lines.Count >= 10);
        foreach (var (a, b) in lines)
            foreach (var p in new[] { a, b })
            {
                Assert.InRange(p.X, band.Left - 1e-9, band.Right + 1e-9);
                Assert.InRange(p.Y, band.Top - 1e-9, band.Bottom + 1e-9);
            }
    }

    [Fact]
    public void Lines_run_at_45_degrees()
    {
        foreach (var (a, b) in CutHatch.Lines(new Rect(0, 0, 40, 40), 8))
            Assert.Equal(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y), 9);
    }

    [Fact]
    public void Empty_band_has_no_lines()
    {
        Assert.Empty(CutHatch.Lines(new Rect(10, 10, 0, 50), 8));
        Assert.Empty(CutHatch.Lines(new Rect(10, 10, 50, 0), 8));
    }
}
