using Snapr.App.Dialogs;

namespace Snapr.App;

public sealed partial class AppController
{
    SettingsWindow? _settingsWindow;

    public void ShowSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Show();
    }
}
