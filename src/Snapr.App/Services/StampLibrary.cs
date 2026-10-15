using System.IO;
using Snapr.App.Rendering;
using Snapr.Core;

namespace Snapr.App.Services;

/// <summary>The user's own stamps: PNG files in one folder (spec parity 6 §2.3).</summary>
public sealed class StampLibrary(string folder)
{
    public static string DefaultFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Snapr", "Stamps");

    public IReadOnlyList<string> List() =>
        Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.png").OrderBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase).ToArray()
            : [];

    /// <summary>Copies a picture into the folder as PNG. Throws (and writes nothing) if it can't be read or is too large.</summary>
    public string Add(string sourcePath)
    {
        var png = ImageIo.EncodePng(ImageIo.ToBgra(ImageObjects.Load(sourcePath)));
        Directory.CreateDirectory(folder);
        var name = FileNames.Unique(FileNames.Clean(Path.GetFileNameWithoutExtension(sourcePath)), "png", n => File.Exists(Path.Combine(folder, n)));
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, png);
        return path;
    }

    public void Remove(string path) => File.Delete(path);
}
