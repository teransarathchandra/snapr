using Snapr.Core;
using Snapr.Core.Settings;

namespace Snapr.Tests;

public sealed class SettingsTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("snapr-settings").FullName;
    string PathOf(string name) => Path.Combine(_dir, name);
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var s = SettingsStore.Load(PathOf("none.json"));
        Assert.Equal("PrintScreen", s.Hotkeys.ImageCapture);
        Assert.Equal(90, s.JpgQuality);
        Assert.True(s.RecordSystemAudio);
    }

    [Fact]
    public void Corrupt_file_gives_defaults()
    {
        File.WriteAllText(PathOf("bad.json"), "{ nope");
        Assert.Equal(new AppSettings().Hotkeys, SettingsStore.Load(PathOf("bad.json")).Hotkeys);
    }

    [Fact]
    public void Save_then_load_round_trips_quick_styles()
    {
        var s = new AppSettings { CaptureDelaySeconds = 3, QuickStyles = new() { [ToolKind.Arrow] = ToolStyle.Default with { Width = 9 } } };
        SettingsStore.Save(s, PathOf("s.json"));
        var back = SettingsStore.Load(PathOf("s.json"));
        Assert.Equal(3, back.CaptureDelaySeconds);
        Assert.Equal(9, back.QuickStyles[ToolKind.Arrow].Width);
    }

    [Fact]
    public void Out_of_range_values_are_clamped_and_unknown_fields_ignored()
    {
        File.WriteAllText(PathOf("x.json"), """{"captureDelaySeconds":99,"jpgQuality":0,"exportFormat":"gif","hotkeys":null,"future":1}""");
        var s = SettingsStore.Load(PathOf("x.json"));
        Assert.Equal((10, 1, "png"), (s.CaptureDelaySeconds, s.JpgQuality, s.ExportFormat));
        Assert.Equal("PrintScreen", s.Hotkeys.ImageCapture);
    }
}
