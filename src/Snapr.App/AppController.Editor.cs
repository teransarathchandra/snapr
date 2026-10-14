using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Snapr.App.Editor;
using Snapr.App.Rendering;
using Snapr.App.Services;
using Snapr.Core;
using Snapr.Core.Interop;

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
            Filter = "Images, Snapr and Snagit files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.snapr;*.snagx|Snagit files|*.snagx|All files|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dialog.ShowDialog() == true) OpenImageFile(dialog.FileName);
    }

    /// <summary>Opens a .snapr in place, imports a Snagit .snagx, or imports any other image as a new capture.</summary>
    public void OpenImageFile(string path)
    {
        var snagx = path.EndsWith(".snagx", StringComparison.OrdinalIgnoreCase);
        try
        {
            if (path.EndsWith(".snapr", StringComparison.OrdinalIgnoreCase))
            {
                var full = Path.GetFullPath(path);
                if (_editor?.IsShowing(full) == true)   // reloading from disk would show a stale copy and the next autosave would lose recent edits
                {
                    _editor.ShowLatest();
                    return;
                }
                Editor.Open(SnaprFile.Load(path), full, saveNow: false);
                return;
            }
            if (snagx)
            {
                ImportSnagx(path);
                return;
            }
            var frame = BitmapDecoder.Create(new Uri(Path.GetFullPath(path)), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            OpenNewCapture(frame, CaptureKind.Region, Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Write($"Open failed: {path}", ex);
            MessageBox.Show($"Snapr couldn't {(snagx ? "import" : "open")} {Path.GetFileName(path)}.\n\n{ex.Message}", "Snapr",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Converts a Snagit .snagx into a new capture. The .snagx itself is never modified.</summary>
    void ImportSnagx(string path)
    {
        SnagxPage page;
        using (var stream = File.OpenRead(path)) page = SnagxReader.Read(stream);
        var notice = page.Report.IsEmpty ? "Imported from Snagit." : $"Imported from Snagit: {page.Report.Summary()}.";
        OpenNewCapture(SnagxImport.ComposeBase(page), CaptureKind.Imported, Path.GetFileName(path), page.CapturedAt, page.Annotations, notice);
    }

    /// <summary>Opens the clipboard image as a new capture (Ctrl+Shift+N, or Ctrl+V when no Snapr object is on the clipboard).</summary>
    public void OpenFromClipboard()
    {
        BitmapSource? image = null;
        ClipboardImage.Try(() => image = Clipboard.ContainsImage() ? Clipboard.GetImage() : null);
        if (image is null)
        {
            Notify("Nothing to paste", "The clipboard has no image or Snapr object.");
            return;
        }
        OpenNewCapture(image, CaptureKind.Clipboard, "Clipboard");
    }

    /// <summary>Wraps a bitmap (and optional objects) in a new document, autosaves it and opens it in the editor.</summary>
    internal void OpenNewCapture(BitmapSource image, CaptureKind kind, string? title, DateTimeOffset? capturedAt = null,
        IReadOnlyList<Annotation>? annotations = null, string? notice = null)
    {
        var bgra = ImageIo.ToBgra(image);
        var objects = annotations ?? [];
        var doc = new CaptureDocument
        {
            BasePng = ImageIo.EncodePng(bgra),
            PixelWidth = bgra.PixelWidth,
            PixelHeight = bgra.PixelHeight,
            Dpi = image.DpiX > 0 ? image.DpiX : 96,
            Meta = new CaptureMeta(capturedAt ?? DateTimeOffset.Now, kind, title),
            State = DocumentState.Empty with
            {
                Annotations = objects,
                NextStepNumber = objects.OfType<StepAnnotation>().Select(s => s.Number).DefaultIfEmpty(0).Max() + 1,
            },
        };
        Editor.Open(doc, CapturePaths.NewCapturePath(CapturePaths.PicturesRoot, DateTime.Now), saveNow: true);
        if (notice is not null) Editor.ShowNotice(notice);
    }
}
