using Snapr.Core;
using Snapr.Core.Capture;

namespace Snapr.Tests;

public class RepeatTargetTests
{
    static readonly Rct Screen = new(-1920, 0, 3840, 1080);

    [Fact]
    public void Region_repeats_the_same_rectangle() =>
        Assert.Equal(new Rct(100, 100, 300, 200),
            RepeatTarget.Resolve(new(SelectionKind.Region, new(100, 100, 300, 200), 0), [], Screen));

    [Fact]
    public void Window_uses_its_current_bounds()
    {
        var last = new LastCapture(SelectionKind.Window, new(0, 0, 400, 300), 7);
        Assert.Equal(new Rct(50, 60, 500, 400), RepeatTarget.Resolve(last, [new(7, new(50, 60, 500, 400), "App")], Screen));
    }

    [Fact]
    public void Closed_window_falls_back_to_last_rectangle()
    {
        var last = new LastCapture(SelectionKind.Window, new(0, 0, 400, 300), 7);
        Assert.Equal(new Rct(0, 0, 400, 300), RepeatTarget.Resolve(last, [new(8, new(9, 9, 9, 9), "Other")], Screen));
    }

    [Fact]
    public void Area_partly_off_screen_is_clipped_and_snapped() =>
        Assert.Equal(new Rct(1800, 1000, 120, 80),
            RepeatTarget.Resolve(new(SelectionKind.Region, new(1800.4, 1000.6, 400, 400), 0), [], Screen));

    [Fact]
    public void Area_off_every_screen_is_null() =>
        Assert.Null(RepeatTarget.Resolve(new(SelectionKind.Region, new(5000, 5000, 100, 100), 0), [], Screen));
}
