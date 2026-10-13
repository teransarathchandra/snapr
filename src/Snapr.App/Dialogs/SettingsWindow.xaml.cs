using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using Snapr.App.Services;
using Snapr.Core;
using Snapr.Core.Recording;
using Snapr.Core.Settings;

namespace Snapr.App.Dialogs;

public partial class SettingsWindow : Window
{
    sealed record MicChoice(string? Id, string Name);

    readonly AppController _app;

    public SettingsWindow(AppController app)
    {
        _app = app;
        InitializeComponent();
        Icon = AppIcon.Render(64);
        var s = app.Settings;
        ImageHotkey.Text = s.Hotkeys.ImageCapture;
        VideoHotkey.Text = s.Hotkeys.VideoCapture;
        ScrollHotkey.Text = s.Hotkeys.ScrollingCapture;
        RepeatHotkey.Text = s.Hotkeys.RepeatLastCapture;
        PauseHotkey.Text = s.Hotkeys.VideoPauseResume;
        DelaySlider.Value = s.CaptureDelaySeconds;
        CursorCheck.IsChecked = s.IncludeCursor;
        PngRadio.IsChecked = s.ExportFormat != "jpg";
        JpgRadio.IsChecked = s.ExportFormat == "jpg";
        QualitySlider.Value = Math.Clamp(s.JpgQuality, 50, 100);
        FfmpegBox.Text = s.FfmpegPath ?? "";
        SystemAudioCheck.IsChecked = s.RecordSystemAudio;
        MicCheck.IsChecked = s.RecordMicrophone;
        LoadMicrophones(s.MicDeviceId);
        UpdateFfmpegStatus();
        FfmpegBox.TextChanged += (_, _) => UpdateFfmpegStatus();
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
        var hotkeys = new[] { ImageHotkey.Text, VideoHotkey.Text, ScrollHotkey.Text, RepeatHotkey.Text, PauseHotkey.Text }
            .Where(h => h.Length > 0).ToArray();   // empty = disabled
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
        var ffmpeg = FfmpegBox.Text.Trim();
        _app.UpdateSettings(_app.Settings with
        {
            Hotkeys = new HotkeySettings(ImageHotkey.Text, VideoHotkey.Text, ScrollHotkey.Text, RepeatHotkey.Text, PauseHotkey.Text),
            CaptureDelaySeconds = (int)DelaySlider.Value,
            IncludeCursor = CursorCheck.IsChecked == true,
            ExportFormat = JpgRadio.IsChecked == true ? "jpg" : "png",
            JpgQuality = (int)QualitySlider.Value,
            FfmpegPath = ffmpeg.Length == 0 ? null : ffmpeg,
            RecordSystemAudio = SystemAudioCheck.IsChecked == true,
            RecordMicrophone = MicCheck.IsChecked == true,
            MicDeviceId = (MicCombo.SelectedItem as MicChoice)?.Id,
        });
        Close();
    }
}
