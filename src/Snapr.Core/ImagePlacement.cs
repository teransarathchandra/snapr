namespace Snapr.Core;

public static class ImagePlacement
{
    /// <summary>Size of a picture added to a capture: its natural size, shrunk uniformly to fit within
    /// <paramref name="fraction"/> of the visible area, never enlarged. An empty view keeps the natural size.</summary>
    public static (double W, double H) Fit(int w, int h, Rct view, double fraction = 0.8)
    {
        if (view.IsEmpty || w <= 0 || h <= 0) return (w, h);
        var scale = Math.Min(1, Math.Min(view.W * fraction / w, view.H * fraction / h));
        return (w * scale, h * scale);
    }
}
