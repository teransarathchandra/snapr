using Snapr.App.Dialogs;

namespace Snapr.Tests;

public class ResizeTextTests
{
    [Theory]
    [InlineData("800", "600", "100")]
    [InlineData("400", "300", "50")]
    [InlineData(" 400 ", "300 ", "50")]
    [InlineData("100", "75", "12.5")]
    [InlineData("400", "600", "")]
    [InlineData("abc", "600", "")]
    [InlineData("", "600", "")]
    public void Percent_shows_one_shared_value(string w, string h, string expected) =>
        Assert.Equal(expected, ResizeText.Percent(800, 600, w, h, System.Globalization.CultureInfo.InvariantCulture));
}
