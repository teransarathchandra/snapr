using System.Diagnostics;
using Snapr.App.Platform;
using Snapr.App.Rendering;
using Snapr.Core;
using Snapr.Core.Capture;
using Snapr.Core.Stitching;
using Forms = System.Windows.Forms;

namespace Snapr.App.Services;

public static class ScrollCaptureService
{
    // About 85 steps of 3 notches reach the stitcher's 32000 px limit on a 125% screen; the caps leave room for that.
    const int MaxFrames = 100;
    static readonly TimeSpan MaxTime = TimeSpan.FromSeconds(60);

    /// <summary>Scrolls the window down three notches at a time, grabbing it after each step, until the content stops
    /// changing, 100 frames, 60 seconds, or Esc. Returns null if nothing was captured; the result is marked incomplete
    /// if a cap or Esc stopped it before the end of the content.</summary>
    public static async Task<StitchResult?> CaptureAsync(WindowInfo window, int originX, int originY)
    {
        // Only the part of the window in its monitor's work area: the taskbar is always on top, so where the window
        // reaches under it the frames would show the taskbar, which does not scroll, and no frames would match.
        var work = Forms.Screen.FromHandle(window.Handle).WorkingArea;
        var b = window.Bounds.Offset(originX, originY).Intersect(new Rct(work.X, work.Y, work.Width, work.Height));
        if (b.IsEmpty) return null;
        int x = (int)b.X, y = (int)b.Y, w = (int)b.W, h = (int)b.H;
        Native.SetForegroundWindow(window.Handle);
        Native.SetCursorPos(x + w / 2, y + h / 2);   // wheel input goes to the window under the pointer
        await Task.Delay(300);

        var frames = new List<Frame>();
        var clock = Stopwatch.StartNew();
        var reachedEnd = false;
        while (frames.Count < MaxFrames && clock.Elapsed < MaxTime)
        {
            if ((Native.GetAsyncKeyState(Native.VK_ESCAPE) & 0x8000) != 0) break;
            var frame = ImageIo.ToFrame(CaptureService.Grab(x, y, w, h, includeCursor: false));
            if (frames.Count > 0 && frame.Pixels.AsSpan().SequenceEqual(frames[^1].Pixels)) { reachedEnd = true; break; }
            frames.Add(frame);
            Native.ScrollWheel(-360);
            await Task.Delay(350);   // let smooth scrolling settle before the next grab
        }
        if (frames.Count == 0) return null;
        var result = Stitcher.Stitch(frames);
        return result with { Complete = result.Complete && reachedEnd };
    }
}
