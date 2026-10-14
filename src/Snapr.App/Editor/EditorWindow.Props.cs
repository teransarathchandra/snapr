using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Snapr.App.Rendering;
using Snapr.Core;

namespace Snapr.App.Editor;

/// <summary>Toolbar and the context-sensitive properties panel.</summary>
public partial class EditorWindow
{
    static readonly string[] Swatches = ["#FFFF3B30", "#FFFF9500", "#FFFFCC00", "#FF34C759", "#FF3B82F6", "#FFAF52DE", "#FF111111", "#FFFFFFFF"];

    void BuildToolbar()
    {
        foreach (var (tool, name, key) in Tools)
        {
            var button = new RadioButton
            {
                Style = (Style)FindResource("ToolButton"),
                GroupName = "tools",
                Content = ToolIcons.Create(tool),
                ToolTip = $"{name} ({key})",
            };
            AutomationProperties.SetName(button, name);
            button.Checked += (_, _) => SetTool(tool);
            _toolButtons[tool] = button;
            ToolPanel.Children.Add(button);
        }
    }

    void SetTool(ToolKind tool)
    {
        Board.CommitTextEditor();
        if (_toolButtons[tool].IsChecked != true)
        {
            _toolButtons[tool].IsChecked = true;   // raises Checked, which calls SetTool again
            return;
        }
        Board.Tool = tool;
        Board.CurrentStyle = _app.Settings.QuickStyles.TryGetValue(tool, out var saved) ? saved : ToolDefaults.For(tool);
        if (tool != ToolKind.Move) Board.Select(null);
        _app.SaveLastTool(tool);
        BuildProps();
    }

    static ToolKind KindOf(Annotation a) => a switch
    {
        ArrowAnnotation => ToolKind.Arrow,
        ShapeAnnotation => ToolKind.Shape,
        TextAnnotation => ToolKind.Text,
        CalloutAnnotation => ToolKind.Callout,
        StepAnnotation => ToolKind.Step,
        HighlightAnnotation => ToolKind.Highlight,
        PenAnnotation => ToolKind.Pen,
        BlurAnnotation => ToolKind.Blur,
        MagnifyAnnotation => ToolKind.Magnify,
        _ => ToolKind.Move,
    };

