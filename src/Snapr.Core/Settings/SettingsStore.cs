using System.Text.Json;
using System.Text.Json.Nodes;

namespace Snapr.Core.Settings;

public static class SettingsStore
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Snapr", "settings.json");

    /// <summary>Never throws for a missing or unreadable file: Snapr must still start.</summary>
    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new();
            var text = File.ReadAllText(path);
            // Only objects are inspected; any other shape is left for Deserialize to reject (JsonException → defaults).
            var given = (JsonNode.Parse(text) as JsonObject)?["hotkeys"] as JsonObject;
            return Normalize(JsonSerializer.Deserialize<AppSettings>(text, SnaprJson.Options) ?? new(),
                repeatGiven: given?["repeatLastCapture"] is not null, pauseGiven: given?["videoPauseResume"] is not null,
                panoramicGiven: given?["panoramicCapture"] is not null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Write($"Settings unreadable, using defaults: {path}", ex);
            return new();
        }
    }

    public static void Save(AppSettings settings, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, SnaprJson.Options));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>A missing or null hotkey gets its default; an empty one stays empty (disabled).</summary>
    static HotkeySettings Normalize(HotkeySettings? h, bool repeatGiven, bool pauseGiven, bool panoramicGiven, IEnumerable<string> presetKeys)
    {
        var d = new HotkeySettings();
        if (h is null) return d;
        var image = h.ImageCapture ?? d.ImageCapture;
        var video = h.VideoCapture ?? d.VideoCapture;
        var scroll = h.ScrollingCapture ?? d.ScrollingCapture;
        // A hotkey missing from the file gets its default, unless that collides with a v1 binding: then it is disabled.
        string[] v1 = [image, video, scroll];
        string Fill(string? value, bool given, string def) =>
            given ? value ?? def : v1.Contains(def, StringComparer.OrdinalIgnoreCase) ? "" : def;
        var repeat = Fill(h.RepeatLastCapture, repeatGiven, d.RepeatLastCapture);
        var pause = Fill(h.VideoPauseResume, pauseGiven, d.VideoPauseResume);
        var panoramic = panoramicGiven ? h.PanoramicCapture ?? d.PanoramicCapture
            : new[] { image, video, scroll, repeat, pause }.Concat(presetKeys).Contains(d.PanoramicCapture, StringComparer.OrdinalIgnoreCase) ? "" : d.PanoramicCapture;
        return new(image, video, scroll, repeat, pause, panoramic);
    }

    static AppSettings Normalize(AppSettings s, bool repeatGiven, bool pauseGiven, bool panoramicGiven)
    {
        var presets = CleanPresets(s.Presets);
        return s with
        {
            Hotkeys = Normalize(s.Hotkeys, repeatGiven, pauseGiven, panoramicGiven, presets.Select(p => p.Hotkey).Where(k => k != "")),
            QuickStyles = s.QuickStyles ?? new(),
            CaptureDelaySeconds = Math.Clamp(s.CaptureDelaySeconds, 0, 10),
            JpgQuality = Math.Clamp(s.JpgQuality, 1, 100),
            ExportFormat = s.ExportFormat is "jpg" ? "jpg" : "png",
            Presets = presets,
        };
    }

    /// <summary>A hand-edited file must not stop Snapr: nameless entries are dropped, the rest clamped, at most 20 kept.</summary>
    static IReadOnlyList<CapturePreset> CleanPresets(IReadOnlyList<CapturePreset>? presets) =>
        (presets ?? [])
        .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Name))
        .Take(PresetRules.MaxPresets)
        .Select(p => p with
        {
            Id = p.Id == Guid.Empty ? Guid.NewGuid() : p.Id,
            DelaySeconds = Math.Clamp(p.DelaySeconds, 0, 10),
            Hotkey = p.Hotkey ?? "",
            SaveFormat = p.SaveFormat is "jpg" ? "jpg" : "png",
        })
        .ToArray();
}
