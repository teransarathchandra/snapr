namespace Snapr.Core.Recording;

[Flags]
public enum KeyMods { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Win = 8, AltGr = 16 }

/// <summary>The on-screen badge for keyboard shortcuts while recording (spec parity 21 §3). Plain typing gives null.</summary>
public static class KeyBadge
{
    public const double Hold = 1.2;

    static readonly Dictionary<int, string> Special = new()
    {
        [0x0D] = "Enter", [0x1B] = "Esc", [0x09] = "Tab", [0x08] = "Backspace", [0x2E] = "Delete", [0x2D] = "Insert",
        [0x24] = "Home", [0x23] = "End", [0x21] = "Page Up", [0x22] = "Page Down",
        [0x25] = "←", [0x26] = "↑", [0x27] = "→", [0x28] = "↓", [0x2C] = "Print Screen",
    };

    static readonly Dictionary<int, string> Oem = new()
    {
        [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/", [0xC0] = "`",
        [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'", [0xDF] = "`", [0xE2] = "\\",
    };

    static bool IsSpecial(int vk) => Special.ContainsKey(vk) || vk is >= 0x70 and <= 0x87;

    static string? Name(int vk) => vk switch
    {
        >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
        0x20 => "Space",
        _ => Special.TryGetValue(vk, out var s) ? s : Oem.GetValueOrDefault(vk),
    };

    public static string? Format(KeyMods mods, int vk)
    {
        if (Name(vk) is not { } name) return null;
        if (mods.HasFlag(KeyMods.AltGr) && !IsSpecial(vk)) return null;   // AltGr types characters such as @ or €
        var shortcut = (mods & (KeyMods.Ctrl | KeyMods.Alt | KeyMods.Win)) != 0;
        if (!shortcut && !IsSpecial(vk)) return null;
        var parts = new List<string>();
        if (mods.HasFlag(KeyMods.Ctrl)) parts.Add("Ctrl");
        if (mods.HasFlag(KeyMods.Alt)) parts.Add("Alt");
        if (mods.HasFlag(KeyMods.Shift)) parts.Add("Shift");
        if (mods.HasFlag(KeyMods.Win)) parts.Add("Win");
        parts.Add(name);
        return string.Join(" + ", parts);
    }

    public static (string Label, int Count, double ShownAt) Merge((string Label, int Count, double ShownAt)? current, string label, double now, double hold = Hold) =>
        current is { } c && c.Label == label && now - c.ShownAt <= hold ? (label, c.Count + 1, now) : (label, 1, now);

    public static string Text((string Label, int Count, double ShownAt) badge) => badge.Count > 1 ? $"{badge.Label}  ×{badge.Count}" : badge.Label;
}

/// <summary>A click ring: grows from 12 to 44 px and fades over 0.4 s, ease-out (spec parity 21 §3).</summary>
public static class ClickRipple
{
    public const double Duration = 0.4;

    public static (double Diameter, double Opacity)? At(double age)
    {
        if (age < 0 || age > Duration) return null;
        var t = age / Duration;
        var e = 1 - (1 - t) * (1 - t);
        return (12 + (44 - 12) * e, 0.9 * (1 - e));
    }
}
