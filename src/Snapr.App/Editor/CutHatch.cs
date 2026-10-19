using System.Windows;

namespace Snapr.App.Editor;

/// <summary>Diagonal hatching for the Cut Out preview (spec parity 11 §6): lines x + y = k every <c>spacing</c>, clipped to the band.</summary>
public static class CutHatch
{
    public static IReadOnlyList<(Point A, Point B)> Lines(Rect band, double spacing)
    {
        var lines = new List<(Point, Point)>();
        if (band.IsEmpty || band.Width <= 0 || band.Height <= 0 || spacing <= 0) return lines;
        for (var k = band.Left + band.Top + spacing; k < band.Right + band.Bottom; k += spacing)
        {
            double x0 = Math.Max(band.Left, k - band.Bottom), x1 = Math.Min(band.Right, k - band.Top);
            if (x1 > x0) lines.Add((new Point(x0, k - x0), new Point(x1, k - x1)));
        }
        return lines;
    }
}
