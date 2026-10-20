namespace Snapr.App.Editor;

/// <summary>The editor's colour swatches with spoken names (spec parity 16 §2.3).</summary>
public static class EditorPalette
{
    public static IReadOnlyList<(string Color, string Name)> Swatches { get; } =
    [
        ("#FFFF3B30", "Red"), ("#FFFF9500", "Orange"), ("#FFFFCC00", "Yellow"), ("#FF34C759", "Green"),
        ("#FF3B82F6", "Blue"), ("#FFAF52DE", "Purple"), ("#FF111111", "Black"), ("#FFFFFFFF", "White"),
    ];

    public static string[] Colors { get; } = Swatches.Select(s => s.Color).ToArray();

    /// <summary>The swatch name of a colour, else the colour text itself.</summary>
    public static string Name(string color) =>
        Swatches.FirstOrDefault(s => string.Equals(s.Color, color, StringComparison.OrdinalIgnoreCase)).Name ?? color;
}
