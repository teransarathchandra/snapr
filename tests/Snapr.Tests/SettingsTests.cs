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

    [Theory]
    [InlineData("[]")]
    [InlineData("""{"hotkeys":"x"}""")]
    [InlineData("""{"hotkeys":5}""")]
    public void Wrong_json_shape_gives_defaults(string json)
    {
        File.WriteAllText(PathOf("shape.json"), json);
        Assert.Equal(new AppSettings().Hotkeys, SettingsStore.Load(PathOf("shape.json")).Hotkeys);
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

    [Fact]
    public void V1_settings_get_new_hotkey_defaults()
    {
        File.WriteAllText(PathOf("v1.json"), """{"hotkeys":{"imageCapture":"F9","videoCapture":"Shift+F9","scrollingCapture":"Ctrl+F9"}}""");
        var h = SettingsStore.Load(PathOf("v1.json")).Hotkeys;
        Assert.Equal(("F9", "Ctrl+Alt+PrintScreen", "Ctrl+Shift+PrintScreen"), (h.ImageCapture, h.RepeatLastCapture, h.VideoPauseResume));
    }

    [Fact]
    public void Null_hotkeys_get_defaults_and_empty_means_disabled()
    {
        File.WriteAllText(PathOf("n.json"), """{"hotkeys":{"imageCapture":null,"repeatLastCapture":"","videoPauseResume":null}}""");
        var h = SettingsStore.Load(PathOf("n.json")).Hotkeys;
        Assert.Equal(("PrintScreen", "", "Ctrl+Shift+PrintScreen"), (h.ImageCapture, h.RepeatLastCapture, h.VideoPauseResume));
    }

    [Fact]
    public void Defaulted_hotkey_that_collides_is_disabled()
    {
        File.WriteAllText(PathOf("c.json"), """{"hotkeys":{"imageCapture":"Ctrl+Shift+PrintScreen"}}""");
        var h = SettingsStore.Load(PathOf("c.json")).Hotkeys;
        Assert.Equal(("Ctrl+Alt+PrintScreen", ""), (h.RepeatLastCapture, h.VideoPauseResume));
    }
}
