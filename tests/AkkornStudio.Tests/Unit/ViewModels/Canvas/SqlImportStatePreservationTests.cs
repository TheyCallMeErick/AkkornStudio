using AkkornStudio.UI.Services.Canvas.AutoJoin;
using AkkornStudio.UI.Services.Explain;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.Canvas;
using AkkornStudio.UI.ViewModels.UndoRedo;
using Xunit;

namespace AkkornStudio.Tests.Unit.ViewModels.Canvas;

/// <summary>
/// Tests for SQL import functionality and state preservation.
/// Regression tests to ensure import doesn't destroy canvas without undo capability.
/// </summary>
public class SqlImportStatePreservationTests
{
    [Fact]
    public void RestoreCanvasStateCommand_IsICanvasCommand()
    {
        // Verify that RestoreCanvasStateCommand implements ICanvasCommand interface
        var commandType = typeof(RestoreCanvasStateCommand);
        var interfaces = commandType.GetInterfaces();

        Assert.Contains(typeof(ICanvasCommand), interfaces);
    }

    [Fact]
    public void RestoreCanvasStateCommand_HasDescription()
    {
        // Verify that restore command has a proper description
        var canvas = new CanvasViewModel();
        var command = new RestoreCanvasStateCommand(canvas, "Test Operation");

        Assert.NotNull(command.Description);
        Assert.Contains("Test Operation", command.Description);
        Assert.Contains("Undo", command.Description);
    }

    [Fact]
    public void RestoreCanvasStateCommand_CanSnapshotEmptyCanvas()
    {
        // Verify that command can snapshot a canvas without error
        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();

        var command = new RestoreCanvasStateCommand(canvas, "Test");

        Assert.NotNull(command);
        Assert.Empty(canvas.Nodes);
        Assert.Empty(canvas.Connections);
    }

    [Fact]
    public void RestoreCanvasStateCommand_Execute_DoesNothing()
    {
        // Verify that Execute() does nothing (operation already happened)
        // This is by design - the destructive operation (import/clear) happens before
        // this command is registered in the undo stack
        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();

        var node = new NodeViewModel("TestTable", [], new(100, 100));
        canvas.Nodes.Add(node);

        var command = new RestoreCanvasStateCommand(canvas, "Test");

        // Before execute
        Assert.Single(canvas.Nodes);

        // Execute (should do nothing)
        command.Execute(canvas);

        // After execute - should still have the node
        Assert.Single(canvas.Nodes);
    }

    [Fact]
    public void RestoreCanvasStateCommand_Redo_RestoresCapturedAfterState()
    {
        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();

        var before = new NodeViewModel("Before", [], new(10, 10));
        canvas.Nodes.Add(before);

        var command = new RestoreCanvasStateCommand(canvas, "Import");

        // Simulate operation result state
        canvas.Nodes.Clear();
        var after = new NodeViewModel("After", [], new(20, 20));
        canvas.Nodes.Add(after);

        command.CaptureAfterState(canvas);

        // Registration call (first execute) should keep current state
        command.Execute(canvas);
        Assert.Single(canvas.Nodes);
        Assert.Same(after, canvas.Nodes[0]);

        // Undo goes back to before-state
        command.Undo(canvas);
        var undone = Assert.Single(canvas.Nodes);
        Assert.Equal(before.Title, undone.Title);
        Assert.NotSame(before, undone);

        // Redo reapplies captured after-state
        command.Execute(canvas);
        var redone = Assert.Single(canvas.Nodes);
        Assert.Equal(after.Title, redone.Title);
        Assert.NotSame(after, redone);
    }

    [Fact]
    public void RestoreCanvasStateCommand_Undo_RestoresSnapshotState()
    {
        // Verify that Undo restores the pre-operation state
        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();

        // Add initial nodes
        var node1 = new NodeViewModel("Table1", [], new(100, 100));
        var node2 = new NodeViewModel("Table2", [], new(200, 200));
        canvas.Nodes.Add(node1);
        canvas.Nodes.Add(node2);

        Assert.Equal(2, canvas.Nodes.Count);

        // Create command that snapshots this state
        var command = new RestoreCanvasStateCommand(canvas, "Before Import");

        // Simulate import: clear canvas and add new node
        canvas.Nodes.Clear();
        var importedNode = new NodeViewModel("ImportedTable", [], new(150, 150));
        canvas.Nodes.Add(importedNode);

        Assert.Single(canvas.Nodes);

        // Undo should restore the pre-import state (the 2 original nodes)
        command.Undo(canvas);

        Assert.Equal(2, canvas.Nodes.Count);
    }

    [Fact]
    public void RegressionTest_SqlImportDoesNotLeaveCanvasInInconsistentState()
    {
        // Regression test for: "SQL Import destrÃ³i canvas sem possibilidade de undo"
        // Previously: import would clear the canvas with no way to get it back
        // Now: RestoreCanvasStateCommand allows restoring pre-import state via Ctrl+Z

        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();

        // Initial state: add some nodes
        var initialNode = new NodeViewModel("InitialTable", [], new(100, 100));
        canvas.Nodes.Add(initialNode);

        Assert.Single(canvas.Nodes);

        // Create restore command that snapshots current state
        var restoreCommand = new RestoreCanvasStateCommand(canvas, "SQL Import");

        // Simulate import: clear and add new nodes
        canvas.Nodes.Clear();
        var importedNode = new NodeViewModel("ImportedTable", [], new(200, 200));
        canvas.Nodes.Add(importedNode);

        Assert.Single(canvas.Nodes);

        // User can now undo import by calling Undo() on the restore command
        restoreCommand.Undo(canvas);

        Assert.Single(canvas.Nodes);
        // Should have the original node back
    }

