namespace Snapr.Core.Stitching;

/// <summary>A BGRA image, one uint per pixel, row-major.</summary>
public sealed record Frame(int Width, int Height, uint[] Pixels)
{
    public ReadOnlySpan<uint> Row(int y) => Pixels.AsSpan(y * Width, Width);
}

public sealed record StitchResult(Frame Image, bool Complete);
