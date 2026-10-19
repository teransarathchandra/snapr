using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Snapr.Core;

namespace Snapr.Tests;

public class SnaprFileTagsTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("snapr-tags").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    string Saved(IReadOnlyList<string>? tags = null)
    {
        var doc = new CaptureDocument
        {
            BasePng = SnaprFileTests.PngHeader(64, 48),
            PixelWidth = 64,
            PixelHeight = 48,
            Meta = new(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero), CaptureKind.Window, "Notepad", tags),
            State = ModelJsonTests.SampleState(),
        };
        var path = Path.Combine(_dir, $"{Guid.NewGuid():N}.snapr");
        SnaprFile.Save(doc, [1, 2, 3, 4], path);
        return path;
    }

    static byte[] Entry(string path, string name)
    {
        using var zip = ZipFile.OpenRead(path);
        using var s = zip.GetEntry(name)!.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Tags_round_trip_and_old_files_have_none()
    {
        Assert.Equal(["a", "b"], SnaprFile.Load(Saved(["a", "b"])).Meta.Tags);
        Assert.Null(SnaprFile.Load(Saved()).Meta.Tags);
    }

    [Fact]
    public void ReadInfo_matches_Load()
    {
        var path = Saved(["x"]);
        var info = SnaprFile.ReadInfo(path);
        var doc = SnaprFile.Load(path);
        Assert.Equal((doc.Meta.CapturedAt, doc.Meta.Kind, doc.Meta.SourceTitle), (info.Meta.CapturedAt, info.Meta.Kind, info.Meta.SourceTitle));
        Assert.Equal(["x"], info.Meta.Tags);
        Assert.Equal((64, 48), (info.PixelWidth, info.PixelHeight));
    }

    [Fact]
    public void ReadInfo_rejects_a_broken_file()
    {
        var path = Path.Combine(_dir, "bad.snapr");
        File.WriteAllBytes(path, [1, 2, 3]);
        Assert.Throws<InvalidDataException>(() => SnaprFile.ReadInfo(path));
        using (var zip = ZipFile.Open(Path.Combine(_dir, "nometa.snapr"), ZipArchiveMode.Create))
        using (var s = zip.CreateEntry("document.json").Open()) s.Write(Encoding.UTF8.GetBytes("""{"formatVersion":1}"""));
        Assert.Throws<InvalidDataException>(() => SnaprFile.ReadInfo(Path.Combine(_dir, "nometa.snapr")));
    }

    [Fact]
    public void SetTags_changes_only_the_tags()
    {
        var path = Saved(["old"]);
        var before = SnaprFile.Load(path);
        var (basePng, thumb) = (Entry(path, "base.png"), Entry(path, "thumbnail.png"));
        var json = JsonNode.Parse(Entry(path, "document.json"))!.AsObject();

        SnaprFile.SetTags(path, ["new", "two"]);

        var after = SnaprFile.Load(path);
        Assert.Equal(["new", "two"], after.Meta.Tags);
        Assert.Equal(basePng, Entry(path, "base.png"));
        Assert.Equal(thumb, Entry(path, "thumbnail.png"));
        var json2 = JsonNode.Parse(Entry(path, "document.json"))!.AsObject();
        json["meta"]!["tags"] = new JsonArray("new", "two");
        Assert.True(JsonNode.DeepEquals(json, json2));
        Assert.Equal(before.State.Annotations.Count, after.State.Annotations.Count);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void SetTags_with_an_empty_list_writes_an_empty_array()
    {
        var path = Saved(["a"]);
        SnaprFile.SetTags(path, []);
        Assert.Empty(SnaprFile.Load(path).Meta.Tags!);
    }

    [Fact]
    public void SetTags_keeps_unknown_fields()
    {
        var path = Saved();
        var root = JsonNode.Parse(Entry(path, "document.json"))!.AsObject();
        root["futureField"] = "keep me";
        root["meta"]!["futureMeta"] = 7;
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            zip.GetEntry("document.json")!.Delete();
            using var s = zip.CreateEntry("document.json").Open();
            s.Write(Encoding.UTF8.GetBytes(root.ToJsonString()));
        }
        SnaprFile.SetTags(path, ["t"]);
        var after = JsonNode.Parse(Entry(path, "document.json"))!;
        Assert.Equal("keep me", (string?)after["futureField"]);
        Assert.Equal(7, (int?)after["meta"]!["futureMeta"]);
        Assert.Equal(["t"], SnaprFile.Load(path).Meta.Tags);
    }

    [Fact]
    public void SetTags_without_document_json_throws_and_keeps_the_file()
    {
        var path = Path.Combine(_dir, "nodoc.snapr");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        using (var s = zip.CreateEntry("base.png").Open()) s.Write([1, 2, 3]);
        var bytes = File.ReadAllBytes(path);
        Assert.Throws<InvalidDataException>(() => SnaprFile.SetTags(path, ["x"]));
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void SetTags_failure_leaves_the_file_intact()
    {
        var path = Saved(["keep"]);
        var bytes = File.ReadAllBytes(path);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            Assert.ThrowsAny<Exception>(() => SnaprFile.SetTags(path, ["lost"]));
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }
}
