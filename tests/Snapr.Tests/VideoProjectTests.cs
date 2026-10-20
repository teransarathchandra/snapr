using Snapr.Core;
using Snapr.Core.Video;

namespace Snapr.Tests;

public class VideoProjectTests
{
    static Annotation Box() => new ShapeAnnotation(ShapeKind.Rectangle, new Rct(10, 10, 50, 40), "#FFFF0000", 4);
    static VideoProject P(double duration = 10) => VideoProject.For(duration);

    [Fact]
    public void Place_shows_for_three_seconds_from_the_playhead()
    {
        var o = P().Place(Box(), 2);
        Assert.Equal((2.0, 5.0), (o.Start, o.End));
        var late = P().Place(Box(), 9);
        Assert.Equal((9.0, 10.0), (late.Start, late.End));
        var end = P().Place(Box(), 10);
        Assert.Equal((9.9, 10.0), (end.Start, end.End), new TupleTolerance());
        Assert.Equal(0, P().Place(Box(), -1).Start);
    }

    [Fact]
    public void SetObjects_clamps_and_caps()
    {
        var a = Box();
        var p = P().SetObjects([new VideoObject(a, -2, 30), new VideoObject(Box(), 5, 5)]);
        Assert.Equal((0.0, 10.0), (p.Objects[0].Start, p.Objects[0].End));
        Assert.Equal((4.9, 5.0), (p.Objects[1].Start, p.Objects[1].End), new TupleTolerance());
        var many = P().SetObjects(Enumerable.Range(0, 250).Select(_ => new VideoObject(Box(), 1, 2)).ToArray());
        Assert.Equal(VideoProject.MaxObjects, many.Objects.Count);
    }

    [Fact]
    public void Retime_clamps_and_ignores_unknown_ids()
    {
        var a = Box();
        var p = P().SetObjects([new VideoObject(a, 1, 2)]);
        var moved = p.Retime(a.Id, 8, 12);
        Assert.Equal((8.0, 10.0), (moved.Objects[0].Start, moved.Objects[0].End));
        Assert.Same(p, p.Retime(Guid.NewGuid(), 0, 1));
    }

    [Fact]
    public void VisibleAt_is_start_inclusive_end_exclusive()
    {
        Annotation a = Box(), b = Box();
        var p = P().SetObjects([new VideoObject(a, 1, 2), new VideoObject(b, 1.5, 3)]);
        Assert.Empty(p.VisibleAt(0.99));
        Assert.Equal([a.Id], p.VisibleAt(1).Select(o => o.Item.Id));
        Assert.Equal([a.Id, b.Id], p.VisibleAt(1.5).Select(o => o.Item.Id));
        Assert.Equal([b.Id], p.VisibleAt(2).Select(o => o.Item.Id));
        Assert.Empty(p.VisibleAt(3));
    }

    [Fact]
    public void Layers_group_identical_times_in_first_seen_order()
    {
        Annotation a = Box(), b = Box(), c = Box();
        var layers = P().SetObjects([new VideoObject(a, 1, 2), new VideoObject(b, 3, 4), new VideoObject(c, 1, 2)]).Layers();
        Assert.Equal(2, layers.Count);
        Assert.Equal((1.0, 2.0), (layers[0].Start, layers[0].End));
        Assert.Equal([a.Id, c.Id], layers[0].Items.Select(i => i.Id));
        Assert.Equal([b.Id], layers[1].Items.Select(i => i.Id));
    }

    [Fact]
    public void Annotated_keeps_edits_deletes_adds_and_leaves_others()
    {
        Annotation shownKept = Box(), shownDeleted = Box(), hidden = Box(), added = Box();
        var p = P().SetObjects([new VideoObject(shownKept, 1, 4), new VideoObject(shownDeleted, 2, 5), new VideoObject(hidden, 7, 8)]);
        var edited = ((ShapeAnnotation)shownKept) with { Stroke = "#FF00FF00" };
        var next = p.Annotated(3, [shownKept.Id, shownDeleted.Id], [edited, added]);
        Assert.Equal([shownKept.Id, hidden.Id, added.Id], next.Objects.Select(o => o.Item.Id));
        Assert.Equal("#FF00FF00", ((ShapeAnnotation)next.Objects[0].Item).Stroke);
        Assert.Equal((1.0, 4.0), (next.Objects[0].Start, next.Objects[0].End));
        Assert.Equal((3.0, 6.0), (next.Objects[2].Start, next.Objects[2].End));
    }

    [Fact]
    public void Annotated_takes_a_repeated_item_once()
    {
        var added = Box();
        Assert.Equal([added.Id], P().Annotated(3, [], [added, added]).Objects.Select(o => o.Item.Id));

        var kept = Box();
        var p = P().SetObjects([new VideoObject(kept, 1, 4)]);
        var edited = ((ShapeAnnotation)kept) with { Stroke = "#FF00FF00" };
        var next = p.Annotated(3, [kept.Id], [edited, edited]);
        Assert.Equal([kept.Id], next.Objects.Select(o => o.Item.Id));
    }

    [Fact]
    public void Rows_stack_overlaps_and_share_the_last_row()
    {
        VideoObject O(double s, double e) => new(Box(), s, e);
        Assert.Equal([0, 1, 0], VideoProject.Rows([O(0, 2), O(1, 3), O(2, 4)]));
        Assert.Equal([0, 1, 1], VideoProject.Rows([O(0, 5), O(0, 5), O(0, 5)], maxRows: 2));
    }

    sealed class TupleTolerance : IEqualityComparer<(double, double)>
    {
        public bool Equals((double, double) x, (double, double) y) => Math.Abs(x.Item1 - y.Item1) < 1e-9 && Math.Abs(x.Item2 - y.Item2) < 1e-9;
        public int GetHashCode((double, double) obj) => 0;
    }
}
