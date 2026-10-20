namespace Snapr.Core.Video;

/// <summary>An object drawn on a video, shown from Start to End in source seconds, before cuts (spec parity 17 §2).</summary>
public sealed record VideoObject(Annotation Item, double Start, double End);

/// <summary>A video's edits: what is kept, and the timed objects in drawing order (spec parity 17 §2). Immutable, for undo.</summary>
public sealed record VideoProject(VideoCut Cut, IReadOnlyList<VideoObject> Objects)
{
    public const int MaxObjects = 200;
    public const double DefaultLength = 3, MinLength = 0.1;

    public static VideoProject For(double duration) => new(VideoCut.For(duration), []);

    double Duration => Cut.Duration;

    /// <summary>Inside the video and at least MinLength long (the start moves back when needed).</summary>
    VideoObject Clamp(VideoObject o)
    {
        var len = Math.Min(MinLength, Duration);
        var end = Math.Clamp(o.End, len, Duration);
        return o with { Start = Math.Clamp(o.Start, 0, end - len), End = end };
    }

    /// <summary>A new object shown from <paramref name="at"/> for DefaultLength (less at the end of the video).</summary>
    public VideoObject Place(Annotation item, double at)
    {
        var start = Math.Clamp(at, 0, Math.Max(0, Duration - MinLength));
        return new VideoObject(item, start, Math.Min(start + DefaultLength, Duration));
    }

    public VideoProject SetObjects(IReadOnlyList<VideoObject> objects) => this with { Objects = objects.Take(MaxObjects).Select(Clamp).ToArray() };

    public VideoProject Retime(Guid id, double start, double end) =>
        Objects.Any(o => o.Item.Id == id)
            ? this with { Objects = Objects.Select(o => o.Item.Id == id ? Clamp(o with { Start = start, End = end }) : o).ToArray() }
            : this;

    public IReadOnlyList<VideoObject> VisibleAt(double sourceTime) => Objects.Where(o => o.Start <= sourceTime && sourceTime < o.End).ToArray();

    /// <summary>Objects with the same times drawn together (one overlay picture each), in order of first appearance.</summary>
    public IReadOnlyList<(double Start, double End, IReadOnlyList<Annotation> Items)> Layers() =>
        Objects.GroupBy(o => (o.Start, o.End))
            .Select(g => (g.Key.Start, g.Key.End, (IReadOnlyList<Annotation>)g.Select(o => o.Item).ToArray()))
            .ToArray();

    /// <summary>The objects after the Annotate window at <paramref name="at"/> (spec parity 17 §4): shown objects that remain keep
    /// their times with their edited item, shown ones missing from <paramref name="result"/> are removed, new ones are placed at
    /// <paramref name="at"/>, and objects that weren't shown are untouched.</summary>
    public VideoProject Annotated(double at, IReadOnlyCollection<Guid> shown, IReadOnlyList<Annotation> result)
    {
        var byId = result.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.Last());
        var list = new List<VideoObject>();
        foreach (var o in Objects)
        {
            if (!shown.Contains(o.Item.Id)) list.Add(o);
            else if (byId.TryGetValue(o.Item.Id, out var edited)) list.Add(o with { Item = edited });
        }
        var known = Objects.Select(o => o.Item.Id).ToHashSet();
        list.AddRange(result.Where(a => !known.Contains(a.Id)).DistinctBy(a => a.Id).Select(a => Place(a, at)));
        return SetObjects(list);
    }

    /// <summary>Timeline row for each object so overlapping bars sit on different rows; past <paramref name="maxRows"/> they share the last row.</summary>
    public static int[] Rows(IReadOnlyList<VideoObject> objects, int maxRows = 4)
    {
        var rowEnds = new double[maxRows];
        Array.Fill(rowEnds, double.NegativeInfinity);
        var rows = new int[objects.Count];
        foreach (var i in Enumerable.Range(0, objects.Count).OrderBy(i => objects[i].Start))
        {
            var r = Array.FindIndex(rowEnds, e => e <= objects[i].Start);
            if (r < 0) r = maxRows - 1;
            rows[i] = r;
            rowEnds[r] = Math.Max(rowEnds[r], objects[i].End);
        }
        return rows;
    }
}
