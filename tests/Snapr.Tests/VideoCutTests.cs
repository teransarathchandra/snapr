using Snapr.Core.Video;

namespace Snapr.Tests;

public class VideoCutTests
{
    [Fact]
    public void Whole_video_is_kept_by_default()
    {
        var c = VideoCut.For(10);
        Assert.Equal([new TimeRange(0, 10)], c.Kept);
        Assert.Equal(10, c.KeptDuration, 6);
    }

    [Fact]
    public void Trim_keeps_the_middle()
    {
        var c = VideoCut.For(10).WithTrim(2, 8);
        Assert.Equal([new TimeRange(2, 8)], c.Kept);
    }

    [Fact]
    public void Trim_of_exactly_min_kept_is_allowed()
    {
        Assert.Equal([new TimeRange(5.9, 6)], VideoCut.For(10).WithTrim(5.9, 6).Kept);
    }

    [Fact]
    public void Trim_is_clamped_and_needs_min_kept()
    {
        Assert.Equal([new TimeRange(0, 10)], VideoCut.For(10).WithTrim(-5, 50).Kept);
        var c = VideoCut.For(10);
        Assert.Same(c, c.WithTrim(5, 5.05));
    }

    [Fact]
    public void Removal_splits_the_kept_span()
    {
        var c = VideoCut.For(10).Remove(new(3, 4));
        Assert.Equal([new TimeRange(0, 3), new TimeRange(4, 10)], c.Kept);
        Assert.Equal(9, c.KeptDuration, 6);
    }

    [Fact]
    public void Overlapping_and_touching_removals_merge()
    {
        var c = VideoCut.For(10).Remove(new(3, 5)).Remove(new(4, 6)).Remove(new(6, 7));
        Assert.Equal([new TimeRange(0, 3), new TimeRange(7, 10)], c.Kept);
    }

    [Fact]
    public void Removal_outside_the_trim_is_ignored_and_partial_is_clamped()
    {
        var c = VideoCut.For(10).WithTrim(2, 8);
        Assert.Same(c, c.Remove(new(0, 1.5)));
        Assert.Equal([new TimeRange(3, 8)], c.Remove(new(1, 3)).Kept);
    }

    [Fact]
    public void Tiny_removal_is_ignored()
    {
        var c = VideoCut.For(10);
        Assert.Same(c, c.Remove(new(3, 3.01)));
    }

    [Fact]
    public void Remove_that_leaves_less_than_min_kept_is_refused()
    {
        var c = VideoCut.For(10);
        Assert.Same(c, c.Remove(new(0, 10)));
        Assert.Same(c, c.Remove(new(0, 9.95)));
    }

    [Fact]
    public void Later_trim_hides_earlier_removals_outside_it()
    {
        var c = VideoCut.For(10).Remove(new(1, 2)).WithTrim(3, 9);
        Assert.Equal([new TimeRange(3, 9)], c.Kept);
    }

    [Fact]
    public void Output_time_maps_to_source_time()
    {
        var c = VideoCut.For(10).WithTrim(1, 9).Remove(new(3, 5));   // kept [1,3] [5,9]
        Assert.Equal(1, c.ToSource(0), 6);
        Assert.Equal(2.5, c.ToSource(1.5), 6);
        Assert.Equal(5.5, c.ToSource(2.5), 6);
        Assert.Equal(9, c.ToSource(100), 6);
    }

    [Fact]
    public void Next_kept_skips_gaps_and_ends_after_the_last_range()
    {
        var c = VideoCut.For(10).WithTrim(1, 9).Remove(new(3, 5));
        Assert.Equal(1, c.NextKept(0));
        Assert.Equal(2, c.NextKept(2));
        Assert.Equal(5, c.NextKept(4));
        Assert.Null(c.NextKept(9));
        Assert.Null(c.NextKept(9.5));
    }
}
