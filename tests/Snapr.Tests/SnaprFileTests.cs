using System.IO.Compression;
using System.Text.Json;
using Snapr.Core;

namespace Snapr.Tests;

public sealed class SnaprFileTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("snapr-tests").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    static CaptureDocument Doc() => new()
    {
        BasePng = [1, 2, 3, 4],
        PixelWidth = 640,
        PixelHeight = 480,
        Dpi = 144,
        Meta = new(new DateTimeOffset(2026, 10, 13, 9, 30, 0, TimeSpan.Zero), CaptureKind.Window, "Notepad"),
        State = ModelJsonTests.SampleState(),
    };

    [Fact]
    public void Save_then_load_round_trips()
    {
        var doc = Doc();
        var path = Path.Combine(_dir, "a.snapr");
        SnaprFile.Save(doc, [9, 9], path);
        var back = SnaprFile.Load(path);

        Assert.Equal(doc.Id, back.Id);
        Assert.Equal(doc.BasePng, back.BasePng);
        Assert.Equal((640, 480, 144.0), (back.PixelWidth, back.PixelHeight, back.Dpi));
        Assert.Equal(doc.Meta, back.Meta);
        Assert.Equal(JsonSerializer.Serialize(doc.State, SnaprJson.Options), JsonSerializer.Serialize(back.State, SnaprJson.Options));
        Assert.Equal([9, 9], SnaprFile.ReadThumbnail(path));
    }

    [Fact]
    public void Archive_has_the_three_documented_entries()
    {
        var path = Path.Combine(_dir, "b.snapr");
        SnaprFile.Save(Doc(), [9], path);
        using var zip = ZipFile.OpenRead(path);
        Assert.Equal(["base.png", "document.json", "thumbnail.png"], zip.Entries.Select(e => e.FullName).Order());
    }

    [Fact]
    public void Saving_over_an_existing_file_replaces_it_and_leaves_no_temp_file()
    {
        var path = Path.Combine(_dir, "c.snapr");
        SnaprFile.Save(Doc(), [1], path);
        SnaprFile.Save(Doc(), [2], path);
        Assert.Equal([2], SnaprFile.ReadThumbnail(path));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public void Save_creates_missing_folders()
    {
        var path = Path.Combine(_dir, "2026-10", "d.snapr");
        SnaprFile.Save(Doc(), [1], path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Corrupt_file_throws_InvalidDataException()
    {
        var path = Path.Combine(_dir, "bad.snapr");
        File.WriteAllText(path, "not a zip");
        Assert.Throws<InvalidDataException>(() => SnaprFile.Load(path));
    }

    [Fact]
    public void Unsupported_version_throws_InvalidDataException()
    {
        var path = Path.Combine(_dir, "future.snapr");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry("document.json").Open()))
            w.Write("""{"formatVersion":2}""");
        Assert.Throws<InvalidDataException>(() => SnaprFile.Load(path));
    }
}
