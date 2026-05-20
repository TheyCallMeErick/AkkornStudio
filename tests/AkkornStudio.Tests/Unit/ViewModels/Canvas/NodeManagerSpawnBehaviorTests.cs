using Avalonia;
using AkkornStudio.Nodes;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.ViewModels.Canvas;

public sealed class NodeManagerSpawnBehaviorTests
{
    [Fact]
    public void SpawnNode_OnEmptyCanvas_DoesNotThrowAndStartsAtZOrderZero()
    {
        var canvas = new CanvasViewModel();
        NodeDefinition definition = NodeDefinitionRegistry.Get(NodeType.Equals);

        NodeViewModel node = canvas.SpawnNode(definition, new Point(40, 20));

        Assert.Equal(0, node.ZOrder);
        Assert.Equal(node, Assert.Single(canvas.Nodes));
    }

    [Fact]
    public void SpawnNode_AssignsIncrementalZOrder_BasedOnCurrentMax()
    {
        var canvas = new CanvasViewModel();
        NodeDefinition definition = NodeDefinitionRegistry.Get(NodeType.Equals);

        NodeViewModel first = canvas.SpawnNode(definition, new Point(40, 20));
        NodeViewModel second = canvas.SpawnNode(definition, new Point(80, 20));

        Assert.Equal(0, first.ZOrder);
        Assert.Equal(1, second.ZOrder);
    }
}
