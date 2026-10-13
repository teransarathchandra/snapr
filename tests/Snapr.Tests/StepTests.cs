using System.Text.Json;
using Snapr.Core;

namespace Snapr.Tests;

public class StepTests
{
    static readonly ToolStyle Red = ToolStyle.Default;

    [Theory]
    [InlineData(1, "A")]
    [InlineData(26, "Z")]
    [InlineData(27, "AA")]
    [InlineData(52, "AZ")]
    [InlineData(53, "BA")]
    [InlineData(702, "ZZ")]
    [InlineData(703, "AAA")]
    public void Letters_follow_spreadsheet_columns(int number, string label) =>
        Assert.Equal(label, StepLabels.Format(number, StepStyle.Letters));

    [Fact]
    public void Numbers_are_plain() => Assert.Equal("12", StepLabels.Format(12, StepStyle.Numbers));

    [Fact]
    public void AddStep_uses_the_chosen_style()
    {
        var s = DocumentState.Empty.AddStep(new(5, 5), Red, StepStyle.Letters);
        Assert.Equal(StepStyle.Letters, Assert.IsType<StepAnnotation>(Assert.Single(s.Annotations)).Style);
    }

    [Fact]
    public void V1_step_without_style_loads_as_numbers()
    {
        const string json = """{"type":"step","center":{"x":1,"y":2},"number":4,"size":32,"fill":"#FFFF0000","textColor":"#FFFFFFFF"}""";
        var step = Assert.IsType<StepAnnotation>(JsonSerializer.Deserialize<Annotation>(json, SnaprJson.Options));
        Assert.Equal(StepStyle.Numbers, step.Style);
    }

    [Fact]
    public void Step_style_round_trips_through_tool_options()
    {
        var step = new StepAnnotation(new(0, 0), 1, 32, "#FFFF0000", "#FFFFFFFF");
        var lettered = AnnotationGeometry.WithOptions(step, ToolOptions.Default with { Steps = StepStyle.Letters });
        Assert.Equal(StepStyle.Letters, AnnotationGeometry.OptionsOf(lettered, ToolOptions.Default).Steps);
    }

    [Fact]
    public void SetStepNumber_clamps_and_changes_only_that_step()
    {
        var s = DocumentState.Empty.AddStep(new(1, 1), Red).AddStep(new(2, 2), Red);
        var first = s.Annotations[0].Id;

        Assert.Equal([9999, 2], s.SetStepNumber(first, 50000).Annotations.OfType<StepAnnotation>().Select(x => x.Number));
        Assert.Equal([1, 2], s.SetStepNumber(first, -3).Annotations.OfType<StepAnnotation>().Select(x => x.Number));
        Assert.Equal(3, s.SetStepNumber(first, 7).NextStepNumber);
    }
}
