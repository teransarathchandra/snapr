using System.Windows.Input;
using Snapr.App.Services;

namespace Snapr.Tests;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("PrintScreen", 0u, 0x2Cu)]
    [InlineData("Shift+PrintScreen", 4u, 0x2Cu)]
    [InlineData("ctrl + alt + S", 3u, 0x53u)]
    [InlineData("Win+Shift+F9", 12u, 0x78u)]
    public void Parses_valid_gestures(string text, uint mods, uint vk)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var m, out var v));
        Assert.Equal((mods, vk), (m, v));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl+NotAKey")]
    [InlineData("A+B")]
    public void Rejects_invalid_gestures(string text) => Assert.False(HotkeyGesture.TryParse(text, out _, out _));

    [Fact]
    public void Formats_keys_and_modifiers()
    {
        Assert.Equal("Ctrl+Shift+S", HotkeyGesture.Format(ModifierKeys.Control | ModifierKeys.Shift, Key.S));
        Assert.Equal("PrintScreen", HotkeyGesture.Format(ModifierKeys.None, Key.Snapshot));
        Assert.Null(HotkeyGesture.Format(ModifierKeys.Shift, Key.LeftShift));
    }

    [Fact]
    public void Formatted_gestures_parse_back()
    {
        var text = HotkeyGesture.Format(ModifierKeys.Alt | ModifierKeys.Windows, Key.F12)!;
        Assert.True(HotkeyGesture.TryParse(text, out var m, out var v));
        Assert.Equal((9u, 0x7Bu), (m, v));
    }
}
