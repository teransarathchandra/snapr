using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Snapr.App.Platform;
using Snapr.Core;

namespace Snapr.App.Dialogs;

/// <summary>Panoramic capture bar (spec parity 12 §3): hint, live counter, Stop and Cancel; excluded from capture.</summary>
public sealed class PanoramaBar : Window
{
    readonly Rct _area;
    readonly TextBlock _count = new() { Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };

    /// <summary>True once Stop, Cancel or closing ended the capture.</summary>
    public bool Finished { get; private set; }
    /// <summary>True when Stop ended it (keep the result); false for Cancel or close.</summary>
    public bool Stopped { get; private set; }

    public PanoramaBar(Rct screenArea)
    {
        DarkTitleBar.Attach(this);
        _area = screenArea;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;

        var hint = new TextBlock { Text = "Move the content: scroll, drag or pan. Then Stop.", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
        var stop = new Button { Content = "Stop", IsDefault = true, Style = (Style)Application.Current.FindResource("AccentButton") };
        stop.Click += (_, _) => { Stopped = true; Finished = true; };
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Finished = true;

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(hint);
        row.Children.Add(_count);
        row.Children.Add(cancel);
        row.Children.Add(stop);
        Content = new Border
        {
            Background = (Brush)Application.Current.FindResource("PanelBrush"),
            BorderBrush = (Brush)Application.Current.FindResource("PanelBorderBrush"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 8, 8),
            Child = row,
        };
        SetCount(0, 0, 0);
        SourceInitialized += (_, _) => Native.ExcludeFromCapture(new WindowInteropHelper(this).Handle);
        Loaded += (_, _) => RecordingBar.PlaceOutside(this, _area);
        SizeChanged += (_, _) => { if (IsLoaded) RecordingBar.PlaceOutside(this, _area); };
        Closed += (_, _) => Finished = true;
    }

    public void SetCount(int frames, int width, int height) =>
        _count.Text = $"{frames} {(frames == 1 ? "frame" : "frames")} · {width} × {height}";
}
