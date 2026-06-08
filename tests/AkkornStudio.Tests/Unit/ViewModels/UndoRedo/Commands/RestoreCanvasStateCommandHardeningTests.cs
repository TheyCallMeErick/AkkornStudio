using Avalonia;
using AkkornStudio.Nodes;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.UndoRedo;

namespace AkkornStudio.Tests.Unit.ViewModels.UndoRedo.Commands;

public sealed class RestoreCanvasStateCommandHardeningTests
{
    [Fact]
    public void Undo_WhenOriginalObjectsAreMutatedAfterSnapshot_RestoresOriginalSerializedState()
    {
        using var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();

        NodeViewModel source = new("public.orders", [("id", PinDataType.Integer)], new Point(0, 0))
        {
            Alias = "orders_before",
        };
        NodeViewModel target = new(NodeDefinitionRegistry.Get(NodeType.Sum), new Point(220, 0));
        target.Parameters["label"] = "before";
        canvas.Nodes.Add(source);
        canvas.Nodes.Add(target);
        canvas.ConnectPins(source.OutputPins.Single(p => p.Name == "id"), target.InputPins.Single(p => p.Name == "value"));

        var sut = new RestoreCanvasStateCommand(canvas, "Import");

        // Mutate existing object references after snapshot.
        source.Alias = "orders_mutated";
        target.Parameters["label"] = "mutated";

        canvas.Nodes.Clear();
        canvas.Connections.Clear();
        NodeViewModel imported = new("public.customers", [("id", PinDataType.Integer)], new Point(10, 10));
        canvas.Nodes.Add(imported);

        sut.Undo(canvas);

        Assert.Equal(2, canvas.Nodes.Count);
        Assert.Single(canvas.Connections);
        NodeViewModel restoredSource = canvas.Nodes.Single(n => n.Title == source.Title);
        NodeViewModel restoredTarget = canvas.Nodes.Single(n => n.Title == target.Title);
        Assert.Equal("orders_before", restoredSource.Alias);
        Assert.Equal("before", restoredTarget.Parameters["label"]);
        Assert.DoesNotContain(imported, canvas.Nodes);
    }

    [Fact]
    public void Execute_WithAfterSnapshot_ReappliesAfterStateAfterUndo()
    {
        using var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();

        NodeViewModel before = new("public.orders", [("id", PinDataType.Integer)], new Point(0, 0))
        {
            Alias = "before_alias",
        };
        canvas.Nodes.Add(before);

        var sut = new RestoreCanvasStateCommand(canvas, "Import");

        canvas.Nodes.Clear();
        NodeViewModel after = new("public.customers", [("id", PinDataType.Integer)], new Point(20, 20))
        {
            Alias = "after_alias",
        };
        canvas.Nodes.Add(after);
        sut.CaptureAfterState(canvas);

        sut.Execute(canvas); // registration
        sut.Undo(canvas);
        NodeViewModel undone = Assert.Single(canvas.Nodes);
        Assert.Equal("before_alias", undone.Alias);

        sut.Execute(canvas); // redo
        NodeViewModel redone = Assert.Single(canvas.Nodes);
        Assert.Equal("after_alias", redone.Alias);
    }

    [Fact]
    public void Execute_WithoutAfterSnapshot_DoesNothingOnSubsequentCalls()
    {
        using var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();

        NodeViewModel node = new("public.orders", [("id", PinDataType.Integer)], new Point(0, 0));
        canvas.Nodes.Add(node);
        var sut = new RestoreCanvasStateCommand(canvas, "Import");

        sut.Execute(canvas); // registration
        sut.Execute(canvas); // no after snapshot available

        Assert.Single(canvas.Nodes);
        Assert.Equal(node.Title, canvas.Nodes[0].Title);
    }

    [Fact]
    public void Execute_WhenAfterSnapshotIsCorrupted_ThrowsRestoreError()
    {
        using var canvas = new CanvasViewModel();
        NodeViewModel node = new("public.orders", [("id", PinDataType.Integer)], new Point(0, 0));
        canvas.Nodes.Add(node);

        var sut = new RestoreCanvasStateCommand(canvas, "Import");
        sut.Execute(canvas); // registration

        typeof(RestoreCanvasStateCommand)
            .GetField("_afterCanvasJson", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(sut, "{ invalid json");
        typeof(RestoreCanvasStateCommand)
            .GetField("_hasAfterSnapshot", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(sut, true);

        var ex = Assert.Throws<InvalidOperationException>(() => sut.Execute(canvas));
        Assert.Contains("Failed to restore canvas snapshot", ex.Message, StringComparison.Ordinal);
    }
}
