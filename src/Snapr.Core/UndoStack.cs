namespace Snapr.Core;

/// <summary>Snapshot-based undo: states are immutable, so history is just a list of earlier states.</summary>
public sealed class UndoStack(DocumentState initial, int capacity = 100)
{
    readonly LinkedList<DocumentState> _undo = new();
    readonly Stack<DocumentState> _redo = new();

    public DocumentState Current { get; private set; } = initial;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Apply(DocumentState next)
    {
        _undo.AddLast(Current);
        if (_undo.Count > capacity) _undo.RemoveFirst();
        _redo.Clear();
        Current = next;
    }

    /// <summary>Updates the current state without a new history entry (used to merge slider drags).</summary>
    public void ReplaceCurrent(DocumentState next)
    {
        _redo.Clear();
        Current = next;
    }

    public void Undo()
    {
        if (_undo.Last is not { } last) return;
        _redo.Push(Current);
        Current = last.Value;
        _undo.RemoveLast();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.AddLast(Current);
        Current = _redo.Pop();
    }
}
