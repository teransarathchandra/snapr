using System.IO;
using System.Windows;
using Snapr.App.Video;

namespace Snapr.App;

public sealed partial class AppController
{
    readonly Dictionary<string, VideoEditorWindow> _videoEditors = new(StringComparer.OrdinalIgnoreCase);

    public void ShowVideoEditor(string file)
    {
        var full = Path.GetFullPath(file);
        if (_videoEditors.TryGetValue(full, out var existing))
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }
        var window = new VideoEditorWindow(this, full);
        _videoEditors[full] = window;
        window.Closed += (_, _) => _videoEditors.Remove(full);
        window.Show();
    }
}
