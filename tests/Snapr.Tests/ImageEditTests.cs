using System.IO;
using System.Text.Json;
using Snapr.Core;

namespace Snapr.Tests;

public class ImageEditTests
{
    [Fact]
    public void Size_after_each_kind_of_edit()
    {
        Assert.Equal((50, 100), ImageEditRules.SizeAfter(100, 50, new RotateEdit(1)));
        Assert.Equal((100, 50), ImageEditRules.SizeAfter(100, 50, new RotateEdit(2)));
        Assert.Equal((50, 100), ImageEditRules.SizeAfter(100, 50, new RotateEdit(3)));
        Assert.Equal((100, 50), ImageEditRules.SizeAfter(100, 50, new FlipEdit(true)));
        Assert.Equal((30, 20), ImageEditRules.SizeAfter(100, 50, new ResizeEdit(30, 20)));
        Assert.Equal((104, 60), ImageEditRules.SizeAfter(100, 50, new MarginsEdit(1, 2, 3, 8, "#FFFFFFFF")));
        Assert.Equal((100, 50), ImageEditRules.SizeAfter(100, 50, new FillEdit(1, 1, "#FFFF0000", 10)));
        Assert.Equal((24, 42), ImageEditRules.SizeAfter(100, 50, [new ResizeEdit(30, 20), new RotateEdit(1), new MarginsEdit(2, 6, 2, 6, "#00000000")]));
    }

    [Fact]
    public void Edits_round_trip_through_json()
    {
        var s = DocumentState.Empty with
        {
            Edits = [new RotateEdit(3), new FlipEdit(false), new ResizeEdit(10, 20), new MarginsEdit(1, 2, 3, 4, "#00000000"), new FillEdit(5, 6, "#FF00FF00", 30)],
        };
        var json = JsonSerializer.Serialize(s, SnaprJson.Options);
        Assert.Contains("\"type\": \"margins\"", json);
        var back = JsonSerializer.Deserialize<DocumentState>(json, SnaprJson.Options)!;
        Assert.Equal(s.Edits, back.Edits);
    }

    [Fact]
    public void Old_state_json_without_edits_has_none()
    {
        var back = JsonSerializer.Deserialize<DocumentState>(
            """{"annotations":[],"crop":null,"effects":{"border":null,"shadow":false,"tornEdge":false},"nextStepNumber":1}""", SnaprJson.Options)!;
        Assert.Empty(back.Edits);
    }

    [Fact]
    public void SameAs_sees_edits()
    {
        var a = DocumentState.Empty;
        Assert.False(a.SameAs(a with { Edits = [new RotateEdit(1)] }));
        Assert.True((a with { Edits = [new RotateEdit(1)] }).SameAs(a with { Edits = [new RotateEdit(1)] }));
    }

    [Fact]
    public void Validate_accepts_sensible_edits() =>
        ImageEditRules.Validate([new RotateEdit(1), new ResizeEdit(200, 400), new MarginsEdit(0, 4000, 0, 0, "#FFFFFFFF"), new FillEdit(199, 4399, "#FF000000", 100)], 100, 50);

