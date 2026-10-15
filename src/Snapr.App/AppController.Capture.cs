using System.Windows;
using System.Windows.Media.Imaging;
using Snapr.App.Capture;
using Snapr.App.Dialogs;
using Snapr.App.Services;
using Snapr.Core;
using Snapr.Core.Capture;
using Forms = System.Windows.Forms;

namespace Snapr.App;

public sealed partial class AppController
{
    /// <summary>True while a capture or recording setup is in progress; a second hotkey press is ignored.</summary>
    bool _busy;

    /// <summary>The last image capture this session, for the repeat hotkey (memory only).</summary>
    LastCapture? _last;
    /// <summary>How the last capture was delivered, so Repeat does the same (a preset's save-to-folder included).</summary>
    Func<BitmapSource, SelectionKind, string?, Task>? _lastDeliver;

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
            _last = new LastCapture(result.Selection.Kind, new Rct(r.X + grab.OriginX, r.Y + grab.OriginY, r.W, r.H), result.Selection.Window);
            var chosen = result.Action;
            _lastDeliver = (img, kind, title) => DeliverAsync(img, chosen, kind, title);
            await DeliverAsync(image, result.Action, result.Selection.Kind, result.Selection.Title);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Captures the last target again, without the overlay, with the same action.</summary>
    public async Task RepeatLastCaptureAsync()
    {
        if (_last is not { } last)
        {
            Notify("Nothing to repeat yet", "Take a capture first; this hotkey repeats it.");
            return;
        }
        if (_busy) return;
        _busy = true;
        try
        {
            if (_settings.CaptureDelaySeconds > 0) await CountdownWindow.RunAsync(_settings.CaptureDelaySeconds);
            var v = Forms.SystemInformation.VirtualScreen;
            var windows = CaptureService.VisibleWindows(0, 0);   // origin 0,0 = absolute screen pixels
            if (RepeatTarget.Resolve(last, windows, new Rct(v.X, v.Y, v.Width, v.Height)) is not { } r)
            {
                Notify("Can't repeat", "The last capture area is no longer on any screen.");
                return;
            }
            var image = CaptureService.Grab((int)r.X, (int)r.Y, (int)r.W, (int)r.H, _settings.IncludeCursor);
            var title = windows.FirstOrDefault(w => w.Handle == last.Window && last.Kind == SelectionKind.Window).Title;
            await (_lastDeliver ?? ((img, kind, t) => DeliverAsync(img, OverlayAction.Edit, kind, t)))(image, last.Kind, title);
        }
        finally
        {
            _busy = false;
        }
    }

    async Task DeliverAsync(BitmapSource image, OverlayAction action, SelectionKind selection, string? title)
    {
        switch (action)
        {
            case OverlayAction.Copy:
                if (ClipboardImage.Set(image)) Notify("Copied", $"{image.PixelWidth} × {image.PixelHeight} is on the clipboard.");
                else Notify("Not copied", "Another app is holding the clipboard. Try again.");
                break;
            case OverlayAction.GrabText:
                await GrabTextAsync(image);
                break;
            default:
                var kind = selection switch
                {
                    SelectionKind.Window => CaptureKind.Window,
                    SelectionKind.Fullscreen => CaptureKind.Fullscreen,
                    _ => CaptureKind.Region,
                };
                OpenNewCapture(image, kind, title);
                break;
        }
    }
}
