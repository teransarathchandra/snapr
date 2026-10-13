namespace Snapr.Core;

public static class ColorText
{
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
