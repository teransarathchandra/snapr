using System.Windows;
using System.Windows.Interop;

namespace Snapr.App.Platform;

/// <summary>Windows' dark title bar (spec parity 16 §2.1, parity 20 §4.10): set before the first paint so it never flashes light.</summary>
public static class DarkTitleBar
{
    public static void Attach(Window w) => w.SourceInitialized += (_, _) => Apply(new WindowInteropHelper(w).Handle);

    public static void Apply(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        var on = 1;
        _ = Native.DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));
        _ = Native.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0037);   // NOSIZE|NOMOVE|NOZORDER|NOACTIVATE|FRAMECHANGED: repaint the caption now
    }
}
