using Snapr.App.Capture;
using Snapr.Core.Settings;
using Forms = System.Windows.Forms;

namespace Snapr.App.Services;

public sealed class TrayService : IDisposable
{
    readonly Forms.NotifyIcon _icon;
    readonly Forms.ToolStripMenuItem _pause;
    readonly Forms.ToolStripMenuItem _stop;
    readonly Forms.ToolStripMenuItem _presets = new("Presets");
    readonly AppController _app;

    public TrayService(AppController app)
    {
        _app = app;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Capture image", null, (_, _) => app.Fire(() => app.CaptureImageAsync()));
        menu.Items.Add("Record video", null, (_, _) => app.Fire(app.ToggleRecordingAsync));
        menu.Items.Add("Scrolling capture", null, (_, _) => app.Fire(app.ScrollingCaptureAsync));
        menu.Items.Add("Grab text", null, (_, _) => app.Fire(() => app.CaptureImageAsync(OverlayAction.GrabText)));
        menu.Items.Add(_presets);
        _pause = new Forms.ToolStripMenuItem("Pause recording", null, (_, _) => app.TogglePause()) { Visible = false };
        _stop = new Forms.ToolStripMenuItem("Stop recording", null, (_, _) => app.Fire(app.ToggleRecordingAsync)) { Visible = false };
        menu.Items.Add(_pause);
        menu.Items.Add(_stop);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Open editor", null, (_, _) => app.ShowEditor());
        menu.Items.Add("Open image…", null, (_, _) => app.OpenImageFromDialog());
        menu.Items.Add("Settings…", null, (_, _) => app.ShowSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit Snapr", null, (_, _) => app.Exit());

        _icon = new Forms.NotifyIcon { Icon = AppIcon.TrayIcon(), Text = "Snapr", Visible = true, ContextMenuStrip = menu };
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) app.Fire(() => app.CaptureImageAsync()); };
    }

    /// <summary>Rebuilds the Presets submenu: one item per preset (hotkey shown), then Manage presets….</summary>
    public void SetPresets(IReadOnlyList<CapturePreset> presets)
    {
        _presets.DropDownItems.Clear();
        foreach (var p in presets)
        {
            var preset = p;
            _presets.DropDownItems.Add(new Forms.ToolStripMenuItem(preset.Name, null, (_, _) => _app.Fire(() => _app.RunPresetAsync(preset)))
            {
                ShortcutKeyDisplayString = preset.Hotkey,
            });
        }
        if (presets.Count > 0) _presets.DropDownItems.Add(new Forms.ToolStripSeparator());
        _presets.DropDownItems.Add("Manage presets…", null, (_, _) => _app.ShowSettings());
    }

    public void Notify(string title, string text) => _icon.ShowBalloonTip(4000, title, text, Forms.ToolTipIcon.None);

    public void SetRecording(bool recording, bool paused)
    {
        _pause.Visible = _stop.Visible = recording;
        _pause.Text = paused ? "Resume recording" : "Pause recording";
        _icon.Text = !recording ? "Snapr" : paused ? "Snapr – recording paused" : "Snapr – recording";
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
