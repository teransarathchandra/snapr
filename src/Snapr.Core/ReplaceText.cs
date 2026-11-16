using Snapr.Core.Stitching;

namespace Snapr.Core;

/// <summary>Replace text (spec parity 22 §3): the pixel rules that hide an OCR line and match its colour.</summary>
public static class ReplaceText
{
    public const double Padding = 2;

    public static Rct LineBox(TextLine line) => line.Words.Aggregate(default(Rct), (r, w) => r.Union(w.Bounds));

    public static string LineText(TextLine line) => string.Join(" ", line.Words.Select(w => w.Text));

    public static TextLine Offset(TextLine line, double dx, double dy) =>
        new(line.Words.Select(w => w with { Bounds = w.Bounds.Offset(dx, dy) }).ToArray());

    public static Rct CoverRect(Rct line, int width, int height) => line.Inflate(Padding).Intersect(new Rct(0, 0, width, height));

    public static uint Background(Frame f, Rct line) =>
        CoverRect(line, f.Width, f.Height) is { IsEmpty: false } cover ? PixelEdits.EdgeColour(f, cover) : 0xFFFFFFFF;

    public static uint Ink(Frame f, Rct line, uint bg)
    {
        var pixels = Pixels(f, line).Select(c => (C: c, D: Distance(c, bg))).OrderByDescending(p => p.D).ToArray();
        if (pixels.Length == 0 || pixels[0].D == 0) return Luma(bg) >= 128 ? 0xFF000000 : 0xFFFFFFFF;
        var top = pixels.Take(Math.Max(1, pixels.Length / 10)).ToArray();
        uint Avg(int shift) => (uint)Math.Round(top.Average(p => (p.C >> shift) & 0xFF));
        return 0xFF000000 | Avg(16) << 16 | Avg(8) << 8 | Avg(0);
    }

    /// <summary>Bounding box of the ink pixels (farther from <paramref name="bg"/> than half the ink's distance); the line box when there are none.</summary>
    public static Rct InkBox(Frame f, Rct line, uint bg, uint ink)
    {
        var (x0, y0, x1, y1) = Span(f, line);
        var limit = Distance(ink, bg) / 4.0;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
                if (Distance(f.Pixels[y * f.Width + x], bg) > limit)
                {
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
        return maxX < 0 ? line : new Rct(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    public static int InkCount(Frame f, Rct box, uint bg, uint ink)
    {
        var limit = Distance(ink, bg) / 4.0;
        return limit == 0 ? 0 : Pixels(f, box).Count(c => Distance(c, bg) > limit);
    }

    public static string Hex(uint argb) => $"#{argb:X8}";

    public static (ShapeAnnotation Cover, TextAnnotation Text) Objects(Rct cover, uint bg, TextAnnotation text)
    {
        var group = Guid.NewGuid();
        return (new ShapeAnnotation(ShapeKind.Rectangle, cover, Hex(bg), 0, Hex(bg)) { GroupId = group }, text with { GroupId = group });
    }

    /// <summary>Adds the cover and the text with the typed words; blank words leave only the cover (the line is erased).</summary>
    public static DocumentState AddReplace(this DocumentState s, ShapeAnnotation cover, TextAnnotation text, string typed) =>
        s.Add(cover).Add(text).CommitText(text.Id, typed);

    /// <summary>Squared RGB distance.</summary>
    static int Distance(uint a, uint b)
    {
        int dr = (int)((a >> 16) & 0xFF) - (int)((b >> 16) & 0xFF), dg = (int)((a >> 8) & 0xFF) - (int)((b >> 8) & 0xFF), db = (int)(a & 0xFF) - (int)(b & 0xFF);
        return dr * dr + dg * dg + db * db;
    }

    static double Luma(uint c) => 0.299 * ((c >> 16) & 0xFF) + 0.587 * ((c >> 8) & 0xFF) + 0.114 * (c & 0xFF);

    static (int X0, int Y0, int X1, int Y1) Span(Frame f, Rct r) =>
        (Math.Max(0, (int)Math.Floor(r.X)), Math.Max(0, (int)Math.Floor(r.Y)),
         Math.Min(f.Width, (int)Math.Ceiling(r.Right)), Math.Min(f.Height, (int)Math.Ceiling(r.Bottom)));

    static IEnumerable<uint> Pixels(Frame f, Rct r)
    {
        var (x0, y0, x1, y1) = Span(f, r);
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
                yield return f.Pixels[y * f.Width + x];
    }
}
