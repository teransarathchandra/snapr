using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Snapr.App.Services;
using Snapr.Core;
using Snapr.Core.Recording;
using Snapr.Core.Video;

namespace Snapr.App.Video;

/// <summary>Trims a recording, removes parts from the middle and saves a new MP4 or GIF (spec parity 8 §5).</summary>
public sealed class VideoEditorWindow : Window
{
    readonly AppController _app;
    readonly string _file;
    readonly MediaElement _player = new() { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Stop, ScrubbingEnabled = true };
    readonly VideoTimeline _timeline = new() { IsEnabled = false, Margin = new Thickness(0, 12, 0, 12) };
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    readonly Button _play = new() { Content = "Play", IsEnabled = false, MinWidth = 72 };
    readonly TextBlock _time = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 12, 0), Text = "0:00.0 / 0:00.0" };
    readonly Button _remove = new() { Content = "Remove selection", IsEnabled = false };
    readonly Button _undo = new() { Content = "Undo", IsEnabled = false };
    readonly Button _redo = new() { Content = "Redo", IsEnabled = false };
    readonly Button _saveMp4 = new() { Content = "Save MP4…", IsEnabled = false };
    readonly Button _saveGif = new() { Content = "Save GIF…", IsEnabled = false };
    readonly ComboBox _width = new() { ItemsSource = new[] { "Original", "800", "640", "480" }, SelectedIndex = 2, Width = 90, Margin = new Thickness(8, 0, 8, 0) };
    readonly ComboBox _fps = new() { ItemsSource = new[] { 10, 15 }, SelectedIndex = 0, Width = 60, Margin = new Thickness(8, 0, 0, 0) };
    readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, Width = 200, Height = 12, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 8, 0) };
    readonly Button _cancel = new() { Content = "Cancel", Visibility = Visibility.Collapsed };
    readonly TextBlock _status = new() { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    readonly Stack<VideoCut> _undoStack = new(), _redoStack = new();
    readonly StackPanel _editRow, _saveRow;

    string? _ffmpeg;
    VideoInfo? _info;
    VideoCut _cut = VideoCut.For(1);
    CancellationTokenSource? _cts;
    bool _opened, _playing, _ready, _playFailed;
    string _statusMain = "";

    public VideoEditorWindow(AppController app, string file)
    {
        _app = app;
        _file = file;
        Title = $"{Path.GetFileName(file)} – Snapr video";
        Style = (Style)Application.Current.FindResource("SnaprWindow");
        Width = 960;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _status.Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush");

        var open = new Button { Content = "Open in default player" };
        open.Click += (_, _) => OpenDefault();
        var transport = Row(_play, _time, open);
        _editRow = Row(_remove, _undo, _redo);
        _saveRow = Row(_saveMp4, _saveGif, new TextBlock { Text = "Width", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) }, _width,
            new TextBlock { Text = "FPS", VerticalAlignment = VerticalAlignment.Center }, _fps, _progress, _cancel);

        var root = new DockPanel { Margin = new Thickness(20) };
        var bottom = new StackPanel();
        bottom.Children.Add(transport);
        bottom.Children.Add(_timeline);
        bottom.Children.Add(_editRow);
        bottom.Children.Add(new Border { Height = 8 });
        bottom.Children.Add(_saveRow);
        bottom.Children.Add(_status);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(_player);
        Content = root;

        _player.MediaOpened += (_, _) =>
        {
            _opened = true;
            _player.Pause();
            _player.Position = TimeSpan.Zero;
            UpdateControls();
        };
        _player.MediaFailed += (_, e) =>
        {
            Log.Write("Video editor: playback failed", e.ErrorException);
            _playFailed = true;
            SetStatus(_statusMain);
        };
        _player.MediaEnded += (_, _) =>
        {
            Pause();
            if (_ready) _player.Position = TimeSpan.FromSeconds(_cut.Kept[^1].End);
            UpdateTime();
        };
        _player.Source = new Uri(file);

        _timer.Tick += (_, _) => Tick();
        _play.Click += (_, _) => TogglePlay();
        _remove.Click += (_, _) => RemoveSelection();
        _undo.Click += (_, _) => Undo();
        _redo.Click += (_, _) => Redo();
        _saveMp4.Click += async (_, _) => await SaveMp4Async();
        _saveGif.Click += async (_, _) => await SaveGifAsync();
        _cancel.Click += (_, _) => _cts?.Cancel();
        _timeline.TrimChanged += (s, e) => Apply(_cut.WithTrim(s, e));
        _timeline.Seek += t =>
        {
            _player.Position = TimeSpan.FromSeconds(t);
            UpdateTime(t);
        };
        _timeline.SelectionChanged += UpdateControls;
        PreviewKeyDown += OnKey;
        Closing += (_, _) => _cts?.Cancel();   // ExportAsync deletes the .part; the window closes right away
        Closed += (_, _) =>
        {
            _timer.Stop();
            _player.Close();
        };
        Loaded += async (_, _) => await ProbeAsync();
    }

    static StackPanel Row(params UIElement[] items)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        foreach (var i in items)
        {
            if (i is Button b) b.Margin = new Thickness(0, 0, 8, 0);
            p.Children.Add(i);
        }
        return p;
    }

    void SetStatus(string text)
    {
        _statusMain = text;
        _status.Text = _playFailed ? $"{text} Windows couldn't play this video here.".Trim() : text;
    }

    /// <summary>Probe failed (no ffmpeg, ffmpeg didn't run, or unreadable file): only the player stays (spec parity 11 §7).</summary>
    void HideEditing() => _timeline.Visibility = _editRow.Visibility = _saveRow.Visibility = Visibility.Collapsed;

    async Task ProbeAsync()
    {
        _player.Pause();   // with Manual loading the media only opens on Play/Pause

        _ffmpeg = FfmpegLocator.Find(_app.Settings.FfmpegPath, Environment.GetEnvironmentVariable("PATH"));
        if (_ffmpeg is null)
        {
            SetStatus("Trimming and GIF need ffmpeg. Set it in Settings.");
            HideEditing();
            return;
        }
        try
        {
            _info = await VideoService.ProbeAsync(_ffmpeg, _file);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Write("Video editor: probe failed", ex);
            SetStatus($"Snapr couldn't run ffmpeg: {ex.Message}");
            HideEditing();
            return;
        }
        if (_info is null)
        {
            SetStatus("Snapr couldn't read this video.");
            HideEditing();
            return;
        }
        _cut = VideoCut.For(_info.Duration);
        _timeline.Cut = _cut;
        _ready = true;
        UpdateControls();
    }

    bool Saving => _cts is not null;

    void UpdateControls()
    {
        _play.IsEnabled = _opened;
        _timeline.IsEnabled = _ready && !Saving;
        _remove.IsEnabled = _ready && !Saving && _timeline.Selection is not null;
        _undo.IsEnabled = _ready && !Saving && _undoStack.Count > 0;
        _redo.IsEnabled = _ready && !Saving && _redoStack.Count > 0;
        _saveMp4.IsEnabled = _saveGif.IsEnabled = _width.IsEnabled = _fps.IsEnabled = _ready && !Saving;
        UpdateTime();
    }

    void Apply(VideoCut next)
    {
        if (next == _cut) return;
        _undoStack.Push(_cut);
        _redoStack.Clear();
        SetCut(next);
    }

    void SetCut(VideoCut cut)
    {
        _cut = cut;
        _timeline.Cut = cut;
        _timeline.Selection = null;
        UpdateControls();
    }

    void Undo()
    {
        if (_undoStack.Count == 0) return;
        _redoStack.Push(_cut);
        SetCut(_undoStack.Pop());
    }

    void Redo()
    {
        if (_redoStack.Count == 0) return;
        _undoStack.Push(_cut);
        SetCut(_redoStack.Pop());
    }

    void RemoveSelection()
    {
        if (_timeline.Selection is { } s) Apply(_cut.Remove(s));
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is ComboBox or ComboBoxItem or TextBox) return;
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (e.Key == Key.Space && _play.IsEnabled && !Saving) TogglePlay();
        else if (e.Key == Key.Delete && _remove.IsEnabled) RemoveSelection();
        else if (e.Key == Key.Z && ctrl && _undo.IsEnabled) Undo();
        else if (e.Key == Key.Y && ctrl && _redo.IsEnabled) Redo();
        else return;
        e.Handled = true;
    }

    void TogglePlay()
    {
        if (_playing)
        {
            Pause();
            return;
        }
        if (_ready && _cut.NextKept(_player.Position.TotalSeconds) is null) _player.Position = TimeSpan.FromSeconds(_cut.Kept[0].Start);
        _player.Play();
        _playing = true;
        _play.Content = "Pause";
        _timer.Start();
    }

    void Pause()
    {
        _player.Pause();
        _playing = false;
        _play.Content = "Play";
        _timer.Stop();
    }

    void Tick()
    {
        if (_ready)
        {
            var s = _player.Position.TotalSeconds;
            var next = _cut.NextKept(s);
            if (next is null)
            {
                Pause();
                _player.Position = TimeSpan.FromSeconds(_cut.Kept[^1].End);
            }
            else if (next > s + 0.02) _player.Position = TimeSpan.FromSeconds(next.Value);
        }
        UpdateTime();
    }

    void UpdateTime(double? at = null)
    {
        var s = at ?? _player.Position.TotalSeconds;
        _timeline.Position = s;
        var shown = _ready ? _cut.Kept.Sum(k => Math.Clamp(s, k.Start, k.End) - k.Start) : s;
        var total = _ready ? _cut.KeptDuration : _player.NaturalDuration.HasTimeSpan ? _player.NaturalDuration.TimeSpan.TotalSeconds : 0;
        _time.Text = $"{Fmt(shown)} / {Fmt(total)}";
    }

    static string Fmt(double s)
    {
        var tenths = (int)Math.Round(s * 10);
        return $"{tenths / 600}:{tenths % 600 / 10.0:00.0}";
    }

    void OpenDefault()
    {
        try { Process.Start(new ProcessStartInfo(_file) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        {
            Log.Write("Video editor: could not open the default player", ex);
            SetStatus($"Snapr couldn't open the video: {ex.Message}");
        }
    }

    string? Ask(string filter, string name)
    {
        var dialog = new SaveFileDialog
        {
            Filter = filter,
            FileName = name,
            InitialDirectory = Path.GetDirectoryName(_file),
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != true) return null;
        if (string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(_file), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Choose a different name: this is the original recording.", "Snapr", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }
        return dialog.FileName;
    }

    async Task SaveMp4Async()
    {
        if (Ask("MP4 video|*.mp4", $"{Path.GetFileNameWithoutExtension(_file)} edited.mp4") is not { } path) return;
        var kept = _cut.Kept;
        await RunExport(part => VideoExportArgs.Mp4(_file, kept, _info!.HasAudio, part), path, _cut.KeptDuration);
    }

    async Task SaveGifAsync()
    {
        if (Ask("GIF animation|*.gif", $"{Path.GetFileNameWithoutExtension(_file)}.gif") is not { } path) return;
        var kept = _cut.Kept;
        int? width = int.TryParse(_width.SelectedItem as string, out var w) && w < _info!.Width ? w : null;
        var fps = (int)_fps.SelectedItem;
        await RunExport(part => VideoExportArgs.Gif(_file, kept, width, fps, part), path, _cut.KeptDuration);
    }

    async Task RunExport(Func<string, IReadOnlyList<string>> args, string path, double seconds)
    {
        Pause();
        _cts = new CancellationTokenSource();
        _progress.Value = 0;
        _progress.Visibility = _cancel.Visibility = Visibility.Visible;
        UpdateControls();
        try
        {
            await VideoService.ExportAsync(_ffmpeg!, args, path, seconds, new Progress<double>(v => _progress.Value = v), _cts.Token);
            _app.RefreshRecents();
            _app.Notify("Saved", Path.GetFileName(path));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is VideoExportException or Win32Exception or IOException or UnauthorizedAccessException)
        {
            Log.Write("Video editor: export failed", ex);
            if (IsLoaded) MessageBox.Show(this, ex.Message, "Snapr", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _progress.Visibility = _cancel.Visibility = Visibility.Collapsed;
            UpdateControls();
        }
    }
}
