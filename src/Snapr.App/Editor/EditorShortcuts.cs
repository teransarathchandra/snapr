using System.Windows.Input;

namespace Snapr.App.Editor;

public enum EditorCommand { Undo, Redo, Copy, Cut, Paste, Delete, Deselect, SaveNow, SaveAsImage, Open, NewFromClipboard, Close, Print, ZoomIn, ZoomOut, ZoomFit, SelectAll, Group, Ungroup }

/// <summary>Editor keyboard shortcuts (spec addendum §2). Unmodified letters are tool keys and are handled by the window.</summary>
public static class EditorShortcuts
{
    const ModifierKeys Ctrl = ModifierKeys.Control, Shift = ModifierKeys.Shift, Alt = ModifierKeys.Alt, None = ModifierKeys.None;

    public static EditorCommand? Resolve(ModifierKeys modifiers, Key key) => (modifiers, key) switch
    {
        (Ctrl, Key.Z) or (Alt, Key.Back) => EditorCommand.Undo,
        (Ctrl, Key.Y) => EditorCommand.Redo,
        (Ctrl, Key.C) or (Ctrl, Key.Insert) => EditorCommand.Copy,
        (Ctrl, Key.X) => EditorCommand.Cut,
        (Ctrl, Key.V) => EditorCommand.Paste,
        (None, Key.Delete) => EditorCommand.Delete,
        (Ctrl, Key.D) or (None, Key.Escape) => EditorCommand.Deselect,
        (Ctrl, Key.S) => EditorCommand.SaveNow,
        (Ctrl | Shift, Key.S) => EditorCommand.SaveAsImage,
        (Ctrl, Key.O) => EditorCommand.Open,
        (Ctrl | Shift, Key.N) => EditorCommand.NewFromClipboard,
        (Ctrl, Key.W) or (Ctrl, Key.F4) => EditorCommand.Close,
        (Ctrl, Key.P) => EditorCommand.Print,
        (Ctrl, Key.OemPlus) or (Ctrl, Key.Add) => EditorCommand.ZoomIn,
        (Ctrl, Key.OemMinus) or (Ctrl, Key.Subtract) => EditorCommand.ZoomOut,
        (Ctrl, Key.D0) or (Ctrl, Key.NumPad0) => EditorCommand.ZoomFit,
        (Ctrl, Key.A) => EditorCommand.SelectAll,
        (Ctrl | Shift, Key.G) => EditorCommand.Group,
        (Ctrl | Shift, Key.U) => EditorCommand.Ungroup,
        _ => null,
    };

    /// <summary>Arrow keys move the selection 1 px, Shift+Arrow 10 px (spec parity 3 §4). Other modifiers: not a nudge.</summary>
    public static (int Dx, int Dy)? Nudge(ModifierKeys modifiers, Key key)
    {
        var step = modifiers switch { None => 1, Shift => 10, _ => 0 };
        if (step == 0) return null;
        return key switch
        {
            Key.Left => (-step, 0),
            Key.Right => (step, 0),
            Key.Up => (0, -step),
            Key.Down => (0, step),
            _ => null,
        };
    }
}
