using System.Diagnostics;
using Snapr.App.Platform;
using Snapr.App.Rendering;
using Snapr.Core.Capture;
using Snapr.Core.Stitching;

namespace Snapr.App.Services;

public static class ScrollCaptureService
{
    const int MaxFrames = 30;
    static readonly TimeSpan MaxTime = TimeSpan.FromSeconds(15);

    /// <summary>Scrolls the window down three notches at a time, grabbing it after each step, until the content stops
    /// changing, 30 frames, 15 seconds, or Esc. Returns null if nothing was captured.</summary>
    public static async Task<StitchResult?> CaptureAsync(WindowInfo window, int originX, int originY)
    {
        var b = window.Bounds;
        int x = (int)b.X + originX, y = (int)b.Y + originY, w = (int)b.W, h = (int)b.H;
        Native.SetForegroundWindow(window.Handle);
        Native.SetCursorPos(x + w / 2, y + h / 2);   // wheel input goes to the window under the pointer
        await Task.Delay(300);

        var frames = new List<Frame>();
        var clock = Stopwatch.StartNew();
        while (frames.Count < MaxFrames && clock.Elapsed < MaxTime)
        {
            if ((Native.GetAsyncKeyState(Native.VK_ESCAPE) & 0x8000) != 0) break;
            var frame = ImageIo.ToFrame(CaptureService.Grab(x, y, w, h, includeCursor: false));
            if (frames.Count > 0 && frame.Pixels.AsSpan().SequenceEqual(frames[^1].Pixels)) break;   // reached the bottom
            frames.Add(frame);
            Native.ScrollWheel(-360);
            await Task.Delay(350);   // let smooth scrolling settle before the next grab
        }
        return frames.Count == 0 ? null : Stitcher.Stitch(frames);
    }
}
