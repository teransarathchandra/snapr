using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Snapr.App.Dialogs;
using Snapr.App.Platform;
using Snapr.App.Rendering;
using Snapr.App.Services;
using Snapr.Core;
using Snapr.Core.Interop;
using Snapr.Core.Output;

namespace Snapr.App.Editor;

public partial class EditorWindow : Window
{
    static readonly (ToolKind Tool, string Name, Key Key)[] Tools =
    [
        (ToolKind.Move, "Select and move", Key.V), (ToolKind.Arrow, "Arrow", Key.A), (ToolKind.Text, "Text", Key.T),
        (ToolKind.Callout, "Callout", Key.K), (ToolKind.Shape, "Shape", Key.S), (ToolKind.Pen, "Pen", Key.P),
        (ToolKind.Highlight, "Highlighter", Key.H), (ToolKind.Step, "Step", Key.N), (ToolKind.Blur, "Blur", Key.B),
        (ToolKind.Magnify, "Magnify", Key.M), (ToolKind.Crop, "Crop", Key.C), (ToolKind.Eraser, "Eraser", Key.E), (ToolKind.Fill, "Fill", Key.F),
        (ToolKind.Stamp, "Stamp", Key.G),
        (ToolKind.CutOut, "Cut out", Key.U),
        (ToolKind.Spotlight, "Spotlight", Key.L),
        (ToolKind.Selection, "Selection", Key.R),
    ];

    readonly AppController _app;
    readonly PickList _picks = new();
    CombineChoice _combineChoice = CombineChoice.Default;
    TemplateChoice _templateChoice = TemplateChoice.Default;
    readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Dictionary<ToolKind, RadioButton> _toolButtons = new();
    CaptureDocument? _doc;
    UndoStack? _undo;
    BitmapSource? _base;
    string? _path;
    bool _dirty;
    double? _manualZoom;   // null = fit to window; otherwise relative to 100%
    int _pasteCount;
    readonly EditedImageCache _images = new();
    bool _allowClose;
    string? _lastMergeKey;
    DateTime _lastMergeAt;

    public EditorWindow(AppController app)
    {
        _app = app;
        InitializeComponent();
        Icon = AppIcon.Render(64);
        LogoImage.Source = AppIcon.Render(40);
        _autosave.Tick += (_, _) => { _autosave.Stop(); SaveNow(); };
        Board.Committed += OnCommitted;
        Board.Notice += ShowNotice;
        Board.Message += text => _app.Notify("Cut out", text);
        Board.SelectionChanged += BuildProps;
        Board.PixelSelectionChanged += () =>
        {
            // The panel only shows whether a selection exists (Crop to selection), so skip the per-mouse-move rebuilds.
            if ((Board.PixelSelection is not null) != _hadPixelSelection) BuildProps();
            _hadPixelSelection = Board.PixelSelection is not null;
        };
        CanvasScroller.SizeChanged += (_, _) => ApplyZoom();
        PreviewKeyDown += OnKey;
        BuildToolbar();
        SetTool(app.Settings.LastTool);
        UpdateEmptyState();
        UpdateUndoButtons();
        RefreshRecents();
    }

    // ---------- Opening documents ----------

    public void Open(CaptureDocument doc, string path, bool saveNow)
    {
        ResolveUnsaved(allowCancel: false);

        _doc = doc;
        _path = path;
        _base = ImageIo.ToBgra(ImageIo.DecodePng(doc.BasePng));
        _undo = new UndoStack(doc.State);
        _dirty = saveNow;
        Board.Load(CurrentImage(), doc.State);
        UpdateTitle();
        _manualZoom = null;
        _pasteCount = 0;
        _pixelCopy = null;
        InfoBanner.Visibility = Visibility.Collapsed;
        UpdateEmptyState();
        UpdateUndoButtons();
        if (saveNow) SaveNow();
        else RefreshRecents();
        ShowAndActivate();
        Dispatcher.BeginInvoke(ApplyZoom, DispatcherPriority.Loaded);
    }

    /// <summary>The original pixels with the document's image edits applied (spec parity 4 §6).</summary>
    BitmapSource CurrentImage() => _images.Get(_base!, _undo!.Current.Edits);

    void UpdateTitle()
    {
        if (_doc is null || _path is null) return;
        var image = CurrentImage();
        TitleText.Text = $"{Path.GetFileNameWithoutExtension(_path)}  ·  {image.PixelWidth} × {image.PixelHeight}";
    }

    /// <summary>One image edit (rotate, flip, resize, margins) as one undo step; objects and crop follow it.</summary>
    void ApplyImageEdit(ImageEdit edit)
    {
        if (_doc is null) return;
        Board.CommitTextEditor();
        var image = CurrentImage();
        Board.Apply(s => s.ApplyEdit((image.PixelWidth, image.PixelHeight), edit));
    }

