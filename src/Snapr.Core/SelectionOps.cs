namespace Snapr.Core;

public enum AlignEdge { Left, Center, Right, Top, Middle, Bottom }
public enum Axis { Horizontal, Vertical }

/// <summary>
/// Operations on several objects at once (spec parity 3 §2). A selection is a list of object IDs;
/// a group is every object sharing one non-null GroupId.
/// </summary>
public static class SelectionOps
{
    /// <summary>The IDs plus every object that shares a group with one of them, in document order.</summary>
    public static IReadOnlyList<Guid> ExpandToGroups(this DocumentState s, IEnumerable<Guid> ids)
    {
        var set = ids.ToHashSet();
        var groups = s.Annotations.Where(a => set.Contains(a.Id) && a.GroupId is not null).Select(a => a.GroupId).ToHashSet();
        return s.Annotations.Where(a => set.Contains(a.Id) || (a.GroupId is not null && groups.Contains(a.GroupId))).Select(a => a.Id).ToArray();
    }

    /// <summary>Every object whose bounds touch the box, with their groups.</summary>
    public static IReadOnlyList<Guid> HitInBox(this DocumentState s, Rct box) =>
        s.ExpandToGroups(s.Annotations.Where(a => Touches(AnnotationGeometry.Bounds(a), box)).Select(a => a.Id));

    static bool Touches(Rct a, Rct b) => a.X <= b.Right && b.X <= a.Right && a.Y <= b.Bottom && b.Y <= a.Bottom;

    /// <summary>Shift+click: adds the object's unit (it and its group) to the selection, or removes it if already selected.</summary>
    public static IReadOnlyList<Guid> Toggle(this DocumentState s, IEnumerable<Guid> current, Guid id)
    {
        var set = current.ToHashSet();
        if (!set.Contains(id)) return s.ExpandToGroups(set.Append(id));
        var unit = s.ExpandToGroups([id]).ToHashSet();
        return s.ExpandToGroups(set.Where(x => !unit.Contains(x)));
    }

    public static DocumentState TranslateMany(this DocumentState s, IEnumerable<Guid> ids, double dx, double dy)
    {
        var set = ids.ToHashSet();
        return s with { Annotations = s.Annotations.Select(a => set.Contains(a.Id) ? AnnotationGeometry.Translate(a, dx, dy) : a).ToArray() };
    }

    public static DocumentState RemoveMany(this DocumentState s, IEnumerable<Guid> ids)
    {
        var set = ids.ToHashSet();
        return s with { Annotations = s.Annotations.Where(a => !set.Contains(a.Id)).ToArray() };
    }

    /// <summary>Moves the objects to the top, keeping their order among themselves.</summary>
    public static DocumentState BringToFront(this DocumentState s, IEnumerable<Guid> ids)
    {
        var set = ids.ToHashSet();
        return s with { Annotations = [.. s.Annotations.Where(a => !set.Contains(a.Id)), .. s.Annotations.Where(a => set.Contains(a.Id))] };
    }

    /// <summary>Moves the objects to the bottom, keeping their order among themselves.</summary>
    public static DocumentState SendToBack(this DocumentState s, IEnumerable<Guid> ids)
    {
        var set = ids.ToHashSet();
        return s with { Annotations = [.. s.Annotations.Where(a => set.Contains(a.Id)), .. s.Annotations.Where(a => !set.Contains(a.Id))] };
    }

    /// <summary>Puts the objects (and every member of groups they belong to) in one new group. Fewer than 2 objects: unchanged.</summary>
    public static DocumentState Group(this DocumentState s, IEnumerable<Guid> ids)
    {
        var set = s.ExpandToGroups(ids).ToHashSet();
        if (set.Count < 2) return s;
        var group = Guid.NewGuid();
        return s with { Annotations = s.Annotations.Select(a => set.Contains(a.Id) ? a with { GroupId = group } : a).ToArray() };
    }

