using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Snapr.App.Rendering;
using Snapr.App.Services;
using Snapr.Core;
using Snapr.App.Platform;

namespace Snapr.App.Dialogs;

public sealed record RedactResult(IReadOnlyList<Rct> Boxes, RedactLook Look);

/// <summary>Smart Redact (spec parity 5 §4): reads the text once, lists what matches, returns the boxes to cover.</summary>
public sealed class RedactWindow : Window
{
    internal static readonly (string Color, string Name)[] Colours =
        [("#FF000000", "Black"), ("#FFFFFFFF", "White"), ("#FFFF3B30", "Red"), ("#FF3B82F6", "Blue"), ("#FF34C759", "Green")];

    readonly Func<Task<IReadOnlyList<TextLine>>> _read;
    readonly CheckBox _email = Check("Email addresses"), _web = Check("Web addresses"), _card = Check("Card numbers"), _phone = Check("Phone numbers");
    readonly TextBox _words = new() { Margin = new Thickness(0, 4, 0, 0) };
    readonly TextBlock _status = new() { Text = "Reading text…", Margin = new Thickness(0, 8, 0, 4), TextWrapping = TextWrapping.Wrap };
    readonly StackPanel _list = new();
    readonly RadioButton _solid = new() { Content = "Solid block", IsChecked = true, GroupName = "look", VerticalContentAlignment = VerticalAlignment.Center };
    readonly RadioButton _pixelate = new() { Content = "Pixelate", GroupName = "look", Margin = new Thickness(12, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };
    readonly WrapPanel _colours = new() { Margin = new Thickness(0, 6, 0, 0) };
    readonly Button _ok;
    readonly HashSet<string> _unticked = [];
    IReadOnlyList<TextLine> _lines = [];
    IReadOnlyList<RedactMatch> _matches = [];
    readonly bool _largeImage;
    bool _hasRead;
    string _colour = Colours[0].Color;
    readonly List<(string Color, string Name, Button Button)> _swatches = [];

    public RedactResult? Result { get; private set; }

    /// <summary>True when Windows has no OCR language; the caller shows the same help as Grab Text.</summary>
    public bool OcrMissing { get; private set; }

    public RedactWindow(Window owner, Func<Task<IReadOnlyList<TextLine>>> read, bool largeImage = false, RedactLook? look = null)
    {
        DarkTitleBar.Attach(this);
        Owner = owner;
        Title = "Redact";
        Style = (Style)Application.Current.FindResource("SnaprWindow");
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        _read = read;
        _largeImage = largeImage;

        _ok = new Button { Content = "Redact", IsDefault = true, IsEnabled = false, Style = (Style)Application.Current.FindResource("AccentButton") };
        _ok.Click += (_, _) =>
        {
            Result = new RedactResult(Ticked().Select(m => m.Box).ToArray(), new RedactLook(_pixelate.IsChecked == true, _colour));
            DialogResult = true;
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        foreach (var c in new[] { _email, _web, _card, _phone })
        {
            c.Checked += (_, _) => Refresh();
            c.Unchecked += (_, _) => Refresh();
        }
        _words.TextChanged += (_, _) => Refresh();
        _solid.Checked += (_, _) => _colours.IsEnabled = true;
        _pixelate.Checked += (_, _) => _colours.IsEnabled = false;
        _words.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) e.Handled = true; };
        if (look is { } l)
        {
            _pixelate.IsChecked = l.Pixelate;
            _solid.IsChecked = !l.Pixelate;
            if (Colours.Any(c => c.Color == l.Color)) _colour = l.Color;
        }
        _colours.IsEnabled = _solid.IsChecked == true;
        BuildColours();

        var find = new WrapPanel();
        foreach (var c in new[] { _email, _web, _card, _phone }) find.Children.Add(c);
        var looks = new StackPanel { Orientation = Orientation.Horizontal };
        looks.Children.Add(_solid);
        looks.Children.Add(_pixelate);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(_ok);

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(Header("Find"));
        root.Children.Add(find);
        root.Children.Add(new TextBlock { Text = "Words or phrases (separate with commas)", Margin = new Thickness(0, 8, 0, 0) });
        root.Children.Add(_words);
        root.Children.Add(Header("Found"));
        root.Children.Add(_status);
        root.Children.Add(new ScrollViewer { Content = _list, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        root.Children.Add(Header("Look"));
        root.Children.Add(looks);
        root.Children.Add(_colours);
        root.Children.Add(new TextBlock
        {
            Text = "Covers are applied when you export, copy or print. The .snapr file keeps the original capture, so share an exported image. For text Snapr can't read, select it with the Selection tool (R) and choose Cover selection.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
            Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush"),
        });
        root.Children.Add(buttons);
        Content = root;

        Loaded += async (_, _) => await ReadAsync();
    }

    static CheckBox Check(string text) => new() { Content = text, IsChecked = true, Margin = new Thickness(0, 4, 16, 4) };

    static TextBlock Header(string text) => new() { Text = text.ToUpperInvariant(), Style = (Style)Application.Current.FindResource("SectionHeader") };

    async Task ReadAsync()
    {
        try
        {
            _lines = await _read();
        }
        catch (OcrUnavailableException)
        {
            OcrMissing = true;
            DialogResult = false;
            return;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Write("Redact: reading text failed", ex);
            _status.Text = $"Snapr couldn't read the text: {ex.Message}";
            return;
        }
        _hasRead = true;
        Refresh();
    }

    void Refresh()
    {
        if (!_hasRead) return;
        _matches = RedactFinder.Find(_lines, new RedactOptions(_email.IsChecked == true, _web.IsChecked == true, _card.IsChecked == true, _phone.IsChecked == true, _words.Text));
        _list.Children.Clear();
        foreach (var g in _matches.GroupBy(m => (m.Category, m.Text)))
        {
            var key = Key(g.Key.Category, g.Key.Text);
            var count = g.Count();
            // A TextBlock, not a string, so underscores in matched text are not taken as access keys.
            var box = new CheckBox
            {
                Content = new TextBlock { Text = $"{g.Key.Text}  ·  {CategoryName(g.Key.Category)}{(count > 1 ? $"  ×{count}" : "")}" },
                IsChecked = !_unticked.Contains(key),
                Margin = new Thickness(0, 2, 0, 2),
            };
            box.Checked += (_, _) => { _unticked.Remove(key); UpdateOk(); };
            box.Unchecked += (_, _) => { _unticked.Add(key); UpdateOk(); };
            _list.Children.Add(box);
        }
        _status.Text = _lines.Count == 0 ? "No text found." : _matches.Count == 0 ? "Nothing matched." : "Tick what to cover:";
        if (_largeImage) _status.Text += " Large image: small text may be missed.";
        UpdateOk();
    }

    IEnumerable<RedactMatch> Ticked() => _matches.Where(m => !_unticked.Contains(Key(m.Category, m.Text)));

    void UpdateOk() => _ok.IsEnabled = Ticked().Any();

    static string Key(RedactCategory c, string text) => $"{c}\u0001{text}";

    static string CategoryName(RedactCategory c) => c switch
    {
        RedactCategory.Email => "email",
        RedactCategory.WebAddress => "web address",
        RedactCategory.CardNumber => "card number",
        RedactCategory.Phone => "phone",
        _ => "word",
    };

    void BuildColours()
    {
        foreach (var (c, name) in Colours)
        {
            var swatch = new Button { Width = 24, Height = 24, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 0), Background = ImageIo.Brush(c), ToolTip = name };
            swatch.Click += (_, _) => { _colour = c; StyleColours(); };
            _swatches.Add((c, name, swatch));
            _colours.Children.Add(swatch);
        }
        StyleColours();
    }

    void StyleColours()
    {
        foreach (var (c, name, swatch) in _swatches)
        {
            var on = c == _colour;
            swatch.BorderBrush = on ? (Brush)Application.Current.FindResource("AccentBrush") : (Brush)Application.Current.FindResource("PanelBorderBrush");
            swatch.BorderThickness = new Thickness(on ? 2 : 1);
            System.Windows.Automation.AutomationProperties.SetName(swatch, on ? $"{name}, selected" : name);
        }
    }
}