    void BuildProps()
    {
        PropsPanel.Children.Clear();
        if (Board.SelectedObjects is { Count: > 1 } many)
        {
            MultiProps(many);
            EffectsProps();
            return;
        }
        var selected = Board.Selected;
        var kind = selected is null ? Board.Tool : KindOf(selected);
        var style = selected is null ? Board.CurrentStyle : AnnotationGeometry.StyleOf(selected, Board.CurrentStyle);
        var options = selected is null ? Board.Options : AnnotationGeometry.OptionsOf(selected, Board.Options);
        var name = Tools.First(t => t.Tool == kind).Name;
        Header(selected is null ? name : $"{name} · selected");

        if (kind is ToolKind.Arrow or ToolKind.Shape or ToolKind.Text or ToolKind.Callout or ToolKind.Step or ToolKind.Highlight or ToolKind.Pen or ToolKind.Fill)
        {
            Label("Colour");
            SwatchRow(style.Color, c => ChangeStyle(s => s with { Color = c }));
        }
        if (kind is ToolKind.Arrow or ToolKind.Shape or ToolKind.Pen)
            SliderRow("Width", 1, 20, style.Width, v => ChangeStyle(s => s with { Width = v }, "width"));
        if (kind is ToolKind.Text or ToolKind.Callout or ToolKind.Step)
            SliderRow("Size", 10, 72, style.FontSize, v => ChangeStyle(s => s with { FontSize = v }, "size"));
        if (kind is ToolKind.Shape or ToolKind.Text or ToolKind.Callout)
            ChoiceRow("Fill", [("None", (string?)null), ("White", "#FFFFFFFF"), ("Tint", ColorText.WithAlpha(style.Color, 0x40))], style.Fill,
                f => ChangeStyle(s => s with { Fill = f }));
        if (kind == ToolKind.Shape && selected is null)
            ChoiceRow("Shape", [("Rectangle", ShapeOption.Rectangle), ("Ellipse", ShapeOption.Ellipse), ("Line", ShapeOption.Line)], options.Shape,
                v => ChangeOptions(o => o with { Shape = v }));
        if (kind == ToolKind.Blur)
        {
            ChoiceRow("Type", [("Pixelate", BlurMode.Pixelate), ("Blur", BlurMode.Blur)], options.Blur, v => ChangeOptions(o => o with { Blur = v }));
            SliderRow("Strength", 1, 20, options.BlurStrength, v => ChangeOptions(o => o with { BlurStrength = (int)v }, "strength"));
        }
        if (kind == ToolKind.Magnify)
        {
            ChoiceRow("Lens", [("Circle", MagnifyShape.Circle), ("Rectangle", MagnifyShape.Rectangle)], options.Lens, v => ChangeOptions(o => o with { Lens = v }));
            SliderRow("Zoom", 1.5, 4, options.Zoom, v => ChangeOptions(o => o with { Zoom = v }, "zoom"));
        }
        if (kind is ToolKind.Arrow or ToolKind.Shape or ToolKind.Text or ToolKind.Callout or ToolKind.Step or ToolKind.Pen)
            CheckRow("Shadow", style.Shadow, v => ChangeStyle(s => s with { Shadow = v }));
        if (kind == ToolKind.Step)
        {
            ChoiceRow("Style", [("1 2 3", StepStyle.Numbers), ("A B C", StepStyle.Letters)], options.Steps,
                v => ChangeOptions(o => o with { Steps = v }));
            if (selected is StepAnnotation step) StepValueRow(step);
            ButtonRow(("Restart numbering", () => Board.Apply(s => s.RestartSteps())));
        }
        if (selected is ArrowAnnotation or PenAnnotation or CalloutAnnotation)
            ButtonRow(("Flip ↔", () => Board.ApplyToSelection(a => AnnotationGeometry.Flip(a, horizontal: true))),
                      ("Flip ↕", () => Board.ApplyToSelection(a => AnnotationGeometry.Flip(a, horizontal: false))));
        if (kind == ToolKind.Fill)
        {
            SliderRow("Tolerance", 0, 100, options.FillTolerance, v => ChangeOptions(o => o with { FillTolerance = (int)Math.Round(v) }, "tolerance"));
            Hint("Click the image to fill the touching area of similar colour. Objects are not changed.");
        }
        if (kind == ToolKind.Crop)
        {
            Hint("Drag over the image to crop. Pixels outside are kept, so you can reset.");
            ButtonRow(("Reset crop", () => Board.Apply(s => s.ResetCrop())));
        }
        if (selected is { GroupId: not null }) ButtonRow(("Ungroup", Ungroup));
        if (selected is not null)
            ButtonRow(("Bring to front", () => Board.Apply(s => s.BringToFront(selected.Id))),
                      ("Send to back", () => Board.Apply(s => s.SendToBack(selected.Id))),
                      ("Delete", Board.DeleteSelected));
        else if (kind is not (ToolKind.Move or ToolKind.Crop or ToolKind.Eraser))
            ButtonRow(("Save as quick style", () => { _app.SaveQuickStyle(Board.Tool, Board.CurrentStyle); _app.Notify("Quick style saved", $"{name} will start with this style."); }));

        EffectsProps();
    }

    void EffectsProps()
    {
        Header("Effects");
        var fx = Board.State.Effects;
        CheckRow("Border", fx.Border is not null, v => Board.Apply(s => s.SetEffects(s.Effects with { Border = v ? new BorderEffect(Board.CurrentStyle.Color, 6) : null })));
        CheckRow("Drop shadow", fx.Shadow, v => Board.Apply(s => s.SetEffects(s.Effects with { Shadow = v })));
        CheckRow("Torn edge", fx.TornEdge, v => Board.Apply(s => s.SetEffects(s.Effects with { TornEdge = v })));
        Hint("Effects are added when you copy, save or export.");
    }

