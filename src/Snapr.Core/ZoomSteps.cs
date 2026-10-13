namespace Snapr.Core;

/// <summary>Editor zoom levels relative to 100% (one image pixel per screen pixel).</summary>
public static class ZoomSteps
{
    public static readonly double[] Steps = [0.10, 0.25, 0.33, 0.50, 0.67, 1, 1.5, 2, 3, 4];

    public static double Next(double current, int direction)
    {
        const double tolerance = 0.005;
        return direction > 0
            ? Steps.FirstOrDefault(s => s > current + tolerance, Steps[^1])
            : Steps.LastOrDefault(s => s < current - tolerance, Steps[0]);
    }
}
