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

    [Fact]
    public void Panoramic_hotkey_defaults_when_missing()
    {
        File.WriteAllText(PathOf("p1.json"), """{"hotkeys":{"imageCapture":"F9"}}""");
        Assert.Equal("Ctrl+Alt+Shift+PrintScreen", SettingsStore.Load(PathOf("p1.json")).Hotkeys.PanoramicCapture);
        Assert.Equal("Ctrl+Alt+Shift+PrintScreen", new HotkeySettings().PanoramicCapture);
    }

    [Fact]
    public void Defaulted_panoramic_hotkey_that_collides_is_disabled()
    {
        File.WriteAllText(PathOf("p2.json"), """{"hotkeys":{"imageCapture":"Ctrl+Alt+Shift+PrintScreen"}}""");
        Assert.Equal("", SettingsStore.Load(PathOf("p2.json")).Hotkeys.PanoramicCapture);
    }

    [Fact]
    public void Defaulted_panoramic_hotkey_that_collides_with_a_preset_is_disabled()
    {
        File.WriteAllText(PathOf("p5.json"), """{"presets":[{"name":"Wide","hotkey":"Ctrl+Alt+Shift+PrintScreen"}]}""");
        var s = SettingsStore.Load(PathOf("p5.json"));
        Assert.Equal("", s.Hotkeys.PanoramicCapture);
        Assert.Equal("Ctrl+Alt+Shift+PrintScreen", Assert.Single(s.Presets).Hotkey);
    }

    [Fact]
    public void Given_panoramic_hotkey_is_kept_and_empty_means_disabled()
    {
        File.WriteAllText(PathOf("p3.json"), """{"hotkeys":{"panoramicCapture":"F7"}}""");
        Assert.Equal("F7", SettingsStore.Load(PathOf("p3.json")).Hotkeys.PanoramicCapture);
        File.WriteAllText(PathOf("p4.json"), """{"hotkeys":{"panoramicCapture":""}}""");
        Assert.Equal("", SettingsStore.Load(PathOf("p4.json")).Hotkeys.PanoramicCapture);
    }

    [Fact]
    public void Webcam_settings_round_trip_and_default()
    {
        var s = new AppSettings { Webcam = new WebcamSettings(true, "cam-1", WebcamShape.RoundedRectangle, WebcamSize.Large, false) };
        SettingsStore.Save(s, PathOf("w.json"));
        Assert.Equal(s.Webcam, SettingsStore.Load(PathOf("w.json")).Webcam);
        File.WriteAllText(PathOf("old.json"), """{"exportFormat":"png"}""");
        Assert.Equal(new WebcamSettings(), SettingsStore.Load(PathOf("old.json")).Webcam);
    }

    [Fact]
    public void Null_webcam_gets_defaults()
    {
        File.WriteAllText(PathOf("wn.json"), """{"webcam":null}""");
        Assert.Equal(new WebcamSettings(), SettingsStore.Load(PathOf("wn.json")).Webcam);
    }
}
