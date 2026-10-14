using System.Text.Json;
using Snapr.Core;

namespace Snapr.Tests;

public class SelectionTests
{
    static ShapeAnnotation Box(double x, double y, double w = 10, double h = 10, Guid? group = null) =>
        new(ShapeKind.Rectangle, new Rct(x, y, w, h), "#FFFF0000", 2) { GroupId = group };

    static DocumentState Doc(params Annotation[] items) => DocumentState.Empty with { Annotations = items };

    [Fact]
    public void Group_id_is_omitted_when_null_and_round_trips_when_set()
    {
        Assert.DoesNotContain("groupId", JsonSerializer.Serialize<Annotation>(Box(0, 0), SnaprJson.Options));
        var grouped = Box(0, 0, group: Guid.NewGuid());
        var json = JsonSerializer.Serialize<Annotation>(grouped, SnaprJson.Options);
        Assert.Equal(grouped, JsonSerializer.Deserialize<Annotation>(json, SnaprJson.Options));
    }

    [Fact]
    public void Old_json_without_group_id_loads_ungrouped()
    {
        var a = JsonSerializer.Deserialize<Annotation>(
            """{"type":"highlight","bounds":{"x":1,"y":2,"w":3,"h":4},"color":"#66FFFF00"}""", SnaprJson.Options);
        Assert.Null(a!.GroupId);
    }

    [Fact]
    public void Expanding_adds_every_member_of_a_touched_group_in_document_order()
    {
        var g = Guid.NewGuid();
        Annotation a = Box(0, 0, group: g), b = Box(50, 0), c = Box(100, 0, group: g);
        var s = Doc(a, b, c);
        Assert.Equal([a.Id, c.Id], s.ExpandToGroups([c.Id]));
        Assert.Equal([b.Id], s.ExpandToGroups([b.Id]));
    }

    [Fact]
    public void Box_selects_objects_it_touches_and_their_groups()
    {
        var g = Guid.NewGuid();
        Annotation a = Box(0, 0), b = Box(50, 0, group: g), c = Box(200, 200, group: g), d = Box(300, 0);
        var s = Doc(a, b, c, d);
        Assert.Equal([a.Id, b.Id, c.Id], s.HitInBox(new Rct(5, 5, 50, 2)));
    }

    [Fact]
    public void Toggle_adds_or_removes_a_whole_unit()
    {
        var g = Guid.NewGuid();
        Annotation a = Box(0, 0), b = Box(50, 0, group: g), c = Box(100, 0, group: g);
        var s = Doc(a, b, c);
        var withGroup = s.Toggle([a.Id], c.Id);
        Assert.Equal([a.Id, b.Id, c.Id], withGroup);
        Assert.Equal([a.Id], s.Toggle(withGroup, b.Id));
    }

    [Fact]
    public void Translate_and_remove_touch_only_the_given_objects()
    {
        Annotation a = Box(0, 0), b = Box(50, 0);
        var s = Doc(a, b);
        var moved = s.TranslateMany([a.Id], 5, 7);
        Assert.Equal(new Rct(5, 7, 10, 10), ((ShapeAnnotation)moved.Annotations[0]).Bounds);
        Assert.Equal(b, moved.Annotations[1]);
        Assert.Equal([b], s.RemoveMany([a.Id]).Annotations);
    }

    [Fact]
    public void Front_and_back_keep_the_selected_objects_order()
    {
        Annotation a = Box(0, 0), b = Box(1, 0), c = Box(2, 0), d = Box(3, 0);
        var s = Doc(a, b, c, d);
        Assert.Equal([b.Id, d.Id, a.Id, c.Id], s.BringToFront([c.Id, a.Id]).Annotations.Select(x => x.Id));
        Assert.Equal([b.Id, d.Id, a.Id, c.Id], s.SendToBack([d.Id, b.Id]).Annotations.Select(x => x.Id));
    }

    [Fact]
    public void Grouping_merges_existing_groups_and_ungrouping_clears_them()
    {
        var old = Guid.NewGuid();
        Annotation a = Box(0, 0), b = Box(10, 0, group: old), c = Box(20, 0, group: old), d = Box(30, 0);
        var s = Doc(a, b, c, d).Group([a.Id, b.Id]);
        var groups = s.Annotations.Take(3).Select(x => x.GroupId).Distinct().ToArray();
        Assert.Single(groups);
        Assert.NotNull(groups[0]);
        Assert.NotEqual(old, groups[0]);
        Assert.Null(s.Annotations[3].GroupId);
        Assert.All(s.Ungroup([a.Id]).Annotations, x => Assert.Null(x.GroupId));
    }

    [Fact]
    public void Grouping_one_object_changes_nothing()
    {
        var s = Doc(Box(0, 0));
        Assert.Same(s, s.Group([s.Annotations[0].Id]));
    }

    [Fact]
    public void Pasted_copies_get_new_ids_and_their_own_groups()
    {
        var g = Guid.NewGuid();
        Annotation a = Box(0, 0, group: g), b = Box(20, 0, group: g), c = Box(40, 0);
        var (next, pasted) = Doc(a, b, c).PasteCopies([a, b, c], 16);
        Assert.Equal(6, next.Annotations.Count);
        var copies = next.Annotations.Skip(3).ToArray();
        Assert.Equal(pasted, copies.Select(x => x.Id));
        Assert.DoesNotContain(copies, x => x.Id == a.Id || x.Id == b.Id || x.Id == c.Id);
        Assert.Equal(copies[0].GroupId, copies[1].GroupId);
        Assert.NotNull(copies[0].GroupId);
        Assert.NotEqual(g, copies[0].GroupId);
        Assert.Null(copies[2].GroupId);
        Assert.Equal(new Rct(16, 16, 10, 10), ((ShapeAnnotation)copies[0]).Bounds);
    }
}
