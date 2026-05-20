using AkkornStudio.UI.Services.Benchmark;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.UndoRedo;
using AkkornStudio.UI.Services.Localization;
using Xunit;

namespace AkkornStudio.Tests.Unit.ViewModels;

/// <summary>
/// Tests for UndoRedoStack to verify correct order preservation,
/// especially during history trimming (regression test for historic bug).
/// </summary>
public class UndoRedoStackTests
{
    // â”€â”€ Mock ICanvasCommand â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private class MockCommand(string description) : ICanvasCommand
    {
        public string Description => description;

        public void Execute(CanvasViewModel canvas) { }
        public void Undo(CanvasViewModel canvas) { }

        public ICanvasCommand? TryMerge(ICanvasCommand other) => null;
    }

    private sealed class StateCommand(List<int> target, int value) : ICanvasCommand
    {
        public string Description => $"Add {value}";

        public void Execute(CanvasViewModel canvas) => target.Add(value);

        public void Undo(CanvasViewModel canvas)
        {
            if (target.Count > 0)
                target.RemoveAt(target.Count - 1);
        }

        public ICanvasCommand? TryMerge(ICanvasCommand other) => null;
    }

    private sealed class PartiallyFailingStateCommand(List<int> target, int value) : ICanvasCommand
    {
        public string Description => $"Failing add {value}";

        public void Execute(CanvasViewModel canvas)
        {
            target.Add(value);
            throw new InvalidOperationException("Simulated execute failure after side-effect.");
        }

        public void Undo(CanvasViewModel canvas)
        {
            int index = target.LastIndexOf(value);
            if (index >= 0)
                target.RemoveAt(index);
        }

        public ICanvasCommand? TryMerge(ICanvasCommand other) => null;
    }

    private sealed class MergeableCommand(string description, string mergeOnDescription) : ICanvasCommand
    {
        public string Description => description;

        public void Execute(CanvasViewModel canvas) { }

        public void Undo(CanvasViewModel canvas) { }

        public ICanvasCommand? TryMerge(ICanvasCommand other)
        {
            return string.Equals(other.Description, mergeOnDescription, StringComparison.Ordinal)
                ? new MockCommand("Merged")
                : null;
        }
    }

