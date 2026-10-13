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
                repeatGiven: given?["repeatLastCapture"] is not null, pauseGiven: given?["videoPauseResume"] is not null);
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
    static HotkeySettings Normalize(HotkeySettings? h, bool repeatGiven, bool pauseGiven)
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
        return new(image, video, scroll, Fill(h.RepeatLastCapture, repeatGiven, d.RepeatLastCapture), Fill(h.VideoPauseResume, pauseGiven, d.VideoPauseResume));
    }

    static AppSettings Normalize(AppSettings s, bool repeatGiven, bool pauseGiven) => s with
    {
        Hotkeys = Normalize(s.Hotkeys, repeatGiven, pauseGiven),
        QuickStyles = s.QuickStyles ?? new(),
        CaptureDelaySeconds = Math.Clamp(s.CaptureDelaySeconds, 0, 10),
        JpgQuality = Math.Clamp(s.JpgQuality, 1, 100),
        ExportFormat = s.ExportFormat is "jpg" ? "jpg" : "png",
    };
}
