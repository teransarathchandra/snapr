using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Threading;
using Snapr.Core.Recording;

namespace Snapr.App.Platform;

public sealed class InputHooksException(string message) : Exception(message);

/// <summary>Global low-level mouse/keyboard hooks for the recording effects (spec parity 21 §4). Callbacks only copy the event and post it to the UI thread; keys are never logged.</summary>
public sealed class InputHooks : IDisposable
{
    readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    readonly Native.HookProc? _mouseProc, _keyProc; // kept in fields so the GC can't collect them while hooked
    IntPtr _mouseHook, _keyHook;
    bool _disposed;
    int _movePending, _mx, _my;

    public event Action<int, int>? Moved;
    public event Action<int, int, MouseButton>? Clicked;
    public event Action<int, KeyMods>? KeyDown;

    InputHooks(bool mouse, bool keyboard)
    {
        var module = Native.GetModuleHandle(null);
        try
        {
            if (mouse)
            {
                _mouseProc = MouseProc;
                _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseProc, module, 0);
                if (_mouseHook == IntPtr.Zero) throw new InputHooksException($"Mouse hook failed (error {Marshal.GetLastWin32Error()}).");
            }
            if (keyboard)
            {
                _keyProc = KeyProc;
                _keyHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _keyProc, module, 0);
                if (_keyHook == IntPtr.Zero) throw new InputHooksException($"Keyboard hook failed (error {Marshal.GetLastWin32Error()}).");
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Call on the UI thread; events are raised on it.</summary>
    public static InputHooks Start(bool mouse, bool keyboard) => new(mouse, keyboard);

    IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                var msg = (int)wParam;
                if (msg is Native.WM_MOUSEMOVE or Native.WM_LBUTTONDOWN or Native.WM_RBUTTONDOWN or Native.WM_MBUTTONDOWN)
                {
                    var pt = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam).pt;
                    if (msg == Native.WM_MOUSEMOVE)
                    {
                        _mx = pt.X;
                        _my = pt.Y;
                        if (Interlocked.Exchange(ref _movePending, 1) == 0)
                            _dispatcher.BeginInvoke(() =>
                            {
                                Interlocked.Exchange(ref _movePending, 0);
                                if (!_disposed) Moved?.Invoke(_mx, _my);
                            });
                    }
                    else
                    {
                        var button = msg == Native.WM_LBUTTONDOWN ? MouseButton.Left : msg == Native.WM_RBUTTONDOWN ? MouseButton.Right : MouseButton.Middle;
                        _dispatcher.BeginInvoke(() => { if (!_disposed) Clicked?.Invoke(pt.X, pt.Y, button); });
                    }
                }
            }
        }
        catch { }
        return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    IntPtr KeyProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && (int)wParam is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN)
            {
                var vk = Marshal.ReadInt32(lParam); // KBDLLHOOKSTRUCT.vkCode
                var mods = ReadMods(); // here, at key-down time: the snapshot must not go stale while the UI thread queue drains
                _dispatcher.BeginInvoke(() => { if (!_disposed) KeyDown?.Invoke(vk, mods); });
            }
        }
        catch { }
        return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    // GetAsyncKeyState, not GetKeyState: GetKeyState is per-thread and only sees input our own UI thread received, which is stale while another app has focus.
    static bool Down(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;

    static KeyMods ReadMods()
    {
        var m = KeyMods.None;
        if (Down(0x11)) m |= KeyMods.Ctrl;
        if (Down(0x12)) m |= KeyMods.Alt;
        if (Down(0x10)) m |= KeyMods.Shift;
        if (Down(0x5B) || Down(0x5C)) m |= KeyMods.Win;
        if (Down(0xA5)) m |= KeyMods.AltGr;
        return m;
    }

    public void Dispose()
    {
        _disposed = true;
        if (_mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_mouseHook);
        if (_keyHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_keyHook);
        _mouseHook = _keyHook = IntPtr.Zero;
    }
}
