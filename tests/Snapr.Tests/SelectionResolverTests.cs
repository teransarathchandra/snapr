using Snapr.Core;
using Snapr.Core.Capture;

namespace Snapr.Tests;

public class SelectionResolverTests
{
    static readonly Rct Screen = new(0, 0, 1920, 1080);
    static readonly WindowInfo Top = new(1, new(100, 100, 400, 300), "Top");
    static readonly WindowInfo Below = new(2, new(0, 0, 1920, 1080), "Desktop");
    static readonly WindowInfo Maximized = new(3, new(-8, -8, 1936, 1096), "Max");

    [Fact]
    public void Tiny_click_on_nothing_is_none() =>
        Assert.Equal(SelectionKind.None, SelectionResolver.Resolve(new(5, 5), new(6, 6), [], Screen).Kind);

    [Fact]
    public void Drag_selects_a_snapped_region()
    {
        var s = SelectionResolver.Resolve(new(10.4, 20.6), new(110.2, 70.1), [Top], Screen);
        Assert.Equal(SelectionKind.Region, s.Kind);
        Assert.Equal(new Rct(10, 20, 101, 51), s.Bounds);
    }

    [Fact]
    public void Click_selects_the_topmost_window_under_the_pointer()
    {
        var s = SelectionResolver.Resolve(new(200, 200), new(200, 200), [Top, Below], Screen);
        Assert.Equal((SelectionKind.Window, (nint)1, "Top"), (s.Kind, s.Window, s.Title));
    }

    [Fact]
    public void Maximized_window_is_clipped_to_the_screen() =>
        Assert.Equal(Screen, SelectionResolver.Resolve(new(5, 5), new(5, 5), [Maximized], Screen).Bounds);

    [Fact]
    public void Drag_outside_the_clip_area_is_none() =>
        Assert.Equal(SelectionKind.None, SelectionResolver.Resolve(new(3000, 10), new(3100, 90), [], Screen).Kind);
}
