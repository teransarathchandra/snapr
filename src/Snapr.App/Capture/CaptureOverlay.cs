using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Snapr.App.Platform;
using Snapr.App.Rendering;
using Snapr.Core;
using Snapr.Core.Capture;

namespace Snapr.App.Capture;

/// <summary>Full-screen frozen frame for one monitor: crosshair, 8× loupe, W×H readout, window highlight.</summary>
sealed class CaptureOverlay : Window
{
    static readonly Brush Accent = ImageIo.Brush("#FF3B82F6");
    static readonly Brush Chip = ImageIo.Brush("#E618181B");

    readonly OverlaySession _session;
    readonly Canvas _layer = new() { IsHitTestVisible = false };
    readonly Path _dim = new() { Fill = ImageIo.Brush("#80000000") };
    readonly Rectangle _outline = new() { Stroke = Accent, StrokeThickness = 2, Visibility = Visibility.Collapsed };
    readonly TextBlock _readoutText = new() { Foreground = Brushes.White, FontSize = 12 };
    readonly Border _readout;
    readonly Rectangle _loupeImage;
    readonly Border _loupe;
    Pt? _down;
    Rct? _current;

    public Rct Monitor { get; }

    public CaptureOverlay(OverlaySession session, Rct monitor)
    {
        _session = session;
        Monitor = monitor;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;
        WindowStartupLocation = WindowStartupLocation.Manual;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        var frozen = new Image
        {
            Source = new CroppedBitmap(session.Grab.Image, new Int32Rect((int)monitor.X, (int)monitor.Y, (int)monitor.W, (int)monitor.H)),
            Stretch = Stretch.Fill,
        };
        Typography.SetNumeralAlignment(_readoutText, FontNumeralAlignment.Tabular);
        _readout = new Border { Background = Chip, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 4, 8, 4), Child = _readoutText };
        _loupeImage = new Rectangle
        {
            Width = 128, Height = 128,
            Fill = new ImageBrush(session.Grab.Image) { ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill },
        };
        RenderOptions.SetBitmapScalingMode(_loupeImage, BitmapScalingMode.NearestNeighbor);
        _loupe = new Border
        {
            Width = 132, Height = 132, BorderBrush = Accent, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(8), ClipToBounds = true,
            Child = new Grid { Children = { _loupeImage, new Path { Data = Geometry.Parse("M64,0 V128 M0,64 H128"), Stroke = Accent, StrokeThickness = 1 } } },
        };
        var hint = new Border
        {
            Background = Chip, CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 8, 14, 8),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 32),
            IsHitTestVisible = false,
            Child = new TextBlock { Text = HintText(session.Mode), Foreground = Brushes.White, FontSize = 13 },
        };

        _layer.Children.Add(_dim);
        _layer.Children.Add(_outline);
        _layer.Children.Add(_loupe);
        _layer.Children.Add(_readout);
        var root = new Grid();
        root.Children.Add(frozen);
        root.Children.Add(_layer);
        root.Children.Add(hint);
        Content = root;

        SourceInitialized += (_, _) =>
        {
            // Move onto the target monitor in physical pixels, then maximise: Windows sizes it to that monitor exactly.
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, (int)(Monitor.X + session.Grab.OriginX) + 8, (int)(Monitor.Y + session.Grab.OriginY) + 8, 200, 200, Native.SWP_NOACTIVATE);
            WindowState = WindowState.Maximized;
        };
        Loaded += (_, _) => Refresh();
        MouseEnter += (_, _) => Activate();
    }

    static string HintText(OverlayMode mode) => mode switch
    {
        OverlayMode.Video => "Drag the area to record  ·  Click a window  ·  Space: this screen  ·  Esc: cancel",
        OverlayMode.ScrollingWindow => "Click the window to scroll-capture  ·  Esc: cancel  (press Esc during capture to stop early)",
        _ => "Drag to select  ·  Click a window  ·  Space: all screens  ·  C: copy only  ·  T: grab text  ·  Esc: cancel",
    };

    Rct ClipArea => _session.Mode == OverlayMode.Video ? Monitor : _session.Virtual;

    Pt ToPx(Point p) => new(Monitor.X + p.X * Monitor.W / ActualWidth, Monitor.Y + p.Y * Monitor.H / ActualHeight);
    Point ToDip(Pt p) => new((p.X - Monitor.X) * ActualWidth / Monitor.W, (p.Y - Monitor.Y) * ActualHeight / Monitor.H);

    public void Refresh()
    {
        if (!IsLoaded || ActualWidth <= 0) return;
        Update(ToPx(Mouse.GetPosition(this)));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _down = ToPx(e.GetPosition(this));
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e) => Update(ToPx(e.GetPosition(this)));

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_down is not { } down) return;
        _down = null;
        ReleaseMouseCapture();
        var up = ToPx(e.GetPosition(this));
        var selection = SelectionResolver.Resolve(down, up, _session.Windows, ClipArea);
        var acceptable = selection.Kind != SelectionKind.None &&
                         (_session.Mode != OverlayMode.ScrollingWindow || selection.Kind == SelectionKind.Window);
        if (acceptable) _session.Complete(selection);
        else Update(up);   // a click on nothing or a tiny drag keeps the overlay open
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: _session.Cancel(); break;
            case Key.Space when _session.Mode != OverlayMode.ScrollingWindow:
                _session.Complete(new Selection(SelectionKind.Fullscreen, ClipArea, 0, null));
                break;
            case Key.C: _session.ToggleAction(OverlayAction.Copy); break;
            case Key.T: _session.ToggleAction(OverlayAction.GrabText); break;
            default: return;
        }
        e.Handled = true;
    }

    string ActionLabel() => _session.Mode switch
    {
        OverlayMode.Video => "  ·  Record",
        OverlayMode.ScrollingWindow => "  ·  Scrolling capture",
        _ => _session.Action switch { OverlayAction.Copy => "  ·  Copy only", OverlayAction.GrabText => "  ·  Grab text", _ => "" },
    };

    void Update(Pt p)
    {
        var full = new Rect(0, 0, ActualWidth, ActualHeight);
        if (_down is { } d && d.DistanceTo(p) >= SelectionResolver.MinDragPixels && _session.Mode != OverlayMode.ScrollingWindow)
            _current = Rct.FromPoints(d, p).Intersect(Monitor);
        else
            _current = SelectionResolver.WindowAt(p, _session.Windows)?.Bounds.Intersect(ClipArea);

        if (_current is { IsEmpty: false } c)
        {
            var r = new Rect(ToDip(new Pt(c.X, c.Y)), ToDip(new Pt(c.Right, c.Bottom)));
            _dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(full), new RectangleGeometry(r));
            _outline.Visibility = Visibility.Visible;
            Canvas.SetLeft(_outline, r.X);
            Canvas.SetTop(_outline, r.Y);
            _outline.Width = r.Width;
            _outline.Height = r.Height;
            _readoutText.Text = $"{(int)c.W} × {(int)c.H}{ActionLabel()}";
        }
        else
        {
            _dim.Data = new RectangleGeometry(full);
            _outline.Visibility = Visibility.Collapsed;
            _readoutText.Text = $"{(int)p.X + _session.Grab.OriginX}, {(int)p.Y + _session.Grab.OriginY}{ActionLabel()}";
        }

        ((ImageBrush)_loupeImage.Fill).Viewbox = new Rect(p.X - 8, p.Y - 8, 16, 16);
        var at = ToDip(p);
        // Loupe and readout sit below-right of the pointer and flip to the other side near screen edges.
        var x = at.X + 24 + 132 > ActualWidth ? at.X - 24 - 132 : at.X + 24;
        var y = at.Y + 24 + 132 + 34 > ActualHeight ? at.Y - 24 - 132 - 34 : at.Y + 24;
        Canvas.SetLeft(_loupe, x);
        Canvas.SetTop(_loupe, y);
        Canvas.SetLeft(_readout, x);
        Canvas.SetTop(_readout, y + 138);
    }
}