    /// <summary>Panel for 2+ selected objects: colour and shadow for all, align/distribute, group, arrange, delete.</summary>
    void MultiProps(IReadOnlyList<Annotation> many)
    {
        var units = Board.State.UnitCount(Board.SelectedIds);
        Header($"{many.Count} objects");
        Label("Colour");
        SwatchRow(AnnotationGeometry.StyleOf(many[0], Board.CurrentStyle).Color, c => ChangeStyle(s => s with { Color = c }));
        var shadowable = many.Where(a => a is not (BlurAnnotation or HighlightAnnotation)).ToArray();
        if (shadowable.Length > 0) CheckRow("Shadow", shadowable.All(a => a.Shadow), v => ChangeStyle(s => s with { Shadow = v }));
        if (units >= 2)
        {
            Label("Align");
            ButtonRow(("Left", () => Align(AlignEdge.Left)), ("Centre", () => Align(AlignEdge.Center)), ("Right", () => Align(AlignEdge.Right)));
            ButtonRow(("Top", () => Align(AlignEdge.Top)), ("Middle", () => Align(AlignEdge.Middle)), ("Bottom", () => Align(AlignEdge.Bottom)));
        }
        if (units >= 3)
        {
            Label("Distribute");
            ButtonRow(("Horizontally", () => Board.Apply(s => s.Distribute(Board.SelectedIds, Axis.Horizontal))),
                      ("Vertically", () => Board.Apply(s => s.Distribute(Board.SelectedIds, Axis.Vertical))));
        }
        var grouping = new List<(string Text, Action Click)>();
        if (!IsOneGroup(many)) grouping.Add(("Group", Group));
        if (many.Any(a => a.GroupId is not null)) grouping.Add(("Ungroup", Ungroup));
        if (grouping.Count > 0) ButtonRow([.. grouping]);
        ButtonRow(("Bring to front", () => Board.Apply(s => s.BringToFront(Board.SelectedIds))),
                  ("Send to back", () => Board.Apply(s => s.SendToBack(Board.SelectedIds))),
                  ("Delete", Board.DeleteSelected));
    }

    void Align(AlignEdge edge) => Board.Apply(s => s.Align(Board.SelectedIds, edge));

    void ChangeStyle(Func<ToolStyle, ToolStyle> change, string? mergeKey = null)
    {
        if (Board.SelectedIds.Count > 0)
            Board.ApplyToSelection(a => AnnotationGeometry.WithStyle(a, change(AnnotationGeometry.StyleOf(a, Board.CurrentStyle))), mergeKey);
        else
            Board.CurrentStyle = change(Board.CurrentStyle);
        if (mergeKey is null) BuildProps();   // sliders keep their own position; everything else redraws its highlight
    }

    void ChangeOptions(Func<ToolOptions, ToolOptions> change, string? mergeKey = null)
    {
        if (Board.SelectedIds.Count > 0)
            Board.ApplyToSelection(a => AnnotationGeometry.WithOptions(a, change(AnnotationGeometry.OptionsOf(a, Board.Options))), mergeKey);
        else
            Board.Options = change(Board.Options);
        if (mergeKey is null) BuildProps();
    }

    void Header(string text) =>
        PropsPanel.Children.Add(new TextBlock { Text = text.ToUpperInvariant(), Style = (Style)FindResource("SectionHeader") });

    void Label(string text) =>
        PropsPanel.Children.Add(new TextBlock { Text = text, Foreground = (Brush)FindResource("TextSecondaryBrush"), Margin = new Thickness(0, 8, 0, 4) });

