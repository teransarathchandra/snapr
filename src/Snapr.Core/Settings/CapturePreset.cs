using System.Globalization;

namespace Snapr.Core.Settings;

public enum PresetTarget { Overlay, ScreenUnderMouse, AllScreens, FixedArea }

public enum PresetAction { Edit, Copy, GrabText, SaveToFolder }

/// <summary>A saved capture (spec parity 6 §3.1). FixedArea is in absolute screen pixels. Hotkey "" = none.</summary>
public sealed record CapturePreset(Guid Id, string Name, PresetTarget Target, Rct? FixedArea, int DelaySeconds,
    PresetAction Action, bool IncludeCursor, string Hotkey, string? SaveFolder, string SaveFormat);

public static class PresetRules
{
    public const int MaxPresets = 20;
    public const int MaxNameLength = 40;

    /// <param name="others">Every other preset (the one being edited left out).</param>
    /// <returns>A short sentence for the editor, or null when the preset is fine.</returns>
    public static string? Validate(CapturePreset p, IReadOnlyList<CapturePreset> others, IEnumerable<string> appHotkeys)
    {
        var name = p.Name.Trim();
        if (name.Length == 0) return "Give the preset a name.";
        if (name.Length > MaxNameLength) return $"Use a name of up to {MaxNameLength} characters.";
        if (others.Any(o => string.Equals(o.Name.Trim(), name, StringComparison.OrdinalIgnoreCase))) return "Another preset already has this name.";
        if (others.Count >= MaxPresets) return $"You can have up to {MaxPresets} presets.";
        if (p.DelaySeconds is < 0 or > 10) return "Choose a delay from 0 to 10 seconds.";
        if (p.Target == PresetTarget.FixedArea && p.FixedArea is not { IsEmpty: false }) return "Choose the area to capture.";
        if (p.Action == PresetAction.SaveToFolder && string.IsNullOrWhiteSpace(p.SaveFolder)) return "Choose the folder to save to.";
        if (p.Hotkey.Length > 0)
        {
            if (others.Any(o => string.Equals(o.Hotkey, p.Hotkey, StringComparison.OrdinalIgnoreCase))) return "Another preset already uses this hotkey.";
            if (appHotkeys.Contains(p.Hotkey, StringComparer.OrdinalIgnoreCase)) return "Snapr already uses this hotkey.";
        }
        return null;
    }

    /// <summary>The app's own hotkeys that are switched on.</summary>
    public static IEnumerable<string> AppHotkeys(HotkeySettings h) =>
        new[] { h.ImageCapture, h.VideoCapture, h.ScrollingCapture, h.RepeatLastCapture, h.VideoPauseResume, h.PanoramicCapture }.Where(k => !string.IsNullOrEmpty(k));

    /// <summary>"name yyyy-MM-dd HH.mm.ss.ext", cleaned for Windows and numbered if taken.</summary>
    public static string FileName(string presetName, DateTime now, string ext, Func<string, bool> exists) =>
        FileNames.Unique(FileNames.Clean($"{presetName.Trim()} {now.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture)}"), ext, exists);
}
