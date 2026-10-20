using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Snapr.App.Rendering;
using Snapr.Core;

namespace Snapr.App.Dialogs;

public sealed record TemplateChoice(TemplateKind Kind, string Background)
{
    public static TemplateChoice Default { get; } = new(TemplateKind.StepByStep, "#FFFFFFFF");
}

/// <summary>Templates (spec parity 14 §3): pick a template that fits the picked captures, and a background.</summary>
public sealed class TemplateWindow : Window
{
    static readonly (TemplateKind Kind, string Name)[] Kinds =
    [
        (TemplateKind.StepByStep, "Step by step"), (TemplateKind.BeforeAfter, "Before / after"),
        (TemplateKind.GridCaptions, "Grid with captions"), (TemplateKind.SingleTitle, "Single with title"),
    ];

    readonly ComboBox _background = new() { Width = 160 };
    readonly List<string> _colours = [];
    readonly List<(TemplateKind Kind, RadioButton Radio)> _radios = [];

    public TemplateChoice? Result { get; private set; }

    public TemplateWindow(Window owner, int count, IReadOnlyList<string> swatches, TemplateChoice initial)
    {
        Owner = owner;
        Title = "Template";
        Style = (Style)Application.Current.FindResource("SnaprWindow");
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var wrap = new WrapPanel { Width = 336 };
        foreach (var (kind, name) in Kinds)
        {
            var reason = TemplateRules.Fits(kind, count);
            var content = new StackPanel();
            content.Children.Add(Schematic(kind));
            content.Children.Add(new TextBlock { Text = name, Margin = new Thickness(0, 4, 0, 0) });
            if (reason is not null)
                content.Children.Add(new TextBlock { Text = reason, FontSize = 11, Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush") });
            var radio = new RadioButton { GroupName = "template", Content = content, IsEnabled = reason is null, Opacity = reason is null ? 1 : 0.4, Width = 160, Margin = new Thickness(0, 0, 0, 8) };
            AutomationProperties.SetName(radio, reason is null ? name : $"{name}, {reason}");
            _radios.Add((kind, radio));
            wrap.Children.Add(radio);
        }
        var first = _radios.FirstOrDefault(r => r.Kind == initial.Kind && r.Radio.IsEnabled).Radio ?? _radios.FirstOrDefault(r => r.Radio.IsEnabled).Radio;
        if (first is not null) first.IsChecked = true;

        AddColour("White", "#FFFFFFFF");
        AddColour("Transparent", "#00000000");
        foreach (var c in swatches.Where(c => !string.Equals(c, "#FFFFFFFF", StringComparison.OrdinalIgnoreCase))) AddColour(c, c);
        _background.SelectedIndex = Math.Max(0, _colours.FindIndex(c => string.Equals(c, initial.Background, StringComparison.OrdinalIgnoreCase)));
        AutomationProperties.SetName(_background, "Background");

        var ok = new Button { Content = "Create", IsDefault = true, Style = (Style)Application.Current.FindResource("AccentButton") };
        ok.Click += (_, _) =>
        {
            var chosen = _radios.FirstOrDefault(r => r.Radio.IsChecked == true);
            if (chosen.Radio is null) return;
            Result = new TemplateChoice(chosen.Kind, _colours[_background.SelectedIndex]);
            DialogResult = true;
        };
        ok.IsEnabled = first is not null;
        var cancel = new Button { Content = "Cancel", IsCancel = true };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        row.Children.Add(new TextBlock { Text = "Background", VerticalAlignment = VerticalAlignment.Center, Width = 110 });
        row.Children.Add(_background);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(wrap);
        root.Children.Add(row);
        root.Children.Add(buttons);
        Content = root;
    }

    /// <summary>A 120 x 80 sketch of the template: picture blocks, title and caption lines, step badges.</summary>
    static Canvas Schematic(TemplateKind kind)
    {
        var canvas = new Canvas { Width = 120, Height = 80 };
        var block = (Brush)Application.Current.FindResource("TextSecondaryBrush");
        var line = (Brush)Application.Current.FindResource("TextBrush");
        var accent = (Brush)Application.Current.FindResource("AccentBrush");
        void Rect(double x, double y, double w, double h, Brush fill, double opacity = 1)
        {
            var r = new Rectangle { Width = w, Height = h, Fill = fill, Opacity = opacity };
            Canvas.SetLeft(r, x);
            Canvas.SetTop(r, y);
            canvas.Children.Add(r);
        }
        void Block(double x, double y, double w, double h) => Rect(x, y, w, h, block, 0.4);
        void Line(double x, double y, double w) => Rect(x, y, w, 2, line);
        switch (kind)
        {
            case TemplateKind.StepByStep:
                for (var i = 0; i < 3; i++)
                {
                    var y = i * 26;
                    var badge = new Ellipse { Width = 10, Height = 10, Fill = accent };
                    Canvas.SetLeft(badge, 4);
                    Canvas.SetTop(badge, y);
                    canvas.Children.Add(badge);
                    Line(20, y + 4, 50);
                    Block(20, y + 10, 60, 12);
                }
                break;
            case TemplateKind.BeforeAfter:
                Line(4, 4, 20);
                Line(64, 4, 20);
                Block(4, 12, 52, 56);
                Block(64, 12, 52, 56);
                break;
            case TemplateKind.GridCaptions:
                Line(4, 2, 50);
                foreach (var y in new[] { 10, 44 })
                    foreach (var x in new[] { 4, 64 })
                    {
                        Block(x, y, 52, 24);
                        Line(x, y + 27, 30);
                    }
                break;
            default:
                Line(4, 4, 50);
                Block(4, 12, 112, 46);
                Line(4, 64, 80);
                Line(4, 70, 60);
                break;
        }
        return canvas;
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
}
