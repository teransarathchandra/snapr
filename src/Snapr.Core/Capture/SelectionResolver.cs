namespace Snapr.Core.Capture;

/// <summary>A top-level window; Bounds are pixels relative to the virtual-screen origin.</summary>
public readonly record struct WindowInfo(nint Handle, Rct Bounds, string Title);

public enum SelectionKind { None, Region, Window, Fullscreen }

public readonly record struct Selection(SelectionKind Kind, Rct Bounds, nint Window, string? Title);

public static class SelectionResolver
{
    public const double MinDragPixels = 4;

    /// <param name="windows">Top-level windows in z-order, topmost first.</param>
    /// <param name="clip">Area the result is clipped to (virtual screen, or one monitor).</param>
    public static Selection Resolve(Pt down, Pt up, IReadOnlyList<WindowInfo> windows, Rct clip)
    {
        if (down.DistanceTo(up) >= MinDragPixels)
        {
            var r = Snap(Rct.FromPoints(down, up).Intersect(clip));
            return r.IsEmpty ? default : new(SelectionKind.Region, r, 0, null);
        }
        if (WindowAt(up, windows) is not { } w) return default;
        var b = Snap(w.Bounds.Intersect(clip));
        return b.IsEmpty ? default : new(SelectionKind.Window, b, w.Handle, w.Title);
    }

    public static WindowInfo? WindowAt(Pt p, IReadOnlyList<WindowInfo> windows)
    {
        foreach (var w in windows)
            if (w.Bounds.Contains(p)) return w;
        return null;
    }

    static Rct Snap(Rct r)
    {
        if (r.IsEmpty) return default;
        double x = Math.Floor(r.X), y = Math.Floor(r.Y);
        return new(x, y, Math.Ceiling(r.Right) - x, Math.Ceiling(r.Bottom) - y);
    }
}
