namespace Snapr.Core.Stitching;

public enum PanoramaAdd { Added, Skipped, Dropped, Full }

/// <summary>Panoramic capture (spec parity 12 §2): each frame is matched to the last accepted one (coarse-to-fine grey
/// search), placed on a growing canvas, and composed newest-on-top. Uncovered canvas pixels are transparent.</summary>
public sealed class PanoramaStitcher(int maxSide = 32000, long maxPixels = 100_000_000, int maxFrames = 400)
{
    const double MinOverlap = 0.25;
    const double MaxScore = 6;
    const int MinDetail = 8;
    const int Refine = 4;

    const int Tile = 256;

    readonly Dictionary<(int TX, int TY), uint[]> _tiles = [];   // painted canvas, 256 × 256 tiles keyed by floor(canvas / 256)
    Frame? _last;        // the last accepted frame, its position and its pyramid: [full, 1/4, 1/16] (coarser levels only when big enough)
    int _lastX, _lastY, _count;
    Grey[] _prev = [];
    int _minX, _minY, _maxX, _maxY;

    public int Frames => _count;
    public int Width => _maxX - _minX;
    public int Height => _maxY - _minY;
    public bool LostParts { get; private set; }

    public PanoramaAdd Add(Frame frame)
    {
        if (_last is null)
        {
            if (maxFrames < 1 || frame.Width > maxSide || frame.Height > maxSide || (long)frame.Width * frame.Height > maxPixels)
            {
                LostParts = true;
                return PanoramaAdd.Full;
            }
            Paint(frame, 0, 0);
            (_last, _lastX, _lastY, _count) = (frame, 0, 0, 1);
            _prev = Pyramid(frame);
            (_minX, _minY, _maxX, _maxY) = (0, 0, frame.Width, frame.Height);
            return PanoramaAdd.Added;
        }
        if (frame.Width != _last.Width || frame.Height != _last.Height)
            throw new ArgumentException("All frames of a panorama must be the same size.", nameof(frame));
        if (frame.Pixels.AsSpan().SequenceEqual(_last.Pixels)) return PanoramaAdd.Skipped;

        var pyramid = Pyramid(frame);
        if (_count == 1 && IsBlank(_prev[0]))
        {
            // The first frame had nothing to match against (spec parity 20 §4.12): start over from this one.
            _tiles.Clear();
            Paint(frame, 0, 0);
            (_last, _lastX, _lastY, _prev) = (frame, 0, 0, pyramid);
            (_minX, _minY, _maxX, _maxY) = (0, 0, frame.Width, frame.Height);
            return PanoramaAdd.Added;
        }
        if (Match(_prev, pyramid) is not { } d)
        {
            LostParts = true;
            return PanoramaAdd.Dropped;
        }
        if (d == (0, 0))
        {
            // Changed without moving (a caret, a clock): newest pixels win, but it is not a new frame.
            Paint(frame, _lastX, _lastY);
            (_last, _prev) = (frame, pyramid);
            return PanoramaAdd.Skipped;
        }
        int x = _lastX + d.Dx, y = _lastY + d.Dy;
        int minX = Math.Min(_minX, x), minY = Math.Min(_minY, y), maxX = Math.Max(_maxX, x + frame.Width), maxY = Math.Max(_maxY, y + frame.Height);
        if (_count >= maxFrames || maxX - minX > maxSide || maxY - minY > maxSide || (long)(maxX - minX) * (maxY - minY) > maxPixels)
        {
            LostParts = true;
            return PanoramaAdd.Full;
        }
        Paint(frame, x, y);
        (_last, _lastX, _lastY, _prev) = (frame, x, y, pyramid);
        _count++;
        (_minX, _minY, _maxX, _maxY) = (minX, minY, maxX, maxY);
        return PanoramaAdd.Added;
    }

    public Frame Compose()
    {
        var px = new uint[(long)Width * Height];
        foreach (var ((tx, ty), tile) in _tiles)
        {
            int cx0 = Math.Max(tx * Tile, _minX), cx1 = Math.Min((tx + 1) * Tile, _maxX);
            int cy0 = Math.Max(ty * Tile, _minY), cy1 = Math.Min((ty + 1) * Tile, _maxY);
            for (var cy = cy0; cy < cy1; cy++)
                if (cx1 > cx0) Array.Copy(tile, (cy - ty * Tile) * Tile + (cx0 - tx * Tile), px, (long)(cy - _minY) * Width + (cx0 - _minX), cx1 - cx0);
        }
        return new Frame(Width, Height, px);
    }

