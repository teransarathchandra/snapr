namespace Snapr.Core;

public static class CapturePaths
{
    static readonly string LocalRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Snapr");

    // Windows Defender Controlled Folder Access can block unknown apps from Pictures and Videos;
    // fall back to %LOCALAPPDATA% so captures still save. Once fallen back, stay there: probing the
    // blocked folder on every start would raise a Windows Security toast each time.
    static readonly Lazy<string> Pictures = new(() => Resolve(Environment.SpecialFolder.MyPictures, "Captures"));
    static readonly Lazy<string> Videos = new(() => Resolve(Environment.SpecialFolder.MyVideos, "Videos"));

    static string Resolve(Environment.SpecialFolder preferred, string fallbackName)
    {
        var fallback = Path.Combine(LocalRoot, fallbackName);
        return Directory.Exists(fallback) ? fallback : FirstWritable(Path.Combine(Environment.GetFolderPath(preferred), "Snapr"), fallback);
    }

    public static string PicturesRoot => Pictures.Value;
    public static string VideosRoot => Videos.Value;

    /// <summary>The first folder we can create and write a file in; the last candidate if none can be.</summary>
    public static string FirstWritable(params string[] candidates)
    {
        foreach (var dir in candidates)
        {
            try
            {
                Directory.CreateDirectory(dir);
                var probe = Path.Combine(dir, $".snapr-probe-{Guid.NewGuid():N}");
                File.WriteAllBytes(probe, []);
                File.Delete(probe);
                return dir;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return candidates[^1];
    }

    public static string NewCapturePath(string root, DateTime now) =>
        Unique(Path.Combine(root, now.ToString("yyyy-MM")), $"Snapr_{now:yyyyMMdd_HHmmss}", ".snapr");

    public static string NewVideoPath(string root, DateTime now) =>
        Unique(root, $"Snapr_{now:yyyyMMdd_HHmmss}", ".mp4");

    static string Unique(string dir, string stem, string ext)
    {
        var path = Path.Combine(dir, stem + ext);
        for (var i = 2; File.Exists(path); i++) path = Path.Combine(dir, $"{stem}_{i}{ext}");
        return path;
    }
}
