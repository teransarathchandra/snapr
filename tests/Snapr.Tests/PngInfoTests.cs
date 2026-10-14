using Snapr.Core.Interop;

namespace Snapr.Tests;

public class PngInfoTests
{
    [Fact]
    public void Reads_width_and_height_from_ihdr()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
            0, 0, 1, 44, 0, 0, 0, 200];
        Assert.Equal((300, 200), PngInfo.Size(png));
    }

    [Fact]
    public void Rejects_non_png()
    {
        Assert.Null(PngInfo.Size("hello world, not a png at all"u8));
        Assert.Null(PngInfo.Size([]));
    }
}
