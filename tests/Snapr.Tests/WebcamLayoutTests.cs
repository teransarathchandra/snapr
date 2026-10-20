using Snapr.Core;
using Snapr.Core.Recording;
using Snapr.Core.Settings;

namespace Snapr.Tests;

public class WebcamLayoutTests
{
    [Fact]
    public void Sizes_in_pixels()
    {
        Assert.Equal(160, WebcamLayout.Pixels(WebcamSize.Small));
        Assert.Equal(240, WebcamLayout.Pixels(WebcamSize.Medium));
        Assert.Equal(320, WebcamLayout.Pixels(WebcamSize.Large));
    }

    [Fact]
    public void Circle_sits_in_the_bottom_right_corner() =>
        Assert.Equal(new Rct(1000 + 1920 - 24 - 240, 100 + 1080 - 24 - 240, 240, 240), WebcamLayout.Place(new Rct(1000, 100, 1920, 1080), 240, WebcamShape.Circle));

    [Fact]
    public void Rounded_rectangle_is_four_by_three() =>
        Assert.Equal(new Rct(1920 - 24 - 320, 1080 - 24 - 240, 320, 240), WebcamLayout.Place(new Rct(0, 0, 1920, 1080), 240, WebcamShape.RoundedRectangle));

    [Fact]
    public void Rounded_rectangle_stays_four_by_three_when_width_is_capped() =>
        Assert.Equal(new Rct(25, 264, 149, 112), WebcamLayout.Place(new Rct(0, 0, 198, 400), 240, WebcamShape.RoundedRectangle));

    [Fact]
    public void Rounded_rectangle_under_min_width_vanishes() =>
        Assert.Null(WebcamLayout.Place(new Rct(0, 0, 100, 400), 240, WebcamShape.RoundedRectangle));

    [Fact]
    public void Shrinks_to_fit_and_vanishes_when_too_small()
    {
        Assert.Equal(new Rct(224, 24, 152, 152), WebcamLayout.Place(new Rct(0, 0, 400, 200), 240, WebcamShape.Circle));
        Assert.Equal(new Rct(24, 24 + 140 - 114, 152, 114), WebcamLayout.Place(new Rct(0, 0, 200, 188), 240, WebcamShape.RoundedRectangle));
        Assert.Null(WebcamLayout.Place(new Rct(0, 0, 111, 400), 240, WebcamShape.Circle));
        Assert.NotNull(WebcamLayout.Place(new Rct(0, 0, 112, 400), 240, WebcamShape.Circle));
    }

    [Fact]
    public void Clamp_moves_inside_without_resizing()
    {
        var area = new Rct(100, 100, 800, 600);
        Assert.Equal(new Rct(300, 300, 200, 200), WebcamLayout.Clamp(new Rct(300, 300, 200, 200), area));
        Assert.Equal(new Rct(100, 100, 200, 200), WebcamLayout.Clamp(new Rct(-50, 20, 200, 200), area));
        Assert.Equal(new Rct(700, 500, 200, 200), WebcamLayout.Clamp(new Rct(950, 900, 200, 200), area));
        Assert.Equal(new Rct(100, 100, 900, 700), WebcamLayout.Clamp(new Rct(400, 400, 900, 700), area));
    }
}
