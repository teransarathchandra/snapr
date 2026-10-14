using System.Globalization;
using System.Text.RegularExpressions;

namespace Snapr.Core.Interop;

/// <summary>Parses the value formats used inside Snagit .snagx JSON.</summary>
public static partial class SnagxValues
{
    /// <summary>"x,y" in invariant culture; null for anything else.</summary>
    public static Pt? Point(string? text)
    {
        var parts = text?.Split(',');
        if (parts is not { Length: 2 }) return null;
        return double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                && double.IsFinite(x) && double.IsFinite(y)
                    ? new Pt(x, y)
                    : null;
    }

    /// <summary>"#AARRGGBB" (or "#RRGGBB", taken as opaque) as upper-case "#AARRGGBB"; null if not a colour.</summary>
    public static string? Color(string? text)
    {
        if (text is null || !HexColour().IsMatch(text)) return null;
        var hex = text[1..].ToUpperInvariant();
        return "#" + (hex.Length == 6 ? "FF" + hex : hex);
    }

    public static byte Alpha(string color) => Convert.ToByte(color.Substring(1, 2), 16);

    [GeneratedRegex(@"^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})\z")]
    private static partial Regex HexColour();
}
