using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Snapr.App.Platform;
using Snapr.App.Rendering;
using Snapr.Core;
using Forms = System.Windows.Forms;

namespace Snapr.App.Dialogs;

/// <summary>Floating timer with Pause and Stop, placed outside the recorded area and excluded from capture.</summary>
public sealed class RecordingBar : Window
{
    readonly Rct _area;
    readonly TextBlock _time;
    readonly Button _pause;
    readonly Stopwatch _elapsed = Stopwatch.StartNew();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public RecordingBar(AppController app, Rct screenRect, bool micUnavailable, bool hasCamera = false, bool micFallback = false)
    {
        DarkTitleBar.Attach(this);
        _area = screenRect;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;

        _time = new TextBlock { Text = "00:00", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 12, 0) };
        Typography.SetNumeralAlignment(_time, FontNumeralAlignment.Tabular);
        _pause = new Button { Content = "Pause" };
        _pause.Click += (_, _) => app.TogglePause();
        var stop = new Button { Content = "Stop", Style = (Style)Application.Current.FindResource("AccentButton") };
        stop.Click += (_, _) => app.Fire(app.ToggleRecordingAsync);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = ImageIo.Brush("#FFEF4444"), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(_time);
        if (micUnavailable || micFallback)
            row.Children.Add(new Border
            {
                Background = ImageIo.Brush("#FF7F1D1D"), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = micUnavailable ? "No mic" : "Default mic", Foreground = Brushes.White, FontSize = 11 },
            });
        if (hasCamera)
        {
            var camera = new Button { Content = "Hide camera" };
            camera.Click += (_, _) =>
            {
                app.ToggleWebcam();
                camera.Content = (string)camera.Content == "Hide camera" ? "Show camera" : "Hide camera";
            };
            row.Children.Add(camera);
        }
        row.Children.Add(_pause);
        row.Children.Add(stop);
        Content = new Border
        {
            Background = (Brush)Application.Current.FindResource("PanelBrush"),
            BorderBrush = (Brush)Application.Current.FindResource("PanelBorderBrush"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 8, 8),
            Child = row,
        };

        _timer.Tick += (_, _) => _time.Text = _elapsed.Elapsed.ToString(@"mm\:ss");
        _timer.Start();
        SourceInitialized += (_, _) => Native.ExcludeFromCapture(new WindowInteropHelper(this).Handle);
        Loaded += (_, _) => PlaceOutside(this, _area);
        Closed += (_, _) => _timer.Stop();
    }

    public void SetPaused(bool paused)
    {
        _pause.Content = paused ? "Resume" : "Pause";
        if (paused) _elapsed.Stop();
        else _elapsed.Start();
    }

    /// <summary>Above the area if there is room, else below, else inside its top-right corner. Screen pixels.</summary>
    internal static void PlaceOutside(Window bar, Rct area)
    {
        var hwnd = new WindowInteropHelper(bar).Handle;
        var dpi = VisualTreeHelper.GetDpi(bar);
        int w = (int)(bar.ActualWidth * dpi.DpiScaleX), h = (int)(bar.ActualHeight * dpi.DpiScaleY);
        var monitor = Forms.Screen.FromRectangle(new System.Drawing.Rectangle((int)area.X, (int)area.Y, (int)area.W, (int)area.H)).Bounds;
        int x = (int)area.X, y = (int)area.Y - h - 8;
        if (y < monitor.Top) y = (int)area.Bottom + 8;
        if (y + h > monitor.Bottom)
        {
            y = (int)area.Y + 8;
            x = (int)area.Right - w - 8;
        }
        x = Math.Clamp(x, monitor.Left, Math.Max(monitor.Left, monitor.Right - w));
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }
}
