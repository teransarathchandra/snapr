using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Snapr.App.Rendering;
using Snapr.App.Services;
using Snapr.App.Platform;

namespace Snapr.App.Dialogs;

/// <summary>Simplify (spec parity 15 §4): what to turn into shapes, how to treat the original, and the bar colour.</summary>
public sealed class SimplifyWindow : Window
{
    readonly CheckBox _text = new() { Content = "Text", Margin = new Thickness(0, 4, 16, 4) };
    readonly CheckBox _blocks = new() { Content = "Blocks", Margin = new Thickness(0, 4, 0, 4) };
    readonly RadioButton _keep = new() { Content = "Keep the original under the shapes", GroupName = "background", Margin = new Thickness(0, 4, 0, 4) };
    readonly RadioButton _plain = new() { Content = "Plain background", GroupName = "background", Margin = new Thickness(0, 4, 0, 4) };
    readonly WrapPanel _colours = new() { Margin = new Thickness(0, 6, 0, 0) };
    readonly List<(string Color, string Name, Button Button)> _swatches = [];
    readonly Button _ok;
    string _colour;

    public SimplifyOptions? Result { get; private set; }

    public SimplifyWindow(Window owner, IReadOnlyList<(string Color, string Name)> swatches, SimplifyOptions initial)
    {
        DarkTitleBar.Attach(this);
        Owner = owner;
        Title = "Simplify";
        Style = (Style)Application.Current.FindResource("SnaprWindow");
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _text.IsChecked = initial.Text;
        _blocks.IsChecked = initial.Blocks;
        _keep.IsChecked = !initial.PlainBackground;
        _plain.IsChecked = initial.PlainBackground;
        _colour = initial.BarColor;

        _ok = new Button { Content = "Simplify", IsDefault = true, Style = (Style)Application.Current.FindResource("AccentButton") };
        _ok.Click += (_, _) =>
        {
            Result = new SimplifyOptions(_text.IsChecked == true, _blocks.IsChecked == true, _plain.IsChecked == true, _colour);
            DialogResult = true;
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        _text.Checked += (_, _) => UpdateOk();
        _text.Unchecked += (_, _) => UpdateOk();
        _blocks.Checked += (_, _) => UpdateOk();
        _blocks.Unchecked += (_, _) => UpdateOk();

        AddSwatch("auto", "Auto", null);
        foreach (var (c, name) in swatches) AddSwatch(c, name, ImageIo.Brush(c));
        if (_swatches.All(s => !string.Equals(s.Color, _colour, StringComparison.OrdinalIgnoreCase))) _colour = "auto";
        StyleSwatches();
        UpdateOk();

        var what = new StackPanel { Orientation = Orientation.Horizontal };
        what.Children.Add(_text);
        what.Children.Add(_blocks);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(_ok);

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(Header("Turn into shapes"));
        root.Children.Add(what);
        root.Children.Add(Header("Background"));
        root.Children.Add(_keep);
        root.Children.Add(_plain);
        root.Children.Add(Header("Bar colour"));
        root.Children.Add(_colours);
        root.Children.Add(buttons);
        Content = root;
    }

    static TextBlock Header(string text) => new() { Text = text.ToUpperInvariant(), Style = (Style)Application.Current.FindResource("SectionHeader") };

    void UpdateOk() => _ok.IsEnabled = _text.IsChecked == true || _blocks.IsChecked == true;

    void AddSwatch(string colour, string name, Brush? fill)
    {
        var swatch = new Button { Width = 24, Height = 24, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 0), ToolTip = name };
        if (fill is null) swatch.Content = new TextBlock { Text = "A", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
        else swatch.Background = fill;
        swatch.Click += (_, _) => { _colour = colour; StyleSwatches(); };
        _swatches.Add((colour, name, swatch));
        _colours.Children.Add(swatch);
    }

    void StyleSwatches()
    {
        foreach (var (c, name, swatch) in _swatches)
        {
            var on = string.Equals(c, _colour, StringComparison.OrdinalIgnoreCase);
            swatch.BorderBrush = on ? (Brush)Application.Current.FindResource("AccentBrush") : (Brush)Application.Current.FindResource("PanelBorderBrush");
            swatch.BorderThickness = new Thickness(on ? 2 : 1);
            AutomationProperties.SetName(swatch, on ? $"{name}, selected" : name);
        }
    }
}
