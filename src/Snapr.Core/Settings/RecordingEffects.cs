namespace Snapr.Core.Settings;

/// <summary>Effects drawn on screen while recording (spec parity 21 §2). All off by default.</summary>
public sealed record RecordingEffects(bool HighlightCursor = false, bool ShowClicks = false, bool ShowShortcuts = false)
{
    public bool Any => HighlightCursor || ShowClicks || ShowShortcuts;
}
