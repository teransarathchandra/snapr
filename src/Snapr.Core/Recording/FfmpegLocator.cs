namespace Snapr.Core.Recording;

public static class FfmpegLocator
{
    /// <summary>The configured path if it exists, else the first ffmpeg.exe on PATH, else null.</summary>
    public static string? Find(string? configured, string? pathVariable)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        foreach (var dir in (pathVariable ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(dir.Trim('"'), "ffmpeg.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
