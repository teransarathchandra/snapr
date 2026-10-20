using System.Text.Json;
using System.Text.RegularExpressions;
using Snapr.Core;
using Snapr.Core.Video;

namespace Snapr.Tests;

public sealed class VideoEditFileTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("snapr-videoedits").FullName;
    string Video => Path.Combine(_dir, "clip.mp4");
    public void Dispose() => Directory.Delete(_dir, true);

    static VideoProject Sample()
    {
        var cut = VideoCut.For(10).WithTrim(1, 9).Remove(new TimeRange(4, 5));
        var arrow = new ArrowAnnotation(new Pt(1, 2), new Pt(30, 40), "#FFFF0000", 4);
        var text = new TextAnnotation(new Pt(5, 5), "Hi", "Segoe UI", 16, false, "#FFFFFFFF");
        return new VideoProject(cut, []).SetObjects([new VideoObject(arrow, 1, 4), new VideoObject(text, 6, 8)]);
    }

    [Fact]
    public void Round_trip_keeps_cut_and_objects()
    {
        var p = Sample();
        VideoEditFile.Save(Video, p);
        Assert.True(File.Exists(Video + ".snapr-edit.json"));
        Assert.False(File.Exists(Video + ".snapr-edit.json.tmp"));
        var (back, problem) = VideoEditFile.Load(Video, 10.2);
        Assert.Null(problem);
        Assert.Equal(p.Cut.Kept.Select(k => (k.Start, k.End)), back!.Cut.Kept.Select(k => (k.Start, k.End)));
        Assert.Equal(p.Objects.Select(o => (o.Item.Id, o.Item.GetType(), o.Start, o.End)), back.Objects.Select(o => (o.Item.Id, o.Item.GetType(), o.Start, o.End)));
    }

    [Fact]
    public void Missing_file_is_no_problem() => Assert.Equal((null, null), VideoEditFile.Load(Video, 10));

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"version":2,"duration":10,"trimStart":0,"trimEnd":10,"removed":[],"objects":[]}""")]
    [InlineData("""{"version":1,"duration":10,"trimStart":0,"trimEnd":10}""")]
    public void Bad_files_start_fresh(string json)
    {
        File.WriteAllText(VideoEditFile.PathFor(Video), json);
        Assert.Equal((null, VideoEditFile.Unreadable), VideoEditFile.Load(Video, 10));
    }

    [Theory]
    [InlineData("""{"version":1,"duration":1e309,"trimStart":0,"trimEnd":10,"removed":[],"objects":[]}""")]
    [InlineData("""{"version":1,"duration":10,"trimStart":0,"trimEnd":1e309,"removed":[],"objects":[]}""")]
    [InlineData("""{"version":1,"duration":10,"trimStart":0,"trimEnd":10,"removed":[{"start":1,"end":1e309}],"objects":[]}""")]
    public void Infinite_numbers_start_fresh(string json)
    {
        File.WriteAllText(VideoEditFile.PathFor(Video), json);
        Assert.Equal((null, VideoEditFile.Unreadable), VideoEditFile.Load(Video, 10));
    }

    [Fact]
    public void Infinite_object_end_starts_fresh()
    {
        VideoEditFile.Save(Video, Sample());
        var text = File.ReadAllText(VideoEditFile.PathFor(Video));
        var broken = Regex.Replace(text, "\"end\":\\s*4\\b", "\"end\": 1e309");
        Assert.NotEqual(text, broken);
        File.WriteAllText(VideoEditFile.PathFor(Video), broken);
        Assert.Equal((null, VideoEditFile.Unreadable), VideoEditFile.Load(Video, 10));
    }

    [Fact]
    public void Round_trip_keeps_every_annotation_kind()
    {
        Annotation[] items =
        [
            new PenAnnotation([new Pt(1, 2), new Pt(3, 4), new Pt(5, 6)], "#FF00FF00", 3),
            new ShapeAnnotation(ShapeKind.Ellipse, new Rct(1, 2, 30, 40), "#FFFF0000", 2, "#22FFFFFF", 5),
            new CalloutAnnotation(new Rct(1, 2, 30, 40), "Note", 14, "#FFFFFFFF", "#FF000000", "#FF000000", new Pt(50, 60)),
            new StepAnnotation(new Pt(10, 10), 3, 24, "#FF0000FF", "#FFFFFFFF", StepStyle.Letters),
            new StampAnnotation(new Rct(5, 5, 40, 40), "check", "#FF00AA00"),
        ];
        VideoEditFile.Save(Video, VideoProject.For(10).SetObjects(items.Select((a, i) => new VideoObject(a, i, i + 1)).ToArray()));
        var (back, problem) = VideoEditFile.Load(Video, 10);
        Assert.Null(problem);
        Assert.Equal(items.Length, back!.Objects.Count);
        for (var i = 0; i < items.Length; i++)
        {
            Assert.Equal(Json(items[i]), Json(back.Objects[i].Item));
            Assert.Equal((i, i + 1.0), (back.Objects[i].Start, back.Objects[i].End));
        }
    }

    static string Json(Annotation a) => JsonSerializer.Serialize<Annotation>(a, SnaprJson.Options);

    [Fact]
    public void Another_length_is_not_loaded()
    {
        VideoEditFile.Save(Video, Sample());
        Assert.Equal((null, VideoEditFile.Changed), VideoEditFile.Load(Video, 11));
    }
}
