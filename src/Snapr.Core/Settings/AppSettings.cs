namespace Snapr.Core.Settings;

public sealed record HotkeySettings(
    string ImageCapture = "PrintScreen",
    string VideoCapture = "Shift+PrintScreen",
    string ScrollingCapture = "Ctrl+PrintScreen");

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
}
