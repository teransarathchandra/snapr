namespace Snapr.Core;

public sealed record RecentItem(string Path, bool IsVideo, DateTime ModifiedUtc);

/// <summary>The recents strip is just the newest files on disk; there is no database.</summary>
public static class RecentsIndex
{
    public static IReadOnlyList<RecentItem> List(IEnumerable<string> roots, int max = 30) =>
        roots.Where(Directory.Exists)
            .SelectMany(r => Directory.EnumerateFiles(r, "*.*", SearchOption.AllDirectories))
            .Select(System.IO.Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(p => IsCapture(p) || IsVideo(p))
            .Select(p => new RecentItem(p, IsVideo(p), File.GetLastWriteTimeUtc(p)))
            .OrderByDescending(i => i.ModifiedUtc)
            .Take(max)
            .ToArray();

    static bool IsCapture(string p) => p.EndsWith(".snapr", StringComparison.OrdinalIgnoreCase);
    static bool IsVideo(string p) => p.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase);
}
