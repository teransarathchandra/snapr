namespace Snapr.Core;

public static class FileNames
{
    /// <summary>Replaces characters Windows does not allow in file names with '_'.</summary>
    public static string Clean(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    /// <summary>"stem.ext", or "stem (2).ext", "stem (3).ext", … — the first one <paramref name="exists"/> says is free.</summary>
    public static string Unique(string stem, string ext, Func<string, bool> exists)
    {
        var name = $"{stem}.{ext}";
        for (var n = 2; exists(name); n++) name = $"{stem} ({n}).{ext}";
        return name;
    }
}
