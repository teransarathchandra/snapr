using System.Windows.Input;
using Snapr.App.Platform;

namespace Snapr.App.Services;

/// <summary>Text form of a global hotkey, e.g. "Ctrl+Shift+S" or "PrintScreen".</summary>
public static class HotkeyGesture
{
    public static bool TryParse(string text, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        Key? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= Native.MOD_CONTROL; break;
                case "alt": modifiers |= Native.MOD_ALT; break;
                case "shift": modifiers |= Native.MOD_SHIFT; break;
                case "win": modifiers |= Native.MOD_WIN; break;
                default:
                    if (key is not null || !Enum.TryParse<Key>(part, ignoreCase: true, out var k) || k == Key.None || int.TryParse(part, out _))
                        return false;
                    key = k;
                    break;
            }
        }
        if (key is null) return false;
        vk = (uint)KeyInterop.VirtualKeyFromKey(key.Value);
        return vk != 0;
    }

    public static string? Format(ModifierKeys modifiers, Key key)
    {
        if (key is Key.None or Key.System or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return null;
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key == Key.Snapshot ? "PrintScreen" : key.ToString());
        return string.Join("+", parts);
    }
}
