using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Snapr.App.Editor;
using Snapr.App.Rendering;
using Snapr.Core;
using Snapr.App.Platform;

namespace Snapr.App.Video;

/// <summary>Draws on one video frame with the editor's canvas and tools; Done returns the frame's annotations.</summary>
public sealed class VideoAnnotateWindow : Window
{
    static readonly ToolKind[] Tools = [ToolKind.Move, ToolKind.Arrow, ToolKind.Text, ToolKind.Callout, ToolKind.Shape,
        ToolKind.Pen, ToolKind.Highlight, ToolKind.Step, ToolKind.Stamp];

    readonly EditorCanvas _board = new();
    readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
    readonly StackPanel _shapeRow = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Margin = new Thickness(8, 0, 0, 0) };
    readonly Dictionary<ToolKind, RadioButton> _toolButtons = new();
    readonly List<DocumentState> _undo = new(), _redo = new();   // _undo's last entry is the current state
    readonly int _width, _height;
    string? _lastMergeKey;
    DateTime _lastMergeAt;

    /// <summary>The frame's annotations after Done; null on Cancel.</summary>
    public IReadOnlyList<Annotation>? Result { get; private set; }
    /// <summary>The last drawing style, so the next Annotate starts with it.</summary>
    public ToolStyle DrawStyle { get; private set; }


    public VideoAnnotateWindow(Window? owner, BitmapSource frame, IReadOnlyList<Annotation> visible, ToolStyle style)
    {
        DarkTitleBar.Attach(this);
        Title = "Annotate frame";
        Style = (System.Windows.Style)Application.Current.FindResource("SnaprWindow");
        WindowState = WindowState.Maximized;
        ShowInTaskbar = false;
        if (owner is not null) Owner = owner;
        DrawStyle = style;
        _width = frame.PixelWidth;
        _height = frame.PixelHeight;

        _board.CurrentStyle = style;
        _board.Load(frame, DocumentState.Empty with { Annotations = visible });
        _undo.Add(_board.State);
        _board.Committed += OnCommitted;

        var bar = new DockPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var tool in Tools) left.Children.Add(ToolButton(tool));
        BuildShapeRow();
        left.Children.Add(_shapeRow);
        left.Children.Add(SwatchRow());
        left.Children.Add(SizeSlider());
        left.Children.Add(TextSizeSlider());
        var delete = new Button { Content = "Delete", ToolTip = "Delete the selected objects (Del)" };
        delete.Click += (_, _) => _board.DeleteSelected();
        left.Children.Add(delete);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => DialogResult = false;
        var done = new Button { Content = "Done", IsDefault = true, Style = (System.Windows.Style)Application.Current.FindResource("AccentButton") };
        done.Click += (_, _) => Done();
        right.Children.Add(cancel);
        right.Children.Add(done);
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        bar.Children.Add(left);

        var top = new Border
        {
            Height = 48, Child = bar,
            Background = (Brush)Application.Current.FindResource("PanelBrush"),
            BorderBrush = (Brush)Application.Current.FindResource("PanelBorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        DockPanel.SetDock(top, Dock.Top);
        _scroller.Content = new Border
        {
            Child = _board, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(32), BorderBrush = (Brush)Application.Current.FindResource("PanelBorderBrush"), BorderThickness = new Thickness(1),
        };
        var canvasHost = new Grid { Background = (Brush)Application.Current.FindResource("CanvasBrush") };
        canvasHost.Children.Add(_scroller);
        Content = new DockPanel { Children = { top, canvasHost } };

        _scroller.SizeChanged += (_, _) => ApplyZoom();
        Loaded += (_, _) => { _board.Focus(); Dispatcher.BeginInvoke(ApplyZoom, DispatcherPriority.Loaded); };
        PreviewKeyDown += OnKeyDown;
        _toolButtons[ToolKind.Arrow].IsChecked = true;
    }

    RadioButton ToolButton(ToolKind tool)
    {
        var name = tool.ToString();
        var button = new RadioButton
        {
            Style = (System.Windows.Style)Application.Current.FindResource("ToolButton"), GroupName = "videotools",
            Content = ToolIcons.Create(tool), ToolTip = name,
        };
        AutomationProperties.SetName(button, name);
        button.Checked += (_, _) =>
        {
            _board.CommitTextEditor();
            _board.Tool = tool;
            if (tool != ToolKind.Move) _board.Select(null);
            _shapeRow.Visibility = tool == ToolKind.Shape ? Visibility.Visible : Visibility.Collapsed;
            _board.Focus();
        };
        return _toolButtons[tool] = button;
    }

    void BuildShapeRow()
    {
        foreach (var shape in Enum.GetValues<ShapeOption>())
        {
            var b = new RadioButton { Content = shape.ToString(), GroupName = "videoshape", Margin = new Thickness(0, 0, 8, 0), IsChecked = shape == _board.Options.Shape, VerticalAlignment = VerticalAlignment.Center };
            b.Checked += (_, _) => { ChangeOptions(o => o with { Shape = shape }); _board.Focus(); };
            _shapeRow.Children.Add(b);
        }
    }

    StackPanel SwatchRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach (var (color, name) in EditorPalette.Swatches)
        {
            var b = new Button
            {
                Width = 28, Height = 28, Margin = new Thickness(2, 0, 2, 0), Padding = new Thickness(0), ToolTip = $"Colour {name}",
                Content = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = ImageIo.Brush(color), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) },
            };
            AutomationProperties.SetName(b, $"Colour {name}");
            b.Click += (_, _) => { ChangeStyle(s => s with { Color = color }, null); _board.Focus(); };
            row.Children.Add(b);
        }
        return row;
    }

    StackPanel SizeSlider()
    {
        var slider = new Slider { Minimum = 1, Maximum = 20, Value = Math.Clamp(_board.CurrentStyle.Width, 1, 20), Width = 120, SmallChange = 0.5, LargeChange = 2, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(slider, "Line width");
        slider.ValueChanged += (_, e) => ChangeStyle(s => s with { Width = Math.Round(e.NewValue, 1) }, "width");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
        row.Children.Add(new TextBlock { Text = "Line width", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush") });
        row.Children.Add(slider);
        return row;
    }

    StackPanel TextSizeSlider()
    {
        var slider = new Slider { Minimum = 10, Maximum = 72, Value = Math.Clamp(_board.CurrentStyle.FontSize, 10, 72), Width = 120, SmallChange = 1, LargeChange = 4, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(slider, "Text size");
        slider.ValueChanged += (_, e) => ChangeStyle(s => s with { FontSize = Math.Round(e.NewValue) }, "fontsize");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
        row.Children.Add(new TextBlock { Text = "Text size", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush") });
        row.Children.Add(slider);
        return row;
    }

    // Like the editor's properties panel: with a selection the change edits those objects, otherwise the next ones drawn.
    void ChangeStyle(Func<ToolStyle, ToolStyle> change, string? mergeKey)
    {
        if (_board.SelectedIds.Count > 0)
            _board.ApplyToSelection(a => AnnotationGeometry.WithStyle(a, change(AnnotationGeometry.StyleOf(a, _board.CurrentStyle))), mergeKey);
        else
            _board.CurrentStyle = change(_board.CurrentStyle);
    }

    void ChangeOptions(Func<ToolOptions, ToolOptions> change)
    {
        if (_board.SelectedIds.Count > 0)
            _board.ApplyToSelection(a => AnnotationGeometry.WithOptions(a, change(AnnotationGeometry.OptionsOf(a, _board.Options))));
        else
            _board.Options = change(_board.Options);
    }

    void OnCommitted(DocumentState next, string? mergeKey)
    {
        var now = DateTime.UtcNow;
        if (mergeKey is not null && mergeKey == _lastMergeKey && now - _lastMergeAt < TimeSpan.FromMilliseconds(800))
            _undo[^1] = next;
        else
        {
            _undo.Add(next);
            _redo.Clear();
        }
        _lastMergeKey = mergeKey;
        _lastMergeAt = now;
        _board.SetState(next);   // the canvas does not apply a commit itself, and SetState does not raise Committed
    }

    void Undo()
    {
        _board.CommitTextEditor();
        if (_undo.Count < 2) return;
        _redo.Add(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
        _lastMergeKey = null;
        _board.SetState(_undo[^1]);
    }

    void Redo()
    {
        _board.CommitTextEditor();
        if (_redo.Count == 0) return;
        _undo.Add(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
        _lastMergeKey = null;
        _board.SetState(_undo[^1]);
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;   // the canvas's text box commits on its own Esc/Enter
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (ctrl && e.Key == Key.Z) Undo();
        else if (ctrl && e.Key == Key.Y) Redo();
        else if (e.Key == Key.Delete) _board.DeleteSelected();
        else if (e.Key == Key.Escape) DialogResult = false;
        else return;
        e.Handled = true;
    }

    void Done()
    {
        _board.CommitTextEditor();
        Result = _board.State.Annotations;
        DrawStyle = _board.CurrentStyle;
        DialogResult = true;
    }

    void ApplyZoom()
    {
        if (!IsLoaded) return;
        var pixel = 1 / VisualTreeHelper.GetDpi(this).DpiScaleX;   // 1 image pixel = 1 screen pixel
        var fit = Math.Min((_scroller.ActualWidth - 66 - SystemParameters.VerticalScrollBarWidth) / _width,
            (_scroller.ActualHeight - 66 - SystemParameters.HorizontalScrollBarHeight) / _height);
        var zoom = Math.Max(0.05, Math.Min(pixel, fit));
        _board.Zoom = zoom;   // screen units per image pixel; the canvas divides handle sizes by it
        _board.LayoutTransform = new ScaleTransform(zoom, zoom);
    }
}
