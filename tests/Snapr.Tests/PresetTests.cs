using Snapr.Core;
using Snapr.Core.Settings;

namespace Snapr.Tests;

public sealed class PresetTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("snapr-presets").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    static CapturePreset Preset(string name = "Bug report", string hotkey = "") =>
        new(Guid.NewGuid(), name, PresetTarget.Overlay, null, 0, PresetAction.Edit, false, hotkey, null, "png");

    static readonly string[] NoAppHotkeys = [];

    [Fact]
    public void Valid_preset_has_no_error() => Assert.Null(PresetRules.Validate(Preset(), [], NoAppHotkeys));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_is_required(string name) => Assert.NotNull(PresetRules.Validate(Preset(name), [], NoAppHotkeys));

    [Fact]
    public void Name_longer_than_40_is_rejected() =>
        Assert.NotNull(PresetRules.Validate(Preset(new string('a', 41)), [], NoAppHotkeys));

    [Fact]
    public void Name_must_be_unique_ignoring_case() =>
        Assert.NotNull(PresetRules.Validate(Preset("bug REPORT "), [Preset("Bug report")], NoAppHotkeys));

    [Fact]
    public void Delay_outside_0_to_10_is_rejected()
    {
        Assert.NotNull(PresetRules.Validate(Preset() with { DelaySeconds = 11 }, [], NoAppHotkeys));
        Assert.NotNull(PresetRules.Validate(Preset() with { DelaySeconds = -1 }, [], NoAppHotkeys));
    }

    [Fact]
    public void Fixed_area_target_needs_an_area()
    {
        Assert.NotNull(PresetRules.Validate(Preset() with { Target = PresetTarget.FixedArea }, [], NoAppHotkeys));
        Assert.NotNull(PresetRules.Validate(Preset() with { Target = PresetTarget.FixedArea, FixedArea = new Rct(0, 0, 0, 10) }, [], NoAppHotkeys));
        Assert.Null(PresetRules.Validate(Preset() with { Target = PresetTarget.FixedArea, FixedArea = new Rct(0, 0, 10, 10) }, [], NoAppHotkeys));
    }

    [Fact]
    public void Save_to_folder_needs_a_folder()
    {
        Assert.NotNull(PresetRules.Validate(Preset() with { Action = PresetAction.SaveToFolder, SaveFolder = " " }, [], NoAppHotkeys));
        Assert.Null(PresetRules.Validate(Preset() with { Action = PresetAction.SaveToFolder, SaveFolder = _dir }, [], NoAppHotkeys));
    }

    [Fact]
    public void Hotkey_must_not_clash_with_other_presets_or_app_hotkeys()
    {
        Assert.NotNull(PresetRules.Validate(Preset(hotkey: "Ctrl+Alt+B"), [Preset("Other", "ctrl+alt+b")], NoAppHotkeys));
        Assert.NotNull(PresetRules.Validate(Preset(hotkey: "PrintScreen"), [], PresetRules.AppHotkeys(new HotkeySettings())));
        Assert.Null(PresetRules.Validate(Preset(hotkey: "Ctrl+Alt+B"), [Preset("Other")], PresetRules.AppHotkeys(new HotkeySettings())));
    }

    [Fact]
    public void App_hotkeys_skip_disabled_ones() =>
        Assert.Equal(["PrintScreen"], PresetRules.AppHotkeys(new HotkeySettings("PrintScreen", "", "", "", "")));

    [Fact]
    public void At_most_20_presets()
    {
        var twenty = Enumerable.Range(0, 20).Select(i => Preset($"P{i}")).ToArray();
        Assert.NotNull(PresetRules.Validate(Preset("New one"), twenty, NoAppHotkeys));
        Assert.Null(PresetRules.Validate(Preset("New one"), twenty[..19], NoAppHotkeys));
    }

    [Fact]
    public void File_name_has_name_and_time()
    {
        var name = PresetRules.FileName("Bug report", new DateTime(2026, 10, 14, 18, 30, 5), "png", _ => false);
        Assert.Equal("Bug report 2026-10-14 18.30.05.png", name);
    }

    [Fact]
    public void File_name_replaces_invalid_characters()
    {
        var name = PresetRules.FileName("a/b:c*?", new DateTime(2026, 10, 14, 18, 30, 5), "jpg", _ => false);
        Assert.Equal("a_b_c__ 2026-10-14 18.30.05.jpg", name);
        File.WriteAllBytes(Path.Combine(_dir, name), [1]);   // proves Windows accepts it
    }

    [Fact]
    public void File_name_collisions_get_a_number()
    {
        var taken = new HashSet<string> { "P 2026-10-14 18.30.05.png", "P 2026-10-14 18.30.05 (2).png" };
        Assert.Equal("P 2026-10-14 18.30.05 (3).png", PresetRules.FileName("P", new DateTime(2026, 10, 14, 18, 30, 5), "png", taken.Contains));
    }

    [Fact]
    public void Presets_round_trip_through_settings()
    {
        var path = Path.Combine(_dir, "settings.json");
        var p = Preset("Area", "Ctrl+Alt+A") with { Target = PresetTarget.FixedArea, FixedArea = new Rct(10, 20, 300, 200), Action = PresetAction.SaveToFolder, SaveFolder = _dir, SaveFormat = "jpg", DelaySeconds = 3, IncludeCursor = true };
        SettingsStore.Save(new AppSettings { Presets = [p] }, path);
        Assert.Equal(p, Assert.Single(SettingsStore.Load(path).Presets));
    }

    [Fact]
    public void Old_settings_without_presets_load_with_none()
    {
        var path = Path.Combine(_dir, "old.json");
        File.WriteAllText(path, """{"captureDelaySeconds":2}""");
        Assert.Empty(SettingsStore.Load(path).Presets);
    }

    [Fact]
    public void Hand_edited_presets_are_cleaned()
    {
        var path = Path.Combine(_dir, "hand.json");
        var many = string.Join(",", Enumerable.Range(0, 25).Select(i => $$"""{"id":"{{Guid.NewGuid()}}","name":"P{{i}}","target":"Overlay","delaySeconds":99,"action":"Edit","includeCursor":false,"hotkey":null,"saveFormat":"gif"}"""));
        File.WriteAllText(path, $$"""{"presets":[{"name":null},{{many}}]}""");
        var presets = SettingsStore.Load(path).Presets;
        Assert.Equal(20, presets.Count);
        Assert.All(presets, p => Assert.Equal(10, p.DelaySeconds));
        Assert.All(presets, p => Assert.Equal("", p.Hotkey));
        Assert.All(presets, p => Assert.Equal("png", p.SaveFormat));

        File.WriteAllText(path, """{"presets":null}""");
        Assert.Empty(SettingsStore.Load(path).Presets);
    }
}
