using System.Windows.Input;
using Snapr.App.Editor;

namespace Snapr.Tests;

public class EditorShortcutsTests
{
    const ModifierKeys C = ModifierKeys.Control, S = ModifierKeys.Shift, A = ModifierKeys.Alt, N = ModifierKeys.None;

    [Theory]
    [InlineData(C, Key.Z, EditorCommand.Undo)]
    [InlineData(A, Key.Back, EditorCommand.Undo)]
    [InlineData(C, Key.Y, EditorCommand.Redo)]
    [InlineData(C, Key.C, EditorCommand.Copy)]
    [InlineData(C, Key.Insert, EditorCommand.Copy)]
    [InlineData(C, Key.X, EditorCommand.Cut)]
    [InlineData(C, Key.V, EditorCommand.Paste)]
    [InlineData(N, Key.Delete, EditorCommand.Delete)]
    [InlineData(C, Key.D, EditorCommand.Deselect)]
    [InlineData(N, Key.Escape, EditorCommand.Deselect)]
    [InlineData(C, Key.S, EditorCommand.SaveNow)]
    [InlineData(C | S, Key.S, EditorCommand.SaveAsImage)]
    [InlineData(C, Key.O, EditorCommand.Open)]
    [InlineData(C | S, Key.N, EditorCommand.NewFromClipboard)]
    [InlineData(C, Key.W, EditorCommand.Close)]
    [InlineData(C, Key.F4, EditorCommand.Close)]
    [InlineData(C, Key.P, EditorCommand.Print)]
    [InlineData(C, Key.OemPlus, EditorCommand.ZoomIn)]
    [InlineData(C, Key.Add, EditorCommand.ZoomIn)]
    [InlineData(C, Key.OemMinus, EditorCommand.ZoomOut)]
    [InlineData(C, Key.Subtract, EditorCommand.ZoomOut)]
    [InlineData(C, Key.D0, EditorCommand.ZoomFit)]
    [InlineData(C, Key.NumPad0, EditorCommand.ZoomFit)]
    [InlineData(C, Key.A, EditorCommand.SelectAll)]
    [InlineData(C | S, Key.G, EditorCommand.Group)]
    [InlineData(C | S, Key.U, EditorCommand.Ungroup)]
    [InlineData(C, Key.L, EditorCommand.Library)]
    public void Maps_every_spec_shortcut(ModifierKeys modifiers, Key key, EditorCommand command) =>
        Assert.Equal(command, EditorShortcuts.Resolve(modifiers, key));

    [Theory]
    [InlineData(C | S, Key.Z)]
    [InlineData(N, Key.S)]
    [InlineData(C, Key.G)]
    public void Unlisted_keys_map_to_nothing(ModifierKeys modifiers, Key key) =>
        Assert.Null(EditorShortcuts.Resolve(modifiers, key));

    [Theory]
    [InlineData(N, Key.Left, -1, 0)]
    [InlineData(N, Key.Right, 1, 0)]
    [InlineData(N, Key.Up, 0, -1)]
    [InlineData(N, Key.Down, 0, 1)]
    [InlineData(S, Key.Left, -10, 0)]
    [InlineData(S, Key.Down, 0, 10)]
    public void Arrow_keys_nudge(ModifierKeys modifiers, Key key, int dx, int dy) =>
        Assert.Equal((dx, dy), EditorShortcuts.Nudge(modifiers, key));

    [Theory]
    [InlineData(C, Key.Left)]
    [InlineData(A, Key.Up)]
    [InlineData(C | S, Key.Right)]
    [InlineData(N, Key.A)]
    public void Other_arrow_combinations_do_not_nudge(ModifierKeys modifiers, Key key) =>
        Assert.Null(EditorShortcuts.Nudge(modifiers, key));
}
