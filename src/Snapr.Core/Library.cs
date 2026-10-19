using System.Globalization;

namespace Snapr.Core;

/// <summary>One file in the Library (spec parity 13 §3). Videos and unreadable captures have no meta.</summary>
public sealed record LibraryItem(string Path, bool IsVideo, DateTime ModifiedUtc, CaptureMeta? Meta, bool Unreadable)
{
    public IReadOnlyList<string> Tags => Meta?.Tags ?? [];
}

public enum LibraryKind { All, Images, Videos, Region, Window, Fullscreen, Scrolling, Panoramic, Clipboard, Imported }

public sealed record LibraryQuery(string Text, LibraryKind Kind, IReadOnlyList<string> Tags);

/// <summary>All captures and videos under the roots, newest first; capture info is cached by (path, modified time).</summary>
public sealed class LibraryIndex
{
    readonly Dictionary<string, (DateTime Modified, CaptureMeta? Meta)> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many capture files were read so far (diagnostic, for tests).</summary>
    public int Reads { get; private set; }

    public IReadOnlyList<LibraryItem> Scan(IEnumerable<string> roots)
    {
        var files = RecentsIndex.List(roots, int.MaxValue);
        var items = new LibraryItem[files.Count];
        for (var n = 0; n < files.Count; n++)
        {
            var f = files[n];
            if (f.IsVideo) { items[n] = new LibraryItem(f.Path, true, f.ModifiedUtc, null, false); continue; }
            var meta = _cache.TryGetValue(f.Path, out var c) && c.Modified == f.ModifiedUtc ? c.Meta : Read(f.Path, f.ModifiedUtc);
            items[n] = new LibraryItem(f.Path, false, f.ModifiedUtc, meta, meta is null);
        }
        var listed = files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var gone in _cache.Keys.Where(k => !listed.Contains(k)).ToArray()) _cache.Remove(gone);
        // Capture date, not file time: tagging rewrites the file but must not move an old capture to the top. OrderBy is stable.
        return items.OrderByDescending(i => i.Meta?.CapturedAt.UtcDateTime ?? i.ModifiedUtc).ToArray();
    }

    /// <summary>Reads one capture. Genuinely bad files are cached at this mtime; transient failures are not, so the next Scan retries.</summary>
    CaptureMeta? Read(string path, DateTime modifiedUtc)
    {
        Reads++;
        try
        {
            var meta = SnaprFile.ReadInfo(path).Meta;
            meta = meta with { Tags = TagRules.Clean(meta.Tags ?? []) };
            _cache[path] = (modifiedUtc, meta);
            return meta;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException)
        {
            Log.Write($"Library: couldn't read {path}", ex);
            if (ex is InvalidDataException or NotSupportedException) _cache[path] = (modifiedUtc, null);
            else _cache.Remove(path);
            return null;
        }
    }

    public void Forget(string path) => _cache.Remove(path);
}

public static class LibrarySearch
{
    static readonly StringComparer Same = StringComparer.OrdinalIgnoreCase;

    public static IReadOnlyList<LibraryItem> Filter(IEnumerable<LibraryItem> items, LibraryQuery q)
    {
        var words = q.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return items.Where(i => KindMatches(i, q.Kind)
            && q.Tags.All(t => i.Tags.Contains(t, Same))
            && words.All(w => Contains(System.IO.Path.GetFileName(i.Path), w) || Contains(i.Meta?.SourceTitle, w) || i.Tags.Any(t => Contains(t, w))))
            .ToArray();
    }

    static bool Contains(string? s, string w) => s?.Contains(w, StringComparison.OrdinalIgnoreCase) == true;

    static bool KindMatches(LibraryItem i, LibraryKind k) => k switch
    {
        LibraryKind.All => true,
        LibraryKind.Images => !i.IsVideo,
        LibraryKind.Videos => i.IsVideo,
        _ => i.Meta is { } m && m.Kind.ToString() == k.ToString(),
    };

    public static IReadOnlyList<(string Heading, IReadOnlyList<LibraryItem> Items)> Months(IReadOnlyList<LibraryItem> items, CultureInfo? culture = null)
    {
        var c = culture ?? CultureInfo.CurrentCulture;
        var groups = new List<(string, IReadOnlyList<LibraryItem>)>();
        List<LibraryItem>? current = null;
        (int, int) key = default;
        foreach (var i in items)
        {
            var at = i.Meta?.CapturedAt.ToLocalTime().DateTime ?? i.ModifiedUtc.ToLocalTime();
            if (current is null || key != (at.Year, at.Month))
            {
                key = (at.Year, at.Month);
                current = [];
                groups.Add((at.ToString("MMMM yyyy", c), current));
            }
            current.Add(i);
        }
        return groups;
    }

    public static IReadOnlyList<string> AllTags(IEnumerable<LibraryItem> items) =>
        items.SelectMany(i => i.Tags).Distinct(Same).Order(Same).ToArray();
}
