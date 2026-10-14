using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Snapr.Core;

namespace Snapr.App.Dialogs;

/// <summary>Resize image… (spec parity 4 §3): width and height in pixels, keep proportions, or a percentage.</summary>
public sealed class ResizeImageWindow : Window
{
    readonly int _w0, _h0;
    readonly TextBox _width = new() { Width = 90 }, _height = new() { Width = 90 }, _percent = new() { Width = 90 };
    readonly CheckBox _keep = new() { Content = "Keep proportions", IsChecked = true, Margin = new Thickness(0, 8, 0, 0) };
    readonly TextBlock _error = new() { Foreground = Brushes.IndianRed, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    readonly Button _ok;
    bool _syncing;

    public (int W, int H)? Result { get; private set; }

    public ResizeImageWindow(Window owner, int width, int height)
    {
        Owner = owner;
        Title = "Resize image";
        Style = (Style)Application.Current.FindResource("SnaprWindow");
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        _w0 = width;
        _h0 = height;
        _ok = new Button { Content = "OK", IsDefault = true, Style = (Style)Application.Current.FindResource("AccentButton") };
        _ok.Click += (_, _) =>
        {
            Result = ImageEditRules.ParseSize(_width.Text, _height.Text);
            DialogResult = Result is not null;
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true };

        _width.Text = width.ToString(CultureInfo.InvariantCulture);
        _height.Text = height.ToString(CultureInfo.InvariantCulture);
        _percent.Text = "100";
        _width.TextChanged += (_, _) => Sync(_width);
        _height.TextChanged += (_, _) => Sync(_height);
        _percent.TextChanged += (_, _) => Sync(_percent);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        AddRow(grid, 0, "Width (px)", _width);
        AddRow(grid, 1, "Height (px)", _height);
        AddRow(grid, 2, "Percent", _percent);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(_ok);
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(grid);
        root.Children.Add(_keep);
        root.Children.Add(_error);
        root.Children.Add(buttons);
        Content = root;
        Validate();
    }

    static void AddRow(Grid grid, int row, string label, TextBox box)
    {
        grid.RowDefinitions.Add(new RowDefinition());
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(text, row);
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        box.Margin = new Thickness(0, 4, 0, 4);
        grid.Children.Add(text);
        grid.Children.Add(box);
    }

    void Sync(TextBox source)
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            static string Text(int v) => v.ToString(CultureInfo.InvariantCulture);
            if (source == _percent)
            {
                if (double.TryParse(_percent.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var p) && p > 0 && double.IsFinite(p))
                {
                    var (w, h) = ImageEditRules.FromPercent(_w0, _h0, p);
                    _width.Text = Text(w);
                    _height.Text = Text(h);
                }
            }
            else if (_keep.IsChecked == true && source == _width && int.TryParse(_width.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var w) && w > 0)
                _height.Text = Text(ImageEditRules.ProportionalHeight(_w0, _h0, w));
            else if (_keep.IsChecked == true && source == _height && int.TryParse(_height.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var h) && h > 0)
                _width.Text = Text(ImageEditRules.ProportionalWidth(_w0, _h0, h));
        }
        finally { _syncing = false; }
        Validate();
    }

    void Validate()
    {
        var ok = ImageEditRules.ParseSize(_width.Text, _height.Text) is not null;
        _ok.IsEnabled = ok;
        _error.Text = ok ? "" : "Between 1 and 32,000 px, at most 100 megapixels.";
    }
}
