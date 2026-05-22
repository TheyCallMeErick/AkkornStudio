using Avalonia;

namespace AkkornStudio.Tests.Unit.ViewModels.PropertyPanel;

public sealed class PropertyPanelSelectedNodeSyncHardeningTests
{
    [Fact]
    public void SynchronizeSelectedNodeParameter_WhenNodeDetachedFromCanvas_ClearsPanelState()
    {
        var canvas = new CanvasViewModel();
        var undo = new UndoRedoStack(canvas);
        var nodes = new List<NodeViewModel>();
        var panel = new PropertyPanelViewModel(undo, nodesResolver: () => nodes);
        var node = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.Join), new Point(0, 0));

        nodes.Add(node);
        panel.ShowNode(node);
        Assert.True(panel.HasNode);

        nodes.Clear();
        panel.SynchronizeSelectedNodeParameter(node, "join_type");

        Assert.False(panel.HasNode);
        Assert.Null(panel.SelectedNode);
        Assert.Empty(panel.Parameters);
    }

    [Fact]
    public void SynchronizeSelectedNodeParameter_WhenNodeStillOnCanvas_UpdatesParameterRow()
    {
        var canvas = new CanvasViewModel();
        var undo = new UndoRedoStack(canvas);
        var nodes = new List<NodeViewModel>();
        var panel = new PropertyPanelViewModel(undo, nodesResolver: () => nodes);
        var node = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.Join), new Point(0, 0));

        node.Parameters["join_type"] = "LEFT";
        nodes.Add(node);
        panel.ShowNode(node);

        node.Parameters["join_type"] = "RIGHT";
        panel.SynchronizeSelectedNodeParameter(node, "join_type");

        ParameterRowViewModel row = Assert.Single(panel.Parameters.Where(p =>
            string.Equals(p.Name, "join_type", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal("RIGHT", row.Value);
    }
}
