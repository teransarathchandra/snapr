using Snapr.Core;

namespace Snapr.Tests;

public class UndoStackTests
{
    static DocumentState S(int n) => DocumentState.Empty with { NextStepNumber = n };

    [Fact]
    public void Undo_and_redo_walk_history()
    {
        var u = new UndoStack(S(1));
        u.Apply(S(2));
        u.Apply(S(3));

        u.Undo(); Assert.Equal(2, u.Current.NextStepNumber);
        u.Undo(); Assert.Equal(1, u.Current.NextStepNumber);
        Assert.False(u.CanUndo);
        u.Redo(); Assert.Equal(2, u.Current.NextStepNumber);
    }

    [Fact]
    public void Applying_after_undo_clears_redo()
    {
        var u = new UndoStack(S(1));
        u.Apply(S(2));
        u.Undo();
        u.Apply(S(5));
        Assert.False(u.CanRedo);
    }

    [Fact]
    public void Capacity_drops_the_oldest_entry()
    {
        var u = new UndoStack(S(0), capacity: 3);
        for (var i = 1; i <= 5; i++) u.Apply(S(i));
        var seen = 0;
        while (u.CanUndo) { u.Undo(); seen++; }
        Assert.Equal(3, seen);
        Assert.Equal(2, u.Current.NextStepNumber);
    }

    [Fact]
    public void ReplaceCurrent_does_not_add_history()
    {
        var u = new UndoStack(S(1));
        u.Apply(S(2));
        u.ReplaceCurrent(S(3));
        u.Undo();
        Assert.Equal(1, u.Current.NextStepNumber);
    }

    [Fact]
    public void Undo_and_redo_on_empty_history_do_nothing()
    {
        var u = new UndoStack(S(1));
        u.Undo();
        u.Redo();
        Assert.Equal(1, u.Current.NextStepNumber);
    }
}
