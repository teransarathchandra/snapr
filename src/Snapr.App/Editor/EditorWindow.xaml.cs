using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Snapr.App.Rendering;
using Snapr.App.Services;
using Snapr.Core;

namespace Snapr.App.Editor;

public partial class EditorWindow : Window
{
    static readonly (ToolKind Tool, string Name, Key Key)[] Tools =
    [
        (ToolKind.Move, "Select and move", Key.V), (ToolKind.Arrow, "Arrow", Key.A), (ToolKind.Text, "Text", Key.T),
        (ToolKind.Callout, "Callout", Key.K), (ToolKind.Shape, "Shape", Key.S), (ToolKind.Pen, "Pen", Key.P),
        (ToolKind.Highlight, "Highlighter", Key.H), (ToolKind.Step, "Step", Key.N), (ToolKind.Blur, "Blur", Key.B),
        (ToolKind.Magnify, "Magnify", Key.M), (ToolKind.Crop, "Crop", Key.C), (ToolKind.Eraser, "Eraser", Key.E),
    ];

    readonly AppController _app;
    readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Dictionary<ToolKind, RadioButton> _toolButtons = new();
    CaptureDocument? _doc;
    UndoStack? _undo;
    BitmapSource? _base;
    string? _path;
    bool _dirty;
    bool _fit = true;
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
        Board.SelectionChanged += BuildProps;
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
        Board.Load(_base, doc.State);
        TitleText.Text = $"{Path.GetFileNameWithoutExtension(path)}  ·  {doc.PixelWidth} × {doc.PixelHeight}";
        _fit = true;
        UpdateEmptyState();
        UpdateUndoButtons();
        if (saveNow) SaveNow();
        else RefreshRecents();
        ShowAndActivate();
        Dispatcher.BeginInvoke(ApplyZoom, DispatcherPriority.Loaded);
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
        Board.SetState(_undo.Current);
        _dirty = true;
        _autosave.Stop();
        _autosave.Start();
        UpdateUndoButtons();
        ApplyZoom();
    }

    void Undo() { if (_undo is { CanUndo: true }) { Board.CommitTextEditor(); _undo.Undo(); _lastMergeKey = null; ShowState(); BuildProps(); } }
    void Redo() { if (_undo is { CanRedo: true }) { _undo.Redo(); _lastMergeKey = null; ShowState(); BuildProps(); } }

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
            var thumb = ImageIo.EncodePng(ImageIo.Thumbnail(DocumentRenderer.Render(_base, _doc.State, applyEffects: false)));
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

    BitmapSource RenderForExport() => DocumentRenderer.Render(_base!, _undo!.Current, applyEffects: true);

    void Copy()
    {
        if (_doc is null) return;
        Board.CommitTextEditor();
        if (ClipboardImage.Set(RenderForExport())) _app.Notify("Copied", "The image is on the clipboard.");
        else MessageBox.Show(this, "Another app is holding the clipboard. Try again.", "Snapr", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    void ExportImage(string format)
    {
        if (_doc is null) return;
        Board.CommitTextEditor();
        var dialog = new SaveFileDialog
        {
            Filter = format == "jpg" ? "JPEG image|*.jpg" : "PNG image|*.png",
            FileName = Path.GetFileNameWithoutExtension(_path) + "." + format,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var image = RenderForExport();
            File.WriteAllBytes(dialog.FileName, format == "jpg" ? ImageIo.EncodeJpg(image, _app.Settings.JpgQuality) : ImageIo.EncodePng(image));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Export failed: {dialog.FileName}", ex);
            MessageBox.Show(this, $"Couldn't save the image.\n\n{ex.Message}", "Snapr", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            SnaprFile.Save(_doc, ImageIo.EncodePng(ImageIo.Thumbnail(DocumentRenderer.Render(_base, _doc.State, false))), dialog.FileName);
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
    void Copy_Click(object sender, RoutedEventArgs e) => Copy();
    void Save_Click(object sender, RoutedEventArgs e) => ExportImage(_app.Settings.ExportFormat);
    void ExportPng_Click(object sender, RoutedEventArgs e) => ExportImage("png");
    void ExportJpg_Click(object sender, RoutedEventArgs e) => ExportImage("jpg");
    void ExportSnapr_Click(object sender, RoutedEventArgs e) => ExportSnapr();

    void Export_Click(object sender, RoutedEventArgs e)
    {
        ExportButton.ContextMenu.PlacementTarget = ExportButton;
        ExportButton.ContextMenu.IsOpen = true;
    }

    void ShowInFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_path is not null && File.Exists(_path)) Process.Start("explorer.exe", $"/select,\"{_path}\"");
    }

    void GrabText_Click(object sender, RoutedEventArgs e)
    {
        if (_base is null) return;
        var area = Board.Selected is { } s ? AnnotationGeometry.Bounds(s).Intersect(Board.View) : Board.View;
        if (area.IsEmpty) area = Board.View;
        int x = (int)Math.Max(0, Math.Floor(area.X)), y = (int)Math.Max(0, Math.Floor(area.Y));
        int w = Math.Max(1, Math.Min(_base.PixelWidth - x, (int)Math.Ceiling(area.W))), h = Math.Max(1, Math.Min(_base.PixelHeight - y, (int)Math.Ceiling(area.H)));
        var crop = new CroppedBitmap(_base, new Int32Rect(x, y, w, h));
        crop.Freeze();
        _app.Fire(() => _app.GrabTextAsync(crop));
    }

    // ---------- Zoom ----------

    void ApplyZoom()
    {
        if (_base is null || !IsLoaded) return;
        var view = Board.View;
        var pixel = 1 / VisualTreeHelper.GetDpi(this).DpiScaleX;   // 1 image pixel = 1 screen pixel
        // Leave room for a scrollbar so "fit" never overflows by a rounding pixel and triggers scrollbars.
        var fit = Math.Min((CanvasScroller.ActualWidth - 66 - SystemParameters.VerticalScrollBarWidth) / view.W,
            (CanvasScroller.ActualHeight - 66 - SystemParameters.HorizontalScrollBarHeight) / view.H);
        var zoom = _fit ? Math.Max(0.05, Math.Min(pixel, fit)) : pixel;
        Board.Zoom = zoom;   // screen units per image pixel; the canvas divides handle sizes by it
        Board.LayoutTransform = new ScaleTransform(zoom, zoom);
        ZoomButton.Content = _fit ? $"Fit · {zoom / pixel:P0}" : "100%";
    }

    void Zoom_Click(object sender, RoutedEventArgs e)
    {
        _fit = !_fit;
        ApplyZoom();
    }

    // ---------- Recents ----------

    public void RefreshRecents()
    {
        RecentsList.Items.Clear();
        foreach (var item in RecentsIndex.List([CapturePaths.PicturesRoot, CapturePaths.VideosRoot]))
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
            var button = new Button
            {
                Content = content, Padding = new Thickness(2), Margin = new Thickness(4, 10, 4, 10),
                ToolTip = Path.GetFileName(item.Path),
                BorderBrush = current ? (Brush)FindResource("AccentBrush") : Brushes.Transparent,
                BorderThickness = new Thickness(current ? 2 : 0),
            };
            AutomationProperties.SetName(button, Path.GetFileName(item.Path));
            button.Click += (_, _) => OpenRecent(item);
            RecentsList.Items.Add(button);
        }
    }

    void OpenRecent(RecentItem item)
    {
        if (item.IsVideo)
        {
            Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
            return;
        }
        if (string.Equals(item.Path, _path, StringComparison.OrdinalIgnoreCase)) return;
        if (TryLoad(item.Path) is { } doc) Open(doc, item.Path, saveNow: false);
    }

    // ---------- Keyboard and lifetime ----------

    void OnKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl && e.Key == Key.Z) Undo();
        else if (ctrl && e.Key == Key.Y) Redo();
        else if (ctrl && e.Key == Key.C) Copy();
        else if (ctrl && e.Key == Key.S) ExportImage(_app.Settings.ExportFormat);
        else if (e.Key == Key.Delete) Board.DeleteSelected();
        else if (Keyboard.Modifiers == ModifierKeys.None && Array.FindIndex(Tools, t => t.Key == e.Key) is var i and >= 0) SetTool(Tools[i].Tool);
        else return;
        e.Handled = true;
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
