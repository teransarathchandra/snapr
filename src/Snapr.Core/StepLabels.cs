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
}
