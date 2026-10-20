using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapr.App.Rendering;
using Snapr.App.Services;
using Snapr.Core;

namespace Snapr.Tests;

public class TemplateServiceTests
{
    static BitmapSource Solid(int w, int h, uint argb = 0xFFFF0000)
    {
        var b = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat(argb, w * h).ToArray(), w * 4);
        b.Freeze();
        return b;
    }

    [Fact]
    public void Step_by_step_objects() => Sta.Run(() =>
    {
        var output = TemplateService.Build(TemplateKind.StepByStep, [Solid(200, 100), Solid(300, 50)], [null, null], "#FFFFFFFF")!;
        Assert.Equal((480, 388), (output.Base.PixelWidth, output.Base.PixelHeight));
        Assert.Equal([new Rct(80, 130, 200, 100), new Rct(80, 306, 300, 50)], output.Objects.OfType<ImageAnnotation>().Select(i => i.Bounds));
        var texts = output.Objects.OfType<TextAnnotation>().ToArray();
        Assert.Equal(["Title", "Step 1", "Step 2"], texts.Select(t => t.Text));
        Assert.Equal((28.0, true, "#FF111111", "Segoe UI"), (texts[0].FontSize, texts[0].Bold, texts[0].Color, texts[0].FontFamily));
        Assert.Equal((16.0, false), (texts[1].FontSize, texts[1].Bold));
        Assert.Equal(texts[0].Id, output.TitleId);
        var steps = output.Objects.OfType<StepAnnotation>().ToArray();
        Assert.Equal([1, 2], steps.Select(s => s.Number));
        Assert.Equal((36.0, "#FF3B82F6", "#FFFFFFFF"), (steps[0].Size, steps[0].Fill, steps[0].TextColor));
        Assert.Equal(new Pt(50, 104), steps[0].Center);
    });

    [Fact]
    public void Text_is_white_on_a_dark_background() => Sta.Run(() =>
    {
        var output = TemplateService.Build(TemplateKind.SingleTitle, [Solid(100, 100)], ["x"], "#FF111111")!;
        Assert.All(output.Objects.OfType<TextAnnotation>(), t => Assert.Equal("#FFFFFFFF", t.Color));
        Assert.Equal(0xFF111111u, ImageIo.PixelAt(output.Base, 1, 1));
    });

    [Fact]
    public void Badges_are_white_on_a_dark_background() => Sta.Run(() =>
    {
        var output = TemplateService.Build(TemplateKind.StepByStep, [Solid(100, 100)], [null], "#FF3B82F6")!;
        var step = output.Objects.OfType<StepAnnotation>().Single();
        Assert.Equal(("#FFFFFFFF", "#FF111111"), (step.Fill, step.TextColor));
    });

    [Fact]
    public void Darkness_of_backgrounds() => Sta.Run(() =>
    {
        Assert.False(TemplateService.IsDark("#00000000"));
        Assert.True(TemplateService.IsDark("#FF3B82F6"));   // (0.2126·59 + 0.7152·130 + 0.0722·246) / 255 ≈ 0.48 < 0.5
        Assert.True(TemplateService.IsDark("#FF000000"));
        Assert.False(TemplateService.IsDark("#FFFFFFFF"));
    });

    [Fact]
    public void Grid_captions_come_from_titles() => Sta.Run(() =>
    {
        var output = TemplateService.Build(TemplateKind.GridCaptions, [Solid(10, 10), Solid(10, 10)], ["Notepad", null], "#FFFFFFFF")!;
        Assert.Equal(["Title", "Notepad", "Caption"], output.Objects.OfType<TextAnnotation>().Select(t => t.Text));
    });

    [Fact]
    public void Too_large_gives_null() => Sta.Run(() =>
        Assert.Null(TemplateService.Build(TemplateKind.SingleTitle, [Solid(32000, 1)], [null], "#FFFFFFFF")));
}
