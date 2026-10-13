using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Snapr.App.Capture;
using Snapr.App.Services;
using Snapr.Core;
using Snapr.Core.Settings;

namespace Snapr.App;

/// <summary>Owns app-wide services and runs every user flow (capture, record, OCR, settings).</summary>
public sealed partial class AppController : IDisposable
{
    readonly HotkeyService _hotkeys = new();
    readonly TrayService _tray;
    AppSettings _settings;
    bool _disposed;

    public AppController()
    {
        _settings = SettingsStore.Load(SettingsStore.DefaultPath);
        _tray = new TrayService(this);
        RegisterHotkeys();
    }

    public AppSettings Settings => _settings;

    public void Notify(string title, string text) => _tray.Notify(title, text);

    /// <summary>Runs a flow from a hotkey or menu; failures are logged and shown, never thrown into the message loop.</summary>
    public async void Fire(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex)
        {
            Log.Write("Action failed", ex);
            Notify("Snapr", ex.Message);
        }
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        TrySaveSettings();
        RegisterHotkeys();
    }

    public void SaveLastTool(ToolKind tool)
    {
        _settings = _settings with { LastTool = tool };
        TrySaveSettings();
    }

    public void SaveQuickStyle(ToolKind tool, ToolStyle style)
    {
        _settings = _settings with { QuickStyles = new(_settings.QuickStyles) { [tool] = style } };
        TrySaveSettings();
    }

    void TrySaveSettings()
    {
        try { SettingsStore.Save(_settings, SettingsStore.DefaultPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write("Settings save failed", ex);
            Notify("Settings not saved", ex.Message);
        }
    }

    void RegisterHotkeys()
    {
        _hotkeys.UnregisterAll();
        var failed = new List<string>();
        void Register(string gesture, Func<Task> action)
        {
            if (!_hotkeys.TryRegister(gesture, () => Fire(action))) failed.Add(gesture);
        }
        Register(_settings.Hotkeys.ImageCapture, () => CaptureImageAsync());
        Register(_settings.Hotkeys.VideoCapture, ToggleRecordingAsync);
        Register(_settings.Hotkeys.ScrollingCapture, ScrollingCaptureAsync);
        if (failed.Count > 0)
            Notify("Hotkey unavailable", $"{string.Join(", ", failed)} is already used by another app. Change it in Settings. The tray menu still works.");
    }

    // ---- Temporary stubs: each is replaced by the task named in its message. ----
    // ---- End of temporary stubs. ----

    public void Exit() => Fire(async () =>
    {
        if (!await ShutdownFlowsAsync()) return;
        Dispose();
        Application.Current.Shutdown();
    });

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hotkeys.Dispose();
        _tray.Dispose();
    }
}
