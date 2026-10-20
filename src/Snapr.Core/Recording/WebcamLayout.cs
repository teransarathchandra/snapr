using Snapr.Core.Settings;

namespace Snapr.Core.Recording;

/// <summary>Where the webcam bubble goes inside the recorded area, in screen pixels (spec parity 18 §3).</summary>
public static class WebcamLayout
{
    public const int Margin = 24, MinSize = 64;

    public static int Pixels(WebcamSize size) => size switch { WebcamSize.Small => 160, WebcamSize.Large => 320, _ => 240 };

    /// <summary>The bubble in the bottom-right corner, shrunk to fit; null when it would be under MinSize tall.</summary>
    public static Rct? Place(Rct area, int size, WebcamShape shape)
    {
        double maxW = Math.Floor(area.W - 2 * Margin), maxH = Math.Floor(area.H - 2 * Margin);
        var h = Math.Min(size, maxH);
        var w = h;
        if (shape == WebcamShape.RoundedRectangle)
        {
            w = Math.Round(h * 4 / 3);
            if (w > maxW)
            {
                w = maxW;
                h = Math.Floor(w * 3 / 4);
                w = Math.Round(h * 4 / 3);
            }
        }
        else if (w > maxW) w = h = maxW;
        if (h < MinSize) return null;
        return new Rct(Math.Round(area.Right - Margin - w), Math.Round(area.Bottom - Margin - h), w, h);
    }

    /// <summary>Moved (never resized) to lie inside the area; a bubble larger than the area is pinned to its top-left.</summary>
    public static Rct Clamp(Rct bubble, Rct area) =>
        bubble with
        {
            X = Math.Max(area.X, Math.Min(bubble.X, area.Right - bubble.W)),
            Y = Math.Max(area.Y, Math.Min(bubble.Y, area.Bottom - bubble.H)),
        };
}
