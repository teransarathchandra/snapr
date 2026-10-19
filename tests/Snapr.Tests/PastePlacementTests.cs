using Snapr.Core;

namespace Snapr.Tests;

public class PastePlacementTests
{
    static readonly PixelCopy Copy = new(new Rct(40, 30, 120, 80), 17);

    [Fact]
    public void Same_sequence_and_size_gives_the_copied_area() =>
        Assert.Equal(new Rct(40, 30, 120, 80), PastePlacement.AtCopy(17, Copy, 120, 80));

    [Fact]
    public void Changed_sequence_gives_null() => Assert.Null(PastePlacement.AtCopy(18, Copy, 120, 80));

    [Fact]
    public void Different_size_gives_null() => Assert.Null(PastePlacement.AtCopy(17, Copy, 121, 80));

    [Fact]
    public void No_copy_gives_null() => Assert.Null(PastePlacement.AtCopy(17, null, 120, 80));

    [Fact]
    public void Zero_sequence_never_matches() =>
        Assert.Null(PastePlacement.AtCopy(0, Copy with { Sequence = 0 }, 120, 80));
}
