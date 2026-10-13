using Snapr.App.Capture;
using Snapr.App.Rendering;
using Snapr.App.Services;
using Snapr.Core;
using Snapr.Core.Capture;

namespace Snapr.App;

public sealed partial class AppController
{
    public async Task ScrollingCaptureAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var grab = CaptureService.GrabVirtualScreen(includeCursor: false);
            var windows = CaptureService.VisibleWindows(grab.OriginX, grab.OriginY);
            if (await OverlaySession.RunAsync(grab, windows, OverlayMode.ScrollingWindow) is not { } result) return;
            var target = new WindowInfo(result.Selection.Window, result.Selection.Bounds, result.Selection.Title ?? "");
            if (await ScrollCaptureService.CaptureAsync(target, grab.OriginX, grab.OriginY) is not { } stitched)
            {
                Notify("Nothing captured", "Snapr couldn't capture that window.");
                return;
            }
            OpenNewCapture(ImageIo.FromFrame(stitched.Image), CaptureKind.Scrolling, target.Title);
            if (!stitched.Complete)
                Notify("Scrolling stopped early", "Snapr couldn't match the next part of the page, or it got too long, so it kept what it captured.");
        }
        finally
        {
            _busy = false;
        }
    }
}
