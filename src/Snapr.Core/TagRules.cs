namespace Snapr.Core;

/// <summary>Capture tags (spec parity 13 §2): trimmed, no commas, at most 40 characters, at most 20, case-insensitively unique.</summary>
public static class TagRules
{
    public const int MaxTags = 20;
    public const int MaxLength = 40;
    static readonly StringComparer Same = StringComparer.OrdinalIgnoreCase;

    public static IReadOnlyList<string> Clean(IEnumerable<string?> tags)
    {
        var result = new List<string>();
        foreach (var raw in tags)
        {
            if (raw is null) continue;
            var t = raw.Replace(",", "").Trim();
            if (t.Length > MaxLength) t = Cut(t).Trim();
            if (t.Length == 0 || result.Contains(t, Same)) continue;
            result.Add(t);
            if (result.Count == MaxTags) break;
        }
        return result;
    }

    /// <summary>At most MaxLength UTF-16 units, ending on a whole text element (emoji, accented letters stay intact).</summary>
    static string Cut(string t)
    {
        var end = 0;
        foreach (var start in System.Globalization.StringInfo.ParseCombiningCharacters(t))
        {
            if (start > MaxLength) break;
            end = start;
        }
        // end = start of the last element beginning at or before MaxLength; include it only if it ends by MaxLength
        var elementEnd = System.Globalization.StringInfo.GetNextTextElementLength(t, end) + end;
        return t[..(elementEnd <= MaxLength ? elementEnd : end)];
    }

    /// <summary>The Tags dialog's text: tags separated by commas.</summary>
    public static IReadOnlyList<string> Parse(string text) => Clean(text.Split(','));

    /// <summary>Tags every list has, in the first list's order and spelling.</summary>
    public static IReadOnlyList<string> Shared(IEnumerable<IReadOnlyList<string>> lists)
    {
        var all = lists.ToArray();
        return all.Length == 0 ? [] : all[0].Where(t => all.All(l => l.Contains(t, Same))).Distinct(Same).ToArray();
    }

    /// <summary>One capture's new tags after the dialog: shared tags the user removed go, the edited tags are added.</summary>
    public static IReadOnlyList<string> Merge(IReadOnlyList<string> current, IReadOnlyList<string> shared, IReadOnlyList<string> edited)
    {
        var removed = shared.Where(s => !edited.Contains(s, Same)).ToArray();
        return Clean(current.Where(t => !removed.Contains(t, Same)).Concat(edited));
    }
}
