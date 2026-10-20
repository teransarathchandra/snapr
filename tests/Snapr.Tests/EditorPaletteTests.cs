using Snapr.App.Editor;

namespace Snapr.Tests;

public class EditorPaletteTests
{
    [Fact]
    public void Every_swatch_has_a_name()
    {
        Assert.Equal(["Red", "Orange", "Yellow", "Green", "Blue", "Purple", "Black", "White"], EditorPalette.Swatches.Select(s => s.Name));
        Assert.Equal(EditorPalette.Swatches.Select(s => s.Color), EditorPalette.Colors);
    }

    [Theory]
    [InlineData("#FF3B82F6", "Blue")]
    [InlineData("#ff3b82f6", "Blue")]
    [InlineData("#FF123456", "#FF123456")]
    public void Name_falls_back_to_hex(string color, string expected) => Assert.Equal(expected, EditorPalette.Name(color));
}
