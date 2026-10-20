using System.Globalization;
using Snapr.Core;

namespace Snapr.App.Dialogs;

/// <summary>Resize dialog text rules (spec parity 16 §4.12).</summary>
public static class ResizeText
{
    /// <summary>The percentage both sides share at one decimal, or "" when they differ or don't parse.</summary>
    public static string Percent(int w0, int h0, string? width, string? height, CultureInfo culture)
    {
        if (!int.TryParse(width?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var w) || w <= 0
            || !int.TryParse(height?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var h) || h <= 0) return "";
        double pw = Math.Round(100.0 * w / w0, 1), ph = Math.Round(100.0 * h / h0, 1);
        return pw == ph ? pw.ToString("0.#", culture) : "";
    }
}
