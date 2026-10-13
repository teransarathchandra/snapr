using System.Text.Json;

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
            return Normalize(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), SnaprJson.Options) ?? new());
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

    static AppSettings Normalize(AppSettings s) => s with
    {
        Hotkeys = s.Hotkeys ?? new(),
        QuickStyles = s.QuickStyles ?? new(),
        CaptureDelaySeconds = Math.Clamp(s.CaptureDelaySeconds, 0, 10),
        JpgQuality = Math.Clamp(s.JpgQuality, 1, 100),
        ExportFormat = s.ExportFormat is "jpg" ? "jpg" : "png",
    };
}
