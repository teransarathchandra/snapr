namespace Snapr.Core.Settings;

/// <summary>Global hotkeys. An empty string disables that hotkey.</summary>
public sealed record HotkeySettings(
    string ImageCapture = "PrintScreen",
    string VideoCapture = "Shift+PrintScreen",
    string ScrollingCapture = "Ctrl+PrintScreen",
    string RepeatLastCapture = "Ctrl+Alt+PrintScreen",
    string VideoPauseResume = "Ctrl+Shift+PrintScreen",
    string PanoramicCapture = "Ctrl+Alt+Shift+PrintScreen");

public sealed record AppSettings
{
    public HotkeySettings Hotkeys { get; init; } = new();
    public int CaptureDelaySeconds { get; init; }
    public bool IncludeCursor { get; init; }
    public string ExportFormat { get; init; } = "png";
    public int JpgQuality { get; init; } = 90;
    public string? FfmpegPath { get; init; }
    public string? MicDeviceId { get; init; }
    public bool RecordMicrophone { get; init; } = true;
    public bool RecordSystemAudio { get; init; } = true;
    public Dictionary<ToolKind, ToolStyle> QuickStyles { get; init; } = new();
    public ToolKind LastTool { get; init; } = ToolKind.Arrow;
    public IReadOnlyList<CapturePreset> Presets { get; init; } = [];
    public WebcamSettings Webcam { get; init; } = new();
    public ShareSettings Share { get; init; } = new();
}
