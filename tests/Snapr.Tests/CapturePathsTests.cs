using Snapr.Core;

namespace Snapr.Tests;

public sealed class CapturePathsTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("snapr-paths").FullName;
    public void Dispose() => Directory.Delete(_dir, true);
    static readonly DateTime Now = new(2026, 10, 13, 14, 5, 9);

    [Fact]
    public void Capture_path_uses_month_folder_and_timestamp() =>
        Assert.Equal(Path.Combine(_dir, "2026-10", "Snapr_20261013_140509.snapr"), CapturePaths.NewCapturePath(_dir, Now));

    [Fact]
    public void Same_second_gets_a_suffix()
    {
        var first = CapturePaths.NewCapturePath(_dir, Now);
        Directory.CreateDirectory(Path.GetDirectoryName(first)!);
        File.WriteAllText(first, "x");
        Assert.Equal(Path.Combine(_dir, "2026-10", "Snapr_20261013_140509_2.snapr"), CapturePaths.NewCapturePath(_dir, Now));
    }

    [Fact]
    public void Video_path_has_no_month_folder() =>
        Assert.Equal(Path.Combine(_dir, "Snapr_20261013_140509.mp4"), CapturePaths.NewVideoPath(_dir, Now));

    [Fact]
    public void Recents_are_newest_first_capped_and_filtered()
    {
        var month = Directory.CreateDirectory(Path.Combine(_dir, "2026-10")).FullName;
        for (var i = 0; i < 5; i++)
        {
            var p = Path.Combine(month, $"c{i}.snapr");
            File.WriteAllText(p, "x");
            File.SetLastWriteTimeUtc(p, new DateTime(2026, 10, 1 + i, 0, 0, 0, DateTimeKind.Utc));
        }
        var video = Path.Combine(_dir, "v.mp4");
        File.WriteAllText(video, "x");
        File.SetLastWriteTimeUtc(video, new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc));
        File.WriteAllText(Path.Combine(month, "ignored.txt"), "x");
        File.WriteAllText(Path.Combine(month, "half.snapr.tmp"), "x");

        var items = RecentsIndex.List([_dir, Path.Combine(_dir, "missing")], max: 3);

        Assert.Equal(["v.mp4", "c4.snapr", "c3.snapr"], items.Select(i => Path.GetFileName(i.Path)));
        Assert.True(items[0].IsVideo);
        Assert.False(items[1].IsVideo);
    }

    [Fact]
    public void Overlapping_roots_do_not_duplicate_items()
    {
        File.WriteAllText(Path.Combine(_dir, "a.snapr"), "x");
        Assert.Single(RecentsIndex.List([_dir, _dir]));
    }

    [Fact]
    public void Unwritable_folder_falls_back_to_the_next_candidate()
    {
        var file = Path.Combine(_dir, "not-a-folder");
        File.WriteAllText(file, "x");
        var fallback = Path.Combine(_dir, "fallback");
        Assert.Equal(fallback, CapturePaths.FirstWritable(Path.Combine(file, "Snapr"), fallback));
        Assert.Empty(Directory.GetFiles(fallback));
    }
}
