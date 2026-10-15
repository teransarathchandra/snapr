namespace Snapr.Core;

/// <summary>The torn seam drawn where a Torn cut joins the two pieces (spec parity 7 §2.2).</summary>
public static class CutOutGeometry
{
    public const double BandThickness = 6, Amplitude = 5, Period = 16;

    /// <summary>A closed polygon in seam coordinates: X runs along the seam from 0 to <paramref name="length"/>, Y is the
    /// offset across it. Its centre line zigzags ±Amplitude with the given period; it is BandThickness thick.</summary>
    public static IReadOnlyList<Pt> TornBand(int length)
    {
        var centre = new List<Pt>();
        for (double u = 0, i = 0; ; u += Period / 2, i++)
        {
            var x = Math.Min(u, length);
            centre.Add(new Pt(x, i % 2 == 0 ? -Amplitude : Amplitude));
            if (x >= length) break;
        }
        var half = BandThickness / 2;
        return [.. centre.Select(p => new Pt(p.X, p.Y - half)), .. centre.AsEnumerable().Reverse().Select(p => new Pt(p.X, p.Y + half))];
    }
}
