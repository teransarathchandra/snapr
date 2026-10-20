using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using Snapr.App.Services;
using Snapr.Core;
using Snapr.Core.Output;
using Snapr.Core.Recording;
using Snapr.Core.Settings;

namespace Snapr.App.Dialogs;

public partial class SettingsWindow : Window
{
    sealed record MicChoice(string? Id, string Name);
    sealed record CameraChoice(string? Id, string Name);

    string? _cameraId;
    bool _camerasLoaded;

    readonly AppController _app;

    public SettingsWindow(AppController app)
    {
        _app = app;
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height;
        Icon = AppIcon.Render(64);
        var s = app.Settings;
        ImageHotkey.Text = s.Hotkeys.ImageCapture;
        VideoHotkey.Text = s.Hotkeys.VideoCapture;
        ScrollHotkey.Text = s.Hotkeys.ScrollingCapture;
        RepeatHotkey.Text = s.Hotkeys.RepeatLastCapture;
        PauseHotkey.Text = s.Hotkeys.VideoPauseResume;
        PanoHotkey.Text = s.Hotkeys.PanoramicCapture;
        DelaySlider.Value = s.CaptureDelaySeconds;
        CursorCheck.IsChecked = s.IncludeCursor;
        PngRadio.IsChecked = s.ExportFormat != "jpg";
        JpgRadio.IsChecked = s.ExportFormat == "jpg";
        QualitySlider.Value = Math.Clamp(s.JpgQuality, 50, 100);
        FfmpegBox.Text = s.FfmpegPath ?? "";
        SystemAudioCheck.IsChecked = s.RecordSystemAudio;
        MicCheck.IsChecked = s.RecordMicrophone;
        LoadMicrophones(s.MicDeviceId);
        var w = s.Webcam;
        WebcamCheck.IsChecked = w.Enabled;
        _cameraId = w.DeviceId;
        CameraCombo.ItemsSource = new[] { new CameraChoice(null, "Default camera") };
        CameraCombo.SelectedIndex = 0;
        ShapeCombo.ItemsSource = new[] { "Circle", "Rounded rectangle" };
        ShapeCombo.SelectedIndex = (int)w.Shape;
        SizeCombo.ItemsSource = new[] { "Small", "Medium", "Large" };
        SizeCombo.SelectedIndex = (int)w.Size;
        MirrorCheck.IsChecked = w.Mirror;
        WebcamRows.IsEnabled = w.Enabled;
        Loaded += async (_, _) => await LoadCamerasAsync();
        UpdateFfmpegStatus();
        FfmpegBox.TextChanged += (_, _) => UpdateFfmpegStatus();
        _presets.AddRange(s.Presets);
        ShowPresets();
        var share = s.Share;
        ShareFolderBox.Text = share.Folder ?? "";
        SharePrefixBox.Text = share.UrlPrefix ?? "";
        UploadUrlBox.Text = share.Upload?.Url ?? "";
        UploadFieldBox.Text = share.Upload?.FieldName ?? "file";
        UploadHeaderNameBox.Text = share.Upload?.HeaderName ?? "";
        _savedHeader = share.Upload?.HeaderValueProtected;   // the PasswordBox is never pre-filled
        ShowHeaderHint();
        LinkBodyRadio.IsChecked = share.Upload?.LinkMode != UploadLinkMode.JsonField;
        LinkJsonRadio.IsChecked = share.Upload?.LinkMode == UploadLinkMode.JsonField;
        JsonPathBox.Text = share.Upload?.JsonPath ?? "";
        JsonPathBox.IsEnabled = LinkJsonRadio.IsChecked == true;
    }

    string? _savedHeader;

    /// <summary>Brings a section into view when Settings is opened from elsewhere (only "Sharing" for now).</summary>
    public void ShowSection(string? section)
    {
        if (section == "Sharing") Dispatcher.BeginInvoke(() => SharingHeader.BringIntoView(), DispatcherPriority.Loaded);
    }

