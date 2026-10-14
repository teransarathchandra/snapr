using System.IO.Compression;
using System.Text.Json;
using Snapr.Core;

namespace Snapr.Tests;

public sealed class SnaprFileTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("snapr-tests").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    /// <summary>PNG signature + an IHDR chunk header declaring w x h; the rest is not a decodable picture.</summary>
    internal static byte[] PngHeader(int w, int h) =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
        (byte)(w >> 24), (byte)(w >> 16), (byte)(w >> 8), (byte)w, (byte)(h >> 24), (byte)(h >> 16), (byte)(h >> 8), (byte)h,
    ];

    static CaptureDocument Doc(byte[]? png = null, int w = 640, int h = 480) => new()
    {
        BasePng = png ?? PngHeader(w, h),
        PixelWidth = w,
        PixelHeight = h,
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

    [Fact]
    public void Load_refuses_a_base_image_that_is_not_the_stated_size()
    {
        var path = Path.Combine(_dir, "liar.snapr");
        SnaprFile.Save(Doc(PngHeader(200, 100), 100, 50), [1], path);
        var ex = Assert.Throws<InvalidDataException>(() => SnaprFile.Load(path));
        Assert.Equal("The capture image does not match its stated size.", ex.Message);
    }

    [Fact]
    public void Load_refuses_a_base_image_that_is_not_a_png()
    {
        var path = Path.Combine(_dir, "junk.snapr");
        SnaprFile.Save(Doc([1, 2, 3]), [1], path);
        Assert.Throws<InvalidDataException>(() => SnaprFile.Load(path));
    }

    [Fact]
    public void Load_accepts_a_base_image_of_the_stated_size()
    {
        var path = Path.Combine(_dir, "ok.snapr");
        SnaprFile.Save(Doc(w: 100, h: 50), [1], path);
        var back = SnaprFile.Load(path);
        Assert.Equal((100, 50), (back.PixelWidth, back.PixelHeight));
    }
}
