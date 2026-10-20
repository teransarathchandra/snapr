using System.Windows;
using System.Windows.Controls;
using Snapr.App.Services;
using Snapr.App.Platform;

namespace Snapr.App.Dialogs;

/// <summary>Shows recognised text (already on the clipboard) so the user can check or copy part of it.</summary>
public sealed class TextResultWindow : Window
{
    TextResultWindow(string text)
    {
        DarkTitleBar.Attach(this);
        Title = "Snapr – Grab Text";
        Style = (Style)Application.Current.FindResource("SnaprWindow");
        Width = 520;
        Height = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = AppIcon.Render(64);

        var box = new TextBox
        {
            Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 14,
        };
        var copy = new Button { Content = "Copy all", Style = (Style)Application.Current.FindResource("AccentButton") };
        copy.Click += (_, _) => ClipboardImage.SetText(box.Text);
        var close = new Button { Content = "Close", IsCancel = true };
        close.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(close);
        buttons.Children.Add(copy);
        var note = new TextBlock { Text = "Copied to the clipboard.", Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextSecondaryBrush"), Margin = new Thickness(0, 0, 0, 8) };

        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(note, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(note);
        root.Children.Add(buttons);
        root.Children.Add(box);
        Content = root;
    }

    public static void Open(string text)
    {
        var window = new TextResultWindow(text);
        window.Show();
        window.Activate();
    }
}
