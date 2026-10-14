namespace Snapr.Core;

public static class DocumentOps
{
    public static DocumentState Add(this DocumentState s, Annotation a) =>
        s with { Annotations = [.. s.Annotations, a] };

    public static DocumentState Replace(this DocumentState s, Annotation a) =>
        s with { Annotations = s.Annotations.Select(x => x.Id == a.Id ? a : x).ToArray() };

    /// <summary>Adds a copy of <paramref name="source"/> with a new ID, moved down-right by <paramref name="offset"/>.</summary>
    public static (DocumentState State, Annotation Pasted) PasteCopy(this DocumentState s, Annotation source, double offset)
    {
        var pasted = AnnotationGeometry.Translate(source, offset, offset) with { Id = Guid.NewGuid() };
        return (s.Add(pasted), pasted);
    }

    public static DocumentState Remove(this DocumentState s, Guid id) =>
        s with { Annotations = s.Annotations.Where(x => x.Id != id).ToArray() };

    public static DocumentState BringToFront(this DocumentState s, Guid id) =>
        s.Annotations.FirstOrDefault(x => x.Id == id) is { } a
            ? s with { Annotations = [.. s.Annotations.Where(x => x.Id != id), a] }
            : s;

    public static DocumentState SendToBack(this DocumentState s, Guid id) =>
        s.Annotations.FirstOrDefault(x => x.Id == id) is { } a
            ? s with { Annotations = [a, .. s.Annotations.Where(x => x.Id != id)] }
            : s;

    public static DocumentState AddStep(this DocumentState s, Pt center, ToolStyle style, StepStyle steps = StepStyle.Numbers) =>
        s.Add(new StepAnnotation(center, s.NextStepNumber, AnnotationGeometry.StepSize(style.FontSize), style.Color, "#FFFFFFFF", steps)
            { Shadow = style.Shadow }) with { NextStepNumber = s.NextStepNumber + 1 };

    /// <summary>Sets one step's value (clamped to 1–9999). Other steps and the next number are unchanged.</summary>
    public static DocumentState SetStepNumber(this DocumentState s, Guid id, int value) =>
        s.Annotations.FirstOrDefault(a => a.Id == id) is StepAnnotation step
            ? s.Replace(step with { Number = Math.Clamp(value, 1, 9999) })
            : s;

    public static DocumentState RestartSteps(this DocumentState s) => s with { NextStepNumber = 1 };

    /// <summary>Clamps the crop to the image and snaps it outward to whole pixels. A crop outside the image is ignored.</summary>
    public static DocumentState SetCrop(this DocumentState s, Rct crop, int imageW, int imageH)
    {
        var c = crop.Intersect(new Rct(0, 0, imageW, imageH));
        if (c.IsEmpty) return s;
        double x = Math.Floor(c.X), y = Math.Floor(c.Y);
        double r = Math.Min(imageW, Math.Ceiling(c.Right)), b = Math.Min(imageH, Math.Ceiling(c.Bottom));
        return s with { Crop = new Rct(x, y, r - x, b - y) };
    }

    public static DocumentState ResetCrop(this DocumentState s) => s with { Crop = null };

    public static DocumentState SetEffects(this DocumentState s, DocumentEffects e) => s with { Effects = e };

    public static Annotation? HitTest(this DocumentState s, Pt p, double tol) =>
        Enumerable.Reverse(s.Annotations).FirstOrDefault(a => AnnotationGeometry.Hit(a, p, tol));

    /// <summary>Sets the text of a Text or Callout. A Text left blank is removed; an empty Callout is kept.</summary>
    public static DocumentState CommitText(this DocumentState s, Guid id, string text)
    {
        var a = s.Annotations.FirstOrDefault(x => x.Id == id);
        if (a is null) return s;
        if (a is TextAnnotation && string.IsNullOrWhiteSpace(text)) return s.Remove(id);
        return s.Replace(a switch
        {
            TextAnnotation t => t with { Text = text },
            CalloutAnnotation c => c with { Text = text },
            _ => a,
        });
    }

    public static bool SameAs(this DocumentState a, DocumentState b) =>
        a.Annotations.SequenceEqual(b.Annotations) && a.Crop == b.Crop && a.Effects == b.Effects && a.NextStepNumber == b.NextStepNumber
        && a.Edits.SequenceEqual(b.Edits);
}
