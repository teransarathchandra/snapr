using System.Text.Json;
using Snapr.Core;

namespace Snapr.Tests;

public class ModelJsonTests
{
    internal static DocumentState SampleState() => DocumentState.Empty with
    {
        Annotations =
        [
            new ArrowAnnotation(new(1, 2), new(30, 40), "#FFFF3B30", 4) { Shadow = true },
            new ShapeAnnotation(ShapeKind.Ellipse, new(5, 5, 20, 10), "#FF3B82F6", 3, "#403B82F6", 4),
            new TextAnnotation(new(10, 10), "Hello\nworld", "Segoe UI", 24, true, "#FF111111", "#FFFFFFFF"),
            new CalloutAnnotation(new(50, 50, 120, 60), "Click here", 18, "#FFFFFFFF", "#FFFF3B30", "#FF111111", new(40, 130)),
            new StepAnnotation(new(80, 80), 3, 32, "#FFFF3B30", "#FFFFFFFF"),
            new HighlightAnnotation(new(0, 0, 50, 12), "#66FFCC00"),
            new PenAnnotation([new(0, 0), new(5, 5), new(9, 2)], "#FF34C759", 3),
            new BlurAnnotation(new(10, 10, 40, 40), BlurMode.Pixelate, 8),
            new MagnifyAnnotation(new(10, 10, 20, 20), new(100, 40), 2.5, MagnifyShape.Circle),
        ],
        Crop = new Rct(1, 2, 300, 200),
        Effects = new DocumentEffects(new BorderEffect("#FF000000", 4), Shadow: true, TornEdge: true),
        NextStepNumber = 4,
    };

    [Fact]
    public void Every_annotation_type_round_trips()
    {
        var state = SampleState();
        var json = JsonSerializer.Serialize(state, SnaprJson.Options);
        var back = JsonSerializer.Deserialize<DocumentState>(json, SnaprJson.Options)!;

        Assert.Equal(json, JsonSerializer.Serialize(back, SnaprJson.Options));
        Assert.Equal(state.Annotations.Select(a => a.GetType()), back.Annotations.Select(a => a.GetType()));
        Assert.Equal(state.Annotations.Select(a => a.Id), back.Annotations.Select(a => a.Id));
    }

    [Fact]
    public void Type_discriminator_and_string_enums_are_written()
    {
        var json = JsonSerializer.Serialize(SampleState(), SnaprJson.Options);
        Assert.Contains("\"type\": \"arrow\"", json);
        Assert.Contains("\"Pixelate\"", json);
    }

    [Fact]
    public void Unknown_fields_are_ignored()
    {
        const string json = """
            {"annotations":[{"futureField":true,"type":"step","center":{"x":1,"y":2},"number":1,"size":24,
              "fill":"#FFFF0000","textColor":"#FFFFFFFF"}],"crop":null,"effects":{},"nextStepNumber":2,"alsoNew":1}
            """;
        var state = JsonSerializer.Deserialize<DocumentState>(json, SnaprJson.Options)!;
        Assert.IsType<StepAnnotation>(Assert.Single(state.Annotations));
        Assert.Equal(2, state.NextStepNumber);
    }
}
