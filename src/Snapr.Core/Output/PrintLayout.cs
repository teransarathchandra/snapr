namespace Snapr.Core.Output;

/// <summary>Where an image goes on the printable area. All values in the same units (WPF: 1/96 inch).</summary>
public static class PrintLayout
{
    /// <summary>Fit: shrink to the area (never enlarge). Otherwise actual size (may overflow and be cropped). Always centred.</summary>
    public static Rct Place(double imageW, double imageH, double areaW, double areaH, bool fit)
    {
        var scale = fit ? Math.Min(1, Math.Min(areaW / imageW, areaH / imageH)) : 1;
        double w = imageW * scale, h = imageH * scale;
        return new Rct((areaW - w) / 2, (areaH - h) / 2, w, h);
    }

    public static bool WantsLandscape(double imageW, double imageH) => imageW > imageH;
}
