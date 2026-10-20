using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualBasic.FileIO;
using Snapr.App.Dialogs;
using Snapr.App.Rendering;
using Snapr.Core;

namespace Snapr.App.Library;

/// <summary>The Library (spec parity 13): every capture and video grouped by month, with search, kind and tag filters.</summary>
public partial class LibraryWindow : Window
{
    readonly AppController _app;
    readonly LibraryIndex _index = new();
    IReadOnlyList<LibraryItem> _all = [];
    readonly HashSet<string> _chosenTags = new(StringComparer.OrdinalIgnoreCase);
    readonly DispatcherTimer _typing = new() { Interval = TimeSpan.FromMilliseconds(150) };
    readonly SemaphoreSlim _thumbs = new(4);
    readonly Dictionary<(string, DateTime), ImageSource> _thumbCache = [];
    CancellationTokenSource? _load;
    bool _built;

    public LibraryWindow(AppController app)
    {
        _app = app;
        InitializeComponent();
        _typing.Tick += (_, _) =>
        {
            _typing.Stop();
            ApplyFilter();
        };
        Loaded += (_, _) => Rescan();
        Activated += (_, _) => { if (IsLoaded) Rescan(); };   // Show() activates before Loaded, which scans
        Closed += (_, _) =>
        {
            _typing.Stop();
            _load?.Cancel();
        };
        UpdateButtons();
    }

    IReadOnlyList<LibraryItem> Selected => Tiles.SelectedItems.Cast<LibraryTile>().Select(t => t.Item).ToArray();

    const string ScanFailed = "Some folders couldn't be read.";

    void Rescan()
    {
        IReadOnlyList<LibraryItem> next;
        try { next = _index.Scan([CapturePaths.PicturesRoot, CapturePaths.VideosRoot]); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No dialog: it would re-activate this window and rescan in a loop.
            Log.Write("Library: couldn't scan", ex);
            Status.Text = ScanFailed;
            return;
        }
        var hadError = Status.Text == ScanFailed;
        // Every save changes the modified time, so an unchanged list means nothing to redraw (and the selection stays).
        if (_built && next.Count == _all.Count && next.Zip(_all).All(p => p.First.Path == p.Second.Path && p.First.ModifiedUtc == p.Second.ModifiedUtc && p.First.Unreadable == p.Second.Unreadable))
        {
            if (hadError) ApplyFilter();   // same list, but the failure notice is stale
            return;
        }
        _built = true;
        _all = next;
        BuildChips();
        ApplyFilter();
    }

    void BuildChips()
    {
        var tags = LibrarySearch.AllTags(_all);
        _chosenTags.IntersectWith(tags);
        TagChips.Children.Clear();
        foreach (var tag in tags)
        {
            var chip = new ToggleButton { Content = tag, IsChecked = _chosenTags.Contains(tag), Margin = new Thickness(0, 0, 6, 6) };
            chip.Style = (Style)FindResource("ChipButton");
            chip.Click += (_, _) =>
            {
                if (chip.IsChecked == true) _chosenTags.Add(tag);
                else _chosenTags.Remove(tag);
                ApplyFilter();
            };
            TagChips.Children.Add(chip);
        }
    }

