using System.Globalization;
using System.Text.Json.Serialization;

namespace Snapr.Core;

/// <summary>One edit of the capture image (spec parity 4 §2). The original pixels are never changed; edits are replayed in order.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RotateEdit), "rotate")]
[JsonDerivedType(typeof(FlipEdit), "flip")]
[JsonDerivedType(typeof(ResizeEdit), "resize")]
[JsonDerivedType(typeof(MarginsEdit), "margins")]
[JsonDerivedType(typeof(FillEdit), "fill")]
public abstract record ImageEdit;

/// <summary>QuarterTurns: 1 = 90° right, 2 = 180°, 3 = 90° left.</summary>
public sealed record RotateEdit(int QuarterTurns) : ImageEdit;

/// <summary>Horizontal: mirror left↔right; otherwise top↔bottom.</summary>
public sealed record FlipEdit(bool Horizontal) : ImageEdit;

public sealed record ResizeEdit(int Width, int Height) : ImageEdit;

/// <summary>Pixels added on each side, in Color ("#AARRGGBB"; "#00000000" = transparent).</summary>
public sealed record MarginsEdit(int Left, int Top, int Right, int Bottom, string Color) : ImageEdit;

/// <summary>Flood fill at pixel (X, Y) of the image produced by the earlier edits.</summary>
public sealed record FillEdit(int X, int Y, string Color, int Tolerance) : ImageEdit;

public static class ImageEditRules
{
    public const int MaxEdits = 1_000;
    public const int MaxSide = 32_000;
    public const long MaxPixels = 100_000_000;
    public const int MaxMargin = 4_000;

    public static bool SizeOk(long w, long h) => w >= 1 && h >= 1 && w <= MaxSide && h <= MaxSide && w * h <= MaxPixels;

    /// <summary>Image size after one edit.</summary>
    public static (int W, int H) SizeAfter(int w, int h, ImageEdit e) => e switch
    {
        RotateEdit { QuarterTurns: 1 or 3 } => (h, w),
        ResizeEdit r => (r.Width, r.Height),
        MarginsEdit m => (w + m.Left + m.Right, h + m.Top + m.Bottom),
        _ => (w, h),
    };

    public static (int W, int H) SizeAfter(int w, int h, IEnumerable<ImageEdit> edits)
    {
        foreach (var e in edits) (w, h) = SizeAfter(w, h, e);
        return (w, h);
    }

    /// <summary>Throws when the edits break a limit of spec §2. Used for files, which can come from anywhere.</summary>
    public static void Validate(IReadOnlyList<ImageEdit?> edits, int w, int h)
    {
        if (edits.Count > MaxEdits) throw Invalid();
        foreach (var e in edits)
        {
            if (!IsValid(e, w, h)) throw Invalid();
            (w, h) = SizeAfter(w, h, e!);
            if (!SizeOk(w, h)) throw Invalid();
        }
    }

    /// <summary>Whether one edit is well formed for an image of size <paramref name="w"/> × <paramref name="h"/> (its size before the edit). Does not check the size limits or the edit count.</summary>
    public static bool IsValid(ImageEdit? e, int w, int h) => e switch
    {
        RotateEdit r => r.QuarterTurns is >= 1 and <= 3,
        FlipEdit => true,
        ResizeEdit => true,
        MarginsEdit m => Margin(m.Left) && Margin(m.Top) && Margin(m.Right) && Margin(m.Bottom) && ColorText.TryArgb(m.Color, out _),
        FillEdit f => f.X >= 0 && f.Y >= 0 && f.X < w && f.Y < h && f.Tolerance is >= 0 and <= 100 && ColorText.TryArgb(f.Color, out _),
        _ => false,
    };

    static bool Margin(int v) => v is >= 0 and <= MaxMargin;

    /// <summary>Height that keeps the proportions of w0 × h0 at width w (rounded, at least 1).</summary>
    public static int ProportionalHeight(int w0, int h0, int w) => Math.Max(1, (int)Math.Round((double)w * h0 / w0, MidpointRounding.AwayFromZero));

    /// <summary>Width that keeps the proportions of w0 × h0 at height h (rounded, at least 1).</summary>
    public static int ProportionalWidth(int w0, int h0, int h) => Math.Max(1, (int)Math.Round((double)h * w0 / h0, MidpointRounding.AwayFromZero));

    public static (int W, int H) FromPercent(int w0, int h0, double percent) =>
        (Math.Max(1, (int)Math.Round(w0 * percent / 100, MidpointRounding.AwayFromZero)),
         Math.Max(1, (int)Math.Round(h0 * percent / 100, MidpointRounding.AwayFromZero)));

    /// <summary>Width and height typed in the Resize dialog: whole numbers within the limits, else null.</summary>
    public static (int W, int H)? ParseSize(string? width, string? height) =>
        int.TryParse(width?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var w)
        && int.TryParse(height?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var h)
        && SizeOk(w, h) ? (w, h) : null;

    /// <summary>A margin typed in the Canvas size dialog: a whole number 0–4,000, else null.</summary>
    public static int? ParseMargin(string? text) =>
        int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v <= MaxMargin ? v : null;

    static InvalidDataException Invalid() => new("The image edits are not valid.");
}
