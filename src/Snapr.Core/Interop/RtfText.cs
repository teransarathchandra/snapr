using System.Globalization;
using System.Text;

namespace Snapr.Core.Interop;

/// <summary>Extracts the plain text from RTF (as stored in .snagx text and callout objects). Formatting is dropped.</summary>
public static class RtfText
{
    static readonly HashSet<string> SkippedDestinations =
        ["fonttbl", "colortbl", "expandedcolortbl", "stylesheet", "info", "pict", "listtable", "listoverridetable"];

    public static string ToPlain(string rtf)
    {
        var text = new StringBuilder();
        var groups = new Stack<bool>();
        var skipping = false;
        var skipFallback = 0;   // characters to drop after \uN (the ANSI fallback)

        for (var i = 0; i < rtf.Length; i++)
        {
            var c = rtf[i];
            switch (c)
            {
                case '{':
                    groups.Push(skipping);
                    break;
                case '}':
                    skipping = groups.Count > 0 && groups.Pop();
                    break;
                case '\r' or '\n':
                    break;   // raw line breaks are only formatting
                case '\\' when i + 1 < rtf.Length:
                    i = ControlSequence(rtf, i, text, ref skipping, ref skipFallback);
                    break;
                default:
                    if (skipFallback > 0) skipFallback--;
                    else if (!skipping) text.Append(c);
                    break;
            }
        }
        return text.ToString().TrimEnd('\n');
    }

    /// <summary>Handles the control sequence starting at <paramref name="i"/>; returns the index of its last character.</summary>
    static int ControlSequence(string rtf, int i, StringBuilder text, ref bool skipping, ref int skipFallback)
    {
        var next = rtf[i + 1];
        switch (next)
        {
            case '\\' or '{' or '}':
                if (!skipping) text.Append(next);
                return i + 1;
            case '\'':
                if (i + 3 < rtf.Length && byte.TryParse(rtf.AsSpan(i + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
                {
                    if (skipFallback > 0) skipFallback--;
                    else if (!skipping) text.Append((char)b);
                }
                return i + 3;
            case '*':
                skipping = true;
                return i + 1;
            case '\n' or '\r':
                if (!skipping) text.Append('\n');   // Cocoa RTF writes a line break as a backslash before the newline
                return i + 1;
        }

        var j = i + 1;
        while (j < rtf.Length && char.IsAsciiLetter(rtf[j])) j++;
        var word = rtf[(i + 1)..j];
        var k = j;
        if (k < rtf.Length && (rtf[k] == '-' || char.IsAsciiDigit(rtf[k])))
        {
            k++;
            while (k < rtf.Length && char.IsAsciiDigit(rtf[k])) k++;
        }
        var parameter = rtf[j..k];
        if (k < rtf.Length && rtf[k] == ' ') k++;   // the space ending a control word belongs to it

        if (SkippedDestinations.Contains(word)) skipping = true;
        else if (!skipping)
        {
            if (word is "par" or "line") text.Append('\n');
            else if (word == "tab") text.Append('\t');
            else if (word == "u" && int.TryParse(parameter, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
            {
                text.Append((char)(code < 0 ? code + 65536 : code));
                skipFallback = 1;
            }
        }
        return k - 1;
    }
}
