namespace Snapr.Core.Stitching;

public static class Stitcher
{
    /// <param name="ignoreRightColumns">Columns at the right edge left out of comparisons (the scrollbar thumb moves).</param>
    /// <param name="ignoreLeftColumns">Columns at the left edge left out of comparisons: a window's semi-transparent
    /// border shows the unscrolled background behind it.</param>
    public static StitchResult Stitch(IReadOnlyList<Frame> frames, int ignoreRightColumns = 24, int maxHeight = 32000, int tolerance = 2,
        int ignoreLeftColumns = 4)
    {
        if (frames.Count == 0) throw new ArgumentException("No frames to stitch.", nameof(frames));
        int w = frames[0].Width, h = frames[0].Height;
        if (frames.Any(f => f.Width != w || f.Height != h))
            throw new ArgumentException("All frames must be the same size.", nameof(frames));

        var end = w > ignoreRightColumns ? w - ignoreRightColumns : w;
        var start = ignoreLeftColumns < end ? ignoreLeftColumns : 0;
        var cols = new Range(start, end);
        var hashes = frames.Select(f => RowHashes(f, cols)).ToArray();
        var header = CommonPrefix(hashes, h);
        var footer = header >= h ? 0 : CommonSuffix(hashes, h, header);
        var regionH = h - header - footer;

        var output = new List<uint>(w * h * Math.Min(frames.Count, 4));
        output.AddRange(frames[0].Pixels.AsSpan(0, w * (h - footer)).ToArray());
        var last = frames[0];
        var complete = true;

        for (var i = 1; i < frames.Count && regionH > 0; i++)
        {
            var prev = frames[i - 1];
            var cur = frames[i];
            if (hashes[i].AsSpan().SequenceEqual(hashes[i - 1])) break;

            var k = Overlap(prev, cur, hashes[i - 1], hashes[i], header, regionH, cols, tolerance);
            if (k == 0) { complete = false; break; }

            var newRows = regionH - k;
            if (output.Count / w + newRows + footer > maxHeight) { complete = false; break; }
            output.AddRange(cur.Pixels.AsSpan(w * (header + k), w * newRows).ToArray());
            last = cur;
        }

        output.AddRange(last.Pixels.AsSpan(w * (h - footer), w * footer).ToArray());
        if (output.Count / w > maxHeight)
        {
            output.RemoveRange(w * maxHeight, output.Count - w * maxHeight);
            complete = false;
        }
        return new StitchResult(new Frame(w, output.Count / w, output.ToArray()), complete);
    }

    /// <summary>Largest k such that prev's last k region rows equal cur's first k region rows; 0 if none.</summary>
    static int Overlap(Frame prev, Frame cur, ulong[] ph, ulong[] ch, int header, int regionH, Range cols, int tol)
    {
        for (var k = regionH - 1; k >= 1; k--)
        {
            var match = true;
            for (var r = 0; r < k && match; r++)
                match = ph[header + regionH - k + r] == ch[header + r];
            if (!match) continue;

            var verified = true;
            for (var r = 0; r < k && verified; r++)
                verified = RowsClose(prev.Row(header + regionH - k + r)[cols], cur.Row(header + r)[cols], tol);
            if (verified) return k;
        }
        return 0;
    }

    static bool RowsClose(ReadOnlySpan<uint> a, ReadOnlySpan<uint> b, int tol)
    {
        for (var i = 0; i < a.Length; i++)
        {
            uint x = a[i], y = b[i];
            if (x == y) continue;
            for (var shift = 0; shift < 32; shift += 8)
                if (Math.Abs((int)((x >> shift) & 0xFF) - (int)((y >> shift) & 0xFF)) > tol) return false;
        }
        return true;
    }

    static ulong[] RowHashes(Frame f, Range cols)
    {
        var hashes = new ulong[f.Height];
        for (var y = 0; y < f.Height; y++)
        {
            var h = 14695981039346656037UL;
            foreach (var px in f.Row(y)[cols]) h = (h ^ px) * 1099511628211UL;
            hashes[y] = h;
        }
        return hashes;
    }

    static int CommonPrefix(ulong[][] hashes, int h)
    {
        var n = 0;
        while (n < h && hashes.All(x => x[n] == hashes[0][n])) n++;
        return n;
    }

    static int CommonSuffix(ulong[][] hashes, int h, int header)
    {
        var n = 0;
        while (n < h - header && hashes.All(x => x[h - 1 - n] == hashes[0][h - 1 - n])) n++;
        return n;
    }
}
