using System.Globalization;

namespace Snapr.Core;

public static class StepLabels
{
    /// <summary>"12" for numbers; letters continue like spreadsheet columns: 1→A, 26→Z, 27→AA.</summary>
    public static string Format(int number, StepStyle style)
    {
        if (style == StepStyle.Numbers || number < 1) return number.ToString(CultureInfo.InvariantCulture);
        var label = "";
        for (var n = number; n > 0; n = (n - 1) / 26) label = (char)('A' + (n - 1) % 26) + label;
        return label;
    }

    /// <summary>Font scale so a label of <paramref name="textWidth"/> fits within 80 % of a circle of <paramref name="size"/> (spec parity 16 §2.2).</summary>
    public static double FitScale(double size, double textWidth) => textWidth <= 0 ? 1 : Math.Min(1, 0.8 * size / textWidth);
}
