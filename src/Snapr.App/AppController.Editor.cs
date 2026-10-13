using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Snapr.App.Editor;
using Snapr.App.Rendering;
using Snapr.Core;

namespace Snapr.App;

public sealed partial class AppController
{
    EditorWindow? _editor;
    EditorWindow Editor => _editor ??= new EditorWindow(this);

    public void ShowEditor() => Editor.ShowLatest();

    public void OpenImageFromDialog()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Images and Snapr files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.snapr|All files|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dialog.ShowDialog() == true) OpenImageFile(dialog.FileName);
    }

    /// <summary>Opens a .snapr in place, or imports any other image as a new capture.</summary>
    public void OpenImageFile(string path)
    {
        try
        {
            if (path.EndsWith(".snapr", StringComparison.OrdinalIgnoreCase))
            {
                Editor.Open(SnaprFile.Load(path), Path.GetFullPath(path), saveNow: false);
                return;
            }
            var frame = BitmapDecoder.Create(new Uri(Path.GetFullPath(path)), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            OpenNewCapture(frame, CaptureKind.Region, Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Write($"Open failed: {path}", ex);
            MessageBox.Show($"Snapr couldn't open {Path.GetFileName(path)}.\n\n{ex.Message}", "Snapr", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Wraps a captured bitmap in a new document, autosaves it and opens it in the editor.</summary>
    internal void OpenNewCapture(BitmapSource image, CaptureKind kind, string? title)
    {
        var bgra = ImageIo.ToBgra(image);
        var doc = new CaptureDocument
        {
            BasePng = ImageIo.EncodePng(bgra),
            PixelWidth = bgra.PixelWidth,
            PixelHeight = bgra.PixelHeight,
            Dpi = image.DpiX > 0 ? image.DpiX : 96,
            Meta = new CaptureMeta(DateTimeOffset.Now, kind, title),
        };
        Editor.Open(doc, CapturePaths.NewCapturePath(CapturePaths.PicturesRoot, DateTime.Now), saveNow: true);
    }
}
