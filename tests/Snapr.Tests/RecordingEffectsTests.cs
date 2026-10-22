using Snapr.Core.Recording;

namespace Snapr.Tests;

public class RecordingEffectsTests
{
    [Theory]
    [InlineData(KeyMods.Ctrl, 0x53, "Ctrl + S")]
    [InlineData(KeyMods.Ctrl | KeyMods.Shift, 0x53, "Ctrl + Shift + S")]
    [InlineData(KeyMods.Alt, 0x09, "Alt + Tab")]
    [InlineData(KeyMods.Win, 0x45, "Win + E")]
    [InlineData(KeyMods.None, 0x74, "F5")]
    [InlineData(KeyMods.Shift, 0x09, "Shift + Tab")]
    [InlineData(KeyMods.None, 0x0D, "Enter")]
    [InlineData(KeyMods.Ctrl, 0xBA, "Ctrl + ;")]
    [InlineData(KeyMods.None, 0x25, "←")]
    [InlineData(KeyMods.Shift | KeyMods.Win | KeyMods.Ctrl | KeyMods.Alt, 0x31, "Ctrl + Alt + Shift + Win + 1")]
    public void Shortcuts_are_labelled(KeyMods mods, int vk, string label) => Assert.Equal(label, KeyBadge.Format(mods, vk));

    [Theory]
    [InlineData(KeyMods.None, 0x41)]   // a
    [InlineData(KeyMods.Shift, 0x41)]  // A
    [InlineData(KeyMods.None, 0x35)]   // 5
    [InlineData(KeyMods.Shift, 0x31)]  // !
    [InlineData(KeyMods.None, 0x20)]   // space
    [InlineData(KeyMods.None, 0xBA)]   // ;
    [InlineData(KeyMods.Ctrl, 0x11)]   // Ctrl itself
    [InlineData(KeyMods.Ctrl, 0xFF)]   // unknown key
    [InlineData(KeyMods.Ctrl | KeyMods.Alt | KeyMods.AltGr, 0x51)]   // AltGr+Q types @ on German layouts
    public void Typing_is_never_labelled(KeyMods mods, int vk) => Assert.Null(KeyBadge.Format(mods, vk));

    [Fact]
    public void Repeats_count_within_the_hold()
    {
        var a = KeyBadge.Merge(null, "Ctrl + S", 10);
        Assert.Equal(("Ctrl + S", 1, 10.0), a);
        var b = KeyBadge.Merge(a, "Ctrl + S", 11);
        Assert.Equal(("Ctrl + S", 2, 11.0), b);
        Assert.Equal("Ctrl + S  ×2", KeyBadge.Text(b));
        Assert.Equal(("Ctrl + S", 1, 12.5), KeyBadge.Merge(b, "Ctrl + S", 12.5));
        Assert.Equal(("Ctrl + Z", 1, 11.5), KeyBadge.Merge(b, "Ctrl + Z", 11.5));
        Assert.Equal("Ctrl + S", KeyBadge.Text(a));
    }

    [Fact]
    public void Ripple_grows_and_fades()
    {
        Assert.Null(ClickRipple.At(-0.01));
        Assert.Null(ClickRipple.At(0.41));
        Assert.Equal((12.0, 0.9), ClickRipple.At(0)!.Value);
        var mid = ClickRipple.At(0.2)!.Value;
        Assert.Equal(12 + 32 * 0.75, mid.Diameter, 6);
        Assert.Equal(0.9 * 0.25, mid.Opacity, 6);
        var end = ClickRipple.At(0.4)!.Value;
        Assert.Equal(44, end.Diameter, 6);
        Assert.Equal(0, end.Opacity, 6);
    }
}
