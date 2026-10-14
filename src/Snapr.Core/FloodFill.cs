using Snapr.Core.Stitching;

namespace Snapr.Core;

public static class FloodFill
{
    /// <summary>
    /// A copy of the frame where the 4-connected area around (x, y) whose A, R, G and B each differ from the clicked pixel
    /// by at most tolerance × 255 / 100 (rounded) is set to <paramref name="argb"/> (spec parity 4 §5).
    /// </summary>
    public static Frame Fill(Frame frame, int x, int y, uint argb, int tolerance)
    {
        int w = frame.Width, h = frame.Height;
        if (x < 0 || y < 0 || x >= w || y >= h) return frame;
        var src = frame.Pixels;
        var dst = (uint[])src.Clone();
        var seed = src[y * w + x];
        var limit = (int)Math.Round(Math.Clamp(tolerance, 0, 100) * 255 / 100.0, MidpointRounding.AwayFromZero);
        var seen = new bool[src.Length];
        var stack = new Stack<int>();
        stack.Push(y * w + x);
        seen[y * w + x] = true;
        while (stack.Count > 0)
        {
            var i = stack.Pop();
            dst[i] = argb;
            int cx = i % w, cy = i / w;
            if (cx > 0) Visit(i - 1);
            if (cx < w - 1) Visit(i + 1);
            if (cy > 0) Visit(i - w);
            if (cy < h - 1) Visit(i + w);
        }
        return frame with { Pixels = dst };

        void Visit(int j)
        {
            if (seen[j] || !Similar(src[j])) return;
            seen[j] = true;
            stack.Push(j);
        }

        bool Similar(uint p) =>
            Near(p >> 24, seed >> 24) && Near((p >> 16) & 255, (seed >> 16) & 255) && Near((p >> 8) & 255, (seed >> 8) & 255) && Near(p & 255, seed & 255);

        bool Near(uint a, uint b) => Math.Abs((int)a - (int)b) <= limit;
    }
}
