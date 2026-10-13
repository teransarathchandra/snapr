using System.Globalization;

namespace Snapr.Core.Recording;

/// <summary>Builds ffmpeg argument lists (passed via ProcessStartInfo.ArgumentList, so no quoting).</summary>
public static class FfmpegArgs
{
    static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public static IReadOnlyList<string> Grab(int screenX, int screenY, int width, int height, string output, int fps = 30)
    {
        int w = width & ~1, h = height & ~1;
        if (w < 2 || h < 2) throw new ArgumentException($"Recording area {width}×{height} is too small.");
        return
        [
            "-y", "-f", "gdigrab", "-framerate", N(fps),
            "-offset_x", N(screenX), "-offset_y", N(screenY), "-video_size", $"{w}x{h}",
            "-draw_mouse", "1", "-i", "desktop",
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-pix_fmt", "yuv420p", "-g", N(fps),
            // Fragmented MP4 stays playable even if ffmpeg is killed before it can finalise the file.
            "-movflags", "+frag_keyframe+empty_moov", "-flush_packets", "1", output,
        ];
    }

    public static IReadOnlyList<string> Concat(string listFile, string output) =>
        ["-y", "-f", "concat", "-safe", "0", "-i", listFile, "-c", "copy", output];

    public static string ConcatList(IEnumerable<string> segments) =>
        string.Concat(segments.Select(s => $"file '{s.Replace('\\', '/').Replace("'", "'\\''")}'\n"));

    /// <param name="audioTrimSeconds">Seconds to drop from the start of each audio file so it lines up with the video.</param>
    public static IReadOnlyList<string> Mux(string video, IReadOnlyList<string> audio, string output, double audioTrimSeconds = 0)
    {
        var a = new List<string> { "-y", "-i", video };
        foreach (var file in audio)
        {
            if (audioTrimSeconds > 0.01) a.AddRange(["-ss", N(audioTrimSeconds)]);
            a.AddRange(["-i", file]);
        }
        if (audio.Count == 0)
        {
            a.AddRange(["-c", "copy", output]);
            return a;
        }
        if (audio.Count == 1) a.AddRange(["-map", "0:v", "-map", "1:a"]);
        else a.AddRange([
            "-filter_complex",
            string.Concat(audio.Select((_, i) => $"[{i + 1}:a]")) + $"amix=inputs={audio.Count}:duration=longest:normalize=0[a]",
            "-map", "0:v", "-map", "[a]",
        ]);
        a.AddRange(["-c:v", "copy", "-c:a", "aac", "-b:a", "160k", "-shortest", output]);
        return a;
    }
}
