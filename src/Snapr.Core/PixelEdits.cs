using Snapr.Core.Stitching;

namespace Snapr.Core;

/// <summary>The pixel work of the Selection tool's edits (spec parity 9 §2). Frames are 0xAARRGGBB, row-major.</summary>
public static class PixelEdits
{
    public static Frame Clear(Frame f, Rct r, uint argb)
    {
        var px = (uint[])f.Pixels.Clone();
        FillRect(px, f.Width, r, argb);
        return new Frame(f.Width, f.Height, px);
    }

    /// <summary>Copies Source, fills it, then pastes the copy at the offset; whatever lands outside the image is dropped.</summary>
    public static Frame Move(Frame f, Rct source, int dx, int dy, uint fillArgb)
    {
        int w = f.Width, h = f.Height, x0 = (int)source.X, y0 = (int)source.Y, sw = (int)source.W, sh = (int)source.H;
        var piece = new uint[sw * sh];
        for (var y = 0; y < sh; y++)
            Array.Copy(f.Pixels, (y0 + y) * w + x0, piece, y * sw, sw);
        var px = (uint[])f.Pixels.Clone();
        FillRect(px, w, source, fillArgb);
        for (var y = 0; y < sh; y++)
        {
            var ty = y0 + y + dy;
            if (ty < 0 || ty >= h) continue;
            for (var x = 0; x < sw; x++)
            {
                var tx = x0 + x + dx;
                if (tx >= 0 && tx < w) px[ty * w + tx] = piece[y * sw + x];
            }
        }
        return new Frame(w, h, px);
    }

    /// <summary>Most frequent colour on the rect's 1-px inner border (top row, right column, bottom row, left column); ties go to the first seen.</summary>
    public static uint EdgeColour(Frame f, Rct r)
    {
        int w = f.Width, x0 = (int)r.X, y0 = (int)r.Y, x1 = x0 + (int)r.W - 1, y1 = y0 + (int)r.H - 1;
        var counts = new Dictionary<uint, (int Count, int First)>();
        var order = 0;
        void See(int x, int y)
        {
            var c = f.Pixels[y * w + x];
            counts[c] = counts.TryGetValue(c, out var e) ? (e.Count + 1, e.First) : (1, order);
            order++;
        }
        for (var x = x0; x <= x1; x++) See(x, y0);
        for (var y = y0; y <= y1; y++) See(x1, y);
        for (var x = x0; x <= x1; x++) See(x, y1);
        for (var y = y0; y <= y1; y++) See(x0, y);
        return counts.OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Value.First).First().Key;
    }

    /// <summary>True when the source overlaps any object; touching edges do not count.</summary>
    public static bool TouchesObjects(Rct source, IEnumerable<Annotation> annotations) =>
        annotations.Any(a => !source.Intersect(AnnotationGeometry.Bounds(a)).IsEmpty);

    public static Frame Apply(Frame f, MovePixelsEdit m) => Move(f, m.Source, m.Dx, m.Dy, FillOf(f, m.Source, m.Fill));

    public static Frame Apply(Frame f, ClearAreaEdit c) => Clear(f, c.Area, FillOf(f, c.Area, c.Fill));

    static uint FillOf(Frame f, Rct r, string? fill) =>
        fill is not null && ColorText.TryArgb(fill, out var argb) ? argb : EdgeColour(f, r);

    static void FillRect(uint[] px, int w, Rct r, uint argb)
    {
        int x0 = (int)r.X, y0 = (int)r.Y, rw = (int)r.W, rh = (int)r.H;
        for (var y = y0; y < y0 + rh; y++) Array.Fill(px, argb, y * w + x0, rw);
    }
}

/// <summary>The pixel rectangle a Selection-tool drag selects (spec parity 9 §2).</summary>
public static class SelectionRect
{
    /// <returns>Whole pixels (floor/ceil) clamped to the image, or null when smaller than 2 × 2.</returns>
    public static Rct? FromDrag(Pt a, Pt b, int width, int height)
    {
        int x0 = (int)Math.Clamp(Math.Floor(Math.Min(a.X, b.X)), 0, width), x1 = (int)Math.Clamp(Math.Ceiling(Math.Max(a.X, b.X)), 0, width);
        int y0 = (int)Math.Clamp(Math.Floor(Math.Min(a.Y, b.Y)), 0, height), y1 = (int)Math.Clamp(Math.Ceiling(Math.Max(a.Y, b.Y)), 0, height);
        return x1 - x0 >= 2 && y1 - y0 >= 2 ? new Rct(x0, y0, x1 - x0, y1 - y0) : null;
    }
}
