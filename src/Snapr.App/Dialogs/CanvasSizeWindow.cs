using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Snapr.Core;

namespace Snapr.App.Dialogs;

/// <summary>Canvas size… (spec parity 4 §3): margins in pixels on each side, in white, transparent or the current colour.</summary>
public sealed class CanvasSizeWindow : Window
{
    readonly int _w, _h;
    readonly TextBox _left = Box(), _top = Box(), _right = Box(), _bottom = Box();
    readonly RadioButton _white = new() { Content = "White", IsChecked = true, GroupName = "margin" };
    readonly RadioButton _transparent = new() { Content = "Transparent", GroupName = "margin", Margin = new Thickness(12, 0, 0, 0) };
    readonly RadioButton _current = new() { Content = "Current colour", GroupName = "margin", Margin = new Thickness(12, 0, 0, 0) };
    readonly TextBlock _error = new() { Foreground = Brushes.IndianRed, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 320 };
    readonly Button _ok;
    readonly string _currentColor;

    public MarginsEdit? Result { get; private set; }

    public CanvasSizeWindow(Window owner, int width, int height, string currentColor)
    {
        Owner = owner;
        Title = "Canvas size";
        Style = (Style)Application.Current.FindResource("SnaprWindow");
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        _w = width;
        _h = height;
        _currentColor = currentColor;

        _ok = new Button { Content = "OK", IsDefault = true, Style = (Style)Application.Current.FindResource("AccentButton") };
        _ok.Click += (_, _) =>
        {
            Result = Read();
            DialogResult = Result is not null;
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        foreach (var box in new[] { _left, _top, _right, _bottom }) box.TextChanged += (_, _) => Validate();

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        AddRow(grid, 0, "Left (px)", _left);
        AddRow(grid, 1, "Top (px)", _top);
        AddRow(grid, 2, "Right (px)", _right);
        AddRow(grid, 3, "Bottom (px)", _bottom);
        var colours = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        colours.Children.Add(_white);
        colours.Children.Add(_transparent);
        colours.Children.Add(_current);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(_ok);
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(grid);
        root.Children.Add(colours);
        root.Children.Add(_error);
        root.Children.Add(buttons);
        Content = root;
        Validate();
    }

    static TextBox Box() => new() { Width = 90, Text = "0", Margin = new Thickness(0, 4, 0, 4) };

    static void AddRow(Grid grid, int row, string label, TextBox box)
    {
        grid.RowDefinitions.Add(new RowDefinition());
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(text, row);
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        grid.Children.Add(text);
        grid.Children.Add(box);
    }

    MarginsEdit? Read()
    {
        if (ImageEditRules.ParseMargin(_left.Text) is not { } l || ImageEditRules.ParseMargin(_top.Text) is not { } t
            || ImageEditRules.ParseMargin(_right.Text) is not { } r || ImageEditRules.ParseMargin(_bottom.Text) is not { } b) return null;
        if (!ImageEditRules.SizeOk((long)_w + l + r, (long)_h + t + b)) return null;
        var color = _transparent.IsChecked == true ? "#00000000" : _current.IsChecked == true ? ColorText.WithAlpha(_currentColor, 0xFF) : "#FFFFFFFF";
        return new MarginsEdit(l, t, r, b, color);
    }

    void Validate()
    {
        var ok = Read() is not null;
        _ok.IsEnabled = ok;
        _error.Text = ok ? "" : "Margins are whole numbers from 0 to 4,000 px, and the result must be at most 32,000 px per side and 100 megapixels.";
    }
}
