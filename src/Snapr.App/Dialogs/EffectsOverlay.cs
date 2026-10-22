using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Snapr.App.Platform;
using Snapr.Core;
using Snapr.Core.Recording;
using Snapr.Core.Settings;

namespace Snapr.App.Dialogs;

/// <summary>Click-through transparent window over the recorded area that draws the cursor ring, click ripples and shortcut badge. Not excluded from capture, so it is recorded.</summary>
public sealed class EffectsOverlay : Window
{
    readonly Rct _area; // physical screen pixels
    readonly RecordingEffects _effects;
    readonly EffectsLayer _layer;

    public EffectsOverlay(Rct screenArea, RecordingEffects effects)
    {
        DarkTitleBar.Attach(this);
        _area = screenArea;
        _effects = effects;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Content = _layer = new EffectsLayer(screenArea, effects);
        Loaded += (_, _) => Reshape();
        // Deferred so WPF's suggested DPI size can't overwrite the physical size.
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => { Reshape(); Move(); });
        Closed += (_, _) => _layer.Stop();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        const long add = Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW;
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, (IntPtr)(Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64() | add));
        Move(); // onto the target monitor first, so WPF measures with that monitor's DPI
    }

    void Reshape()
    {
        var s = VisualTreeHelper.GetDpi(this);
        Width = _area.W / s.DpiScaleX;
        Height = _area.H / s.DpiScaleY;
    }

    void Move()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, (int)_area.X, (int)_area.Y, (int)_area.W, (int)_area.H, Native.SWP_NOACTIVATE);
    }

    /// <summary>Subscribes only to the events the chosen effects need; unsubscribes when the window closes.</summary>
    public void Attach(InputHooks hooks)
    {
        Action<int, int>? moved = _effects.HighlightCursor ? _layer.OnMoved : null;
        Action<int, int, MouseButton>? clicked = _effects.ShowClicks ? _layer.OnClicked : null;
        Action<int, KeyMods>? key = _effects.ShowShortcuts ? _layer.OnKey : null;
        if (moved is not null)
        {
            hooks.Moved += moved;
            if (Native.GetCursorPos(out var p)) _layer.OnMoved(p.X, p.Y); // so the ring shows before the first mouse move
        }
        if (clicked is not null) hooks.Clicked += clicked;
        if (key is not null) hooks.KeyDown += key;
        Closed += (_, _) =>
        {
            if (moved is not null) hooks.Moved -= moved;
            if (clicked is not null) hooks.Clicked -= clicked;
            if (key is not null) hooks.KeyDown -= key;
        };
    }

    sealed class EffectsLayer(Rct area, RecordingEffects effects) : FrameworkElement
    {
        const int MaxRipples = 20;
        const double RingSize = 48, FadeOut = 0.3;
        static readonly Brush RingBrush = Frozen("#66FFD60A"), BadgeBrush = Frozen("#E6111113");
        static readonly Color Left = Hex("#FF3B82F6"), Right = Hex("#FFF97316"), Middle = Hex("#FF9CA3AF");

        readonly Stopwatch _clock = Stopwatch.StartNew();
        readonly List<(int X, int Y, MouseButton Button, double Start)> _ripples = [];
        (string Label, int Count, double ShownAt)? _badge;
        int _cx = int.MinValue, _cy;
        bool _moved, _subscribed;

        static Color Hex(string s) => (Color)ColorConverter.ConvertFromString(s);
        static Brush Frozen(string s) { var b = new SolidColorBrush(Hex(s)); b.Freeze(); return b; }
        double Now => _clock.Elapsed.TotalSeconds;

        public void OnMoved(int x, int y)
        {
            _cx = x;
            _cy = y;
            _moved = true;
            Subscribe();
        }

        public void OnClicked(int x, int y, MouseButton button)
        {
            _ripples.Add((x, y, button, Now));
            if (_ripples.Count > MaxRipples) _ripples.RemoveAt(0);
            Subscribe();
        }

        public void OnKey(int vk, KeyMods mods)
        {
            if (KeyBadge.Format(mods, vk) is not { } label) return;
            _badge = KeyBadge.Merge(_badge, label, Now);
            Subscribe();
        }

        public void Stop()
        {
            if (!_subscribed) return;
            CompositionTarget.Rendering -= Frame;
            _subscribed = false;
        }

        void Subscribe()
        {
            if (_subscribed) return;
            CompositionTarget.Rendering += Frame;
            _subscribed = true;
            InvalidateVisual();
        }

        void Frame(object? sender, EventArgs e)
        {
            var now = Now;
            _ripples.RemoveAll(r => now - r.Start > ClickRipple.Duration);
            var badgeLive = _badge is { } b && now - b.ShownAt <= KeyBadge.Hold;
            if (!badgeLive) _badge = null;
            InvalidateVisual(); // the last frame renders the cleared state
            if (_ripples.Count == 0 && !badgeLive && !_moved) Stop();
            _moved = false;
        }

        bool Inside(int x, int y) => x >= area.X && y >= area.Y && x < area.X + area.W && y < area.Y + area.H;

        protected override void OnRender(DrawingContext dc)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            double sx = 1 / dpi.DpiScaleX, sy = 1 / dpi.DpiScaleY;
            var now = Now;
            // Screen pixels -> this window's DIPs.
            Point P(int x, int y) => new((x - area.X) * sx, (y - area.Y) * sy);

            if (effects.HighlightCursor && _cx != int.MinValue && Inside(_cx, _cy))
                dc.DrawEllipse(RingBrush, null, P(_cx, _cy), RingSize / 2, RingSize / 2);

            if (effects.ShowClicks)
                foreach (var r in _ripples)
                {
                    if (ClickRipple.At(now - r.Start) is not { } v || !Inside(r.X, r.Y)) continue;
                    var c = r.Button == MouseButton.Left ? Left : r.Button == MouseButton.Right ? Right : Middle;
                    dc.PushOpacity(v.Opacity);
                    dc.DrawEllipse(null, new Pen(new SolidColorBrush(c), 3), P(r.X, r.Y), v.Diameter / 2, v.Diameter / 2);
                    dc.Pop();
                }

            if (effects.ShowShortcuts && _badge is { } badge && now - badge.ShownAt <= KeyBadge.Hold)
            {
                var text = new FormattedText(KeyBadge.Text(badge), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                    18, Brushes.White, dpi.PixelsPerDip);
                double w = text.Width + 28, h = text.Height + 16;
                double areaW = area.W * sx, areaH = area.H * sy; // overlay size in DIPs
                double bottom = areaH < 200 ? 12 : 48;
                var box = new Rect((areaW - w) / 2, areaH - bottom - h, w, h);
                var left = KeyBadge.Hold - (now - badge.ShownAt);
                dc.PushOpacity(left < FadeOut ? Math.Max(0, left / FadeOut) : 1);
                dc.DrawRoundedRectangle(BadgeBrush, null, box, 10, 10);
                dc.DrawText(text, new Point(box.X + 14, box.Y + 8));
                dc.Pop();
            }
        }
    }
}
