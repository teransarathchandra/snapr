using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Snapr.App.Platform;
using Snapr.App.Services;
using Snapr.Core;
using Snapr.Core.Recording;
using Snapr.Core.Settings;

namespace Snapr.App.Dialogs;

/// <summary>Draggable camera bubble inside the recorded area. Not excluded from capture, so it is recorded.</summary>
public sealed class WebcamBubble : Window
{
    const double Radius = 16;
    readonly Rct _area;
    readonly Image _image = new() { Stretch = Stretch.UniformToFill };
    Rct _rect; // physical screen pixels
    bool _dragging;
    Native.POINT _grabCursor;
    Rct _grabRect;
    Action? _reshape;

    public bool Placed { get; }

    public WebcamBubble(WebcamService camera, Rct screenArea, WebcamSettings settings)
    {
        DarkTitleBar.Attach(this);
        _area = screenArea;
        var r = WebcamLayout.Place(screenArea, WebcamLayout.Pixels(settings.Size), settings.Shape);
        Placed = r is not null;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        if (r is null) return;
        _rect = r.Value;

        if (settings.Mirror)
        {
            _image.RenderTransform = new ScaleTransform(-1, 1);
            _image.RenderTransformOrigin = new Point(0.5, 0.5);
        }
        var outline = new Path { Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E6FFFFFF")), StrokeThickness = 2 };
        var backdrop = new Path { Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF18181B")) };
        var grid = new Grid();
        grid.Children.Add(backdrop);
        grid.Children.Add(_image);
        grid.Children.Add(outline);
        Content = grid;

        void Shape()
        {
            var s = VisualTreeHelper.GetDpi(this);
            double w = _rect.W / s.DpiScaleX, h = _rect.H / s.DpiScaleY;
            Width = w;
            Height = h;
            Geometry Make(double inset) => settings.Shape == WebcamShape.Circle
                ? new EllipseGeometry(new Rect(inset, inset, w - 2 * inset, h - 2 * inset))
                : new RectangleGeometry(new Rect(inset, inset, w - 2 * inset, h - 2 * inset), Radius / s.DpiScaleX, Radius / s.DpiScaleY);
            _image.Clip = Make(0);
            backdrop.Data = Make(0);
            outline.Data = Make(1);
        }

        void Refresh() => _image.Source = camera.Frame;
        camera.FrameChanged += Refresh;
        Closed += (_, _) => camera.FrameChanged -= Refresh;
        Refresh();

        _reshape = () => { Shape(); Move(false); };
        // Deferred so WPF's suggested DPI size can't overwrite the physical size.
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(_reshape);
        Loaded += (_, _) => _reshape();

        MouseLeftButtonDown += (_, e) =>
        {
            Native.GetCursorPos(out _grabCursor);
            _grabRect = _rect;
            _dragging = CaptureMouse();
            e.Handled = true;
        };
        MouseMove += (_, _) =>
        {
            if (!_dragging) return;
            Native.GetCursorPos(out var p);
            _rect = WebcamLayout.Clamp(_grabRect with { X = _grabRect.X + p.X - _grabCursor.X, Y = _grabRect.Y + p.Y - _grabCursor.Y }, _area);
            Move(true);
        };
        MouseLeftButtonUp += (_, _) => ReleaseMouseCapture();
        LostMouseCapture += (_, _) => _dragging = false;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (!Placed)
        {
            Close();
            return;
        }
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, (IntPtr)(Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64() | Native.WS_EX_NOACTIVATE));
        Move(false); // onto the target monitor first, so WPF measures with that monitor's DPI
    }

    void Move(bool keepSize)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, (int)_rect.X, (int)_rect.Y, (int)_rect.W, (int)_rect.H,
            Native.SWP_NOACTIVATE | (keepSize ? Native.SWP_NOSIZE : 0));
    }

    public void Toggle() => Visibility = Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
}
