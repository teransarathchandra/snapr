using Snapr.App.Dialogs;

namespace Snapr.App;

public sealed partial class AppController
{
    SettingsWindow? _settingsWindow;

    /// <summary>Opens Settings; section "Sharing" scrolls to the SHARING section.</summary>
    public void ShowSettings(string? section = null)
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            _settingsWindow.ShowSection(section);
            return;
        }
        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Show();
        _settingsWindow.ShowSection(section);
    }
}
