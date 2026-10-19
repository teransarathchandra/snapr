using System.IO;
using Snapr.App.Library;

namespace Snapr.App;

public sealed partial class AppController
{
    LibraryWindow? _library;

    /// <summary>One Library window; opening it again brings it to the front.</summary>
    public void ShowLibrary()
    {
        if (_library is null)
        {
            _library = new LibraryWindow(this);
            _library.Closed += (_, _) => _library = null;
            _library.Show();
        }
        if (_library.WindowState == System.Windows.WindowState.Minimized) _library.WindowState = System.Windows.WindowState.Normal;
        _library.Activate();
    }

    public bool IsOpenInEditor(string fullPath) => _editor?.IsShowing(Path.GetFullPath(fullPath)) == true;

    public bool ReleaseFromEditor(string fullPath) => _editor?.ReleaseIfHidden(Path.GetFullPath(fullPath)) == true;

    public bool? SetTagsInEditor(string fullPath, IReadOnlyList<string> tags) => _editor?.TrySetTags(Path.GetFullPath(fullPath), tags);
}
