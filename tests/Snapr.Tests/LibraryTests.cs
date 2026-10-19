using System.Globalization;
using Snapr.Core;

namespace Snapr.Tests;

public class LibraryTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("snapr-lib").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    static readonly DateTime T0 = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    string Capture(string name, DateTime modifiedUtc, CaptureKind kind = CaptureKind.Region, string? title = null, IReadOnlyList<string>? tags = null)
    {
        var path = Path.Combine(_dir, name);
        SnaprFile.Save(new CaptureDocument
        {
            BasePng = SnaprFileTests.PngHeader(10, 10), PixelWidth = 10, PixelHeight = 10,
            Meta = new(new DateTimeOffset(modifiedUtc), kind, title, tags),
        }, [0], path);
        File.SetLastWriteTimeUtc(path, modifiedUtc);
        return path;
    }

    static LibraryItem Item(string name, CaptureKind kind = CaptureKind.Region, string? title = null, string[]? tags = null, bool video = false, DateTime? at = null) =>
        new(@"C:\caps\" + name, video, at ?? T0, video ? null : new CaptureMeta(new DateTimeOffset(at ?? T0), kind, title, tags), false);

    [Fact]
    public void Scan_lists_captures_and_videos_newest_first()
    {
        Capture("old.snapr", T0.AddDays(-2));
        Capture("new.snapr", T0, tags: ["x"]);
        File.WriteAllBytes(Path.Combine(_dir, "bad.snapr"), [1, 2, 3]);
        File.SetLastWriteTimeUtc(Path.Combine(_dir, "bad.snapr"), T0.AddDays(-1));
        File.WriteAllBytes(Path.Combine(_dir, "clip.mp4"), []);
        File.SetLastWriteTimeUtc(Path.Combine(_dir, "clip.mp4"), T0.AddDays(-3));
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "ignored");

        var items = new LibraryIndex().Scan([_dir]);

        Assert.Equal(["new.snapr", "bad.snapr", "old.snapr", "clip.mp4"], items.Select(i => Path.GetFileName(i.Path)));
        Assert.Equal(["x"], items[0].Tags);
        Assert.True(items[1].Unreadable);
        Assert.Null(items[1].Meta);
        Assert.True(items[3].IsVideo);
        Assert.Null(items[3].Meta);
    }

    [Fact]
    public void Order_is_by_capture_date_not_file_time()
    {
        var p1 = Capture("a.snapr", T0.AddDays(-5));
        var p2 = Capture("b.snapr", T0.AddDays(-1));
        File.SetLastWriteTimeUtc(p1, T0);   // a was captured earlier but touched last
        File.SetLastWriteTimeUtc(p2, T0.AddDays(-2));

        var items = new LibraryIndex().Scan([_dir]);

        Assert.Equal(["b.snapr", "a.snapr"], items.Select(i => Path.GetFileName(i.Path)));
    }

    [Fact]
    public void Scan_reads_only_new_or_changed_files_again()
    {
        var a = Capture("a.snapr", T0);
        Capture("b.snapr", T0.AddDays(-1));
        var index = new LibraryIndex();
        index.Scan([_dir]);
        Assert.Equal(2, index.Reads);
        index.Scan([_dir]);
        Assert.Equal(2, index.Reads);
        SnaprFile.SetTags(a, ["t"]);
        File.SetLastWriteTimeUtc(a, T0.AddMinutes(1));
        var items = index.Scan([_dir]);
        Assert.Equal(3, index.Reads);
        Assert.Equal(["t"], items[0].Tags);
        index.Forget(a);
        index.Scan([_dir]);
        Assert.Equal(4, index.Reads);
    }

    [Fact]
    public void Scan_forgets_deleted_files()
    {
        var a = Capture("a.snapr", T0);
        Capture("b.snapr", T0.AddDays(-1));
        var index = new LibraryIndex();
        index.Scan([_dir]);
        Assert.Equal(2, index.Reads);
        var bytes = File.ReadAllBytes(a);
        File.Delete(a);
        Assert.Equal(["b.snapr"], Names(index.Scan([_dir])));
        File.WriteAllBytes(a, bytes);
        File.SetLastWriteTimeUtc(a, T0);
        index.Scan([_dir]);
        Assert.Equal(3, index.Reads);
    }

    [Fact]
    public void Hand_edited_tags_are_cleaned()
    {
        var a = Capture("a.snapr", T0);
        SnaprFile.SetTags(a, [" Work", "work", "", "UI "]);
        var items = new LibraryIndex().Scan([_dir]);
        Assert.Equal(["Work", "UI"], items[0].Tags);
    }

    [Fact]
    public void Locked_file_is_unreadable_for_now_and_retried_next_scan()
    {
        var a = Capture("a.snapr", T0);
        var index = new LibraryIndex();
        using (var locked = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.True(index.Scan([_dir])[0].Unreadable);
        }
        Assert.False(index.Scan([_dir])[0].Unreadable);
        Assert.Equal(2, index.Reads);
    }

    [Fact]
    public void Text_words_must_all_match_name_title_or_tag_ignoring_case()
    {
        LibraryItem[] items =
        [
            Item("invoice march.snapr"),
            Item("x.snapr", title: "Invoice - Outlook"),
            Item("y.snapr", tags: ["Invoice", "march"]),
            Item("z.snapr", title: "Notepad"),
        ];
        Assert.Equal(["invoice march.snapr", "x.snapr", "y.snapr"], Names(LibrarySearch.Filter(items, new("INVOICE", LibraryKind.All, []))));
        Assert.Equal(["invoice march.snapr", "y.snapr"], Names(LibrarySearch.Filter(items, new("invoice  march", LibraryKind.All, []))));
        Assert.Equal(4, LibrarySearch.Filter(items, new("  ", LibraryKind.All, [])).Count);
    }

    [Fact]
    public void Kind_filters()
    {
        LibraryItem[] items =
        [
            Item("r.snapr", CaptureKind.Region),
            Item("p.snapr", CaptureKind.Panoramic),
            Item("v.mp4", video: true),
            new(@"C:\caps\bad.snapr", false, T0, null, true),
        ];
        Assert.Equal(4, LibrarySearch.Filter(items, new("", LibraryKind.All, [])).Count);
        Assert.Equal(["r.snapr", "p.snapr", "bad.snapr"], Names(LibrarySearch.Filter(items, new("", LibraryKind.Images, []))));
        Assert.Equal(["v.mp4"], Names(LibrarySearch.Filter(items, new("", LibraryKind.Videos, []))));
        Assert.Equal(["p.snapr"], Names(LibrarySearch.Filter(items, new("", LibraryKind.Panoramic, []))));
        Assert.Empty(LibrarySearch.Filter(items, new("", LibraryKind.Window, [])));
    }

    [Fact]
    public void Tag_filter_needs_every_tag_and_never_matches_videos()
    {
        LibraryItem[] items =
        [
            Item("a.snapr", tags: ["Work", "UI"]),
            Item("b.snapr", tags: ["work"]),
            Item("v.mp4", video: true),
        ];
        Assert.Equal(["a.snapr", "b.snapr"], Names(LibrarySearch.Filter(items, new("", LibraryKind.All, ["work"]))));
        Assert.Equal(["a.snapr"], Names(LibrarySearch.Filter(items, new("", LibraryKind.All, ["work", "ui"]))));
    }

    [Fact]
    public void Months_groups_consecutive_items_with_culture_headings()
    {
        LibraryItem[] items =
        [
            Item("a.snapr", at: new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc)),
            Item("b.snapr", at: new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc)),
            Item("v.mp4", video: true, at: new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc)),
        ];
        var groups = LibrarySearch.Months(items, CultureInfo.GetCultureInfo("en-US"));
        Assert.Equal(["October 2026", "September 2026"], groups.Select(g => g.Heading));
        Assert.Equal([2, 1], groups.Select(g => g.Items.Count));
    }

    [Fact]
    public void AllTags_is_distinct_and_sorted() =>
        Assert.Equal(["bug", "UI", "work"], LibrarySearch.AllTags([Item("a", tags: ["work", "UI"]), Item("b", tags: ["Work", "bug"]), Item("v.mp4", video: true)]));

    static IEnumerable<string> Names(IEnumerable<LibraryItem> items) => items.Select(i => Path.GetFileName(i.Path));
}
