using System.Text.Json;
using Snapr.App.Services;
using Snapr.Core;

namespace Snapr.Tests;

public class AnnotationClipboardTests
{
    static readonly ArrowAnnotation Arrow = new(new(1, 2), new(30, 40), "#FFFF3B30", 4) { Shadow = true, GroupId = Guid.NewGuid() };
    static readonly StepAnnotation Step = new(new(5, 5), 3, 32, "#FFFF3B30", "#FFFFFFFF", StepStyle.Letters);
    static string One(Annotation a) => JsonSerializer.Serialize(a, SnaprJson.Options);

    [Fact]
    public void Several_objects_round_trip_in_order() =>
        Assert.Equal([Arrow, Step], AnnotationClipboard.Deserialize(AnnotationClipboard.Serialize([Arrow, Step]))!);

    [Fact]
    public void A_single_object_payload_from_earlier_versions_is_still_read() =>
        Assert.Equal([Step], AnnotationClipboard.Deserialize(One(Step))!);

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("""{"type":"teleporter","x":1}""")]
    [InlineData("""{"type":"arrow"}""")]
    [InlineData("""{"type":"pen"}""")]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("""[{"type":"pen"}]""")]
    public void Garbage_is_rejected(string? json) => Assert.Null(AnnotationClipboard.Deserialize(json));

    [Fact]
    public void One_bad_element_rejects_the_whole_list() =>
        Assert.Null(AnnotationClipboard.Deserialize($"[{One(Arrow)},{{\"type\":\"pen\"}}]"));

    [Fact]
    public void More_than_ten_thousand_objects_are_rejected()
    {
        var h = One(new HighlightAnnotation(new(0, 0, 1, 1), "#66FFFF00"));
        Assert.Null(AnnotationClipboard.Deserialize("[" + string.Join(",", Enumerable.Repeat(h, AnnotationClipboard.MaxObjects + 1)) + "]"));
    }

    [Fact]
    public void Step_payload_without_style_still_deserializes()
    {
        var items = AnnotationClipboard.Deserialize("""{"type":"step","center":{"x":5,"y":5},"number":3,"size":32,"fill":"#FFFF3B30","textColor":"#FFFFFFFF"}""");
        var step = Assert.IsType<StepAnnotation>(Assert.Single(items!));
        Assert.Equal(StepStyle.Numbers, step.Style);
    }
}
