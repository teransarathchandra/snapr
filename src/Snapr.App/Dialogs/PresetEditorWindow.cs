using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Snapr.App.Services;
using Snapr.Core;
using Snapr.Core.Settings;

namespace Snapr.App.Dialogs;

/// <summary>Create or edit a capture preset. Non-modal so it can hide itself and its owner while the user drags an area.</summary>
public sealed class PresetEditorWindow : Window
{
    readonly AppController _app;
    readonly Guid _id;
    readonly IReadOnlyList<CapturePreset> _others;
    readonly Func<HotkeySettings> _appHotkeys;
    readonly Action<CapturePreset> _saved;
    Rct? _area;

    readonly TextBox _name = new() { MaxLength = PresetRules.MaxNameLength };
    readonly RadioButton _overlay = Radio("Choose an area each time", "target"), _screen = Radio("Screen under the mouse", "target"),
        _all = Radio("All screens", "target"), _fixedRadio = Radio("This area:", "target");
    readonly TextBlock _areaText = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0), Foreground = Secondary() };
    readonly Slider _delay = new() { Minimum = 0, Maximum = 10, TickFrequency = 1, IsSnapToTickEnabled = true };
    readonly TextBlock _delayValue = new() { Width = 40, TextAlignment = TextAlignment.Right, Foreground = Secondary() };
    readonly RadioButton _edit = Radio("Open in the editor", "action"), _copy = Radio("Copy to clipboard", "action"),
        _grab = Radio("Grab text", "action"), _save = Radio("Save to a folder", "action");
    readonly TextBox _folder = new();
    readonly Button _browse = new() { Content = "Browse…", Margin = new Thickness(6, 0, 0, 0) };
    readonly RadioButton _png = Radio("PNG", "format"), _jpg = Radio("JPG", "format");
    readonly CheckBox _cursor = new() { Content = "Include the mouse pointer" };
    readonly TextBox _hotkey = new() { IsReadOnly = true, IsReadOnlyCaretVisible = false };
    readonly TextBlock _error = new() { Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = Secondary() };
    readonly StackPanel _saveRow = new() { Margin = new Thickness(20, 4, 0, 0) };

    public PresetEditorWindow(Window owner, AppController app, CapturePreset? existing, IReadOnlyList<CapturePreset> others,
        Func<HotkeySettings> appHotkeys, Action<CapturePreset> saved)
    {
        Owner = owner;
        Title = existing is null ? "New preset" : "Edit preset";
        Style = (Style)Application.Current.FindResource("SnaprWindow");
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        _app = app;
        _id = existing?.Id ?? Guid.NewGuid();
        _others = others;
        _appHotkeys = appHotkeys;
        _saved = saved;
        _area = existing?.FixedArea;

        _name.Text = existing?.Name ?? "";
        (existing?.Target switch
        {
            PresetTarget.ScreenUnderMouse => _screen,
            PresetTarget.AllScreens => _all,
            PresetTarget.FixedArea => _fixedRadio,
            _ => _overlay,
        }).IsChecked = true;
        (existing?.Action switch
        {
            PresetAction.Copy => _copy,
            PresetAction.GrabText => _grab,
            PresetAction.SaveToFolder => _save,
            _ => _edit,
        }).IsChecked = true;
        _delay.ValueChanged += (_, e) => _delayValue.Text = $"{(int)e.NewValue} s";
        _delay.Value = existing?.DelaySeconds ?? 0;
        _delayValue.Text = $"{(int)_delay.Value} s";
        _folder.Text = string.IsNullOrWhiteSpace(existing?.SaveFolder) ? CapturePaths.PicturesRoot : existing.SaveFolder;
        (existing?.SaveFormat == "jpg" ? _jpg : _png).IsChecked = true;
        _cursor.IsChecked = existing?.IncludeCursor ?? false;
        _hotkey.Text = existing?.Hotkey ?? "";
        _hotkey.PreviewKeyDown += Hotkey_KeyDown;
        UpdateArea();

        _save.Checked += (_, _) => _saveRow.IsEnabled = true;
        _save.Unchecked += (_, _) => _saveRow.IsEnabled = false;
        _saveRow.IsEnabled = _save.IsChecked == true;
        _browse.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog { FolderName = _folder.Text };
            if (dialog.ShowDialog(this) == true) _folder.Text = dialog.FolderName;
        };

        var areaButton = new Button { Content = "Set area…" };
        areaButton.Click += SetArea_Click;
        var areaRow = new StackPanel { Orientation = Orientation.Horizontal };
        areaRow.Children.Add(_fixedRadio);
        areaRow.Children.Add(_areaText);
        areaRow.Children.Add(areaButton);

        var delayRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(_delayValue, Dock.Right);
        delayRow.Children.Add(_delayValue);
        delayRow.Children.Add(_delay);

        var folderRow = new DockPanel();
        DockPanel.SetDock(_browse, Dock.Right);
        folderRow.Children.Add(_browse);
        folderRow.Children.Add(_folder);
        var formatRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        formatRow.Children.Add(_png);
        formatRow.Children.Add(_jpg);
        _saveRow.Children.Add(folderRow);
        _saveRow.Children.Add(formatRow);

        var ok = new Button { Content = "OK", IsDefault = true, Style = (Style)Application.Current.FindResource("AccentButton") };
        ok.Click += Ok_Click;
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(Header("Name"));
        root.Children.Add(_name);
        root.Children.Add(Header("Capture"));
        root.Children.Add(_overlay);
        root.Children.Add(_screen);
        root.Children.Add(_all);
        root.Children.Add(areaRow);
        root.Children.Add(Header("Delay"));
        root.Children.Add(delayRow);
        root.Children.Add(Header("Then"));
        root.Children.Add(_edit);
        root.Children.Add(_copy);
        root.Children.Add(_grab);
        root.Children.Add(_save);
        root.Children.Add(_saveRow);
        root.Children.Add(new Separator { Margin = new Thickness(0, 10, 0, 6), Opacity = 0 });
        root.Children.Add(_cursor);
        root.Children.Add(Header("Hotkey"));
        root.Children.Add(_hotkey);
        root.Children.Add(new TextBlock { Text = "Click the box and press keys. Backspace clears.", FontSize = 11, Margin = new Thickness(0, 4, 0, 0), Foreground = Secondary() });
        root.Children.Add(_error);
        root.Children.Add(buttons);
        Content = root;
    }

    static RadioButton Radio(string text, string group) => new() { Content = text, GroupName = group, Margin = new Thickness(0, 2, 12, 2), VerticalContentAlignment = VerticalAlignment.Center };

    static Brush Secondary() => (Brush)Application.Current.FindResource("TextSecondaryBrush");

    static TextBlock Header(string text) => new() { Text = text.ToUpperInvariant(), Style = (Style)Application.Current.FindResource("SectionHeader") };

    PresetTarget SelectedTarget() =>
        _screen.IsChecked == true ? PresetTarget.ScreenUnderMouse :
        _all.IsChecked == true ? PresetTarget.AllScreens :
        _fixedRadio.IsChecked == true ? PresetTarget.FixedArea : PresetTarget.Overlay;

    PresetAction SelectedAction() =>
        _copy.IsChecked == true ? PresetAction.Copy :
        _grab.IsChecked == true ? PresetAction.GrabText :
        _save.IsChecked == true ? PresetAction.SaveToFolder : PresetAction.Edit;

    void UpdateArea() => _areaText.Text = _area is { IsEmpty: false } a ? $"{(int)a.W} × {(int)a.H} at {(int)a.X}, {(int)a.Y}" : "not set";

    void Hotkey_KeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Keyboard.Modifiers == ModifierKeys.None && key is Key.Back or Key.Delete) _hotkey.Text = "";
        else if (HotkeyGesture.Format(Keyboard.Modifiers, key) is { } gesture) _hotkey.Text = gesture;
    }

    void ShowError(string message)
    {
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
    }

    async void SetArea_Click(object sender, RoutedEventArgs e)
    {
        var owner = Owner;
        Hide();
        owner.Hide();
        try
        {
            if (await _app.PickAreaAsync() is { } area)
            {
                _area = area;
                _fixedRadio.IsChecked = true;
            }
        }
        finally
        {
            owner.Show();
            Show();
            Activate();
            UpdateArea();
        }
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        var hotkey = _hotkey.Text.Trim();
        if (hotkey.Length > 0 && !HotkeyGesture.TryParse(hotkey, out _, out _)) { ShowError("That hotkey isn't valid. Click the box and press a key combination."); return; }
        var preset = new CapturePreset(_id, _name.Text.Trim(), SelectedTarget(), _area, (int)_delay.Value, SelectedAction(),
            _cursor.IsChecked == true, hotkey, _folder.Text.Trim(), _jpg.IsChecked == true ? "jpg" : "png");
        if (PresetRules.Validate(preset, _others, PresetRules.AppHotkeys(_appHotkeys())) is { } error) { ShowError(error); return; }
        _saved(preset);
        Close();
    }
}
