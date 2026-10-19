using Snapr.Core.Interop;

namespace Snapr.Core;

public enum CombineLayout { SideBySide, Stacked, Grid }

public enum CombineAlign { Start, Centre, End }

public sealed record CombineResult(int Width, int Height, IReadOnlyList<Rct> Places);

/// <summary>Lays out pictures for Combine images (spec parity 10 §2): a grid whose cells are as wide as their column and as tall as their row.</summary>
public static class CombineArrange
{
    public const int MaxPictures = 20;
    public const int MaxGap = 200;
    const int MaxSide = 32000;

    public static CombineResult Arrange(IReadOnlyList<(int W, int H)> sizes, CombineLayout layout, CombineAlign align, int gap)
    {
        var n = sizes.Count;
        if (n is < 2 or > MaxPictures) throw new ArgumentException($"Combine needs 2 to {MaxPictures} pictures.", nameof(sizes));
        if (gap is < 0 or > MaxGap) throw new ArgumentException($"The gap must be 0 to {MaxGap}.", nameof(gap));
        if (sizes.Any(s => s.W < 1 || s.H < 1)) throw new ArgumentException("Every picture needs a size.", nameof(sizes));

        var cols = layout switch
        {
            CombineLayout.SideBySide => n,
            CombineLayout.Stacked => 1,
            _ => (int)Math.Ceiling(Math.Sqrt(n)),
        };
        var rows = (n + cols - 1) / cols;
        var colW = new long[cols];
        var rowH = new long[rows];
        for (var i = 0; i < n; i++)
        {
            colW[i % cols] = Math.Max(colW[i % cols], sizes[i].W);
            rowH[i / cols] = Math.Max(rowH[i / cols], sizes[i].H);
        }
        var colX = new long[cols];
        var rowY = new long[rows];
        for (var c = 0; c < cols; c++) colX[c] = c == 0 ? gap : colX[c - 1] + colW[c - 1] + gap;
        for (var r = 0; r < rows; r++) rowY[r] = r == 0 ? gap : rowY[r - 1] + rowH[r - 1] + gap;

        static long Offset(long cell, int size, CombineAlign a) => a switch
        {
            CombineAlign.Start => 0,
            CombineAlign.End => cell - size,
            _ => (cell - size) / 2,
        };

        var places = new Rct[n];
        for (var i = 0; i < n; i++)
        {
            int c = i % cols, r = i / cols;
            var (w, h) = sizes[i];
            places[i] = new Rct(colX[c] + Offset(colW[c], w, align), rowY[r] + Offset(rowH[r], h, align), w, h);
        }
        var width = colW.Sum() + (long)gap * (cols + 1);
        var height = rowH.Sum() + (long)gap * (rows + 1);
        // Sizes are at most 20 × int.MaxValue, so the clamp only matters for absurd inputs; TooLarge rejects those.
        return new CombineResult((int)Math.Min(width, int.MaxValue), (int)Math.Min(height, int.MaxValue), places);
    }

    public static bool TooLarge(CombineResult r) =>
        r.Width > MaxSide || r.Height > MaxSide || (long)r.Width * r.Height > SnagxReader.MaxTotalPixels;
}

/// <summary>The captures picked in the recents strip, in pick order (spec parity 10 §3).</summary>
public sealed class PickList
{
    readonly List<string> _paths = [];

    public IReadOnlyList<string> Paths => _paths;
    public int Count => _paths.Count;

    /// <summary>Adds or removes the path. Returns false, changing nothing, only when adding would pass MaxPictures.</summary>
    public bool Toggle(string path)
    {
        var i = _paths.FindIndex(p => Same(p, path));
        if (i >= 0)
        {
            _paths.RemoveAt(i);
            return true;
        }
        if (_paths.Count >= CombineArrange.MaxPictures) return false;
        _paths.Add(path);
        return true;
    }

    /// <summary>1-based pick number, or 0 when not picked.</summary>
    public int NumberOf(string path) => _paths.FindIndex(p => Same(p, path)) + 1;

    public void Clear() => _paths.Clear();

    public void Retain(IEnumerable<string> existing)
    {
        var keep = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        _paths.RemoveAll(p => !keep.Contains(p));
    }

    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
