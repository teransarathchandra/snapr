using Snapr.App.Video;
using Snapr.Core;
using Snapr.Core.Video;

namespace Snapr.Tests;

public sealed class VideoTimelineTests
{
    static readonly Rct Box = new(0, 0, 10, 10);

    [Fact]
    public void HitBar_finds_ends_middle_and_misses()
    {
        (double, double, int)[] bars = [(100, 200, 0), (150, 250, 1)];
        Assert.Equal((0, 1), VideoTimeline.HitBar(bars, 96, 0));
        Assert.Equal((0, 2), VideoTimeline.HitBar(bars, 204, 0));
        Assert.Equal((0, 3), VideoTimeline.HitBar(bars, 150, 0));
        Assert.Equal((1, 3), VideoTimeline.HitBar(bars, 180, 1));
        Assert.Equal((-1, 0), VideoTimeline.HitBar(bars, 50, 0));
        Assert.Equal((-1, 0), VideoTimeline.HitBar(bars, 150, 2));
    }

    [Fact]
    public void HitBar_prefers_the_later_bar_and_keeps_a_short_bars_middle()
    {
        (double, double, int)[] bars = [(100, 200, 0), (140, 160, 0), (300, 309, 0)];
        Assert.Equal((1, 3), VideoTimeline.HitBar(bars, 150, 0));
        Assert.Equal((2, 3), VideoTimeline.HitBar(bars, 304.5, 0));
        Assert.Equal((2, 1), VideoTimeline.HitBar(bars, 301, 0));
        Assert.Equal((2, 2), VideoTimeline.HitBar(bars, 308, 0));
    }

    [Fact]
    public void MainColor_and_kind_per_type()
    {
        Assert.Equal("#FF000001", VideoTimeline.MainColor(new ArrowAnnotation(new Pt(0, 0), new Pt(1, 1), "#FF000001", 2)));
        Assert.Equal("#FF000002", VideoTimeline.MainColor(new ShapeAnnotation(ShapeKind.Rectangle, Box, "#FF000002", 2, "#FFFFFFFF")));
        Assert.Equal("#FF000003", VideoTimeline.MainColor(new CalloutAnnotation(Box, "", 12, "#FFFFFFFF", "#FF000003", "#FF111111", new Pt(0, 0))));
        Assert.Equal("#FF000004", VideoTimeline.MainColor(new StepAnnotation(new Pt(0, 0), 1, 20, "#FF000004", "#FFFFFFFF")));
        Assert.Equal("#66000005", VideoTimeline.MainColor(new HighlightAnnotation(Box, "#66000005")));
        Assert.Null(VideoTimeline.MainColor(new BlurAnnotation(Box, BlurMode.Pixelate, 8)));

        var o = new VideoObject(new StampAnnotation(Box, "check", "#FF000006"), 3.2, 6.2);
        Assert.Equal("Stamp, 0:03.2 – 0:06.2", VideoTimeline.Tip(o));
        Assert.Equal("Text", VideoTimeline.KindName(new TextAnnotation(new Pt(0, 0), "a", "Segoe UI", 12, false, "#FF000000")));
    }
}
