using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Snapr.Core;
using Snapr.App.Platform;

namespace Snapr.App.Dialogs;

/// <summary>Tags… (spec parity 13 §4): comma-separated tags for the selected captures, prefilled with the ones they all share.</summary>
public sealed class TagsWindow : Window
{
    public IReadOnlyList<string>? Result { get; private set; }

    public TagsWindow(Window owner, IReadOnlyList<string> shared)
    {
        DarkTitleBar.Attach(this);
        Owner = owner;
        Title = "Tags";
        Style = (Style)Application.Current.FindResource("SnaprWindow");
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var box = new TextBox { Width = 320, Text = string.Join(", ", shared), Margin = new Thickness(0, 6, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(box, "Tags");
        var ok = new Button { Content = "OK", IsDefault = true, Style = (Style)Application.Current.FindResource("AccentButton") };
        ok.Click += (_, _) =>
        {
            Result = TagRules.Parse(box.Text);
            DialogResult = true;
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = "Tags (separate with commas)" });
        root.Children.Add(box);
        root.Children.Add(new TextBlock
        {
            Text = "Up to 20 tags of up to 40 characters.",
            FontSize = 11,
            Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 6, 0, 0),
        });
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
    }
}