    void Hint(string text) =>
        PropsPanel.Children.Add(new TextBlock { Text = text, Foreground = (Brush)FindResource("TextSecondaryBrush"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });

    void SwatchRow(string current, Action<string> pick)
    {
        var row = new WrapPanel();
        foreach (var c in Swatches)
        {
            var on = ColorText.WithAlpha(c, 0xFF) == ColorText.WithAlpha(current, 0xFF);
            var swatch = new Button
            {
                Width = 26, Height = 26, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 6),
                Background = ImageIo.Brush(c),
                BorderBrush = on ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("PanelBorderBrush"),
                BorderThickness = new Thickness(on ? 2 : 1),
                ToolTip = c,
            };
            AutomationProperties.SetName(swatch, $"Colour {c}");
            swatch.Click += (_, _) => { pick(c); Board.Focus(); };
            row.Children.Add(swatch);
        }
        PropsPanel.Children.Add(row);
    }

    void SliderRow(string label, double min, double max, double value, Action<double> changed)
    {
        static string Show(double v) => v.ToString(v % 1 == 0 ? "0" : "0.0", CultureInfo.CurrentCulture);
        var valueText = new TextBlock { Text = Show(value), Foreground = (Brush)FindResource("TextSecondaryBrush") };
        var header = new DockPanel { Margin = new Thickness(0, 8, 0, 4) };
        DockPanel.SetDock(valueText, Dock.Right);
        header.Children.Add(valueText);
        header.Children.Add(new TextBlock { Text = label, Foreground = (Brush)FindResource("TextSecondaryBrush") });
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), SmallChange = 0.5, LargeChange = 2 };
        AutomationProperties.SetName(slider, label);
        slider.ValueChanged += (_, e) =>
        {
            var v = Math.Round(e.NewValue, 1);
            valueText.Text = Show(v);
            changed(v);
        };
        PropsPanel.Children.Add(header);
        PropsPanel.Children.Add(slider);
    }

    /// <summary>Edits one step's value; Enter or leaving the box commits, anything that isn't a number is ignored.</summary>
    void StepValueRow(StepAnnotation step)
    {
        Label("Value");
        var box = new TextBox { Text = step.Number.ToString(CultureInfo.InvariantCulture), Width = 80, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(box, "Step value");
        void Commit()
        {
            if (long.TryParse(box.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var v))
            {
                var n = (int)Math.Clamp(v, 1, 9999);
                if (Board.State.Annotations.OfType<StepAnnotation>().FirstOrDefault(x => x.Id == step.Id)?.Number != n)
                    Board.Apply(s => s.SetStepNumber(step.Id, n));
            }
            // Show the number the step really has, whatever was typed.
            var now = Board.State.Annotations.OfType<StepAnnotation>().FirstOrDefault(x => x.Id == step.Id);
            if (now is not null) box.Text = now.Number.ToString(CultureInfo.InvariantCulture);
        }
        box.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { Commit(); e.Handled = true; } };
        box.LostKeyboardFocus += (_, _) => Commit();
        PropsPanel.Children.Add(box);
    }

    void ChoiceRow<T>(string label, (string Text, T Value)[] choices, T current, Action<T> pick)
    {
        Label(label);
        var row = new WrapPanel();
        foreach (var (text, value) in choices)
        {
            var on = EqualityComparer<T>.Default.Equals(value, current);
            var button = new Button
            {
                Content = text, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 4, 10, 4),
                BorderBrush = on ? (Brush)FindResource("AccentBrush") : Brushes.Transparent,
                BorderThickness = new Thickness(on ? 2 : 0),
            };
            button.Click += (_, _) => pick(value);
            row.Children.Add(button);
        }
        PropsPanel.Children.Add(row);
    }

    void CheckRow(string label, bool value, Action<bool> changed)
    {
        var box = new CheckBox { Content = label, IsChecked = value };
        // Checked/Unchecked (not Click) so keyboard, mouse and UI Automation toggles all apply.
        box.Checked += (_, _) => { changed(true); Board.Focus(); };
        box.Unchecked += (_, _) => { changed(false); Board.Focus(); };
        PropsPanel.Children.Add(box);
    }

    void ButtonRow(params (string Text, Action Click)[] buttons)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var (text, click) in buttons)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 6) };
            b.Click += (_, _) => { click(); Board.Focus(); };
            row.Children.Add(b);
        }
        PropsPanel.Children.Add(row);
    }
}
