namespace Snapr.Core.Interop;

/// <summary>Reads a PNG's declared size from its IHDR chunk without decoding it.</summary>
public static class PngInfo
{
    static ReadOnlySpan<byte> Header => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Width and height, or null if this is not a PNG, is too short, or declares a zero or oversized dimension.</summary>
    public static (int Width, int Height)? Size(ReadOnlySpan<byte> png)
    {
        if (png.Length < 24 || !png.StartsWith(Header) || !png.Slice(12, 4).SequenceEqual("IHDR"u8)) return null;
        var w = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png[16..]);
        var h = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png[20..]);
        return w is 0 or > int.MaxValue || h is 0 or > int.MaxValue ? null : ((int)w, (int)h);
    }
}