    [Fact]
    public void RestoreCanvasStateCommand_UndoRestoresSnapshotState()
    {
        // Verify that Undo restores the pre-operation state correctly
        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();

        // Create initial state: 1 node
        var node = new NodeViewModel("TestTable", [], new(100, 100));
        canvas.Nodes.Add(node);
        Assert.Single(canvas.Nodes);

        // Create command that snapshots this state
        var command = new RestoreCanvasStateCommand(canvas, "Test Operation");

        // Simulate destructive operation: clear the canvas
        canvas.Nodes.Clear();
        Assert.Empty(canvas.Nodes);

        // Undo should restore the saved state
        command.Undo(canvas);
        var restored = Assert.Single(canvas.Nodes);
        Assert.Equal(node.Title, restored.Title);
        Assert.NotSame(node, restored);
    }

    [Fact]
    public void RestoreCanvasStateCommand_ImplementsExecuteMethod()
    {
        // Verify the command has the Execute method required by ICanvasCommand
        var commandType = typeof(RestoreCanvasStateCommand);
        var executeMethod = commandType.GetMethod("Execute",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(executeMethod);
    }

    [Fact]
    public void RestoreCanvasStateCommand_ImplementsUndoMethod()
    {
        // Verify the command has the Undo method required by ICanvasCommand
        var commandType = typeof(RestoreCanvasStateCommand);
        var undoMethod = commandType.GetMethod("Undo",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(undoMethod);
    }

    [Fact]
    public void RegressionTest_MultipleImportsCanBeUndoneSequentially()
    {
        // Regression test: verify sequential imports can be undone in reverse order

        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();

        // First state: table1
        var node1 = new NodeViewModel("Table1", [], new(100, 100));
        canvas.Nodes.Add(node1);
        var restore1 = new RestoreCanvasStateCommand(canvas, "Import 1");

        // Simulate import 2: clear table1, add table2
        canvas.Nodes.Clear();
        var node2 = new NodeViewModel("Table2", [], new(100, 100));
        canvas.Nodes.Add(node2);
        var restore2 = new RestoreCanvasStateCommand(canvas, "Import 2");

        // Now simulate undo sequence
        // Undo import 2 should restore table2 state
        restore2.Undo(canvas);
        Assert.Single(canvas.Nodes);

        // Undo import 1 should restore table1 state
        restore1.Undo(canvas);
        Assert.Single(canvas.Nodes);
    }

    [Fact]
    public void RestoreCanvasStateCommand_IsReusable()
    {
        // Verify that the same restore command can be undone multiple times
        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();

        var node = new NodeViewModel("Test", [], new(100, 100));
        canvas.Nodes.Add(node);

        var command = new RestoreCanvasStateCommand(canvas, "Test");

        // First undo: restore state
        canvas.Nodes.Clear();
        command.Undo(canvas);
        Assert.Single(canvas.Nodes);

        // Second undo: restore state again
        canvas.Nodes.Clear();
        command.Undo(canvas);
        Assert.Single(canvas.Nodes);

        // Third undo: restore state again
        canvas.Nodes.Clear();
        command.Undo(canvas);
        Assert.Single(canvas.Nodes);
    }

    [Fact]
    public void RestoreCanvasStateCommand_WithCustomDescription()
    {
        // Verify that custom descriptions are preserved
        var canvas = new CanvasViewModel();
        var command1 = new RestoreCanvasStateCommand(canvas, "Custom Operation");
        var command2 = new RestoreCanvasStateCommand(canvas, "SQL Import");

        Assert.Contains("Custom Operation", command1.Description);
        Assert.Contains("SQL Import", command2.Description);
        Assert.NotEqual(command1.Description, command2.Description);
    }

    [Fact]
    public void RegressionTest_RestoreCommandRestoresEquivalentNodeState()
    {
        // Regression test: restored snapshot keeps equivalent state (aliases/params),
        // even though node instances are reconstructed.
        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();

        var originalNode = new NodeViewModel("Table", [], new(100, 100));
        originalNode.Alias = "table_alias";
        originalNode.Parameters["foo"] = "bar";
        canvas.Nodes.Add(originalNode);

        var command = new RestoreCanvasStateCommand(canvas, "Test");

        // Simulate clear
        canvas.Nodes.Clear();

        // Undo (restore)
        command.Undo(canvas);

        // Restored node is equivalent state, not same reference
        var restoredNode = Assert.Single(canvas.Nodes);
        Assert.NotSame(originalNode, restoredNode);
        Assert.Equal("table_alias", restoredNode.Alias);
        Assert.Equal("bar", restoredNode.Parameters["foo"]);
    }
}

