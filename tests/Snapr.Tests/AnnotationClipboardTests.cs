using Snapr.App.Services;
using Snapr.Core;

namespace Snapr.Tests;

public class AnnotationClipboardTests
{
    [Fact]
    public void Arrow_round_trips()
    {
        var a = new ArrowAnnotation(new(1, 2), new(30, 40), "#FFFF3B30", 4) { Shadow = true };
        Assert.Equal(a, AnnotationClipboard.Deserialize(AnnotationClipboard.Serialize(a)));
    }

    [Fact]
    public void Lettered_step_round_trips()
    {
        var s = new StepAnnotation(new(5, 5), 3, 32, "#FFFF3B30", "#FFFFFFFF", StepStyle.Letters);
        Assert.Equal(s, AnnotationClipboard.Deserialize(AnnotationClipboard.Serialize(s)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("""{"type":"teleporter","x":1}""")]
    [InlineData("""{"type":"arrow"}""")]
    [InlineData("""{"type":"pen"}""")]
    public void Garbage_is_rejected(string? json) => Assert.Null(AnnotationClipboard.Deserialize(json));

    [Fact]
    public void Step_payload_without_style_still_deserializes()
    {
        var s = AnnotationClipboard.Deserialize("""{"type":"step","center":{"x":5,"y":5},"number":3,"size":32,"fill":"#FFFF3B30","textColor":"#FFFFFFFF"}""");
        var step = Assert.IsType<StepAnnotation>(s);
        Assert.Equal(StepStyle.Numbers, step.Style);
    }
}
