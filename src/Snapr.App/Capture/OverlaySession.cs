using Snapr.App.Services;
using Snapr.Core;
using Snapr.Core.Capture;
using Forms = System.Windows.Forms;

namespace Snapr.App.Capture;

/// <summary>One overlay window per monitor over a frozen screenshot; the first selection or Esc closes them all.</summary>
public sealed class OverlaySession
{
    readonly TaskCompletionSource<OverlayResult?> _done = new();
    readonly List<CaptureOverlay> _overlays = new();
    readonly HashSet<CaptureOverlay> _closed = new();

    OverlaySession(ScreenGrab grab, IReadOnlyList<WindowInfo> windows, OverlayMode mode, OverlayAction action, string? label)
    {
        Grab = grab;
        Windows = windows;
        Mode = mode;
        Action = action;
        Label = label;
        Virtual = new Rct(0, 0, grab.Image.PixelWidth, grab.Image.PixelHeight);
    }

    public ScreenGrab Grab { get; }
    public IReadOnlyList<WindowInfo> Windows { get; }
    public OverlayMode Mode { get; }
    public Rct Virtual { get; }
    public OverlayAction Action { get; private set; }
    public string? Label { get; }

    public static Task<OverlayResult?> RunAsync(ScreenGrab grab, IReadOnlyList<WindowInfo> windows, OverlayMode mode, OverlayAction initial = OverlayAction.Edit, string? label = null)
    {
        var session = new OverlaySession(grab, windows, mode, initial, label);
        session.Open();
        return session._done.Task;
    }

    void Open()
    {
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var b = screen.Bounds;
            var overlay = new CaptureOverlay(this, new Rct(b.X - Grab.OriginX, b.Y - Grab.OriginY, b.Width, b.Height));
            _overlays.Add(overlay);
            // Alt+F4 (or anything else) closing one overlay must end the session, or the app stays "busy" forever.
            overlay.Closed += (_, _) => { _closed.Add(overlay); Cancel(); };
            overlay.Show();
        }
        var cursor = Forms.Cursor.Position;
        var under = _overlays.FirstOrDefault(o => o.Monitor.Contains(new Pt(cursor.X - Grab.OriginX, cursor.Y - Grab.OriginY))) ?? _overlays[0];
        under.Activate();
    }

    /// <summary>C and T switch what an image selection does; pressing the same key again switches back to Edit.</summary>
    public void ToggleAction(OverlayAction action)
    {
        if (Label is not null) return;   // a preset decides what happens
        if (Mode != OverlayMode.Image) return;
        Action = Action == action ? OverlayAction.Edit : action;
        foreach (var o in _overlays) o.Refresh();
    }

    public void Complete(Selection selection) => Finish(new OverlayResult(selection, Action));
    public void Cancel() => Finish(null);

    void Finish(OverlayResult? result)
    {
        if (!_done.TrySetResult(result)) return;
        foreach (var o in _overlays.Where(o => !_closed.Contains(o)).ToList()) o.Close();
    }
}
