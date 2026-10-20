using System.Text.Json;

namespace Snapr.Core.Video;

/// <summary>A video's cuts and objects, saved next to it as "clip.mp4.snapr-edit.json" (spec parity 17 §3). The video is never changed.</summary>
public static class VideoEditFile
{
    public const string Unreadable = "Snapr couldn't read the saved edits for this video, so it starts fresh.";
    public const string Changed = "This video changed since it was edited, so its saved edits weren't loaded.";
    const int Version = 1;
    const double DurationTolerance = 0.5;

    internal sealed record FileDto(int Version, double Duration, double TrimStart, double TrimEnd, IReadOnlyList<TimeRange>? Removed, IReadOnlyList<ObjectDto>? Objects);
    internal sealed record ObjectDto(double Start, double End, Annotation? Item);

    public static string PathFor(string videoPath) => videoPath + ".snapr-edit.json";

    static bool Finite(params double[] values) => values.All(double.IsFinite);

    /// <summary>Writes through a temp file then a move, so a crash never leaves half a file.</summary>
    public static void Save(string videoPath, VideoProject p)
    {
        var path = PathFor(videoPath);
        var tmp = path + ".tmp";
        var c = p.Cut;
        var dto = new FileDto(Version, c.Duration, c.TrimStart, c.TrimEnd, c.Removed, p.Objects.Select(o => new ObjectDto(o.Start, o.End, o.Item)).ToArray());
        try
        {
            File.WriteAllBytes(tmp, JsonSerializer.SerializeToUtf8Bytes(dto, SnaprJson.Options));
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>(null, null) without a file; (null, problem) for a bad file or another video's edits; else the project.</summary>
    public static (VideoProject? Project, string? Problem) Load(string videoPath, double duration)
    {
        var path = PathFor(videoPath);
        if (!File.Exists(path)) return (null, null);
        FileDto? d;
        try { d = JsonSerializer.Deserialize<FileDto>(File.ReadAllBytes(path), SnaprJson.Options); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Write($"Couldn't read video edits {path}", ex);
            return (null, Unreadable);
        }
        if (d is null || d.Version != Version || d.Removed is null || d.Objects is null || d.Objects.Any(o => o?.Item is null)
            || !Finite(d.Duration, d.TrimStart, d.TrimEnd) || d.Removed.Any(r => !Finite(r.Start, r.End)) || d.Objects.Any(o => !Finite(o.Start, o.End)))
            return (null, Unreadable);
        if (Math.Abs(d.Duration - duration) > DurationTolerance) return (null, Changed);
        var cut = VideoCut.For(duration).WithTrim(d.TrimStart, d.TrimEnd);
        foreach (var r in d.Removed) cut = cut.Remove(r);
        return (new VideoProject(cut, []).SetObjects(d.Objects.Select(o => new VideoObject(o.Item!, o.Start, o.End)).ToArray()), null);
    }
}