    /// <summary>Clears the group tag of the objects and of every member of their groups.</summary>
    public static DocumentState Ungroup(this DocumentState s, IEnumerable<Guid> ids)
    {
        var set = s.ExpandToGroups(ids).ToHashSet();
        return s with { Annotations = s.Annotations.Select(a => set.Contains(a.Id) ? a with { GroupId = null } : a).ToArray() };
    }

    /// <summary>Adds copies with new IDs moved by (+offset, +offset). Each source group becomes one fresh group among the copies.</summary>
    public static (DocumentState State, IReadOnlyList<Guid> Pasted) PasteCopies(this DocumentState s, IReadOnlyList<Annotation> sources, double offset)
    {
        var fresh = new Dictionary<Guid, Guid>();
        Guid? NewGroup(Guid? old)
        {
            if (old is not { } g) return null;
            if (!fresh.TryGetValue(g, out var n)) fresh[g] = n = Guid.NewGuid();
            return n;
        }
        var copies = sources.Select(a => AnnotationGeometry.Translate(a, offset, offset) with { Id = Guid.NewGuid(), GroupId = NewGroup(a.GroupId) }).ToArray();
        return (s with { Annotations = [.. s.Annotations, .. copies] }, copies.Select(c => c.Id).ToArray());
    }

    /// <summary>The selection as units: each whole group, or one ungrouped object, with its overall bounds. Document order.</summary>
    static (IReadOnlyList<Guid> Ids, Rct Bounds)[] Units(DocumentState s, IEnumerable<Guid> ids)
    {
        var set = s.ExpandToGroups(ids).ToHashSet();
        return s.Annotations.Where(a => set.Contains(a.Id))
            .GroupBy(a => a.GroupId ?? a.Id)
            .Select(g => ((IReadOnlyList<Guid>)g.Select(a => a.Id).ToArray(), g.Select(AnnotationGeometry.Bounds).Aggregate((x, y) => x.Union(y))))
            .ToArray();
    }

    public static int UnitCount(this DocumentState s, IEnumerable<Guid> ids) => Units(s, ids).Length;

    /// <summary>Lines up each unit's edge (or centre) with the same edge of all selected bounds. Needs 2+ units.</summary>
    public static DocumentState Align(this DocumentState s, IEnumerable<Guid> ids, AlignEdge edge)
    {
        var units = Units(s, ids);
        if (units.Length < 2) return s;
        var all = units.Select(u => u.Bounds).Aggregate((x, y) => x.Union(y));
        foreach (var (unit, b) in units)
        {
            var (dx, dy) = edge switch
            {
                AlignEdge.Left => (all.X - b.X, 0.0),
                AlignEdge.Center => (all.Center.X - b.Center.X, 0.0),
                AlignEdge.Right => (all.Right - b.Right, 0.0),
                AlignEdge.Top => (0.0, all.Y - b.Y),
                AlignEdge.Middle => (0.0, all.Center.Y - b.Center.Y),
                _ => (0.0, all.Bottom - b.Bottom),
            };
            s = s.TranslateMany(unit, dx, dy);
        }
        return s;
    }

    /// <summary>Keeps the first and last unit (by left or top edge) and spaces the others with equal gaps. Needs 3+ units.</summary>
    public static DocumentState Distribute(this DocumentState s, IEnumerable<Guid> ids, Axis axis)
    {
        var h = axis == Axis.Horizontal;
        double Start(Rct r) => h ? r.X : r.Y;
        double Size(Rct r) => h ? r.W : r.H;
        var units = Units(s, ids).OrderBy(u => Start(u.Bounds)).ToArray();
        if (units.Length < 3) return s;
        var first = units[0].Bounds;
        var last = units[^1].Bounds;
        var gap = (Start(last) + Size(last) - Start(first) - units.Sum(u => Size(u.Bounds))) / (units.Length - 1);
        var pos = Start(first) + Size(first) + gap;
        foreach (var (unit, b) in units[1..^1])
        {
            var d = pos - Start(b);
            s = s.TranslateMany(unit, h ? d : 0, h ? 0 : d);
            pos += Size(b) + gap;
        }
        return s;
    }
}