    static readonly ImageEdit?[] BadEdits =
    [
        new RotateEdit(0), new RotateEdit(4), new ResizeEdit(0, 10), new ResizeEdit(32_001, 10), new ResizeEdit(20_000, 20_000),
        new MarginsEdit(-1, 0, 0, 0, "#FFFFFFFF"), new MarginsEdit(4_001, 0, 0, 0, "#FFFFFFFF"), new MarginsEdit(0, 0, 0, 0, "white"),
        new FillEdit(100, 0, "#FFFFFFFF", 10), new FillEdit(0, -1, "#FFFFFFFF", 10), new FillEdit(0, 0, "#FFFFFFFF", 101), new FillEdit(0, 0, "#FFF", 10),
        null,
    ];

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(12)]
    public void Validate_refuses_bad_values(int index)
    {
        var ex = Assert.Throws<InvalidDataException>(() => ImageEditRules.Validate([BadEdits[index]], 100, 50));
        Assert.Equal("The image edits are not valid.", ex.Message);
    }

    [Fact]
    public void Validate_refuses_more_than_1000_edits() =>
        Assert.Throws<InvalidDataException>(() => ImageEditRules.Validate(Enumerable.Repeat<ImageEdit?>(new FlipEdit(true), 1001).ToArray(), 100, 50));

    [Fact]
    public void Load_refuses_a_file_with_invalid_edits()
    {
        var path = Path.Combine(Path.GetTempPath(), $"snapr-test-{Guid.NewGuid():N}.snapr");
        try
        {
            var doc = new CaptureDocument
            {
                BasePng = SnaprFileTests.PngHeader(100, 50), PixelWidth = 100, PixelHeight = 50,
                Meta = new CaptureMeta(DateTimeOffset.UnixEpoch, CaptureKind.Region, null),
                State = DocumentState.Empty with { Edits = [new ResizeEdit(60_000, 60_000)] },
            };
            SnaprFile.Save(doc, [1], path);
            var ex = Assert.Throws<InvalidDataException>(() => SnaprFile.Load(path));
            Assert.Equal("The image edits are not valid.", ex.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_keeps_valid_edits()
    {
        var path = Path.Combine(Path.GetTempPath(), $"snapr-test-{Guid.NewGuid():N}.snapr");
        try
        {
            var doc = new CaptureDocument
            {
                BasePng = SnaprFileTests.PngHeader(100, 50), PixelWidth = 100, PixelHeight = 50,
                Meta = new CaptureMeta(DateTimeOffset.UnixEpoch, CaptureKind.Region, null),
                State = DocumentState.Empty with { Edits = [new RotateEdit(1)] },
            };
            SnaprFile.Save(doc, [1], path);
            Assert.Equal([new RotateEdit(1)], SnaprFile.Load(path).State.Edits);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("#FF102030", true, 0xFF102030u)]
    [InlineData("#00000000", true, 0u)]
    [InlineData("#102030", false, 0u)]
    [InlineData("# FF10203", false, 0u)]
    [InlineData("#GG102030", false, 0u)]
    [InlineData(null, false, 0u)]
    public void TryArgb_parses_only_aarrggbb(string? text, bool ok, uint expected)
    {
        Assert.Equal(ok, ColorText.TryArgb(text, out var argb));
        if (ok) Assert.Equal(expected, argb);
    }

    [Fact]
    public void Proportions_round_to_whole_pixels()
    {
        Assert.Equal(25, ImageEditRules.ProportionalHeight(100, 50, 50));
        Assert.Equal(1, ImageEditRules.ProportionalHeight(1000, 1, 10));     // never 0
        Assert.Equal(133, ImageEditRules.ProportionalWidth(200, 150, 100));   // 133.33 -> 133
        Assert.Equal((150, 75), ImageEditRules.FromPercent(100, 50, 150));
        Assert.Equal((1, 1), ImageEditRules.FromPercent(100, 50, 0.1));
    }

    [Theory]
    [InlineData("640", "480", true)]
    [InlineData(" 640 ", "480", true)]
    [InlineData("0", "480", false)]
    [InlineData("32001", "10", false)]
    [InlineData("20000", "20000", false)]
    [InlineData("12.5", "10", false)]
    [InlineData("-5", "10", false)]
    [InlineData("abc", "10", false)]
    [InlineData("", "10", false)]
    public void ParseSize_accepts_only_whole_numbers_within_limits(string w, string h, bool ok) =>
        Assert.Equal(ok, ImageEditRules.ParseSize(w, h) is not null);

    [Theory]
    [InlineData("0", 0)]
    [InlineData("4000", 4000)]
    [InlineData("4001", null)]
    [InlineData("-1", null)]
    [InlineData("x", null)]
    public void ParseMargin_accepts_0_to_4000(string text, int? expected) => Assert.Equal(expected, ImageEditRules.ParseMargin(text));

    [Fact]
    public void Fill_tool_defaults()
    {
        Assert.Equal(10, ToolOptions.Default.FillTolerance);
        Assert.Equal(ToolKind.Eraser + 1, ToolKind.Fill);   // appended, so saved LastTool/QuickStyles keep their meaning
    }

    [Fact]
    public void Limit_message_names_the_limit() =>
        Assert.Equal("This capture has reached its limit of 1,000 image edits.", ImageEditRules.LimitMessage);
}
