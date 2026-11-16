namespace Snapr.Core;

/// <summary>The fonts offered for text objects (spec parity 22 §4).</summary>
public static class TextFonts
{
    public static IReadOnlyList<string> All { get; } = ["Segoe UI", "Arial", "Calibri", "Verdana", "Tahoma", "Times New Roman", "Georgia", "Consolas"];

    /// <summary>The list, with <paramref name="current"/> first when it isn't in it (older files, Snagit imports).</summary>
    public static IReadOnlyList<string> For(string current) => All.Contains(current) ? All : [current, .. All];
}
