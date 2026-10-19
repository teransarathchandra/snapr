namespace Snapr.Core;

/// <summary>A Selection-tool copy: where it came from and the clipboard sequence number right after it.</summary>
public sealed record PixelCopy(Rct Area, uint Sequence);

/// <summary>Paste position (spec parity 11 §4): a picture pasted straight after a selection copy goes back where it came from.</summary>
public static class PastePlacement
{
    /// <summary>The copied area when the clipboard hasn't changed since the copy and the image is the same size; otherwise null. Sequence 0 means no clipboard access and never matches.</summary>
    public static Rct? AtCopy(uint sequenceNow, PixelCopy? last, int imageW, int imageH) =>
        last is { Sequence: not 0 } c && c.Sequence == sequenceNow && (int)c.Area.W == imageW && (int)c.Area.H == imageH ? c.Area : null;
}
