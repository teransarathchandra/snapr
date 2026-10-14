using Snapr.Core;

namespace Snapr.Tests;

public class AlignTests
{
    static ShapeAnnotation Box(double x, double y, double w, double h, Guid? group = null) =>
        new(ShapeKind.Rectangle, new Rct(x, y, w, h), "#FFFF0000", 2) { GroupId = group };

    static DocumentState Doc(params Annotation[] items) => DocumentState.Empty with { Annotations = items };
    static Rct B(DocumentState s, int i) => ((ShapeAnnotation)s.Annotations[i]).Bounds;
    static Guid[] Ids(DocumentState s) => s.Annotations.Select(a => a.Id).ToArray();

    // a = (0,0,10,10), b = (40,30,20,40); union = (0,0,60,70), centre x 30, middle y 35.
    [Theory]
    [InlineData(AlignEdge.Left, 0, 0, 0, 30)]
    [InlineData(AlignEdge.Center, 25, 0, 20, 30)]
    [InlineData(AlignEdge.Right, 50, 0, 40, 30)]
    [InlineData(AlignEdge.Top, 0, 0, 40, 0)]
    [InlineData(AlignEdge.Middle, 0, 30, 40, 15)]
    [InlineData(AlignEdge.Bottom, 0, 60, 40, 30)]
    public void Aligns_to_the_edge_of_the_selection(AlignEdge edge, double ax, double ay, double bx, double by)
    {
        var s = Doc(Box(0, 0, 10, 10), Box(40, 30, 20, 40));
        var r = s.Align(Ids(s), edge);
        Assert.Equal((ax, ay), (B(r, 0).X, B(r, 0).Y));
        Assert.Equal((bx, by), (B(r, 1).X, B(r, 1).Y));
    }

    [Fact]
    public void A_group_aligns_as_one_unit()
    {
        var g = Guid.NewGuid();
        var s = Doc(Box(0, 0, 10, 10, g), Box(20, 20, 10, 10, g), Box(100, 50, 10, 10));
        var r = s.Align(Ids(s), AlignEdge.Right);
        Assert.Equal(new Rct(80, 0, 10, 10), B(r, 0));
        Assert.Equal(new Rct(100, 20, 10, 10), B(r, 1));
        Assert.Equal(new Rct(100, 50, 10, 10), B(r, 2));
    }

    [Fact]
    public void Aligning_a_single_unit_changes_nothing()
    {
        var g = Guid.NewGuid();
        var s = Doc(Box(0, 0, 10, 10, g), Box(20, 20, 10, 10, g));
        Assert.Same(s, s.Align(Ids(s), AlignEdge.Left));
    }

    [Fact]
    public void Distribute_horizontally_makes_equal_gaps_whatever_the_selection_order()
    {
        var s = Doc(Box(0, 0, 10, 10), Box(15, 0, 10, 10), Box(100, 0, 20, 10));
        var r = s.Distribute([s.Annotations[2].Id, s.Annotations[0].Id, s.Annotations[1].Id], Axis.Horizontal);
        Assert.Equal(0, B(r, 0).X);
        Assert.Equal(50, B(r, 1).X);   // span 0..120, sizes 40, gap (120 - 40) / 2 = 40
        Assert.Equal(100, B(r, 2).X);
    }

    [Fact]
    public void Distribute_vertically_makes_equal_gaps()
    {
        var s = Doc(Box(0, 0, 10, 10), Box(0, 5, 10, 10), Box(0, 40, 10, 10));
        var r = s.Distribute(Ids(s), Axis.Vertical);
        Assert.Equal(20, B(r, 1).Y);   // span 0..50, sizes 30, gap 10
    }

    [Fact]
    public void Distributing_fewer_than_three_units_changes_nothing()
    {
        var s = Doc(Box(0, 0, 10, 10), Box(50, 0, 10, 10));
        Assert.Same(s, s.Distribute(Ids(s), Axis.Horizontal));
    }

    [Fact]
    public void Unit_count_counts_a_group_once()
    {
        var g = Guid.NewGuid();
        var s = Doc(Box(0, 0, 10, 10, g), Box(20, 20, 10, 10, g), Box(100, 50, 10, 10));
        Assert.Equal(2, s.UnitCount(Ids(s)));
    }
}
