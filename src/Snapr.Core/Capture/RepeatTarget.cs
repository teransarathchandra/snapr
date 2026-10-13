namespace Snapr.Core.Capture;

/// <summary>The last image capture's target, in absolute screen pixels.</summary>
public readonly record struct LastCapture(SelectionKind Kind, Rct ScreenBounds, nint Window);

public static class RepeatTarget
{
    /// <param name="windows">Visible top-level windows, bounds in absolute screen pixels.</param>
    /// <param name="virtualScreen">The whole desktop in absolute screen pixels.</param>
    /// <returns>The area to grab, or null if it is no longer on any screen.</returns>
    public static Rct? Resolve(LastCapture last, IReadOnlyList<WindowInfo> windows, Rct virtualScreen)
    {
        var bounds = last.ScreenBounds;
        if (last.Kind == SelectionKind.Window)
            foreach (var w in windows)
                if (w.Handle == last.Window) { bounds = w.Bounds; break; }
        var r = bounds.Intersect(virtualScreen);
        if (r.IsEmpty) return null;
        double x = Math.Floor(r.X), y = Math.Floor(r.Y);
        return new Rct(x, y, Math.Ceiling(r.Right) - x, Math.Ceiling(r.Bottom) - y);
    }
}
