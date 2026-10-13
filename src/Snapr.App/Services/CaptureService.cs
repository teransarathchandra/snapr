using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapr.App.Platform;
using Snapr.App.Rendering;
using Snapr.Core;
using Snapr.Core.Capture;

namespace Snapr.App.Services;

public sealed record ScreenGrab(BitmapSource Image, int OriginX, int OriginY);

public static class CaptureService
{
    public static ScreenGrab GrabVirtualScreen(bool includeCursor)
    {
        int x = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN), y = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
        int w = Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN), h = Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN);
        return new ScreenGrab(Grab(x, y, w, h, includeCursor), x, y);
    }

    /// <summary>GDI copy of a screen rectangle (screen pixels). Works for all monitors and DPI mixes because the process is PerMonitorV2-aware.</summary>
    public static BitmapSource Grab(int x, int y, int w, int h, bool includeCursor)
    {
        var screen = Native.GetDC(IntPtr.Zero);
        var memory = Native.CreateCompatibleDC(screen);
        var bitmap = Native.CreateCompatibleBitmap(screen, w, h);
        var old = Native.SelectObject(memory, bitmap);
        try
        {
            Native.BitBlt(memory, 0, 0, w, h, screen, x, y, Native.SRCCOPY | Native.CAPTUREBLT);
            if (includeCursor) DrawCursor(memory, x, y);
            Native.SelectObject(memory, old);
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            return ImageIo.ToBgra(source);   // copies the pixels, so the GDI bitmap can be freed
        }
        finally
        {
            Native.DeleteObject(bitmap);
            Native.DeleteDC(memory);
            Native.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    static void DrawCursor(IntPtr dc, int originX, int originY)
    {
        var info = new Native.CURSORINFO { cbSize = Marshal.SizeOf<Native.CURSORINFO>() };
        if (!Native.GetCursorInfo(ref info) || info.flags != Native.CURSOR_SHOWING) return;
        if (!Native.GetIconInfo(info.hCursor, out var icon)) return;
        try
        {
            Native.DrawIconEx(dc, info.ptScreenPos.X - icon.xHotspot - originX, info.ptScreenPos.Y - icon.yHotspot - originY,
                info.hCursor, 0, 0, 0, IntPtr.Zero, Native.DI_NORMAL);
        }
        finally
        {
            if (icon.hbmMask != IntPtr.Zero) Native.DeleteObject(icon.hbmMask);
            if (icon.hbmColor != IntPtr.Zero) Native.DeleteObject(icon.hbmColor);
        }
    }

    /// <summary>Visible, uncloaked top-level windows in z-order (topmost first), with DWM frame bounds (no drop shadow).</summary>
    public static IReadOnlyList<WindowInfo> VisibleWindows(int originX, int originY)
    {
        var list = new List<WindowInfo>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return true;
            if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            if ((Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64() & Native.WS_EX_TOOLWINDOW) != 0) return true;
            if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out Native.RECT r, Marshal.SizeOf<Native.RECT>()) != 0) return true;
            if (r.Right - r.Left < 8 || r.Bottom - r.Top < 8) return true;
            var title = new StringBuilder(Native.GetWindowTextLength(hwnd) + 1);
            Native.GetWindowText(hwnd, title, title.Capacity);
            list.Add(new WindowInfo(hwnd, new Rct(r.Left - originX, r.Top - originY, r.Right - r.Left, r.Bottom - r.Top), title.ToString()));
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
