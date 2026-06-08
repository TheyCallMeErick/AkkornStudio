using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.ViewModels.Canvas;

public class CanvasNodeDeletionSelectionHardeningTests
{
    [Fact]
    public void DeleteSelected_WhenSelectedNodeIsRemoved_ClearsPropertyPanelState()
    {
        var canvas = new CanvasViewModel();
        canvas.InitializeDemoNodes();

        NodeViewModel selected = canvas.Nodes[0];
        canvas.SelectNode(selected);
        Assert.Same(selected, canvas.PropertyPanel.SelectedNode);

        canvas.DeleteSelected();

        Assert.DoesNotContain(selected, canvas.Nodes);
        Assert.Null(canvas.PropertyPanel.SelectedNode);
        Assert.False(canvas.PropertyPanel.IsVisible);
    }

    [Fact]
    public void NodesCollectionRemoval_ReconcilesPropertyPanelToRemainingSelection()
    {
        var canvas = new CanvasViewModel();
        canvas.InitializeDemoNodes();

        NodeViewModel first = canvas.Nodes[0];
        NodeViewModel second = canvas.Nodes[1];

        canvas.SelectNode(first);
        canvas.SelectNode(second, add: true);
        Assert.Null(canvas.PropertyPanel.SelectedNode);

        canvas.Nodes.Remove(first);

        Assert.DoesNotContain(first, canvas.Nodes);
        Assert.True(second.IsSelected);
        Assert.Same(second, canvas.PropertyPanel.SelectedNode);
        Assert.True(canvas.PropertyPanel.IsVisible);
    }
}
