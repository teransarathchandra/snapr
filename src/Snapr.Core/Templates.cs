namespace Snapr.Core;

public enum TemplateKind { StepByStep, BeforeAfter, GridCaptions, SingleTitle }

public enum TextRole { Title, Caption, Label, Description }

public sealed record TextSlot(Pt Position, TextRole Role, int Index);

public sealed record StepSlot(Pt Center, int Number);

public sealed record TemplateResult(int Width, int Height, IReadOnlyList<Rct> Pictures, IReadOnlyList<TextSlot> Texts, IReadOnlyList<StepSlot> Steps);

/// <summary>How many captures each template takes (spec parity 14 §2).</summary>
public static class TemplateRules
{
    public static string? Fits(TemplateKind kind, int count) => kind switch
    {
        TemplateKind.StepByStep => count is >= 1 and <= 20 ? null : "Needs 1 to 20 captures",
        TemplateKind.BeforeAfter => count == 2 ? null : "Needs exactly 2 captures",
        TemplateKind.GridCaptions => count is >= 2 and <= 20 ? null : "Needs 2 to 20 captures",
        _ => count == 1 ? null : "Needs exactly 1 capture",
    };
}

/// <summary>Template geometry (spec parity 14 §2): a title on top, then the pictures with their captions, labels or steps.</summary>
public static class TemplateLayout
{
    public const int Margin = 32, TitleSize = 28, TitleLine = 38, SmallSize = 16, SmallLine = 22, Gap = 32, BadgeSize = 36, MinWidth = 480;
    public const int Top = Margin + TitleLine + 16;   // 86
    const int MaxSide = 32000;
    const long MaxPixels = 100_000_000;

    public static TemplateResult Arrange(TemplateKind kind, IReadOnlyList<(int W, int H)> sizes)
    {
        if (TemplateRules.Fits(kind, sizes.Count) is { } reason) throw new ArgumentException(reason, nameof(sizes));
        if (sizes.Any(s => s.W < 1 || s.H < 1)) throw new ArgumentException("Every picture needs a size.", nameof(sizes));
        var title = new TextSlot(new Pt(Margin, Margin), TextRole.Title, 0);
        var pictures = new List<Rct>();
        var texts = new List<TextSlot> { title };
        var steps = new List<StepSlot>();
        int width = 0, height = 0;
        switch (kind)
        {
            case TemplateKind.StepByStep:
            {
                var y = Top;
                for (var i = 0; i < sizes.Count; i++)
                {
                    var (w, h) = sizes[i];
                    steps.Add(new StepSlot(new Pt(Margin + BadgeSize / 2, y + BadgeSize / 2), i + 1));
                    texts.Add(new TextSlot(new Pt(Margin + 48, y + 7), TextRole.Caption, i));
                    pictures.Add(new Rct(Margin + 48, y + 44, w, h));
                    y += 44 + h + Gap;
                }
                width = Math.Max(MinWidth, Margin + 48 + sizes.Max(s => s.W) + Margin);
                height = (int)pictures[^1].Bottom + Margin;
                break;
            }
            case TemplateKind.BeforeAfter:
            {
                var rowTop = Top + SmallLine + 8;
                var maxH = Math.Max(sizes[0].H, sizes[1].H);
                int x0 = Margin, x1 = Margin + sizes[0].W + Gap;
                foreach (var (x, i) in new[] { (x0, 0), (x1, 1) })
                {
                    texts.Add(new TextSlot(new Pt(x, Top), TextRole.Label, i));
                    pictures.Add(new Rct(x, rowTop + (maxH - sizes[i].H), sizes[i].W, sizes[i].H));
                }
                width = Math.Max(MinWidth, x1 + sizes[1].W + Margin);
                height = rowTop + maxH + Margin;
                break;
            }
            case TemplateKind.GridCaptions:
            {
                var grid = CombineArrange.Arrange(sizes.Select(s => (s.W, s.H + 30)).ToArray(), CombineLayout.Grid, CombineAlign.Start, Gap);
                var shift = Top - Gap;
                for (var i = 0; i < sizes.Count; i++)
                {
                    var cell = grid.Places[i];
                    pictures.Add(new Rct(cell.X, cell.Y + shift, sizes[i].W, sizes[i].H));
                    texts.Add(new TextSlot(new Pt(cell.X, cell.Y + shift + sizes[i].H + 8), TextRole.Caption, i));
                }
                width = Math.Max(MinWidth, grid.Width);
                height = grid.Height + shift;
                break;
            }
            default:
            {
                var (w, h) = sizes[0];
                pictures.Add(new Rct(Margin, Top, w, h));
                texts.Add(new TextSlot(new Pt(Margin, Top + h + 12), TextRole.Description, 0));
                width = Math.Max(MinWidth, Margin + w + Margin);
                height = Top + h + 12 + SmallLine + Margin;
                break;
            }
        }
        return new TemplateResult(width, height, pictures, texts, steps);
    }

    public static bool TooLarge(TemplateResult r) => r.Width > MaxSide || r.Height > MaxSide || (long)r.Width * r.Height > MaxPixels;
}

/// <summary>The editable placeholder texts a template starts with (spec parity 14 §2).</summary>
public static class TemplateTexts
{
    public static string For(TemplateKind kind, TextSlot slot, IReadOnlyList<string?> sourceTitles) => slot.Role switch
    {
        TextRole.Title => "Title",
        TextRole.Description => "Description",
        TextRole.Label => slot.Index == 0 ? "Before" : "After",
        _ when kind == TemplateKind.GridCaptions =>
            slot.Index < sourceTitles.Count && !string.IsNullOrWhiteSpace(sourceTitles[slot.Index]) ? sourceTitles[slot.Index]!.Trim() : "Caption",
        _ => $"Step {slot.Index + 1}",
    };
}
