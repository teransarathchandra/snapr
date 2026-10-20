using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Snapr.App.Platform;
using Snapr.App.Rendering;

namespace Snapr.App.Dialogs;

/// <summary>A 3-2-1 style countdown that is itself excluded from screen capture.</summary>
public sealed class CountdownWindow : Window
{
    readonly TextBlock _number = new()
    {
        FontSize = 64, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    CountdownWindow()
    {
        DarkTitleBar.Attach(this);
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = Height = 140;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        IsHitTestVisible = false;
        FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
        Content = new Border { Background = ImageIo.Brush("#E618181B"), CornerRadius = new CornerRadius(70), Child = _number };
        SourceInitialized += (_, _) => Native.ExcludeFromCapture(new WindowInteropHelper(this).Handle);
    }

    public static async Task RunAsync(int seconds)
    {
        var window = new CountdownWindow();
        window.Show();
        try
        {
            for (var i = seconds; i > 0; i--)
            {
                window._number.Text = i.ToString(CultureInfo.CurrentCulture);
                await Task.Delay(1000);
            }
        }
        finally
        {
            window.Close();
        }
        await Task.Delay(150);   // let the window disappear before the screen is grabbed on older Windows builds
    }
}
