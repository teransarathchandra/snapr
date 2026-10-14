using System.Globalization;

namespace Snapr.Core;

public static class ColorText
{
    /// <summary>Parses exactly "#AARRGGBB" into 0xAARRGGBB; false for anything else.</summary>
    public static bool TryArgb(string? color, out uint argb)
    {
        argb = 0;
        return color is { Length: 9 } && color[0] == '#'
            && uint.TryParse(color.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out argb);
    }

    /// <summary>Returns "#AARRGGBB" with the given alpha. Accepts "#RRGGBB" or "#AARRGGBB".</summary>
    public static string WithAlpha(string color, byte alpha)
    {
        var hex = color.TrimStart('#');
        var rgb = hex.Length switch
        {
            6 => hex,
            8 => hex[2..],
            _ => throw new FormatException($"Expected #RRGGBB or #AARRGGBB, got '{color}'."),
        };
        return $"#{alpha:X2}{rgb.ToUpperInvariant()}";
    }
}