    // â”€â”€ Tests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Undo_WithSingleCommand_RemovesFromStack()
    {
        var canvas = new CanvasViewModel();
        var stack = new UndoRedoStack(canvas);
        var cmd = new MockCommand("Test Command");

        stack.Execute(cmd);
        Assert.True(stack.CanUndo);
        Assert.Equal("Test Command", stack.UndoDescription);

        stack.Undo();
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public void Execute_MultipleCommands_PreservesOrder()
    {
        var canvas = new CanvasViewModel();
        var stack = new UndoRedoStack(canvas);

        var cmd1 = new MockCommand("First");
        var cmd2 = new MockCommand("Second");
        var cmd3 = new MockCommand("Third");

        stack.Execute(cmd1);
        stack.Execute(cmd2);
        stack.Execute(cmd3);

        // Undo order should be LIFO: Third, Second, First
        Assert.Equal("Third", stack.UndoDescription);

        stack.Undo();
        Assert.Equal("Second", stack.UndoDescription);

        stack.Undo();
        Assert.Equal("First", stack.UndoDescription);
    }

    [Fact]
    public void TrimHistory_KeepsMostRecentCommands()
    {
        var canvas = new CanvasViewModel();
        var stack = new UndoRedoStack(canvas);

        // Execute 25 commands
        for (int i = 1; i <= 25; i++)
        {
            stack.Execute(new MockCommand($"Cmd{i:D3}"));
        }

        // After executing, we should still be able to undo the most recent commands
        // and the MOST RECENT should be last in undo stack (LIFO order)
        Assert.Equal("Cmd025", stack.UndoDescription);

        stack.Undo();
        Assert.Equal("Cmd024", stack.UndoDescription);

        stack.Undo();
        Assert.Equal("Cmd023", stack.UndoDescription);
    }

    [Fact]
    public void Redo_AfterUndo_RestoresCommand()
    {
        var canvas = new CanvasViewModel();
        var stack = new UndoRedoStack(canvas);

        var cmd = new MockCommand("Test");
        stack.Execute(cmd);
        stack.Undo();

        Assert.True(stack.CanRedo);
        Assert.Equal("Test", stack.RedoDescription);

        stack.Redo();
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void UndoDescription_EmptyWhenNoCommands()
    {
        var canvas = new CanvasViewModel();
        var stack = new UndoRedoStack(canvas);

        Assert.Equal(string.Empty, stack.UndoDescription);
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public void RedoDescription_EmptyWhenNoRedoAvailable()
    {
        var canvas = new CanvasViewModel();
        var stack = new UndoRedoStack(canvas);

        Assert.Equal(string.Empty, stack.RedoDescription);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Transaction_BuffersCommands()
    {
        var canvas = new CanvasViewModel();
        var stack = new UndoRedoStack(canvas);

        var cmd1 = new MockCommand("Tx1");
        var cmd2 = new MockCommand("Tx2");

        stack.BeginTransaction("Composite");
        stack.Execute(cmd1);
        stack.Execute(cmd2);
        stack.CommitTransaction();

        // Should have one composite entry in undo stack
        Assert.Equal("Composite", stack.UndoDescription);

        stack.Undo();
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public void RollbackTransaction_RevertsBufferedSideEffects()
    {
        var canvas = new CanvasViewModel();
        var stack = new UndoRedoStack(canvas);
        var values = new List<int>();

        stack.BeginTransaction("Tx");
        stack.Execute(new StateCommand(values, 1));
        stack.Execute(new StateCommand(values, 2));

        Assert.Equal([1, 2], values);

        stack.RollbackTransaction();

        Assert.Empty(values);
        Assert.False(stack.InTransaction);
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public void RollbackTransaction_AddsDiagnosticsWarningEntry()
    {
        var canvas = new CanvasViewModel();
        var stack = new UndoRedoStack(canvas);
        var values = new List<int>();

        int before = canvas.Diagnostics.SnapshotEntries().Count;

        stack.BeginTransaction("Bulk Move");
        stack.Execute(new StateCommand(values, 1));

        stack.RollbackTransaction();

        IReadOnlyList<AppDiagnosticEntry> entries = canvas.Diagnostics.SnapshotEntries();
        string expectedArea = LocalizationService.Instance["diagnostics.area.undoRedoTransaction"];
        Assert.True(entries.Count > before);
        Assert.Contains(
            entries,
            e =>
                e.Name == expectedArea
                && e.Details.Contains("Bulk Move", StringComparison.Ordinal)
                && e.Status == DiagnosticStatus.Warning
        );
    }

    [Fact]
    public void BeginTransaction_WhenAlreadyOpen_ThrowsInvalidOperation()
    {
        var canvas = new CanvasViewModel();
        var stack = new UndoRedoStack(canvas);
        stack.BeginTransaction("First");

        Assert.Throws<InvalidOperationException>(() => stack.BeginTransaction("Second"));
    }

    [Fact]
    public void TransactionScope_DisposeWithoutCommit_RollsBack()
    {
        var canvas = new CanvasViewModel();
        var stack = new UndoRedoStack(canvas);
        var values = new List<int>();

        using (stack.BeginTransaction("Scoped"))
        {
            stack.Execute(new StateCommand(values, 1));
            stack.Execute(new StateCommand(values, 2));
        }

        Assert.Empty(values);
        Assert.False(stack.InTransaction);
    }

    [Fact]
    public void TransactionScope_Commit_PersistsAsSingleUndoEntry()
    {
        var canvas = new CanvasViewModel();
        var stack = new UndoRedoStack(canvas);
        var values = new List<int>();

        using var tx = stack.BeginTransaction("Scoped");
        stack.Execute(new StateCommand(values, 1));
        stack.Execute(new StateCommand(values, 2));
        tx.Commit();

        Assert.Equal([1, 2], values);
        Assert.True(stack.CanUndo);
        Assert.Equal("Scoped", stack.UndoDescription);

        stack.Undo();
        Assert.Empty(values);
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public void RollbackTransaction_RevertsPartiallyAppliedCommand_WhenExecuteFailsDuringTransaction()
    {
        var canvas = new CanvasViewModel();
        var stack = new UndoRedoStack(canvas);
        var values = new List<int>();

        stack.BeginTransaction("Tx Failure");
        stack.Execute(new StateCommand(values, 1));

        Assert.Throws<InvalidOperationException>(() =>
            stack.Execute(new PartiallyFailingStateCommand(values, 2)));

        // Side-effect happened before exception.
        Assert.Equal([1, 2], values);

        stack.RollbackTransaction();

        Assert.Empty(values);
        Assert.False(stack.InTransaction);
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public void Clear_ErasesAllHistory()
    {
        var canvas = new CanvasViewModel();
        var stack = new UndoRedoStack(canvas);

        stack.Execute(new MockCommand("Cmd1"));
        stack.Execute(new MockCommand("Cmd2"));

        stack.Clear();

        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
        Assert.Equal(string.Empty, stack.UndoDescription);
    }

    [Fact]
    public void Execute_ClearsRedoStack()
    {
        var canvas = new CanvasViewModel();
        var stack = new UndoRedoStack(canvas);

        var cmd1 = new MockCommand("First");
        var cmd2 = new MockCommand("Second");

        stack.Execute(cmd1);
        stack.Execute(cmd2);
        stack.Undo();

        Assert.True(stack.CanRedo);

        // Execute a new command should clear redo
        stack.Execute(new MockCommand("Third"));
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Execute_WhenTryMergeReturnsCommand_ReplacesTopUndoEntry()
    {
        var canvas = new CanvasViewModel();
        var stack = new UndoRedoStack(canvas);

        stack.Execute(new MergeableCommand("First", "Second"));
        stack.Execute(new MockCommand("Second"));

        Assert.True(stack.CanUndo);
        Assert.Equal("Merged", stack.UndoDescription);
        Assert.Single(stack.UndoHistory);
        Assert.Equal("Merged", stack.UndoHistory[0]);
    }

    [Fact]
    public void UndoHistory_ContainsAllCommands()
    {
        var canvas = new CanvasViewModel();
        var stack = new UndoRedoStack(canvas);

        stack.Execute(new MockCommand("Cmd1"));
        stack.Execute(new MockCommand("Cmd2"));
        stack.Execute(new MockCommand("Cmd3"));

        var history = stack.UndoHistory;
        Assert.Equal(3, history.Count);
        Assert.Equal("Cmd1", history[0]);
        Assert.Equal("Cmd2", history[1]);
        Assert.Equal("Cmd3", history[2]);
    }

    [Fact]
    public void UndoHistory_ReusesSnapshot_WhenHistoryUnchanged()
    {
        var canvas = new CanvasViewModel();
        var stack = new UndoRedoStack(canvas);

        stack.Execute(new MockCommand("Cmd1"));

        var firstRead = stack.UndoHistory;
        var secondRead = stack.UndoHistory;

        Assert.Same(firstRead, secondRead);
    }

    [Fact]
    public void HistoryTrim_RegressionTest_MaintainsCorrectOrder()
    {
        // This is a regression test for the bug where TakeLast wasn't used
        // Instead of Take().Reverse(), causing oldest commands to become newest
        var canvas = new CanvasViewModel();
        var stack = new UndoRedoStack(canvas);

        // Execute commands that will exceed MaxHistory (200)
        // and verify that after trim, the order is still correct
        for (int i = 1; i <= 220; i++)
        {
            stack.Execute(new MockCommand($"Cmd{i:D3}"));
        }

        // After trim, we should have only the last 200 commands
        // The most recent (Cmd220) should be undoable first
        Assert.Equal("Cmd220", stack.UndoDescription);

        // When we undo, we should get Cmd219, not Cmd001
        stack.Undo();
        Assert.Equal("Cmd219", stack.UndoDescription);

        // Continue undoing and verify we get the right sequence
        stack.Undo();
        Assert.Equal("Cmd218", stack.UndoDescription);
    }
}
