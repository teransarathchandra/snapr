namespace Snapr.Core.Video;

/// <summary>A span of a video in seconds.</summary>
public readonly record struct TimeRange(double Start, double End)
{
    public double Length => End - Start;
}

/// <summary>What is kept of a video (spec parity 8 §2): the trimmed span minus the removed parts. Immutable, for undo.</summary>
public sealed record VideoCut(double Duration, double TrimStart, double TrimEnd, IReadOnlyList<TimeRange> Removed)
{
    public const double MinKept = 0.1, MinRemove = 0.05;
    const double Eps = 1e-9;

    public static VideoCut For(double duration) => new(duration, 0, duration, []);

    /// <summary>New trim, clamped to the video; unchanged if less than MinKept would remain.</summary>
    public VideoCut WithTrim(double start, double end)
    {
        start = Math.Clamp(start, 0, Duration);
        end = Math.Clamp(end, 0, Duration);
        if (end - start < MinKept - Eps) return this;
        var next = this with { TrimStart = start, TrimEnd = end };
        return next.KeptDuration < MinKept - Eps ? this : next;
    }

    /// <summary>Removes a part (clamped to the trimmed span); unchanged if it is shorter than MinRemove or would leave less than MinKept.</summary>
    public VideoCut Remove(TimeRange r)
    {
        double s = Math.Max(r.Start, TrimStart), e = Math.Min(r.End, TrimEnd);
        if (e - s < MinRemove) return this;
        var next = this with { Removed = [.. Removed, new TimeRange(s, e)] };
        return next.KeptDuration < MinKept - Eps ? this : next;
    }

    /// <summary>The kept ranges in order; overlapping or touching removals act as one.</summary>
    public IReadOnlyList<TimeRange> Kept
    {
        get
        {
            var kept = new List<TimeRange>();
            var t = TrimStart;
            foreach (var cut in Removed.Where(x => x.End > TrimStart && x.Start < TrimEnd).OrderBy(x => x.Start))
            {
                if (cut.Start > t + Eps) kept.Add(new TimeRange(t, Math.Min(cut.Start, TrimEnd)));
                t = Math.Max(t, cut.End);
                if (t >= TrimEnd) break;
            }
            if (t < TrimEnd - Eps) kept.Add(new TimeRange(t, TrimEnd));
            return kept;
        }
    }

    public double KeptDuration => Kept.Sum(k => k.Length);

    /// <summary>Source time of a time in the result (0..KeptDuration); past the end gives the last kept moment.</summary>
    public double ToSource(double outputTime)
    {
        var kept = Kept;
        var acc = 0.0;
        foreach (var k in kept)
        {
            if (outputTime <= acc + k.Length) return k.Start + Math.Max(0, outputTime - acc);
            acc += k.Length;
        }
        return kept.Count > 0 ? kept[^1].End : TrimEnd;
    }

    /// <summary>Where playback should be for a source time: the time itself inside a kept range, the start of the next kept
    /// range in a gap, or null at or after the end of the result.</summary>
    public double? NextKept(double sourceTime)
    {
        foreach (var k in Kept)
        {
            if (sourceTime < k.Start) return k.Start;
            if (sourceTime < k.End - Eps) return sourceTime;
        }
        return null;
    }
}
