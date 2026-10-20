using System.Globalization;
using System.Text.RegularExpressions;

namespace Snapr.Core.Video;

public sealed record VideoInfo(double Duration, int Width, int Height, bool HasAudio);

/// <summary>Reads what <c>ffmpeg -i file</c> prints about a video (spec parity 8 §3).</summary>
public static partial class FfmpegProbe
{
    [GeneratedRegex(@"Duration:\s*(\d+):(\d{2}):(\d{2}(?:\.\d+)?)")] private static partial Regex DurationRx();
    [GeneratedRegex(@"Stream #\d+:\d+.*?: Video: .*?\b(\d{2,5})x(\d{2,5})\b")] private static partial Regex VideoRx();
    [GeneratedRegex(@"Stream #\d+:\d+.*?: Audio: ")] private static partial Regex AudioRx();

    public static VideoInfo? Parse(string stderr)
    {
        var d = DurationRx().Match(stderr);
        var v = VideoRx().Match(stderr);
        if (!d.Success || !v.Success) return null;
        var seconds = int.Parse(d.Groups[1].Value, CultureInfo.InvariantCulture) * 3600
            + int.Parse(d.Groups[2].Value, CultureInfo.InvariantCulture) * 60
            + double.Parse(d.Groups[3].Value, CultureInfo.InvariantCulture);
        return new VideoInfo(seconds, int.Parse(v.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(v.Groups[2].Value, CultureInfo.InvariantCulture), AudioRx().IsMatch(stderr));
    }
}

/// <summary>Reads ffmpeg's <c>-progress</c> lines.</summary>
public static class FfmpegProgress
{
    /// <returns>Seconds of output written so far, or null for any other line.</returns>
    public static double? Parse(string line)
    {
        var eq = line.IndexOf('=');
        if (eq < 0) return null;
        string key = line[..eq].Trim(), value = line[(eq + 1)..].Trim();
        if (key is "out_time_us" or "out_time_ms")   // both are microseconds
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var us) && us >= 0 ? us / 1_000_000.0 : null;
        if (key == "out_time" && TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var t) && t >= TimeSpan.Zero) return t.TotalSeconds;
        return null;
    }
}

/// <summary>One transparent picture drawn over the video between Start and End (source seconds) (spec parity 17 §6).</summary>
public sealed record VideoOverlayLayer(string Png, double Start, double End);

/// <summary>ffmpeg arguments for saving an edited video (spec parity 8 §3). Passed via ArgumentList, so no quoting.</summary>
public static class VideoExportArgs
{
    static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    static string Trims(IReadOnlyList<TimeRange> kept, bool audio, IReadOnlyList<string> video) => string.Concat(kept.Select((k, i) =>
        $"{video[i]}trim=start={N(k.Start)}:end={N(k.End)},setpts=PTS-STARTPTS[v{i}];"
        + (audio ? $"[0:a]atrim=start={N(k.Start)}:end={N(k.End)},asetpts=PTS-STARTPTS[a{i}];" : "")));

    /// <summary>The overlay chain: extra inputs, the graph before the trims, and the video label each kept range trims from.</summary>
    static (IReadOnlyList<string> Inputs, string Graph, IReadOnlyList<string> Video) Overlays(IReadOnlyList<VideoOverlayLayer>? overlays, int ranges)
    {
        if (overlays is not { Count: > 0 }) return ([], "", Enumerable.Repeat("[0:v]", ranges).ToArray());
        var inputs = overlays.SelectMany(o => new[] { "-i", o.Png }).ToArray();
        var graph = string.Concat(overlays.Select((o, i) =>
            $"{(i == 0 ? "[0:v]" : $"[o{i}]")}[{i + 1}:v]overlay=0:0:enable='gte(t,{N(o.Start)})*lt(t,{N(o.End)})'[o{i + 1}];"));
        var last = $"[o{overlays.Count}]";
        if (ranges == 1) return (inputs, graph, [last]);
        var labels = Enumerable.Range(0, ranges).Select(i => $"[r{i}]").ToArray();
        return (inputs, graph + $"{last}split={ranges}{string.Concat(labels)};", labels);
    }

    public static IReadOnlyList<string> Mp4(string input, IReadOnlyList<TimeRange> kept, bool hasAudio, string output, IReadOnlyList<VideoOverlayLayer>? overlays = null)
    {
        var (extra, graph, video) = Overlays(overlays, kept.Count);
        var inputs = string.Concat(kept.Select((_, i) => hasAudio ? $"[v{i}][a{i}]" : $"[v{i}]"));
        // libx264 needs even sizes, so the frame is rounded down to them after the concat
        var filter = graph + Trims(kept, hasAudio, video) + inputs + FormattableString.Invariant($"concat=n={kept.Count}:v=1:a={(hasAudio ? 1 : 0)}")
            + (hasAudio ? "[cv][a]" : "[cv]") + ";[cv]scale=trunc(iw/2)*2:trunc(ih/2)*2[v]";
        var a = new List<string> { "-y", "-i", input };
        a.AddRange(extra);
        a.AddRange(["-filter_complex", filter, "-map", "[v]"]);
        if (hasAudio) a.AddRange(["-map", "[a]"]);
        a.AddRange(["-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-pix_fmt", "yuv420p"]);
        if (hasAudio) a.AddRange(["-c:a", "aac", "-b:a", "160k"]);
        a.AddRange(["-movflags", "+faststart", "-progress", "pipe:1", "-nostats", "-f", "mp4", output]);
        return a;
    }

    /// <param name="width">Output width in pixels, or null to keep the source width.</param>
    public static IReadOnlyList<string> Gif(string input, IReadOnlyList<TimeRange> kept, int? width, int fps, string output, IReadOnlyList<VideoOverlayLayer>? overlays = null)
    {
        var (extra, graph, video) = Overlays(overlays, kept.Count);
        var inputs = string.Concat(kept.Select((_, i) => $"[v{i}]"));
        var scale = width is { } w ? FormattableString.Invariant($",scale={w}:-1:flags=lanczos") : "";
        var filter = graph + Trims(kept, false, video) + inputs + FormattableString.Invariant($"concat=n={kept.Count}:v=1:a=0[c];")
            + FormattableString.Invariant($"[c]fps={fps}{scale},split[s0][s1];") + "[s0]palettegen=stats_mode=diff[p];[s1][p]paletteuse=dither=bayer:bayer_scale=5[g]";
        return ["-y", "-i", input, .. extra, "-filter_complex", filter, "-map", "[g]", "-loop", "0", "-progress", "pipe:1", "-nostats", "-f", "gif", output];
    }
}