    /// <summary>Copies the frame's rows into the tiles it covers (newer over older), creating zeroed tiles as needed.</summary>
    void Paint(Frame f, int x, int y)
    {
        for (var row = 0; row < f.Height; row++)
        {
            int cy = y + row, ty = cy >> 8;
            for (var cx = x; cx < x + f.Width;)
            {
                int tx = cx >> 8, n = Math.Min(x + f.Width, (tx + 1) * Tile) - cx;
                if (!_tiles.TryGetValue((tx, ty), out var tile)) _tiles[(tx, ty)] = tile = new uint[Tile * Tile];
                Array.Copy(f.Pixels, row * f.Width + (cx - x), tile, (cy & (Tile - 1)) * Tile + (cx & (Tile - 1)), n);
                cx += n;
            }
        }
    }

    // ---- Matching ----

    readonly record struct Grey(int W, int H, byte[] P);

    static bool IsBlank(Grey g) { byte lo = 255, hi = 0; foreach (var v in g.P) { if (v < lo) lo = v; if (v > hi) hi = v; } return hi - lo < 8; }

    static Grey[] Pyramid(Frame f)
    {
        var full = new byte[f.Width * f.Height];
        for (var i = 0; i < full.Length; i++)
        {
            var c = f.Pixels[i];
            full[i] = (byte)((((c >> 16) & 0xFF) * 77 + ((c >> 8) & 0xFF) * 150 + (c & 0xFF) * 29) >> 8);
        }
        var levels = new List<Grey> { new(f.Width, f.Height, full) };
        for (var k = 0; k < 2; k++)
        {
            var g = levels[^1];
            int w = g.W / 4, h = g.H / 4;
            if (w < 8 || h < 8) break;
            var p = new byte[w * h];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var sum = 0;
                    for (var yy = 0; yy < 4; yy++)
                        for (var xx = 0; xx < 4; xx++) sum += g.P[(y * 4 + yy) * g.W + x * 4 + xx];
                    p[y * w + x] = (byte)(sum / 16);
                }
            levels.Add(new Grey(w, h, p));
        }
        return [.. levels];
    }

    /// <summary>The offset (dx, dy) with b(x, y) ≈ a(x + dx, y + dy), or null when b has no detail or nothing matches well enough.</summary>
    static (int Dx, int Dy)? Match(Grey[] a, Grey[] b)
    {
        var top = Math.Min(a.Length, b.Length) - 1;
        var coarse = b[top];
        byte lo = 255, hi = 0;
        foreach (var v in coarse.P) { if (v < lo) lo = v; if (v > hi) hi = v; }
        if (hi - lo < MinDetail) return null;

        // Coarsest level: the whole range. Each finer level: ±Refine around the previous best ×4.
        int rx = coarse.W / 2, ry = coarse.H / 2;
        var (bx, by, _) = Best(a[top], coarse, -rx, rx, -ry, ry, step: 1);
        double score = double.MaxValue;
        for (var level = top - 1; level >= 0; level--)
        {
            int cx = bx * 4, cy = by * 4, lw = a[level].W / 2, lh = a[level].H / 2;
            (bx, by, score) = Best(a[level], b[level],
                Math.Max(-lw, cx - Refine), Math.Min(lw, cx + Refine), Math.Max(-lh, cy - Refine), Math.Min(lh, cy + Refine),
                step: level == 0 ? 2 : 1);
        }
        if (top == 0) (bx, by, score) = Best(a[0], b[0], bx, bx, by, by, step: 2);   // tiny frames: only the full level exists
        return score <= MaxScore ? (bx, by) : null;
    }

    static (int Dx, int Dy, double Score) Best(Grey a, Grey b, int x0, int x1, int y0, int y1, int step)
    {
        (int, int, double) best = (0, 0, double.MaxValue);
        for (var dy = y0; dy <= y1; dy++)
            for (var dx = x0; dx <= x1; dx++)
            {
                var s = Score(a, b, dx, dy, step);
                if (s < best.Item3 || s == best.Item3 && Math.Abs(dx) + Math.Abs(dy) < Math.Abs(best.Item1) + Math.Abs(best.Item2))
                    best = (dx, dy, s);
            }
        return best;
    }

    /// <summary>Mean absolute grey difference over the overlap of b with a shifted by (dx, dy); +∞ under 25 % overlap.</summary>
    static double Score(Grey a, Grey b, int dx, int dy, int step)
    {
        int x0 = Math.Max(0, -dx), x1 = Math.Min(b.W, a.W - dx), y0 = Math.Max(0, -dy), y1 = Math.Min(b.H, a.H - dy);
        if (x1 <= x0 || y1 <= y0 || (double)(x1 - x0) * (y1 - y0) < MinOverlap * b.W * b.H) return double.MaxValue;
        long sum = 0, n = 0;
        for (var y = y0; y < y1; y += step)
        {
            int rb = y * b.W, ra = (y + dy) * a.W + dx;
            for (var x = x0; x < x1; x += step)
            {
                sum += Math.Abs(b.P[rb + x] - a.P[ra + x]);
                n++;
            }
        }
        return (double)sum / n;
    }
}
