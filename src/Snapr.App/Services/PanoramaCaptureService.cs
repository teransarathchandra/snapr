using Snapr.App.Dialogs;
using Snapr.App.Platform;
using Snapr.App.Rendering;
using Snapr.Core;
using Snapr.Core.Stitching;

namespace Snapr.App.Services;

/// <summary>Grabs the area every 150 ms and feeds the stitcher until Stop, Cancel, Esc or a cap (spec parity 12 §3).</summary>
public static class PanoramaCaptureService
{
    static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(150);

    /// <summary>True to keep the result (Stop, or a cap reached); false for Cancel, Esc or closing the bar.</summary>
    public static async Task<bool> RunAsync(Rct area, PanoramaStitcher stitcher, PanoramaBar bar)
    {
        int x = (int)area.X, y = (int)area.Y, w = (int)area.W, h = (int)area.H;
        await Task.Delay(300);   // let the overlay leave the screen before the first grab
        while (!bar.Finished)
        {
            if ((Native.GetAsyncKeyState(Native.VK_ESCAPE) & 0x8000) != 0) return false;
            var frame = ImageIo.ToFrame(CaptureService.Grab(x, y, w, h, includeCursor: false));
            var added = await Task.Run(() => stitcher.Add(frame));   // the next grab waits for this one (spec §2)
            if (bar.Finished && !bar.Stopped) return false;   // Cancel during stitching discards, even on a cap
            bar.SetCount(stitcher.Frames, stitcher.Width, stitcher.Height);
            if (added == PanoramaAdd.Full) return true;
            await Task.Delay(Interval);
        }
        return bar.Stopped;
    }
}