    /// <summary>True when the editor is showing this saved file, so reloading it from disk would drop pending edits.</summary>
    public bool IsShowing(string fullPath) => _path is not null && string.Equals(_path, fullPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>Library delete of the capture a closed (hidden) editor still holds: saves, then drops the document so the file can go.
    /// False when the editor is visible, shows another file, or couldn't save.</summary>
    public bool ReleaseIfHidden(string fullPath)
    {
        if (!IsShowing(fullPath) || IsVisible || !FlushAutosave()) return false;
        _doc = null;
        _path = null;
        _base = null;
        _undo = null;
        _dirty = false;
        TitleText.Text = "";
        SaveBanner.Visibility = Visibility.Collapsed;
        InfoBanner.Visibility = Visibility.Collapsed;
        _magnifierNotice = false;
        Board.Select(null);
        Board.ClearPixelSelection();
        UpdateEmptyState();
        UpdateUndoButtons();
        BuildProps();
        return true;
    }

    void Library_Click(object sender, RoutedEventArgs e) => _app.ShowLibrary();

    /// <summary>Library tagging (spec parity 13 §4): the open capture's tags change in memory and are saved now, so autosave can't
    /// write the old ones back. Null when <paramref name="fullPath"/> isn't the open capture.</summary>
    public bool? TrySetTags(string fullPath, IReadOnlyList<string> tags)
    {
        if (_doc is null || !IsShowing(fullPath)) return null;
        _doc.Meta = _doc.Meta with { Tags = tags };
        _dirty = true;
        return SaveNow();
    }

    /// <summary>Shows the current capture, or the most recent saved one, or the empty state.</summary>
    public void ShowLatest()
    {
        if (_doc is null && RecentsIndex.List([CapturePaths.PicturesRoot]).FirstOrDefault() is { } latest && TryLoad(latest.Path) is { } doc)
        {
            Open(doc, latest.Path, saveNow: false);
            return;
        }
        ShowAndActivate();
    }

    CaptureDocument? TryLoad(string path)
    {
        try { return SnaprFile.Load(path); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Log.Write($"Could not open {path}", ex);
            MessageBox.Show(this, $"Snapr couldn't open {Path.GetFileName(path)}.\n\n{ex.Message}", "Snapr", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;   // brings the window above the app that had focus when the hotkey was pressed
        Topmost = false;
        Board.Focus();
    }

    void UpdateEmptyState()
    {
        var hasDoc = _doc is not null;
        EmptyText.Visibility = hasDoc ? Visibility.Collapsed : Visibility.Visible;
        CanvasFrame.Visibility = ZoomButton.Visibility = hasDoc ? Visibility.Visible : Visibility.Collapsed;
        ImageButton.IsEnabled = hasDoc;
        RedactButton.IsEnabled = hasDoc;
    }

    // ---------- Edits, undo, autosave ----------

    void OnCommitted(DocumentState next, string? mergeKey)
    {
        if (_undo is null || _doc is null) return;
        var now = DateTime.UtcNow;
        if (mergeKey is not null && mergeKey == _lastMergeKey && now - _lastMergeAt < TimeSpan.FromMilliseconds(800))
            _undo.ReplaceCurrent(next);
        else
            _undo.Apply(next);
        _lastMergeKey = mergeKey;
        _lastMergeAt = now;
        ShowState();
    }

    void ShowState()
    {
        _doc!.State = _undo!.Current;
        Board.SetImage(CurrentImage());
        Board.SetState(_undo.Current);
        UpdateTitle();
        _dirty = true;
        _autosave.Stop();
        _autosave.Start();
        UpdateUndoButtons();
        ApplyZoom();
    }

    void Undo() { if (_undo is { CanUndo: true }) { HideMagnifierNotice(); Board.CommitTextEditor(); Board.ClearPixelSelection(); _undo.Undo(); _lastMergeKey = null; ShowState(); BuildProps(); } }
    void Redo() { if (_undo is { CanRedo: true }) { HideMagnifierNotice(); Board.ClearPixelSelection(); _undo.Redo(); _lastMergeKey = null; ShowState(); BuildProps(); } }

    void UpdateUndoButtons()
    {
        UndoButton.IsEnabled = _undo?.CanUndo == true;
        RedoButton.IsEnabled = _undo?.CanRedo == true;
    }

    bool SaveNow()
    {
        if (_doc is null || _path is null || _base is null) return true;
        try
        {
            var thumb = ImageIo.EncodePng(ImageIo.Thumbnail(DocumentRenderer.Render(CurrentImage(), _doc.State, applyEffects: false)));
            SnaprFile.Save(_doc, thumb, _path);
            _dirty = false;
            SaveBanner.Visibility = Visibility.Collapsed;
            RefreshRecents();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Autosave failed for {_path}", ex);
            SaveBannerText.Text = $"Not saved: {ex.Message}";
            SaveBanner.Visibility = Visibility.Visible;
            return false;
        }
    }

    /// <summary>Saves pending edits now. Returns false if the save failed (edits stay in memory).</summary>
    public bool FlushAutosave()
    {
        _autosave.Stop();
        return !_dirty || SaveNow();
    }

    void RetrySave_Click(object sender, RoutedEventArgs e) => SaveNow();

    // ---------- Output ----------

    BitmapSource RenderForExport() => DocumentRenderer.Render(CurrentImage(), _undo!.Current, applyEffects: true);

    /// <summary>Returns true only if the clipboard write succeeded.</summary>
    bool Copy(bool wholeImage = false)
    {
        if (_doc is null) return false;
        Board.CommitTextEditor();
        bool ok;
        if (!wholeImage && Board.SelectedObjects is { Count: > 0 } items)
        {
            var area = items.Select(AnnotationGeometry.Bounds).Aggregate((a, b) => a.Union(b)).Intersect(Board.View);
            ok = AnnotationClipboard.Set(items, RenderArea(area.IsEmpty ? Board.View : area));
        }
        else if ((ok = ClipboardImage.Set(RenderForExport())))
            _app.Notify("Copied", "The image is on the clipboard.");
        if (ok) _pasteCount = 0;
        else MessageBox.Show(this, "Another app is holding the clipboard. Try again.", "Snapr", MessageBoxButton.OK, MessageBoxImage.Warning);
        return ok;
    }

    bool _hadPixelSelection;
    PixelCopy? _pixelCopy;
    RedactLook _coverLook = new(false, "#FF000000");
    bool _magnifierNotice;

    bool PixelSelectionActive => Board.Tool == ToolKind.Selection && Board.PixelSelection is not null;

    /// <summary>Copies what is visible inside the selection (objects included, no effects).</summary>
    bool CopyPixels()
    {
        if (Board.SelectedPixels() is not { } pixels) return false;
        var area = Board.PixelSelection!.Value;
        if (ClipboardImage.Set(pixels))
        {
            _pixelCopy = new PixelCopy(area, Native.GetClipboardSequenceNumber());
            _app.Notify("Copied", "The selection is on the clipboard.");
            return true;
        }
        MessageBox.Show(this, "Another app is holding the clipboard. Try again.", "Snapr", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    void Cut()
    {
        if (Board.SelectedIds.Count == 0) return;
        if (Copy()) Board.DeleteSelected();
    }

    void Paste()
    {
        if (_doc is not null && AnnotationClipboard.Get() is { } sources)
        {
            Board.CommitTextEditor();
            var (next, pasted) = Board.State.PasteCopies(sources, 16 * ++_pasteCount);
            Board.Apply(_ => next);
            SetTool(ToolKind.Move);
            Board.SelectIds(pasted);
            return;
        }
        if (_doc is null) _app.OpenFromClipboard();   // no capture open: nothing to paste into, open it as a new capture
        else PasteImage();
    }

    void PasteImage()
    {
        BitmapSource? image = null;
        ClipboardImage.Try(() => image = Clipboard.ContainsImage() ? Clipboard.GetImage() : null);
        if (image is null)
        {
            _app.Notify("Nothing to paste", "The clipboard has no image or Snapr object.");
            return;
        }
        BitmapSource picture = image;
        if (PastePlacement.AtCopy(Native.GetClipboardSequenceNumber(), _pixelCopy, picture.PixelWidth, picture.PixelHeight) is { } area && area.Intersect(Board.View) == area)
        {
            Board.CommitTextEditor();
            var a = ImageObjects.CreateAt(picture, area);
            Board.Apply(s => s.Add(a));
            SetTool(ToolKind.Move);
            Board.SelectIds([a.Id]);
            return;
        }
        AddImageObjects([("the clipboard image", () => picture)], Board.View.Center);
    }

    /// <summary>Adds pictures as Image objects (each next one +16, +16 px) as one undo step and selects them.</summary>
    void AddImageObjects(IEnumerable<(string Name, Func<BitmapSource> Load)> sources, Pt at)
    {
        Board.CommitTextEditor();
        var state = Board.State;
        var added = new List<Guid>();
        foreach (var (name, load) in sources)
        {
            try
            {
                var a = ImageObjects.Create(load(), at.Offset(16 * added.Count, 16 * added.Count), Board.View);
                state = state.Add(a);
                added.Add(a.Id);
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
            {
                Log.Write($"Could not add image {name}", ex);
                MessageBox.Show(this, $"Snapr couldn't add that image: {ex.Message}", "Snapr", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        if (added.Count == 0) return;
        Board.Apply(_ => state);
        SetTool(ToolKind.Move);
        Board.SelectIds(added);
    }

    /// <summary>The document (no effects) rendered and cut to an area, for copying one object to other apps.</summary>
    BitmapSource RenderArea(Rct area)
    {
        var full = DocumentRenderer.Render(CurrentImage(), _undo!.Current with { Crop = null }, applyEffects: false);
        var crop = new CroppedBitmap(full, PixelRect(area));
        crop.Freeze();
        return crop;
    }

    /// <summary>Whole-pixel part of an area inside the current image; the whole image if the area is empty or outside.</summary>
    Int32Rect PixelRect(Rct area)
    {
        var image = CurrentImage();
        var a = area.Intersect(new Rct(0, 0, image.PixelWidth, image.PixelHeight));
        if (a.IsEmpty) return new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight);
        int x = (int)Math.Floor(a.X), y = (int)Math.Floor(a.Y);
        return new Int32Rect(x, y, Math.Max(1, Math.Min(image.PixelWidth - x, (int)Math.Ceiling(a.Right) - x)),
            Math.Max(1, Math.Min(image.PixelHeight - y, (int)Math.Ceiling(a.Bottom) - y)));
    }

    /// <summary>Prints the image with effects: fitted to the page (never enlarged) or at actual size, centred.</summary>
    void Print(bool fit = true)
    {
        if (_doc is null) return;
        Board.CommitTextEditor();
        var image = RenderForExport();
        var dialog = new PrintDialog();
        try
        {
            if (PrintLayout.WantsLandscape(image.Width, image.Height) && dialog.PrintTicket is { } ticket)
                ticket.PageOrientation = PageOrientation.Landscape;
            if (dialog.ShowDialog() != true) return;
            // The printer's imageable area is smaller than the paper (hardware margins); fit to that, not the paper.
            var area = dialog.PrintQueue.GetPrintCapabilities(dialog.PrintTicket).PageImageableArea;
            double ox = area?.OriginWidth ?? 0, oy = area?.OriginHeight ?? 0;
            double aw = area?.ExtentWidth ?? dialog.PrintableAreaWidth, ah = area?.ExtentHeight ?? dialog.PrintableAreaHeight;
            var r = PrintLayout.Place(image.Width, image.Height, aw, ah, fit);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushClip(new RectangleGeometry(new Rect(ox, oy, aw, ah)));
                dc.DrawImage(image, new Rect(ox + r.X, oy + r.Y, r.W, r.H));
                dc.Pop();
            }
            dialog.PrintVisual(visual, Path.GetFileNameWithoutExtension(_path) ?? "Snapr");
        }
        catch (Exception ex) when (ex is PrintSystemException or InvalidOperationException)
        {
            Log.Write("Print failed", ex);
            MessageBox.Show(this, $"Couldn't print.\n\n{ex.Message}", "Snapr", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void ExportImage(string format)
    {
        if (_doc is null) return;
        Board.CommitTextEditor();
        (string Filter, Func<BitmapSource, byte[]> Encode) export = format switch
        {
            "jpg" => ("JPEG image|*.jpg", i => ImageIo.EncodeJpg(i, _app.Settings.JpgQuality)),
            "gif" => ("GIF image|*.gif", ImageIo.EncodeGif),
            "bmp" => ("Bitmap image|*.bmp", ImageIo.EncodeBmp),
            "pdf" => ("PDF document|*.pdf", ImageIo.EncodePdf),
            _ => ("PNG image|*.png", ImageIo.EncodePng),
        };
        var dialog = new SaveFileDialog
        {
            Filter = export.Filter,
            FileName = Path.GetFileNameWithoutExtension(_path) + "." + format,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllBytes(dialog.FileName, export.Encode(RenderForExport()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Export failed: {dialog.FileName}", ex);
            MessageBox.Show(this, $"Couldn't save the file.\n\n{ex.Message}", "Snapr", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Returns true only if the file was written.</summary>
    bool ExportSnapr()
    {
        if (_doc is null || _base is null) return false;
        var dialog = new SaveFileDialog { Filter = "Snapr file|*.snapr", FileName = Path.GetFileName(_path) ?? "" };
        if (dialog.ShowDialog(this) != true) return false;
        try
        {
            SnaprFile.Save(_doc, ImageIo.EncodePng(ImageIo.Thumbnail(DocumentRenderer.Render(CurrentImage(), _doc.State, false))), dialog.FileName);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Export failed: {dialog.FileName}", ex);
            MessageBox.Show(this, $"Couldn't save the file.\n\n{ex.Message}", "Snapr", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    /// <summary>
    /// If autosave is failing, asks before unsaved edits are dropped: Yes saves a copy elsewhere (asking again if that
    /// fails or is cancelled), No discards, Cancel (when allowed) keeps the document open. Returns false only for Cancel.
    /// </summary>
    bool ResolveUnsaved(bool allowCancel)
    {
        Board.CommitTextEditor();
        while (!FlushAutosave() && _path is not null)
        {
            var answer = MessageBox.Show(this,
                $"Snapr couldn't save {Path.GetFileName(_path)}. Save a copy somewhere else first?\n\nNo discards the unsaved changes.",
                "Snapr", allowCancel ? MessageBoxButton.YesNoCancel : MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer == MessageBoxResult.No) return true;
            if (answer == MessageBoxResult.Cancel) return false;
            if (ExportSnapr()) return true;
        }
        return true;
    }

    void Undo_Click(object sender, RoutedEventArgs e) => Undo();
    void Redo_Click(object sender, RoutedEventArgs e) => Redo();
    void Copy_Click(object sender, RoutedEventArgs e) => Copy(wholeImage: true);
    void Save_Click(object sender, RoutedEventArgs e) => ExportImage(_app.Settings.ExportFormat);
    void ExportPng_Click(object sender, RoutedEventArgs e) => ExportImage("png");
    void ExportJpg_Click(object sender, RoutedEventArgs e) => ExportImage("jpg");
    void ExportGif_Click(object sender, RoutedEventArgs e) => ExportImage("gif");
    void ExportBmp_Click(object sender, RoutedEventArgs e) => ExportImage("bmp");
    void ExportPdf_Click(object sender, RoutedEventArgs e) => ExportImage("pdf");
    void PrintFit_Click(object sender, RoutedEventArgs e) => Print(fit: true);
    void PrintActual_Click(object sender, RoutedEventArgs e) => Print(fit: false);
    void ExportSnapr_Click(object sender, RoutedEventArgs e) => ExportSnapr();

    void Export_Click(object sender, RoutedEventArgs e)
    {
        ExportButton.ContextMenu.PlacementTarget = ExportButton;
        ExportButton.ContextMenu.IsOpen = true;
    }

    void ImageMenu_Click(object sender, RoutedEventArgs e)
    {
        RevertImageItem.IsEnabled = _undo?.Current.Edits.Count > 0;
        ImageButton.ContextMenu.PlacementTarget = ImageButton;
        ImageButton.ContextMenu.IsOpen = true;
    }

    void RotateRight_Click(object sender, RoutedEventArgs e) => ApplyImageEdit(new RotateEdit(1));
    void RotateLeft_Click(object sender, RoutedEventArgs e) => ApplyImageEdit(new RotateEdit(3));
    void Rotate180_Click(object sender, RoutedEventArgs e) => ApplyImageEdit(new RotateEdit(2));
    void FlipHorizontal_Click(object sender, RoutedEventArgs e) => ApplyImageEdit(new FlipEdit(true));
    void FlipVertical_Click(object sender, RoutedEventArgs e) => ApplyImageEdit(new FlipEdit(false));

    void ResizeImage_Click(object sender, RoutedEventArgs e)
    {
        if (_doc is null) return;
        var image = CurrentImage();
        var dialog = new ResizeImageWindow(this, image.PixelWidth, image.PixelHeight);
        if (dialog.ShowDialog() == true && dialog.Result is { } size) ApplyImageEdit(new ResizeEdit(size.W, size.H));
    }

    void CanvasSize_Click(object sender, RoutedEventArgs e)
    {
        if (_doc is null) return;
        var image = CurrentImage();
        var dialog = new CanvasSizeWindow(this, image.PixelWidth, image.PixelHeight, Board.CurrentStyle.Color);
        if (dialog.ShowDialog() == true && dialog.Result is { } margins) ApplyImageEdit(margins);
    }

    /// <summary>Removes every image edit and moves objects back onto the original capture, as one undo step (spec parity 5 §5).</summary>
    void RevertImage_Click(object sender, RoutedEventArgs e)
    {
        if (_doc is null || _undo is null || _undo.Current.Edits.Count == 0) return;
        Board.CommitTextEditor();
        Board.Apply(s => s.RevertEdits((_doc.PixelWidth, _doc.PixelHeight)));
    }

    void ShowInFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_path is not null && File.Exists(_path)) Process.Start("explorer.exe", $"/select,\"{_path}\"");
    }

    void GrabText_Click(object sender, RoutedEventArgs e)
    {
        if (_base is null) return;
        var crop = new CroppedBitmap(CurrentImage(), PixelRect(Board.Selected is { } s ? AnnotationGeometry.Bounds(s).Intersect(Board.View) : Board.View));
        crop.Freeze();
        _app.Fire(() => _app.GrabTextAsync(crop));
    }

    /// <summary>Smart Redact (spec parity 5 §4): OCR the visible area, let the user pick matches, cover them as one undo step.</summary>
    void Redact_Click(object sender, RoutedEventArgs e)
    {
        if (_doc is null) return;
        Board.CommitTextEditor();
        var area = PixelRect(Board.View);
        var visible = new CroppedBitmap(CurrentImage(), area);
        visible.Freeze();
        var dialog = new RedactWindow(this, () => OcrService.RecognizeLinesAsync(visible), OcrService.WillDownscale(visible), look: _coverLook);
        var doc = _doc;
        var ok = dialog.ShowDialog() == true;
        if (dialog.OcrMissing)
        {
            _app.ShowOcrHelp();
            return;
        }
        if (!ReferenceEquals(doc, _doc)) return;   // another capture was opened while the dialog was up
        if (!ok || dialog.Result is not { Boxes.Count: > 0 } result) return;
        var image = CurrentImage();
        var boxes = result.Boxes.Select(b => b.Offset(area.X, area.Y)).ToArray();
        _coverLook = result.Look;
        var (next, added) = Board.State.AddCovers(boxes, result.Look, (image.PixelWidth, image.PixelHeight));
        Board.Apply(_ => next);
        SetTool(ToolKind.Move);
        Board.SelectIds(added);
        WarnIfMagnified(next, added);
    }

    /// <summary>Warns when a magnifier still shows part of the new covers; the warning hides again on undo/redo.</summary>
    void WarnIfMagnified(DocumentState next, IReadOnlyList<Guid> added)
    {
        var covers = next.Annotations.Where(a => added.Contains(a.Id)).Select(AnnotationGeometry.Bounds).ToArray();
        if (!next.Annotations.OfType<MagnifyAnnotation>().Any(m => covers.Any(c => !m.SourceBounds.Intersect(c).IsEmpty))) return;
        ShowNotice("A magnifier still shows part of a covered area. Move or delete it before sharing.");
        _magnifierNotice = true;
    }

    void HideMagnifierNotice()
    {
        if (!_magnifierNotice) return;
        InfoBanner.Visibility = Visibility.Collapsed;
        _magnifierNotice = false;
    }

    /// <summary>Cover selection (spec parity 11 §2): a redaction cover over the Selection-tool rectangle, for text OCR can't read.</summary>
    void CoverSelection()
    {
        if (_doc is null || Board.PixelSelection is not { } sel) return;
        var image = CurrentImage();
        var (next, added) = Board.State.AddCovers([sel], _coverLook, (image.PixelWidth, image.PixelHeight));
        Board.Apply(_ => next);
        Board.ClearPixelSelection();
        WarnIfMagnified(next, added);
    }

    // ---------- Zoom ----------

    double PixelScale => 1 / VisualTreeHelper.GetDpi(this).DpiScaleX;   // 1 image pixel = 1 screen pixel

    void ApplyZoom()
    {
        if (_base is null || !IsLoaded) return;
        var view = Board.View;
        var pixel = PixelScale;
        // Leave room for a scrollbar so "fit" never overflows by a rounding pixel and triggers scrollbars.
        var fit = Math.Min((CanvasScroller.ActualWidth - 66 - SystemParameters.VerticalScrollBarWidth) / view.W,
            (CanvasScroller.ActualHeight - 66 - SystemParameters.HorizontalScrollBarHeight) / view.H);
        var zoom = _manualZoom is { } m ? m * pixel : Math.Max(0.05, Math.Min(pixel, fit));
        Board.Zoom = zoom;   // screen units per image pixel; the canvas divides handle sizes by it
        Board.LayoutTransform = new ScaleTransform(zoom, zoom);
        ZoomButton.Content = _manualZoom is null ? $"Fit · {zoom / pixel:P0}" : $"{zoom / pixel:P0}";
    }

    void Zoom_Click(object sender, RoutedEventArgs e)
    {
        _manualZoom = _manualZoom is null ? 1 : null;
        ApplyZoom();
    }

    void ZoomBy(int direction)
    {
        if (_base is null) return;
        _manualZoom = ZoomSteps.Next(Board.Zoom / PixelScale, direction);
        ApplyZoom();
    }

    // ---------- Recents ----------

    public void RefreshRecents()
    {
        RecentsList.Items.Clear();
        var items = RecentsIndex.List([CapturePaths.PicturesRoot, CapturePaths.VideosRoot]).ToList();
        _picks.Retain(_picks.Paths.Where(File.Exists));
        if (_picks.Count >= 1)
        {
            var bar = new StackPanel { Margin = new Thickness(4, 10, 4, 10), VerticalAlignment = VerticalAlignment.Center };
            var template = new Button { Content = "Template…" };
            if (_picks.Count == 1) template.Style = (Style)FindResource("AccentButton");
            template.Click += (_, _) => MakeTemplate(this, _picks.Paths.ToArray());
            bar.Children.Add(template);
            if (_picks.Count >= 2)
            {
                var combine = new Button { Content = $"Combine ({_picks.Count})…", Margin = new Thickness(0, 4, 0, 0), Style = (Style)FindResource("AccentButton") };
                combine.Click += Combine_Click;
                bar.Children.Add(combine);
            }
            var clear = new Button { Content = "Clear", Margin = new Thickness(0, 4, 0, 0) };
            clear.Click += (_, _) => { _picks.Clear(); RefreshRecents(); };
            bar.Children.Add(clear);
            RecentsList.Items.Add(bar);
        }
        foreach (var item in items)
        {
            UIElement content;
            if (item.IsVideo)
            {
                content = new Grid
                {
                    Width = 120, Height = 68, Background = (Brush)FindResource("CanvasBrush"),
                    Children =
                    {
                        new TextBlock { Text = "▶", FontSize = 22, Foreground = (Brush)FindResource("TextBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                    },
                };
            }
            else
            {
                try
                {
                    content = new Image { Source = ImageIo.DecodePng(SnaprFile.ReadThumbnail(item.Path)), Width = 120, Height = 68, Stretch = Stretch.Uniform };
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or FileFormatException or UnauthorizedAccessException)
                {
                    Log.Write($"Skipping unreadable capture {item.Path}", ex);
                    continue;
                }
            }
            var current = string.Equals(item.Path, _path, StringComparison.OrdinalIgnoreCase);
            var picked = !item.IsVideo && _picks.NumberOf(item.Path) is > 0 and var n ? n : 0;
            if (picked > 0)
            {
                content = new Grid
                {
                    Children =
                    {
                        content,
                        new Border
                        {
                            Background = (Brush)FindResource("AccentFillBrush"), CornerRadius = new CornerRadius(9), MinWidth = 18, Margin = new Thickness(2),
                            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                            Child = new TextBlock { Text = picked.ToString(), Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center },
                        },
                    },
                };
            }
            var button = new Button
            {
                Content = content, Padding = new Thickness(2), Margin = new Thickness(4, 10, 4, 10),
                ToolTip = Path.GetFileName(item.Path),
                BorderBrush = current || picked > 0 ? (Brush)FindResource("AccentBrush") : Brushes.Transparent,
                BorderThickness = new Thickness(picked > 0 ? 3 : current ? 2 : 0),
            };
            AutomationProperties.SetName(button, picked > 0 ? $"{Path.GetFileName(item.Path)}, picked {picked}" : Path.GetFileName(item.Path));
            button.Click += (_, _) => TileClick(item);
            RecentsList.Items.Add(button);
        }
    }

    void TileClick(RecentItem item)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (item.IsVideo) return;
            if (!_picks.Toggle(item.Path)) ShowNotice("You can combine up to 20 captures.");
            RefreshRecents();
            return;
        }
        if (_picks.Count > 0)
        {
            _picks.Clear();
            RefreshRecents();
        }
        OpenRecent(item);
    }

    enum PickPurpose { Combine, Template }

    sealed record LoadedPicks(IReadOnlyList<BitmapSource> Pictures, IReadOnlyList<string?> Titles, IReadOnlyList<string> Skipped);

    /// <summary>Saves the open capture, then flattens each pick (skipping unreadable ones) until the pixel cap. Shows its own
    /// message and returns null when it can't go on. Shared by Combine and Templates (spec parity 14 §3).</summary>
    LoadedPicks? LoadPicks(Window owner, IReadOnlyList<string> paths, PickPurpose purpose)
    {
        Board.CommitTextEditor();
        if (_doc is not null && !FlushAutosave())
        {
            MessageBox.Show(owner, purpose == PickPurpose.Combine
                ? "Snapr couldn't save the open capture, so it can't be combined yet."
                : "Snapr couldn't save the open capture, so it can't be used in a template yet.", "Snapr", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        var pictures = new List<BitmapSource>();
        var titles = new List<string?>();
        var skipped = new List<string>();
        var tooLarge = false;
        long area = 0;
        foreach (var path in paths)
        {
            try
            {
                var doc = SnaprFile.Load(path);
                var picture = CombineService.Flatten(doc);
                pictures.Add(picture);
                titles.Add(doc.Meta.SourceTitle);
                area += (long)picture.PixelWidth * picture.PixelHeight;
            }
            catch (OutOfMemoryException ex)
            {
                Log.Write($"Combine ran out of memory on {path}", ex);
                tooLarge = true;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException or FormatException)
            {
                Log.Write($"Combine skipped {path}", ex);
                skipped.Add(Path.GetFileName(path));
            }
            if (tooLarge || area > SnagxReader.MaxTotalPixels) { tooLarge = true; break; }
        }
        if (tooLarge)
        {
            MessageBox.Show(owner, purpose == PickPurpose.Combine ? "The combined image would be too large." : "The template image would be too large.",
                "Snapr", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        return new LoadedPicks(pictures, titles, skipped);
    }

    void Combine_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CombineWindow(this, Swatches, _combineChoice);
        if (dialog.ShowDialog() != true || dialog.Result is not { } choice) return;
        _combineChoice = choice;
        if (LoadPicks(this, _picks.Paths, PickPurpose.Combine) is not { } loaded) return;
        var pictures = loaded.Pictures;
        var skipped = loaded.Skipped;
        if (pictures.Count < 2)
        {
            MessageBox.Show(this, "Snapr needs at least two readable captures to combine.", "Snapr", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (CombineService.Build(pictures, choice) is not { } output)
        {
            MessageBox.Show(this, "The combined image would be too large.", "Snapr", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _picks.Clear();
        _app.OpenNewCapture(output.Base, CaptureKind.Region, "Combined", annotations: output.Objects,
            notice: skipped.Count > 0 ? "Skipped (couldn't read): " + string.Join(", ", skipped) : null);
        SetTool(ToolKind.Move);
        RefreshRecents();
    }

    /// <summary>Templates (spec parity 14 §3) from the recents pick or the Library; the result opens as a new capture.</summary>
    public void MakeTemplate(Window owner, IReadOnlyList<string> paths)
    {
        var dialog = new TemplateWindow(owner, paths.Count, Swatches, _templateChoice);
        if (dialog.ShowDialog() != true || dialog.Result is not { } choice) return;
        _templateChoice = choice;
        if (LoadPicks(owner, paths, PickPurpose.Template) is not { } picks) return;
        if (TemplateRules.Fits(choice.Kind, picks.Pictures.Count) is not null)
        {
            MessageBox.Show(owner, "Snapr couldn't read enough captures for this template.", "Snapr", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (TemplateService.Build(choice.Kind, picks.Pictures, picks.Titles, choice.Background) is not { } output)
        {
            MessageBox.Show(owner, "The template image would be too large.", "Snapr", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _picks.Clear();
        _app.OpenNewCapture(output.Base, CaptureKind.Region, "Template", annotations: output.Objects,
            notice: picks.Skipped.Count > 0 ? "Skipped (couldn't read): " + string.Join(", ", picks.Skipped) : null);
        SetTool(ToolKind.Move);
        Board.SelectIds([output.TitleId]);
        RefreshRecents();
    }

    void OpenRecent(RecentItem item)
    {
        if (item.IsVideo)
        {
            _app.ShowVideoEditor(item.Path);
            return;
        }
        if (string.Equals(item.Path, _path, StringComparison.OrdinalIgnoreCase)) return;
        if (TryLoad(item.Path) is { } doc) Open(doc, item.Path, saveNow: false);
    }

    // ---------- Notices and drag-and-drop ----------

    /// <summary>A one-time note on the current capture, such as what a Snagit import approximated.</summary>
    public void ShowNotice(string text)
    {
        _magnifierNotice = false;
        InfoBannerText.Text = text;
        InfoBanner.Visibility = Visibility.Visible;
    }

    void DismissNotice_Click(object sender, RoutedEventArgs e) => InfoBanner.Visibility = Visibility.Collapsed;

    void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Image files become Image objects in the open capture at the drop point; other files (.snapr, .snagx…) open as before.</summary>
    void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files) return;
        if (_doc is null)
        {
            _app.OpenImageFile(files[0]);
            return;
        }
        var images = files.Where(ImageObjects.IsImageFile).ToArray();
        if (images.Length > 0)
        {
            var p = e.GetPosition(Board);
            var v = Board.View;
            var at = new Pt(Math.Clamp(p.X + v.X, v.X, v.Right), Math.Clamp(p.Y + v.Y, v.Y, v.Bottom));
            AddImageObjects(images.Select(f => (Path.GetFileName(f), (Func<BitmapSource>)(() => ImageObjects.Load(f)))), at);
        }
        if (files.FirstOrDefault(f => !ImageObjects.IsImageFile(f)) is { } other) _app.OpenImageFile(other);
    }

    // ---------- Keyboard and lifetime ----------

    void OnKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        // Arrow keys nudge only when the canvas has focus and something is selected; otherwise the scroll view and panel controls keep them.
        if (ReferenceEquals(Keyboard.FocusedElement, Board) && Board.SelectedIds.Count > 0 && EditorShortcuts.Nudge(Keyboard.Modifiers, key) is { } n)
            Board.Nudge(n.Dx, n.Dy);
        else if (EditorShortcuts.Resolve(Keyboard.Modifiers, key) is { } command) Run(command);
        else if (Keyboard.Modifiers == ModifierKeys.None && Array.FindIndex(Tools, t => t.Key == key) is var i and >= 0) SetTool(Tools[i].Tool);
        else return;
        e.Handled = true;
    }

    void Run(EditorCommand command)
    {
        switch (command)
        {
            case EditorCommand.Undo: Undo(); break;
            case EditorCommand.Redo: Redo(); break;
            case EditorCommand.Copy when PixelSelectionActive: CopyPixels(); break;
            case EditorCommand.Cut when PixelSelectionActive: if (CopyPixels()) Board.ClearSelectedPixels(); break;
            case EditorCommand.Delete when PixelSelectionActive: Board.ClearSelectedPixels(); break;
            case EditorCommand.Deselect when PixelSelectionActive: Board.ClearPixelSelection(); break;
            case EditorCommand.Copy: Copy(); break;
            case EditorCommand.Cut: Cut(); break;
            case EditorCommand.Paste: Paste(); break;
            case EditorCommand.Delete: Board.DeleteSelected(); break;
            case EditorCommand.Deselect: Board.CommitTextEditor(); Board.Select(null); break;
            case EditorCommand.SaveNow:
                Board.CommitTextEditor();
                _autosave.Stop();
                if (_doc is not null && SaveNow()) _app.Notify("Saved", Path.GetFileName(_path) ?? "Saved");
                break;
            case EditorCommand.SaveAsImage: ExportImage(_app.Settings.ExportFormat); break;
            case EditorCommand.Open: _app.OpenImageFromDialog(); break;
            case EditorCommand.NewFromClipboard: _app.OpenFromClipboard(); break;
            case EditorCommand.Close: Close(); break;
            case EditorCommand.Print: Print(); break;
            case EditorCommand.ZoomIn: ZoomBy(+1); break;
            case EditorCommand.ZoomOut: ZoomBy(-1); break;
            case EditorCommand.ZoomFit: _manualZoom = null; ApplyZoom(); break;
            case EditorCommand.SelectAll:
                if (_doc is null) break;
                Board.CommitTextEditor();
                if (Board.Tool == ToolKind.Selection) { Board.SelectAllPixels(); Board.Focus(); break; }
                SetTool(ToolKind.Move);
                Board.SelectAll();
                Board.Focus();
                break;
            case EditorCommand.Group: Group(); break;
            case EditorCommand.Ungroup: Ungroup(); break;
            case EditorCommand.Library: _app.ShowLibrary(); break;
        }
    }

    static bool IsOneGroup(IReadOnlyList<Annotation> items) =>
        items.Count > 0 && items[0].GroupId is { } g && items.All(a => a.GroupId == g);

    void Group()
    {
        var items = Board.SelectedObjects;
        if (items.Count < 2 || IsOneGroup(items)) return;
        Board.Apply(s => s.Group(Board.SelectedIds));
        BuildProps();
    }

    void Ungroup()
    {
        if (!Board.SelectedObjects.Any(a => a.GroupId is not null)) return;
        Board.Apply(s => s.Ungroup(Board.SelectedIds));
        BuildProps();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        Board.CommitTextEditor();
        FlushAutosave();
        if (_allowClose) return;
        e.Cancel = true;   // closing only hides; Snapr keeps running in the tray
        Hide();
    }

    /// <summary>Closes for app exit. Returns false if the user cancelled to keep unsaved edits.</summary>
    public bool Shutdown()
    {
        if (!ResolveUnsaved(allowCancel: true)) return false;
        _allowClose = true;
        Close();
        return true;
    }
}