    void ApplyFilter()
    {
        var keep = Tiles.SelectedItems.Cast<LibraryTile>().Select(t => t.Item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var shown = LibrarySearch.Filter(_all, new(SearchBox.Text, (LibraryKind)Math.Max(0, KindBox.SelectedIndex), _chosenTags.ToArray()));
        var tiles = LibrarySearch.Months(shown).SelectMany(m => m.Items.Select(i => new LibraryTile(i, m.Heading))).ToList();
        var view = new ListCollectionView(tiles);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LibraryTile.Heading)));
        Tiles.ItemsSource = view;
        foreach (var t in tiles.Where(t => keep.Contains(t.Item.Path))) Tiles.SelectedItems.Add(t);
        var n = shown.Count;
        Status.Text = n == _all.Count ? $"{n} {(n == 1 ? "capture" : "captures")}" : $"{n} of {_all.Count} shown";
        UpdateButtons();

        _load?.Cancel();
        _load = new CancellationTokenSource();
        _ = LoadThumbnailsAsync(tiles, _load.Token);
    }

    async Task LoadThumbnailsAsync(IReadOnlyList<LibraryTile> tiles, CancellationToken token)
    {
        try { await Task.WhenAll(tiles.Where(t => !t.IsVideo && !t.Unreadable).Select(t => LoadThumbnailAsync(t, token))); }
        catch (OperationCanceledException) { }
    }

    async Task LoadThumbnailAsync(LibraryTile tile, CancellationToken token)
    {
        var key = (tile.Item.Path, tile.Item.ModifiedUtc);
        if (_thumbCache.TryGetValue(key, out var cached))
        {
            tile.Thumbnail = cached;
            return;
        }
        await _thumbs.WaitAsync(token);   // at most 4 reads and decodes at a time
        try
        {
            var image = await Task.Run(() => (ImageSource)ImageIo.DecodePng(SnaprFile.ReadThumbnail(tile.Item.Path)), token);
            if (image.CanFreeze) image.Freeze();
            _thumbCache[key] = image;
            tile.Thumbnail = image;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            // leave the placeholder
        }
        finally { _thumbs.Release(); }
    }

    void UpdateButtons()
    {
        var sel = Selected;
        OpenButton.IsEnabled = FolderButton.IsEnabled = DeleteButton.IsEnabled = sel.Count > 0;
        TagsButton.IsEnabled = TemplateButton.IsEnabled = sel.Count > 0 && sel.All(i => !i.IsVideo && !i.Unreadable);
    }

    void Menu_Opened(object sender, RoutedEventArgs e)
    {
        var items = ((ContextMenu)sender).Items.Cast<MenuItem>().ToArray();
        items[0].IsEnabled = OpenButton.IsEnabled;
        items[1].IsEnabled = TagsButton.IsEnabled;
        items[2].IsEnabled = TemplateButton.IsEnabled;
        items[3].IsEnabled = FolderButton.IsEnabled;
        items[4].IsEnabled = DeleteButton.IsEnabled;
    }

    void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        _typing.Stop();
        _typing.Start();
    }

    void Kind_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) ApplyFilter();
    }

    void Refresh_Click(object sender, RoutedEventArgs e) => Rescan();

    void Tiles_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    void Tiles_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(Tiles, d) is not null) Open_Click(sender, e);
    }

    void Tiles_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && OpenButton.IsEnabled) Open_Click(sender, e);
        else if (e.Key == Key.Delete && DeleteButton.IsEnabled) Delete_Click(sender, e);
        else return;
        e.Handled = true;
    }

    void Open_Click(object sender, RoutedEventArgs e)
    {
        if (Selected.FirstOrDefault() is not { } item) return;
        if (item.IsVideo) _app.ShowVideoEditor(item.Path);
        else _app.OpenImageFile(item.Path);
    }

    void Tags_Click(object sender, RoutedEventArgs e)
    {
        var sel = Selected;
        if (sel.Count == 0 || !TagsButton.IsEnabled) return;
        var shared = TagRules.Shared(sel.Select(i => i.Tags));
        var dialog = new TagsWindow(this, shared);
        if (dialog.ShowDialog() != true || dialog.Result is not { } result) return;

        var failed = new List<string>();
        foreach (var item in sel)
        {
            var tags = TagRules.Merge(item.Tags, shared, result);
            var inEditor = _app.SetTagsInEditor(item.Path, tags);
            if (inEditor is null)
            {
                try { SnaprFile.SetTags(item.Path, tags); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or System.Text.Json.JsonException)
                {
                    Log.Write($"Library: couldn't save tags for {item.Path}", ex);
                    failed.Add(Path.GetFileName(item.Path));
                }
            }
            else if (inEditor == false) failed.Add(Path.GetFileName(item.Path));
            _index.Forget(item.Path);
        }
        if (failed.Count > 0)
            MessageBox.Show(this, $"Snapr couldn't save tags for: {string.Join(", ", failed)}", "Tags", MessageBoxButton.OK, MessageBoxImage.Warning);
        Rescan();
    }

    void Template_Click(object sender, RoutedEventArgs e)
    {
        var sel = Selected;
        if (sel.Count == 0 || !TemplateButton.IsEnabled) return;
        _app.ShowTemplate(this, sel.Select(i => i.Path).ToArray());
        Rescan();
    }

    void Folder_Click(object sender, RoutedEventArgs e)
    {
        if (Selected.FirstOrDefault() is { } item) Process.Start("explorer.exe", $"/select,\"{item.Path}\"");
    }

    void Delete_Click(object sender, RoutedEventArgs e)
    {
        var sel = Selected;
        if (sel.Count == 0) return;
        var what = sel.Count == 1 ? "1 item" : $"{sel.Count} items";
        if (MessageBox.Show(this, $"Move {what} to the Recycle Bin?", "Delete", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        var open = new List<string>();
        var failed = new List<string>();
        foreach (var item in sel)
        {
            var name = Path.GetFileName(item.Path);
            if (_app.IsOpenInEditor(item.Path) && !_app.ReleaseFromEditor(item.Path))
            {
                open.Add(name);
                continue;
            }
            try { FileSystem.DeleteFile(item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                Log.Write($"Library: couldn't delete {item.Path}", ex);
                failed.Add(name);
            }
        }
        if (open.Count > 0)
            MessageBox.Show(this, $"Close it in the editor first.\n\n{string.Join("\n", open)}", "Delete", MessageBoxButton.OK, MessageBoxImage.Information);
        if (failed.Count > 0)
            MessageBox.Show(this, $"Snapr couldn't delete: {string.Join(", ", failed)}", "Delete", MessageBoxButton.OK, MessageBoxImage.Warning);
        _app.RefreshRecents();
        Rescan();
    }
}
