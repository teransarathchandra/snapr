namespace Snapr.Core;

public enum StampShape { Path, Badge }

/// <summary>A built-in stamp. Path: WPF path markup in a 100 × 100 box (even-odd, one colour). Badge: the text on a coloured pill or circle.</summary>
public sealed record StampDef(string Id, string Name, StampShape Shape, string Data);

/// <summary>Snapr's own built-in stamps (spec parity 6 §2.1).</summary>
public static class StampCatalog
{
    static StampDef P(string id, string name, string data) => new(id, name, StampShape.Path, data);
    static StampDef B(string id, string name, string text) => new(id, name, StampShape.Badge, text);

    public static IReadOnlyList<StampDef> All { get; } =
    [
        P("check", "Check", "F0 M8,52 L22,38 L40,56 L78,18 L92,32 L40,84 Z"),
        P("cross", "Cross", "F0 M22,8 L50,36 L78,8 L92,22 L64,50 L92,78 L78,92 L50,64 L22,92 L8,78 L36,50 L8,22 Z"),
        P("warning", "Warning", "F0 M50,6 L96,90 L4,90 Z M45,34 L55,34 L53,64 L47,64 Z M45,72 L55,72 L55,82 L45,82 Z"),
        P("info", "Info", "F0 M50,4 A46,46 0 1 1 49.99,4 Z M44,42 L56,42 L56,78 L44,78 Z M44,22 L56,22 L56,34 L44,34 Z"),
        B("question", "Question", "?"),
        P("star", "Star", "F0 M50,4 L61.76,35.82 L95.65,37.17 L69.02,58.18 L78.21,90.83 L50,72 L21.79,90.83 L30.98,58.18 L4.35,37.17 L38.24,35.82 Z"),
        P("heart", "Heart", "F0 M50,90 L12,52 C0,40 4,14 26,12 C38,11 46,18 50,26 C54,18 62,11 74,12 C96,14 100,40 88,52 Z"),
        P("thumbs-up", "Thumbs up", "F0 M8,44 L24,44 L24,92 L8,92 Z M30,46 L46,20 C48,10 62,10 60,24 L56,40 L86,40 C94,40 96,48 92,54 C96,58 94,66 88,68 C92,72 90,80 84,82 C86,88 82,92 76,92 L30,92 Z"),
        P("thumbs-down", "Thumbs down", "F0 M8,56 L24,56 L24,8 L8,8 Z M30,54 L46,80 C48,90 62,90 60,76 L56,60 L86,60 C94,60 96,52 92,46 C96,42 94,34 88,32 C92,28 90,20 84,18 C86,12 82,8 76,8 L30,8 Z"),
        P("pin", "Pin", "F0 M50,96 C40,78 18,58 18,38 A32,32 0 0 1 82,38 C82,58 60,78 50,96 Z M50,24 A14,14 0 1 1 49.99,24 Z"),
        P("flag", "Flag", "F0 M14,6 L22,6 L22,96 L14,96 Z M22,10 L86,10 L72,30 L86,50 L22,50 Z"),
        P("lock", "Lock", "F0 M18,44 L82,44 L82,94 L18,94 Z M30,44 L30,30 A20,20 0 0 1 70,30 L70,44 L60,44 L60,30 A10,10 0 0 0 40,30 L40,44 Z M45,60 L55,60 L55,80 L45,80 Z"),
        P("bulb", "Idea", "F0 M36,70 C36,58 18,50 18,32 A32,32 0 0 1 82,32 C82,50 64,58 64,70 Z M38,76 L62,76 L62,84 L38,84 Z M42,88 L58,88 L56,96 L44,96 Z"),
        P("arrow-up", "Arrow up", "F0 M38,94 L38,44 L14,44 L50,6 L86,44 L62,44 L62,94 Z"),
        P("arrow-down", "Arrow down", "F0 M38,6 L38,56 L14,56 L50,94 L86,56 L62,56 L62,6 Z"),
        P("arrow-left", "Arrow left", "F0 M94,38 L44,38 L44,14 L6,50 L44,86 L44,62 L94,62 Z"),
        P("arrow-right", "Arrow right", "F0 M6,38 L56,38 L56,14 L94,50 L56,86 L56,62 L6,62 Z"),
        B("new", "New", "NEW"),
        B("todo", "To do", "TODO"),
        B("done", "Done", "DONE"),
        B("badge-1", "1", "1"),
        B("badge-2", "2", "2"),
        B("badge-3", "3", "3"),
        B("badge-4", "4", "4"),
        B("badge-5", "5", "5"),
    ];

    public static StampDef? Find(string id) => All.FirstOrDefault(s => s.Id == id);

    /// <summary>Width ÷ height of the stamp's natural box: square for drawings and one-character badges, wider for words.</summary>
    public static double Aspect(StampDef d) => d.Shape == StampShape.Badge && d.Data.Length > 1 ? 0.55 * d.Data.Length + 0.7 : 1;
}

/// <summary>Where a new stamp goes (spec parity 6 §2.4). Aspect = width ÷ height.</summary>
public static class StampPlacement
{
    public const double ClickSize = 64;

    /// <summary>A click: ClickSize on the longest side, centred on the point.</summary>
    public static Rct Centered(Pt center, double aspect)
    {
        var (w, h) = Size(ClickSize, aspect);
        return new Rct(center.X - w / 2, center.Y - h / 2, w, h);
    }

    /// <summary>A drag: the larger drag side becomes the longest side; the box grows from the press point toward the release point.</summary>
    public static Rct FromDrag(Pt down, Pt up, double aspect)
    {
        var (w, h) = Size(Math.Max(Math.Abs(up.X - down.X), Math.Abs(up.Y - down.Y)), aspect);
        return new Rct(up.X >= down.X ? down.X : down.X - w, up.Y >= down.Y ? down.Y : down.Y - h, w, h);
    }

    static (double W, double H) Size(double longest, double aspect) =>
        aspect >= 1 ? (longest, longest / aspect) : (longest * aspect, longest);
}
