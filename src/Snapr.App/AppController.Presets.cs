using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Snapr.App.Capture;
using Snapr.App.Dialogs;
using Snapr.App.Rendering;
using Snapr.App.Services;
using Snapr.Core;
using Snapr.Core.Capture;
using Snapr.Core.Settings;
using Forms = System.Windows.Forms;

namespace Snapr.App;

/// <summary>Capture presets (spec parity 6 §3.2): the normal capture path with the preset's own delay, pointer, target and action.</summary>
public sealed partial class AppController
{
    public async Task RunPresetAsync(CapturePreset p)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (p.DelaySeconds > 0) await CountdownWindow.RunAsync(p.DelaySeconds);
            Func<BitmapSource, SelectionKind, string?, Task> deliver = (image, kind, title) => DeliverPresetAsync(p, image, kind, title);

            if (p.Target == PresetTarget.Overlay)
            {
                var grab = CaptureService.GrabVirtualScreen(p.IncludeCursor);
                var windows = CaptureService.VisibleWindows(grab.OriginX, grab.OriginY);
                if (await OverlaySession.RunAsync(grab, windows, OverlayMode.Image, OverlayAction.Edit, p.Name) is not { } result) return;
                var r = result.Selection.Bounds;
                var cropped = new CroppedBitmap(grab.Image, new Int32Rect((int)r.X, (int)r.Y, (int)r.W, (int)r.H));
                cropped.Freeze();
                _last = new LastCapture(result.Selection.Kind, new Rct(r.X + grab.OriginX, r.Y + grab.OriginY, r.W, r.H), result.Selection.Window);
                _lastDeliver = deliver;
                await deliver(cropped, result.Selection.Kind, result.Selection.Title);
                return;
            }

            var v = Forms.SystemInformation.VirtualScreen;
            var desktop = new Rct(v.X, v.Y, v.Width, v.Height);
            var (area, selection) = p.Target switch
            {
                PresetTarget.ScreenUnderMouse => (ToRct(Forms.Screen.FromPoint(Forms.Cursor.Position).Bounds), SelectionKind.Fullscreen),
                PresetTarget.AllScreens => (desktop, SelectionKind.Fullscreen),
                _ => (p.FixedArea ?? default, SelectionKind.Region),
            };
            var target = new LastCapture(selection, area, 0);
            if (RepeatTarget.Resolve(target, [], desktop) is not { } grabArea)
            {
                Notify($"Can't run {p.Name}", "Its area is not on any screen.");
                return;
            }
            var shot = CaptureService.Grab((int)grabArea.X, (int)grabArea.Y, (int)grabArea.W, (int)grabArea.H, p.IncludeCursor);
            _last = target with { ScreenBounds = grabArea };
            _lastDeliver = deliver;
            await deliver(shot, selection, null);
        }
        finally
        {
            _busy = false;
        }
    }

    static Rct ToRct(System.Drawing.Rectangle r) => new(r.X, r.Y, r.Width, r.Height);

    Task DeliverPresetAsync(CapturePreset p, BitmapSource image, SelectionKind kind, string? title) => p.Action switch
    {
        PresetAction.SaveToFolder => SaveToFolder(p, image),
        PresetAction.Copy => DeliverAsync(image, OverlayAction.Copy, kind, title),
        PresetAction.GrabText => DeliverAsync(image, OverlayAction.GrabText, kind, title),
        _ => DeliverAsync(image, OverlayAction.Edit, kind, title),
    };

    Task SaveToFolder(CapturePreset p, BitmapSource image)
    {
        try
        {
            var folder = p.SaveFolder ?? CapturePaths.PicturesRoot;
            Directory.CreateDirectory(folder);
            var ext = p.SaveFormat == "jpg" ? "jpg" : "png";
            var name = PresetRules.FileName(p.Name, DateTime.Now, ext, n => File.Exists(Path.Combine(folder, n)));
            File.WriteAllBytes(Path.Combine(folder, name), ext == "jpg" ? ImageIo.EncodeJpg(image, _settings.JpgQuality) : ImageIo.EncodePng(image));
            Notify("Saved", name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log.Write("Preset save failed", ex);
            Notify("Couldn't save", ex.Message);
        }
        return Task.CompletedTask;
    }

    /// <summary>Lets the user drag the area for a Fixed-area preset (absolute screen pixels), or null if cancelled or busy.</summary>
    public async Task<Rct?> PickAreaAsync()
    {
        if (_busy) return null;
        _busy = true;
        try
        {
            await Task.Delay(250);   // let the Settings windows the caller hid leave the screen before the grab
            var grab = CaptureService.GrabVirtualScreen(false);
            var windows = CaptureService.VisibleWindows(grab.OriginX, grab.OriginY);
            if (await OverlaySession.RunAsync(grab, windows, OverlayMode.Image, OverlayAction.Edit, "Choose the preset's area") is not { } result) return null;
            var r = result.Selection.Bounds;
            return new Rct(r.X + grab.OriginX, r.Y + grab.OriginY, r.W, r.H);
        }
        finally
        {
            _busy = false;
        }
    }
}
