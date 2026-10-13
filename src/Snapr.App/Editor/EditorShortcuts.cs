using System.Windows.Input;

namespace Snapr.App.Editor;

public enum EditorCommand { Undo, Redo, Copy, Cut, Paste, Delete, Deselect, SaveNow, SaveAsImage, Open, NewFromClipboard, Close, Print, ZoomIn, ZoomOut, ZoomFit }

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
        _ => null,
    };
}