    void ShowHeaderHint()
    {
        HeaderHint.Text = _savedHeader is null ? "" : "A value is saved.";
        ClearHeaderButton.IsEnabled = _savedHeader is not null;
    }

    void ClearHeader_Click(object sender, RoutedEventArgs e)
    {
        _savedHeader = null;
        UploadHeaderValueBox.Clear();
        ShowHeaderHint();
    }

    void LinkMode_Changed(object sender, RoutedEventArgs e) { if (JsonPathBox is not null) JsonPathBox.IsEnabled = LinkJsonRadio.IsChecked == true; }

    void BrowseShareFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (!string.IsNullOrWhiteSpace(ShareFolderBox.Text) && Directory.Exists(ShareFolderBox.Text.Trim())) dialog.InitialDirectory = ShareFolderBox.Text.Trim();
        if (dialog.ShowDialog(this) == true) ShareFolderBox.Text = dialog.FolderName;
    }

    static string? NullIfEmpty(string text) => text.Trim() is { Length: > 0 } t ? t : null;

    UploadLinkMode LinkMode => LinkJsonRadio.IsChecked == true ? UploadLinkMode.JsonField : UploadLinkMode.WholeBody;

    /// <summary>The upload values as entered: a typed header value replaces the saved one (protected in memory).</summary>
    UploadSettings TypedUpload() => new(UploadUrlBox.Text.Trim(), NullIfEmpty(UploadFieldBox.Text) ?? "file", NullIfEmpty(UploadHeaderNameBox.Text),
        UploadHeaderValueBox.Password.Length > 0 ? SecretStore.Protect(UploadHeaderValueBox.Password) : _savedHeader,
        LinkMode, LinkMode == UploadLinkMode.JsonField ? NullIfEmpty(JsonPathBox.Text) : null);

    void ShowShareError(string? message)
    {
        ShareError.Text = message ?? "";
        ShareError.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    async void TestUpload_Click(object sender, RoutedEventArgs e)
    {
        var error = ShareRules.UploadUrlError(UploadUrlBox.Text) ?? ShareRules.JsonPathError(LinkMode, JsonPathBox.Text);
        ShowShareError(error);
        if (error is not null) return;
        var upload = TypedUpload();
        var folder = Path.Combine(Path.GetTempPath(), "snapr-share-" + Guid.NewGuid().ToString("N"));
        var previous = Cursor;
        Cursor = Cursors.Wait;
        TestUploadButton.IsEnabled = false;
        try
        {
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, "snapr-test.png");
            File.WriteAllBytes(file, Convert.FromBase64String(TestPng));
            var link = await new ShareService().UploadAsync(file, upload, CancellationToken.None);
            MessageBox.Show(this, "The upload worked. The link is:\n\n" + link, "Snapr", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (ShareException ex)
        {
            MessageBox.Show(this, ex.Message, "Snapr", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write("Test upload failed", ex);
            MessageBox.Show(this, "Snapr couldn't prepare the test file.", "Snapr", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Cursor = previous;
            TestUploadButton.IsEnabled = true;
            try { Directory.Delete(folder, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Write("Couldn't delete the test upload folder", ex); }
        }
    }

    // A 1 x 1 transparent PNG.
    const string TestPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    sealed record PresetRow(CapturePreset Preset)
    {
        public string Summary =>
            $"{Preset.Name}  ·  {TargetText(Preset.Target)}  ·  {ActionText(Preset.Action)}{(Preset.Hotkey.Length > 0 ? "  ·  " + Preset.Hotkey : "")}";
        static string TargetText(PresetTarget t) => t switch
        {
            PresetTarget.ScreenUnderMouse => "Screen under mouse", PresetTarget.AllScreens => "All screens", PresetTarget.FixedArea => "Fixed area", _ => "Choose area",
        };
        static string ActionText(PresetAction a) => a switch
        {
            PresetAction.Copy => "Copy", PresetAction.GrabText => "Grab text", PresetAction.SaveToFolder => "Save to folder", _ => "Edit",
        };
    }

    readonly List<CapturePreset> _presets = [];

    void ShowPresets(CapturePreset? select = null)
    {
        PresetList.ItemsSource = _presets.Select(p => new PresetRow(p)).ToArray();
        PresetList.SelectedIndex = select is null ? -1 : _presets.FindIndex(p => p.Id == select.Id);
        PresetList_SelectionChanged(this, null!);
    }

    CapturePreset? SelectedPreset => (PresetList.SelectedItem as PresetRow)?.Preset;

    void PresetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        EditPreset.IsEnabled = DeletePreset.IsEnabled = RunPreset.IsEnabled = SelectedPreset is not null;
        AddPreset.IsEnabled = _presets.Count < PresetRules.MaxPresets;
    }

    /// <summary>The app hotkeys as currently typed in this window (not yet saved), for clash checks.</summary>
    HotkeySettings TypedHotkeys() => new(ImageHotkey.Text, VideoHotkey.Text, ScrollHotkey.Text, RepeatHotkey.Text, PauseHotkey.Text, PanoHotkey.Text);

    void AddPreset_Click(object sender, RoutedEventArgs e) => OpenEditor(null);

    void EditPreset_Click(object sender, RoutedEventArgs e) { if (SelectedPreset is { } p) OpenEditor(p); }

    void OpenEditor(CapturePreset? existing)
    {
        var others = _presets.Where(p => p.Id != existing?.Id).ToArray();
        IsEnabled = false;
        var editor = new PresetEditorWindow(this, _app, existing, others, TypedHotkeys, saved =>
        {
            var i = _presets.FindIndex(p => p.Id == saved.Id);
            if (i >= 0) _presets[i] = saved; else _presets.Add(saved);
            ShowPresets(saved);
        });
        editor.Closed += (_, _) => { IsEnabled = true; Activate(); };
        editor.Show();
    }

    void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPreset is not { } p) return;
        if (MessageBox.Show(this, $"Delete the preset {p.Name}?", "Snapr", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _presets.RemoveAll(x => x.Id == p.Id);
        ShowPresets();
    }

    void RunPreset_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPreset is not { } p) return;
        _app.Fire(async () =>
        {
            Hide();
            try
            {
                await Task.Delay(250);   // let the window leave the screen before the capture
                await _app.RunPresetAsync(p);
            }
            finally
            {
                ShowActivated = false;   // keep the capture editor the preset opened on top
                Show();
                ShowActivated = true;
            }
        });
    }

    void LoadMicrophones(string? selectedId)
    {
        var choices = new List<MicChoice> { new(null, "Default microphone") };
        try
        {
            using var devices = new MMDeviceEnumerator();
            choices.AddRange(devices.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).Select(d => new MicChoice(d.ID, d.FriendlyName)));
        }
        catch (COMException ex)
        {
            Log.Write("Could not list microphones", ex);
        }
        MicCombo.ItemsSource = choices;
        MicCombo.SelectedItem = choices.FirstOrDefault(c => c.Id == selectedId) ?? choices[0];
    }

    async Task LoadCamerasAsync()
    {
        try
        {
            var list = await WebcamService.ListAsync();
            var choices = new List<CameraChoice> { new(null, "Default camera") };
            choices.AddRange(list.Select(c => new CameraChoice(c.Id, c.Name)));
            CameraCombo.ItemsSource = choices;
            CameraCombo.SelectedItem = choices.FirstOrDefault(c => c.Id is not null && c.Id == _cameraId) ?? choices[0];
            _camerasLoaded = true;
        }
        catch (Exception ex)
        {
            Log.Write("Could not list cameras", ex);   // keep the saved camera id; the list stays at "Default camera"
        }
    }

    void WebcamCheck_Changed(object sender, RoutedEventArgs e) => WebcamRows.IsEnabled = WebcamCheck.IsChecked == true;

    void Hotkey_KeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Keyboard.Modifiers == ModifierKeys.None && key is Key.Back or Key.Delete) ((TextBox)sender).Text = "";
        else if (HotkeyGesture.Format(Keyboard.Modifiers, key) is { } gesture) ((TextBox)sender).Text = gesture;
    }

    void Delay_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => DelayValue.Text = $"{(int)e.NewValue} s";
    void Quality_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => QualityValue.Text = $"{(int)e.NewValue}";

    void UpdateFfmpegStatus()
    {
        var found = FfmpegLocator.Find(string.IsNullOrWhiteSpace(FfmpegBox.Text) ? null : FfmpegBox.Text, Environment.GetEnvironmentVariable("PATH"));
        FfmpegStatus.Text = found is null ? "Not found. Video recording is unavailable until you set it." : $"Using {found}";
    }

    void DetectFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        FfmpegBox.Text = FfmpegLocator.Find(null, Environment.GetEnvironmentVariable("PATH")) ?? "";
        UpdateFfmpegStatus();
    }

    void BrowseFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "ffmpeg.exe|ffmpeg.exe|Programs|*.exe" };
        if (dialog.ShowDialog(this) == true) FfmpegBox.Text = dialog.FileName;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        var appKeys = new[] { ImageHotkey.Text, VideoHotkey.Text, ScrollHotkey.Text, RepeatHotkey.Text, PauseHotkey.Text, PanoHotkey.Text }
            .Where(h => h.Length > 0).ToArray();   // empty = disabled
        var hotkeys = appKeys.Concat(_presets.Select(p => p.Hotkey).Where(h => h.Length > 0)).ToArray();
        if (hotkeys.Any(h => !HotkeyGesture.TryParse(h, out _, out _)))
        {
            MessageBox.Show(this, "One of the hotkeys isn't valid. Click the box and press a key combination.", "Snapr", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (hotkeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() < hotkeys.Length)
        {
            MessageBox.Show(this, "Each action needs a different hotkey.", "Snapr", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var shareError = ShareSetup.Validate(ShareFolderBox.Text, SharePrefixBox.Text, UploadUrlBox.Text, LinkMode, JsonPathBox.Text);
        ShowShareError(shareError);
        if (shareError is not null)
        {
            SharingHeader.BringIntoView();
            return;
        }
        var ffmpeg = FfmpegBox.Text.Trim();
        _app.UpdateSettings(_app.Settings with
        {
            Hotkeys = new HotkeySettings(ImageHotkey.Text, VideoHotkey.Text, ScrollHotkey.Text, RepeatHotkey.Text, PauseHotkey.Text, PanoHotkey.Text),
            CaptureDelaySeconds = (int)DelaySlider.Value,
            IncludeCursor = CursorCheck.IsChecked == true,
            ExportFormat = JpgRadio.IsChecked == true ? "jpg" : "png",
            JpgQuality = (int)QualitySlider.Value,
            FfmpegPath = ffmpeg.Length == 0 ? null : ffmpeg,
            RecordSystemAudio = SystemAudioCheck.IsChecked == true,
            RecordMicrophone = MicCheck.IsChecked == true,
            MicDeviceId = (MicCombo.SelectedItem as MicChoice)?.Id,
            Webcam = new WebcamSettings(WebcamCheck.IsChecked == true,
                _camerasLoaded ? (CameraCombo.SelectedItem as CameraChoice)?.Id : _cameraId,   // list not loaded yet: keep the saved camera
                (WebcamShape)Math.Max(0, ShapeCombo.SelectedIndex), (WebcamSize)Math.Max(0, SizeCombo.SelectedIndex), MirrorCheck.IsChecked == true),
            Presets = _presets.ToArray(),
            Share = new ShareSettings(NullIfEmpty(ShareFolderBox.Text), NullIfEmpty(SharePrefixBox.Text),
                string.IsNullOrWhiteSpace(UploadUrlBox.Text) ? null : TypedUpload()),
        });
        Close();
    }
}
