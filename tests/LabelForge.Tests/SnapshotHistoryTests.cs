using LabelForge.Core.Editing;

namespace LabelForge.Tests;

public sealed class SnapshotHistoryTests
{
    [Fact]
    public void UndoRedo_WalkTheRecordedStates()
    {
        var history = new SnapshotHistory();
        history.Record("a");
        history.Record("b");
        history.Record("c");

        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);

        Assert.Equal("b", history.Undo());
        Assert.Equal("a", history.Undo());
        Assert.False(history.CanUndo);
        Assert.Null(history.Undo());

        Assert.Equal("b", history.Redo());
        Assert.Equal("c", history.Redo());
        Assert.Null(history.Redo());
    }

    [Fact]
    public void Record_AfterUndo_DiscardsTheRedoTail()
    {
        var history = new SnapshotHistory();
        history.Record("a");
        history.Record("b");
        history.Undo();

        history.Record("c");

        Assert.False(history.CanRedo);
        Assert.Equal("a", history.Undo());
        Assert.Equal("c", history.Redo());
    }

    [Fact]
    public void Record_IgnoresIdenticalConsecutiveStates()
    {
        var history = new SnapshotHistory();
        history.Record("a");
        history.Record("a");

        Assert.False(history.CanUndo);
    }

    [Fact]
    public void ReplaceCurrent_CoalescesIntoOneUndoStep()
    {
        var history = new SnapshotHistory();
        history.Record("start");
        history.Record("typing-h");
        history.ReplaceCurrent("typing-he");
        history.ReplaceCurrent("typing-hello");

        Assert.Equal("start", history.Undo());
        Assert.Equal("typing-hello", history.Redo());
    }

    /// <summary>Current follows the cursor, which is what lets undoing back to a saved
    /// state read as unmodified again.</summary>
    [Fact]
    public void Current_FollowsUndoRedoAndReplacement()
    {
        var history = new SnapshotHistory();
        Assert.Null(history.Current);

        history.Record("saved");
        history.Record("edited");
        Assert.Equal("edited", history.Current);

        history.Undo();
        Assert.Equal("saved", history.Current);

        history.Redo();
        history.ReplaceCurrent("edited again");
        Assert.Equal("edited again", history.Current);

        history.Clear();
        Assert.Null(history.Current);
    }

    [Fact]
    public void Capacity_DropsOldestStates()
    {
        var history = new SnapshotHistory(capacity: 3);
        history.Record("a");
        history.Record("b");
        history.Record("c");
        history.Record("d");

        Assert.Equal("c", history.Undo());
        Assert.Equal("b", history.Undo());
        Assert.False(history.CanUndo);
    }
}
