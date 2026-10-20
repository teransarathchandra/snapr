using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapr.App.Rendering;
using Snapr.App.Services;

namespace Snapr.Tests;

public sealed class StampLibraryTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("snapr-stamps").FullName;
    string Lib => Path.Combine(_dir, "Stamps");
    public void Dispose() => Directory.Delete(_dir, true);

    string Picture(string name)
    {
        var bmp = BitmapSource.Create(4, 3, 96, 96, PixelFormats.Bgra32, null, new byte[4 * 3 * 4], 16);
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, ImageIo.EncodePng(bmp));
        return path;
    }

    [Fact]
    public void Lists_in_explorer_order() => Sta.Run(() =>
    {
        var lib = new StampLibrary(Lib);
        Directory.CreateDirectory(Lib);
        foreach (var n in new[] { "Logo (10).png", "logo (2).png", "arrow.png" }) File.Copy(Picture("src-" + n), Path.Combine(Lib, n));
        Assert.Equal(["arrow", "logo (2)", "Logo (10)"], lib.List().Select(Path.GetFileNameWithoutExtension));
    });

    [Fact]
    public void Missing_folder_lists_nothing() => Sta.Run(() =>
    {
        Assert.Empty(new StampLibrary(Lib).List());
    });

    [Fact]
    public void Add_copies_as_png_with_unique_names() => Sta.Run(() =>
    {
        var lib = new StampLibrary(Lib);
        var first = lib.Add(Picture("logo.png"));
        var second = lib.Add(Picture("logo.png"));
        Assert.Equal("logo.png", Path.GetFileName(first));
        Assert.Equal("logo (2).png", Path.GetFileName(second));
        Assert.Equal([first, second], lib.List());
        Assert.Equal(4, BitmapFrame.Create(new MemoryStream(File.ReadAllBytes(first))).PixelWidth);
    });

    [Fact]
    public void Add_rejects_non_image_and_leaves_nothing() => Sta.Run(() =>
    {
        var fake = Path.Combine(_dir, "notes.png");
        File.WriteAllText(fake, "not a picture");
        var lib = new StampLibrary(Lib);
        Assert.ThrowsAny<Exception>(() => { lib.Add(fake); });
        Assert.Empty(lib.List());
    });

    [Fact]
    public void Remove_deletes_the_file() => Sta.Run(() =>
    {
        var lib = new StampLibrary(Lib);
        var path = lib.Add(Picture("a.png"));
        lib.Remove(path);
        Assert.Empty(lib.List());
    });
}
