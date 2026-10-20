using Snapr.Core.Stitching;

namespace Snapr.Core;

/// <summary>A flat UI block found by Simplify (spec parity 15 §2). Depth 0 = top level, 1 = inside a top-level block.</summary>
public sealed record Block(Rct Bounds, uint Color, int Depth);

/// <summary>Finds solid UI blocks as connected regions that differ from the background (spec parity 15 §2).</summary>
public static class BlockFinder
{
    public const int Tolerance = 24, MinSize = 12, MaxBlocks = 300;
    const long DownscaleAbove = 4_000_000;
    const double MinFill = 0.5, MaxTextOverlap = 0.5, MaxInner = 0.9;

    sealed record Component(int X, int Y, int W, int H, uint Color);

    public static IReadOnlyList<Block> Find(Frame frame, Rct area, IReadOnlyList<Rct> textBoxes)
    {
        var a = area.Intersect(new Rct(0, 0, frame.Width, frame.Height));
        int ax = (int)a.X, ay = (int)a.Y, aw = (int)a.W, ah = (int)a.H;
        if (aw <= 0 || ah <= 0) return [];
        var background = PixelEdits.EdgeColour(frame, new Rct(ax, ay, aw, ah));
        var scale = (long)aw * ah > DownscaleAbove ? 2 : 1;
        var work = scale == 1 ? Crop(frame, ax, ay, aw, ah) : Half(frame, ax, ay, aw, ah);
        var min = (MinSize + scale - 1) / scale;

        // Text boxes in work coordinates.
        var text = textBoxes.Select(t => new Rct((t.X - ax) / scale, (t.Y - ay) / scale, t.W / scale, t.H / scale)).ToArray();

        var found = new List<(Component C, int Depth, int Parent)>();
        foreach (var top in Components(work, 0, 0, work.Width, work.Height, background, min, text))
        {
            var parent = found.Count;
            found.Add((top, 0, parent));
            var limit = MaxInner * top.W * top.H;
            foreach (var inner in Components(work, top.X, top.Y, top.W, top.H, top.Color, min, text))
                if ((double)inner.W * inner.H <= limit) found.Add((inner, 1, parent));
        }
        var kept = found.Count <= MaxBlocks ? found : found.OrderByDescending(f => (long)f.C.W * f.C.H).Take(MaxBlocks).ToList();
        // Parent-major drawing order: a top-level block sitting in a larger block's hole is drawn after that block's inner boxes.
        var tops = kept.Where(f => f.Depth == 0).OrderByDescending(f => (long)f.C.W * f.C.H).ToList();
        return tops
            .SelectMany(t => kept.Where(f => f.Depth == 1 && f.Parent == t.Parent).OrderByDescending(f => (long)f.C.W * f.C.H).Prepend(t))
            .Select(f => new Block(ToImage(f.C, scale, ax, ay, a), f.C.Color, f.Depth))
            .ToArray();
    }

    static Rct ToImage(Component c, int scale, int ax, int ay, Rct area) =>
        new Rct(ax + c.X * scale, ay + c.Y * scale, c.W * scale, c.H * scale).Intersect(area);

