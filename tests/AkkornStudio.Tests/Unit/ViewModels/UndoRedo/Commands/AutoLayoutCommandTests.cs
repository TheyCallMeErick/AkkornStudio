using Avalonia;
using AkkornStudio.Nodes;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.UndoRedo.Commands;

namespace AkkornStudio.Tests.Unit.ViewModels.UndoRedo.Commands;

public sealed class AutoLayoutCommandTests
{
    [Fact]
    public void Constructor_WithEmptyScope_ProducesNoMovesAndNoThrow()
    {
        using var canvas = new CanvasViewModel();
        var sut = new AutoLayoutCommand(canvas, []);

        Assert.Contains("0 node(s) repositioned", sut.Description, StringComparison.Ordinal);
        sut.Execute(canvas);
        sut.Undo(canvas);
    }

    [Fact]
    public void ExecuteUndo_WithMixedNodeTypes_RunsAndRestoresOriginalPositions()
    {
        using var canvas = new CanvasViewModel();
        NodeViewModel source = new("public.orders", [("id", PinDataType.Integer)], new Point(340, 280));
        NodeViewModel result = new(NodeDefinitionRegistry.Get(NodeType.ResultOutput), new Point(40, 40));
        canvas.Nodes.Add(source);
        canvas.Nodes.Add(result);

        Point sourceBefore = source.Position;
        Point resultBefore = result.Position;

        var sut = new AutoLayoutCommand(canvas, [source, result]);
        Assert.Contains("node(s) repositioned", sut.Description, StringComparison.Ordinal);

        sut.Execute(canvas);
        sut.Undo(canvas);

        Assert.Equal(sourceBefore, source.Position);
        Assert.Equal(resultBefore, result.Position);
    }

    [Fact]
    public void Constructor_WithNullScope_UsesCanvasNodes()
    {
        using var canvas = new CanvasViewModel();
        NodeViewModel source = new("public.orders", [("id", PinDataType.Integer)], new Point(100, 100));
        canvas.Nodes.Add(source);

        var sut = new AutoLayoutCommand(canvas, null);

        Assert.Contains("1 node(s) repositioned", sut.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecuteUndo_WhenNodeWasDeleted_DoesNotTouchRemovedNode()
    {
        using var canvas = new CanvasViewModel();
        NodeViewModel source = new("public.orders", [("id", PinDataType.Integer)], new Point(200, 120));
        NodeViewModel result = new(NodeDefinitionRegistry.Get(NodeType.ResultOutput), new Point(20, 20));
        canvas.Nodes.Add(source);
        canvas.Nodes.Add(result);

        var sut = new AutoLayoutCommand(canvas, [source, result]);

        canvas.Nodes.Remove(result);

        sut.Execute(canvas);
        sut.Undo(canvas);

        Assert.DoesNotContain(result, canvas.Nodes);
        Assert.Contains(source, canvas.Nodes);
    }
}
