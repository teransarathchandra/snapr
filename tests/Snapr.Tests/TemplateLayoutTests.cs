using Snapr.Core;

namespace Snapr.Tests;

public class TemplateLayoutTests
{
    [Theory]
    [InlineData(TemplateKind.StepByStep, 1, null)]
    [InlineData(TemplateKind.StepByStep, 20, null)]
    [InlineData(TemplateKind.StepByStep, 0, "Needs 1 to 20 captures")]
    [InlineData(TemplateKind.StepByStep, 21, "Needs 1 to 20 captures")]
    [InlineData(TemplateKind.BeforeAfter, 2, null)]
    [InlineData(TemplateKind.BeforeAfter, 3, "Needs exactly 2 captures")]
    [InlineData(TemplateKind.GridCaptions, 2, null)]
    [InlineData(TemplateKind.GridCaptions, 1, "Needs 2 to 20 captures")]
    [InlineData(TemplateKind.SingleTitle, 1, null)]
    [InlineData(TemplateKind.SingleTitle, 2, "Needs exactly 1 capture")]
    public void Fits_counts(TemplateKind kind, int count, string? reason) => Assert.Equal(reason, TemplateRules.Fits(kind, count));

    [Fact]
    public void Step_by_step_rows()
    {
        var r = TemplateLayout.Arrange(TemplateKind.StepByStep, [(200, 100), (300, 50)]);
        Assert.Equal((480, 388), (r.Width, r.Height));
        Assert.Equal([new Rct(80, 130, 200, 100), new Rct(80, 306, 300, 50)], r.Pictures);
        Assert.Equal([new StepSlot(new Pt(50, 104), 1), new StepSlot(new Pt(50, 280), 2)], r.Steps);
        Assert.Equal(
            [new TextSlot(new Pt(32, 32), TextRole.Title, 0), new TextSlot(new Pt(80, 93), TextRole.Caption, 0), new TextSlot(new Pt(80, 269), TextRole.Caption, 1)],
            r.Texts);
    }

    [Fact]
    public void Step_by_step_grows_wider_than_the_minimum() =>
        Assert.Equal(712, TemplateLayout.Arrange(TemplateKind.StepByStep, [(600, 100)]).Width);   // 32 + 48 + 600 + 32

    [Fact]
    public void Before_after_bottom_aligns_the_pictures()
    {
        var r = TemplateLayout.Arrange(TemplateKind.BeforeAfter, [(200, 100), (300, 150)]);
        Assert.Equal((596, 298), (r.Width, r.Height));
        Assert.Equal([new Rct(32, 166, 200, 100), new Rct(264, 116, 300, 150)], r.Pictures);
        Assert.Equal(
            [new TextSlot(new Pt(32, 32), TextRole.Title, 0), new TextSlot(new Pt(32, 86), TextRole.Label, 0), new TextSlot(new Pt(264, 86), TextRole.Label, 1)],
            r.Texts);
        Assert.Empty(r.Steps);
    }

    [Fact]
    public void Grid_uses_the_combine_grid_below_the_title()
    {
        var r = TemplateLayout.Arrange(TemplateKind.GridCaptions, [(100, 50), (100, 50), (100, 50)]);
        Assert.Equal((480, 310), (r.Width, r.Height));
        Assert.Equal([new Rct(32, 86, 100, 50), new Rct(164, 86, 100, 50), new Rct(32, 198, 100, 50)], r.Pictures);
        Assert.Equal(
            [
                new TextSlot(new Pt(32, 32), TextRole.Title, 0),
                new TextSlot(new Pt(32, 144), TextRole.Caption, 0),
                new TextSlot(new Pt(164, 144), TextRole.Caption, 1),
                new TextSlot(new Pt(32, 256), TextRole.Caption, 2),
            ],
            r.Texts);
    }

    [Fact]
    public void Single_with_title_and_description()
    {
        var r = TemplateLayout.Arrange(TemplateKind.SingleTitle, [(300, 200)]);
        Assert.Equal((480, 352), (r.Width, r.Height));
        Assert.Equal([new Rct(32, 86, 300, 200)], r.Pictures);
        Assert.Equal([new TextSlot(new Pt(32, 32), TextRole.Title, 0), new TextSlot(new Pt(32, 298), TextRole.Description, 0)], r.Texts);
    }

    [Fact]
    public void Arrange_rejects_a_wrong_count_or_an_empty_size()
    {
        Assert.Throws<ArgumentException>(() => TemplateLayout.Arrange(TemplateKind.BeforeAfter, [(10, 10)]));
        Assert.Throws<ArgumentException>(() => TemplateLayout.Arrange(TemplateKind.SingleTitle, [(0, 10)]));
    }

    [Fact]
    public void Too_large()
    {
        Assert.False(TemplateLayout.TooLarge(TemplateLayout.Arrange(TemplateKind.SingleTitle, [(300, 200)])));
        Assert.True(TemplateLayout.TooLarge(TemplateLayout.Arrange(TemplateKind.SingleTitle, [(32000, 10)])));            // width 32064
        Assert.True(TemplateLayout.TooLarge(TemplateLayout.Arrange(TemplateKind.SingleTitle, [(10000, 10000)])));         // 10064 × 10152
    }

    [Fact]
    public void Starting_texts()
    {
        var r = TemplateLayout.Arrange(TemplateKind.StepByStep, [(10, 10), (10, 10)]);
        Assert.Equal(["Title", "Step 1", "Step 2"], r.Texts.Select(t => TemplateTexts.For(TemplateKind.StepByStep, t, ["Notepad", null])));
        var ba = TemplateLayout.Arrange(TemplateKind.BeforeAfter, [(10, 10), (10, 10)]);
        Assert.Equal(["Title", "Before", "After"], ba.Texts.Select(t => TemplateTexts.For(TemplateKind.BeforeAfter, t, [null, null])));
        var s = TemplateLayout.Arrange(TemplateKind.SingleTitle, [(10, 10)]);
        Assert.Equal(["Title", "Description"], s.Texts.Select(t => TemplateTexts.For(TemplateKind.SingleTitle, t, ["Notepad"])));
    }

    [Fact]
    public void Grid_captions_use_window_titles_or_Caption()
    {
        var r = TemplateLayout.Arrange(TemplateKind.GridCaptions, [(10, 10), (10, 10), (10, 10)]);
        Assert.Equal(["Title", "Notepad", "Caption", "Caption"], r.Texts.Select(t => TemplateTexts.For(TemplateKind.GridCaptions, t, ["Notepad", null, "   "])));
    }
}
