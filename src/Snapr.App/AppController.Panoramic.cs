using Snapr.App.Capture;
using Snapr.App.Dialogs;
using Snapr.App.Rendering;
using Snapr.App.Services;
using Snapr.Core;
using Snapr.Core.Stitching;

namespace Snapr.App;

public sealed partial class AppController
{
    /// <summary>Panoramic capture (spec parity 12): pick an area, the user moves the content, Snapr stitches.</summary>
    public async Task PanoramicCaptureAsync()
    {
        if (_busy) return;
        _busy = true;
        PanoramaBar? bar = null;
        try
        {
            var grab = CaptureService.GrabVirtualScreen(includeCursor: false);
            var windows = CaptureService.VisibleWindows(grab.OriginX, grab.OriginY);
            if (await OverlaySession.RunAsync(grab, windows, OverlayMode.Image, OverlayAction.Edit, "Choose the area to pan across") is not { } result) return;
            var r = result.Selection.Bounds;
            var area = new Rct(r.X + grab.OriginX, r.Y + grab.OriginY, r.W, r.H);
            if (area.W < 1 || area.H < 1) return;
            var stitcher = new PanoramaStitcher();
            bar = new PanoramaBar(area);
            bar.Show();
            var keep = await PanoramaCaptureService.RunAsync(area, stitcher, bar);
            bar.Close();
            bar = null;
            if (!keep || stitcher.Frames == 0) return;
            OpenNewCapture(ImageIo.FromFrame(stitcher.Compose()), CaptureKind.Panoramic, "Panoramic capture",
                notice: stitcher.LostParts ? "Some movement was too fast to follow, so parts may be missing. Move more slowly next time." : null);
        }
        finally
        {
            bar?.Close();
            _busy = false;
        }
    }
}
