using System.Windows.Media.Imaging;
using Snapr.App.Rendering;
using Snapr.Core;

namespace Snapr.App.Services;

public sealed record SimplifyOptions(bool Text, bool Blocks, bool PlainBackground, string BarColor)
{
    public static SimplifyOptions Default { get; } = new(true, true, false, "auto");
}

/// <summary>Simplify (spec parity 15 §4): blocks become flat boxes, text lines rounded bars, all in one group.</summary>
public static class SimplifyService
{
    public const string ViewChangedNotice = "The view changed while Simplify was working, so nothing was added. Run it again.";
    const string NoOcrNotice = "Windows has no text recognition language, so text was left as is.";

    /// <summary>The editor notice after a run, or null (spec parity 15 §4, parity 16 §3.7).</summary>
    public static string? Notice(SimplifyOptions o, bool noOcr, bool found) => (noOcr && o.Text, found) switch
    {
        (true, true) => NoOcrNotice,
        (true, false) when o.Blocks => "Windows has no text recognition language, so text was left as is, and Snapr found no blocks to simplify.",
        (true, false) => NoOcrNotice,
        (false, true) => null,
        (false, false) => "Snapr found nothing to simplify here.",
    };

    /// <summary>New shapes go under the existing objects (spec parity 16 §4.10).</summary>
    public static IReadOnlyList<Annotation> PlaceUnder(IReadOnlyList<Annotation> existing, IReadOnlyList<Annotation> shapes) => [.. shapes, .. existing];

    public static IReadOnlyList<Annotation> Build(BitmapSource visible, Rct area, IReadOnlyList<TextLine> lines, SimplifyOptions options)
    {
        var frame = ImageIo.ToFrame(visible);
        var whole = new Rct(0, 0, frame.Width, frame.Height);
        var background = PixelEdits.EdgeColour(frame, whole) | 0xFF000000u;
        var bars = options.Text ? lines.Select(SimplifyShapes.Bar).OfType<Rct>().ToArray() : [];
        var lineBoxes = lines.Select(l => l.Words.Count == 0 ? default : l.Words.Select(w => w.Bounds).Aggregate((p, q) => p.Union(q))).ToArray();
        var textBoxes = lineBoxes.Where(r => !r.IsEmpty).ToArray();
        var blocks = options.Blocks ? BlockFinder.Find(frame, whole, textBoxes) : [];
        if (bars.Length == 0 && blocks.Count == 0) return [];

        Rct Place(Rct r) => r.Offset(area.X, area.Y);
        static string Hex(uint c) => $"#{c:X8}";
        var shapes = new List<Annotation>();
        if (options.PlainBackground)
            shapes.Add(new ShapeAnnotation(ShapeKind.Rectangle, Place(whole), Hex(background), 0, Hex(background)));
        shapes.AddRange(blocks.Select(b => new ShapeAnnotation(ShapeKind.Rectangle, Place(b.Bounds), Hex(b.Color), 0, Hex(b.Color), 4)));
        foreach (var (line, box) in lines.Zip(lineBoxes))
        {
            if (!options.Text || SimplifyShapes.Bar(line) is not { } bar) continue;
            var centre = bar.Center;
            var under = blocks.LastOrDefault(b => b.Bounds.Contains(centre))?.Color ?? background;
            var fill = SimplifyShapes.BarColor(options.BarColor, under);
            if (!options.PlainBackground)   // Keep mode: hide the glyph tops/bottoms the 0.55-high bar leaves visible
            {
                var cover = Hex(under | 0xFF000000u);
                shapes.Add(new ShapeAnnotation(ShapeKind.Rectangle, Place(box.Inflate(1).Intersect(whole)), cover, 0, cover));
            }
            shapes.Add(new ShapeAnnotation(ShapeKind.Rectangle, Place(bar), fill, 0, fill, bar.H / 2));
        }
        var group = Guid.NewGuid();
        return shapes.Select(s => s with { GroupId = group }).ToArray();
    }
}