    /// <summary>Components inside the box (bx, by, bw, bh) of pixels differing from <paramref name="background"/>,
    /// in discovery order (row by row), filtered by size, fill and text overlap.</summary>
    static List<Component> Components(Frame f, int bx, int by, int bw, int bh, uint background, int min, Rct[] text)
    {
        var result = new List<Component>();
        var seen = new bool[bw * bh];
        var stack = new Stack<int>();
        for (var y = 0; y < bh; y++)
            for (var x = 0; x < bw; x++)
            {
                var start = y * bw + x;
                if (seen[start] || !Differs(f.Pixels[(by + y) * f.Width + bx + x], background)) continue;
                int minX = x, maxX = x, minY = y, maxY = y, count = 0;
                var counts = new Dictionary<uint, (int Count, int First, uint Exact)>();
                seen[start] = true;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    var i = stack.Pop();
                    int px = i % bw, py = i / bw;
                    count++;
                    if (px < minX) minX = px;
                    if (px > maxX) maxX = px;
                    if (py < minY) minY = py;
                    if (py > maxY) maxY = py;
                    var c = f.Pixels[(by + py) * f.Width + bx + px];
                    var key = (c & 0x00F8F8F8u) | 0xFF000000u;
                    // Ties and the exact colour follow row-by-row order (smallest index), not flood-fill order.
                    var idx = py * bw + px;
                    counts[key] = !counts.TryGetValue(key, out var e) ? (1, idx, c | 0xFF000000u)
                        : idx < e.First ? (e.Count + 1, idx, c | 0xFF000000u) : (e.Count + 1, e.First, e.Exact);
                    for (var k = 0; k < 4; k++)
                    {
                        int nx = px + Dx[k], ny = py + Dy[k];
                        if (nx < 0 || ny < 0 || nx >= bw || ny >= bh) continue;
                        var n = ny * bw + nx;
                        if (seen[n] || !Differs(f.Pixels[(by + ny) * f.Width + bx + nx], background)) continue;
                        seen[n] = true;
                        stack.Push(n);
                    }
                }
                int w = maxX - minX + 1, h = maxY - minY + 1;
                if (w < min || h < min || count < MinFill * w * h) continue;
                var box = new Rct(bx + minX, by + minY, w, h);
                if (text.Any(t => Area(t.Intersect(box)) >= MaxTextOverlap * w * h)) continue;
                var colour = counts.Values.OrderByDescending(v => v.Count).ThenBy(v => v.First).First().Exact;
                result.Add(new Component(bx + minX, by + minY, w, h, colour));
            }
        return result;
    }

    static readonly int[] Dx = [-1, 1, 0, 0], Dy = [0, 0, -1, 1];

    static double Area(Rct r) => r.IsEmpty ? 0 : r.W * r.H;

    static bool Differs(uint a, uint b) =>
        Math.Abs((int)((a >> 16) & 0xFF) - (int)((b >> 16) & 0xFF)) > Tolerance
        || Math.Abs((int)((a >> 8) & 0xFF) - (int)((b >> 8) & 0xFF)) > Tolerance
        || Math.Abs((int)(a & 0xFF) - (int)(b & 0xFF)) > Tolerance;

    static Frame Crop(Frame f, int x, int y, int w, int h)
    {
        var px = new uint[w * h];
        for (var r = 0; r < h; r++) Array.Copy(f.Pixels, (y + r) * f.Width + x, px, r * w, w);
        return new Frame(w, h, px);
    }

    /// <summary>2 × 2 box average of the area (odd last row/column dropped).</summary>
    static Frame Half(Frame f, int x, int y, int w, int h)
    {
        int hw = w / 2, hh = h / 2;
        var px = new uint[hw * hh];
        for (var r = 0; r < hh; r++)
            for (var c = 0; c < hw; c++)
            {
                uint a = 0, rr = 0, g = 0, b = 0;
                for (var dy = 0; dy < 2; dy++)
                    for (var dx = 0; dx < 2; dx++)
                    {
                        var p = f.Pixels[(y + r * 2 + dy) * f.Width + x + c * 2 + dx];
                        a += p >> 24; rr += (p >> 16) & 0xFF; g += (p >> 8) & 0xFF; b += p & 0xFF;
                    }
                px[r * hw + c] = (a / 4) << 24 | (rr / 4) << 16 | (g / 4) << 8 | b / 4;
            }
        return new Frame(hw, hh, px);
    }
}

/// <summary>Text bars and their colour (spec parity 15 §3).</summary>
public static class SimplifyShapes
{
    public static Rct? Bar(TextLine line)
    {
        if (line.Words.Count == 0) return null;
        var u = line.Words.Select(w => w.Bounds).Aggregate((p, q) => p.Union(q));
        var h = Math.Max(2, Math.Round(0.55 * u.H, MidpointRounding.AwayFromZero));
        return new Rct(u.X, u.Y + (u.H - h) / 2, u.W, h);
    }

    public static double Luminance(uint c) => 0.2126 * ((c >> 16) & 0xFF) + 0.7152 * ((c >> 8) & 0xFF) + 0.0722 * (c & 0xFF);

    public static string BarColor(string chosen, uint under)
    {
        if (chosen != "auto") return chosen;
        var lum = Luminance(under);
        var light = lum >= 127.5;
        var bar = light ? 0xFF9CA3AFu : 0xFF6B7280u;
        if (Math.Abs(Luminance(bar) - lum) < 40) bar = light ? 0xFF111111u : 0xFFE5E7EBu;
        return $"#{bar:X8}";
    }
}
