using System.Text.RegularExpressions;

namespace Snapr.Core;

public enum RedactCategory { Email, WebAddress, CardNumber, Phone, Custom }

/// <summary>A recognised word and its box in image pixels.</summary>
public sealed record TextWord(string Text, Rct Bounds);

public sealed record TextLine(IReadOnlyList<TextWord> Words);

/// <summary>What Smart Redact looks for. Words: comma-separated words or phrases, case-insensitive.</summary>
public sealed record RedactOptions(bool Email = true, bool WebAddress = true, bool CardNumber = true, bool Phone = true, string Words = "");

/// <summary>One match: its text and the union of the boxes of the words it touches (matches never span lines).</summary>
public sealed record RedactMatch(RedactCategory Category, string Text, Rct Box);

/// <summary>Finds sensitive text in OCR lines (spec parity 5 §3). Pure; no OCR here.</summary>
public static class RedactFinder
{
    static readonly Regex EmailPattern = new(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant);
    static readonly Regex WebPattern = new(@"(?:https?://|www\.)\S+?(?=[.,;:)]*(?:\s|$))", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    static readonly Regex DigitRunPattern = new(@"(?<![\w+])\+?\d(?:[\d\s().\-/]*\d)?(?!\w)", RegexOptions.CultureInvariant);
    static readonly Regex CardRunPattern = new(@"(?<!\d)\d(?:[\d\s().\-/]*\d)?(?!\d)", RegexOptions.CultureInvariant);   // digit-only boundaries: letters touching a card must not hide it
    static readonly Regex DigitGroupPattern = new(@"\d+", RegexOptions.CultureInvariant);

    /// <summary>
    /// Spans inside a digit-run match made of whole digit groups with min..max digits: longest window first, then leftmost, then on to the right of each reported window.
    /// </summary>
    static IEnumerable<(int Start, int End)> Windows(Match run, int min, int max, Func<string, bool> accept)
    {
        var groups = DigitGroupPattern.Matches(run.Value);
        for (var from = 0; from < groups.Count;)
        {
            (int A, int B)? best = null;
            var bestDigits = 0;
            for (var a = from; a < groups.Count; a++)
                for (int b = a, digits = 0; b < groups.Count && (digits += groups[b].Length) <= max; b++)
                    if (digits >= min && digits > bestDigits && accept(string.Concat(Enumerable.Range(a, b - a + 1).Select(i => groups[i].Value))))
                    {
                        best = (a, b);
                        bestDigits = digits;
                    }
            if (best is not var (x, y)) yield break;
            yield return (run.Index + groups[x].Index, run.Index + groups[y].Index + groups[y].Length);
            from = y + 1;
        }
    }

    /// <summary>
    /// A phone run minus the card spans in it, as contiguous segments of digit groups. If the whole run (card groups included) has any 7-15 digit window of whole groups,
    /// every segment is returned whole, whatever its own length (over-covering, so no digit of the phone stays visible); otherwise none.
    /// </summary>
    static IEnumerable<(int Start, int End)> PhoneSegments(Match run, (int Start, int End, RedactCategory Category)[] cards)
    {
        var groups = DigitGroupPattern.Matches(run.Value).Select(g => (Start: run.Index + g.Index, End: run.Index + g.Index + g.Length)).ToArray();
        if (!Enumerable.Range(0, groups.Length).Any(a => Enumerable.Range(a, groups.Length - a).Any(b => groups[a..(b + 1)].Sum(x => x.End - x.Start) is >= 7 and <= 15))) yield break;
        var segment = new List<(int Start, int End)>();
        foreach (var g in groups.Append((Start: -1, End: -1)))   // sentinel flushes the last segment
        {
            if (g.Start >= 0 && !cards.Any(c => g.Start < c.End && c.Start < g.End))
            {
                segment.Add(g);
                continue;
            }
            if (segment.Count > 0) yield return (segment[0].Start, segment[^1].End);
            segment.Clear();
        }
    }

    public static IReadOnlyList<RedactMatch> Find(IReadOnlyList<TextLine> lines, RedactOptions options)
    {
        var terms = options.Words.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var found = new List<RedactMatch>();
        foreach (var line in lines)
        {
            if (line.Words.Count == 0) continue;
            var text = string.Join(' ', line.Words.Select(w => w.Text));
            var starts = new int[line.Words.Count];
            for (int i = 0, pos = 0; i < starts.Length; pos += line.Words[i].Text.Length + 1, i++) starts[i] = pos;

            var spans = new List<(int Start, int End, RedactCategory Category)>();
            void Add(int start, int end, RedactCategory category)
            {
                if (end > start && !spans.Any(x => start < x.End && x.Start < end)) spans.Add((start, end, category));
            }

            if (options.Email) foreach (Match m in EmailPattern.Matches(text)) Add(m.Index, m.Index + m.Length, RedactCategory.Email);
            if (options.WebAddress) foreach (Match m in WebPattern.Matches(text)) Add(m.Index, m.Index + m.Length, RedactCategory.WebAddress);
            if (options.CardNumber)
                foreach (Match m in CardRunPattern.Matches(text))
                    foreach (var (start, end) in Windows(m, 13, 19, Luhn)) Add(start, end, RedactCategory.CardNumber);
            if (options.Phone)
                foreach (Match m in DigitRunPattern.Matches(text))
                    foreach (var (start, end) in PhoneSegments(m, spans.Where(x => x.Category == RedactCategory.CardNumber).ToArray())) Add(start > 0 && text[start - 1] == '+' ? start - 1 : start, end, RedactCategory.Phone);
            foreach (var term in terms)
                for (var i = text.IndexOf(term, StringComparison.OrdinalIgnoreCase); i >= 0; i = text.IndexOf(term, i + term.Length, StringComparison.OrdinalIgnoreCase))
                    Add(i, i + term.Length, RedactCategory.Custom);

            foreach (var (start, end, category) in spans.OrderBy(x => x.Start))
            {
                var box = default(Rct);
                for (var i = 0; i < starts.Length; i++)
                    if (starts[i] < end && start < starts[i] + line.Words[i].Text.Length) box = box.Union(line.Words[i].Bounds);
                found.Add(new RedactMatch(category, text[start..end], box));
            }
        }
        return found;
    }

    /// <summary>The Luhn checksum used by payment card numbers.</summary>
    public static bool Luhn(string digits)
    {
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit)) return false;
        var sum = 0;
        var twice = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var d = digits[i] - '0';
            if (twice && (d *= 2) > 9) d -= 9;
            sum += d;
            twice = !twice;
        }
        return sum % 10 == 0;
    }
}
