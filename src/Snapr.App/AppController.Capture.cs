using System.Windows;
using System.Windows.Media.Imaging;
using Snapr.App.Capture;
using Snapr.App.Dialogs;
using Snapr.App.Services;
using Snapr.Core;
using Snapr.Core.Capture;

namespace Snapr.App;

public sealed partial class AppController
{
    /// <summary>True while a capture or recording setup is in progress; a second hotkey press is ignored.</summary>
    bool _busy;

    public async Task CaptureImageAsync(OverlayAction action = OverlayAction.Edit)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (_settings.CaptureDelaySeconds > 0) await CountdownWindow.RunAsync(_settings.CaptureDelaySeconds);
            var grab = CaptureService.GrabVirtualScreen(_settings.IncludeCursor);
            var windows = CaptureService.VisibleWindows(grab.OriginX, grab.OriginY);
            if (await OverlaySession.RunAsync(grab, windows, OverlayMode.Image, action) is not { } result) return;

            var r = result.Selection.Bounds;
            var image = new CroppedBitmap(grab.Image, new Int32Rect((int)r.X, (int)r.Y, (int)r.W, (int)r.H));
            image.Freeze();
            switch (result.Action)
            {
                case OverlayAction.Copy:
                    if (ClipboardImage.Set(image)) Notify("Copied", $"{(int)r.W} × {(int)r.H} is on the clipboard.");
                    else Notify("Not copied", "Another app is holding the clipboard. Try again.");
                    break;
                case OverlayAction.GrabText:
                    await GrabTextAsync(image);
                    break;
                default:
                    var kind = result.Selection.Kind switch
                    {
                        SelectionKind.Window => CaptureKind.Window,
                        SelectionKind.Fullscreen => CaptureKind.Fullscreen,
                        _ => CaptureKind.Region,
                    };
                    OpenNewCapture(image, kind, result.Selection.Title);
                    break;
            }
        }
        finally
        {
            _busy = false;
        }
    }
}
