using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Snapr.App.Rendering;
using Snapr.App.Services;
using Snapr.Core;

namespace Snapr.App.Dialogs;

/// <summary>Combine images (spec parity 10 §4): layout, alignment, gap and background.</summary>
public sealed class CombineWindow : Window
{
    static readonly int[] Gaps = [0, 10, 20, 40];
    readonly ComboBox _layout = new() { Width = 160 }, _align = new() { Width = 160 }, _gap = new() { Width = 160 }, _background = new() { Width = 160 };
    readonly List<string> _colours = [];

    public CombineChoice? Result { get; private set; }

    public CombineWindow(Window owner, IReadOnlyList<string> swatches, CombineChoice initial)
    {
        Owner = owner;
        Title = "Combine images";
        Style = (Style)Application.Current.FindResource("SnaprWindow");
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        foreach (var name in new[] { "Side by side", "Stacked", "Grid" }) _layout.Items.Add(name);
        for (var i = 0; i < 3; i++) _align.Items.Add("");
        foreach (var g in Gaps) _gap.Items.Add($"{g} px");
        AddColour("White", "#FFFFFFFF");
        AddColour("Transparent", "#00000000");
        foreach (var c in swatches.Where(c => !string.Equals(c, "#FFFFFFFF", StringComparison.OrdinalIgnoreCase))) AddColour(c, c);

        _layout.SelectionChanged += (_, _) => LabelAlign();
        _layout.SelectedIndex = (int)initial.Layout;
        _align.SelectedIndex = (int)initial.Align;
        _gap.SelectedIndex = Math.Max(0, Array.IndexOf(Gaps, initial.Gap));
        _background.SelectedIndex = Math.Max(0, _colours.FindIndex(c => string.Equals(c, initial.Background, StringComparison.OrdinalIgnoreCase)));
        LabelAlign();
        AutomationProperties.SetName(_layout, "Layout");
        AutomationProperties.SetName(_align, "Align");
        AutomationProperties.SetName(_gap, "Gap");
        AutomationProperties.SetName(_background, "Background");

        var ok = new Button { Content = "Combine", IsDefault = true, Style = (Style)Application.Current.FindResource("AccentButton") };
        ok.Click += (_, _) =>
        {
            Result = new CombineChoice((CombineLayout)_layout.SelectedIndex, (CombineAlign)_align.SelectedIndex, Gaps[_gap.SelectedIndex], _colours[_background.SelectedIndex]);
            DialogResult = true;
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        AddRow(grid, 0, "Layout", _layout);
        AddRow(grid, 1, "Align", _align);
        AddRow(grid, 2, "Gap", _gap);
        AddRow(grid, 3, "Background", _background);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(grid);
        root.Children.Add(buttons);
        Content = root;
    }

    void AddColour(string label, string colour)
    {
        _colours.Add(colour);
        var swatch = new Border
        {
            Width = 14, Height = 14, Margin = new Thickness(0, 0, 8, 0), BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Gray, Background = ImageIo.Brush(colour),
        };
        var item = new StackPanel { Orientation = Orientation.Horizontal, Children = { swatch, new TextBlock { Text = label } } };
        AutomationProperties.SetName(item, label);
        _background.Items.Add(item);
    }

    /// <summary>Side by side aligns vertically; Stacked and Grid align horizontally (spec §4).</summary>
    void LabelAlign()
    {
        string[] names = _layout.SelectedIndex == (int)CombineLayout.SideBySide ? ["Top", "Middle", "Bottom"] : ["Left", "Centre", "Right"];
        var keep = _align.SelectedIndex;
        for (var i = 0; i < 3; i++) _align.Items[i] = names[i];
        _align.SelectedIndex = keep;
    }

    static void AddRow(Grid grid, int row, string label, Control box)
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
}
