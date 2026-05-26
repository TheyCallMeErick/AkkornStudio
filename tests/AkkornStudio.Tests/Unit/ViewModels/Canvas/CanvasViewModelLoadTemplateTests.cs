using AkkornStudio.UI.Services.Canvas.AutoJoin;
using AkkornStudio.UI.Services.Explain;
using Avalonia;
using AkkornStudio.Nodes;
using AkkornStudio.UI.ViewModels;
using Xunit;

namespace AkkornStudio.Tests.Unit.ViewModels.Canvas;

public class CanvasViewModelLoadTemplateTests
{
    [Fact]
    public void LoadTemplate_ResetsCanvasState_AndBuildsTemplateGraph()
    {
        var canvas = new CanvasViewModel();

        canvas.SpawnNode(NodeDefinitionRegistry.Get(NodeType.Equals), new Point(120, 80));
        canvas.CurrentFilePath = "query.vsa";
        canvas.QueryText = "SELECT 1";
        canvas.Zoom = 1.75;
        canvas.PanOffset = new Point(45, 90);
        canvas.IsDirty = true;

        QueryTemplate template = QueryTemplateLibrary.All.First(t => t.Name == "Simple SELECT");
        canvas.LoadTemplate(template);

        Assert.Null(canvas.CurrentFilePath);
        Assert.Equal(string.Empty, canvas.QueryText);
        Assert.Equal(1.0, canvas.Zoom);
        Assert.Equal(new Point(0, 0), canvas.PanOffset);
        Assert.False(canvas.IsDirty);

        Assert.NotEmpty(canvas.Nodes);
        Assert.Contains(canvas.Nodes, n => n.Type == NodeType.ResultOutput);
        Assert.NotEmpty(canvas.Connections);

        Assert.True(canvas.UndoRedo.CanUndo);
    }

    [Fact]
    public async Task LoadTemplate_WhileCteEditorActive_AbortsSubEditorSessionAndLoadsTemplate()
    {
        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();
        canvas.UndoRedo.Clear();

        NodeViewModel table = new("public.orders", [("id", PinDataType.Number)], new Point(0, 0));
        NodeViewModel columns = new(NodeDefinitionRegistry.Get(NodeType.ColumnList), new Point(130, 0));
        NodeViewModel result = new(NodeDefinitionRegistry.Get(NodeType.ResultOutput), new Point(260, 0));
        NodeViewModel cte = new(NodeDefinitionRegistry.Get(NodeType.CteDefinition), new Point(400, 0));
        cte.Parameters["name"] = "orders_cte";

        canvas.Nodes.Add(table);
        canvas.Nodes.Add(columns);
        canvas.Nodes.Add(result);
        canvas.Nodes.Add(cte);

        Connect(canvas, table, "id", columns, "columns");
        Connect(canvas, columns, "result", result, "columns");
        Connect(canvas, result, "result", cte, "query");

        Assert.True(await canvas.EnterCteEditorAsync(cte));
        Assert.True(canvas.IsInCteEditor);

        QueryTemplate template = QueryTemplateLibrary.All.First(t => t.Name == "Simple SELECT");
        canvas.LoadTemplate(template);

        Assert.False(canvas.IsInCteEditor);
        Assert.Equal(string.Empty, canvas.CteEditorBreadcrumb);
        Assert.Contains(canvas.Nodes, n => n.Type == NodeType.ResultOutput);
        Assert.NotEmpty(canvas.Connections);
    }

    [Fact]
    public async Task LoadTemplate_WhenSubEditorHasUnsavedChanges_KeepsEditorSessionAndBlocksTemplateLoad()
    {
        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();
        canvas.UndoRedo.Clear();

        NodeViewModel table = new("public.orders", [("id", PinDataType.Number)], new Point(0, 0));
        NodeViewModel columns = new(NodeDefinitionRegistry.Get(NodeType.ColumnList), new Point(130, 0));
        NodeViewModel result = new(NodeDefinitionRegistry.Get(NodeType.ResultOutput), new Point(260, 0));
        NodeViewModel cte = new(NodeDefinitionRegistry.Get(NodeType.CteDefinition), new Point(400, 0));
        cte.Parameters["name"] = "orders_cte";

        canvas.Nodes.Add(table);
        canvas.Nodes.Add(columns);
        canvas.Nodes.Add(result);
        canvas.Nodes.Add(cte);

        Connect(canvas, table, "id", columns, "columns");
        Connect(canvas, columns, "result", result, "columns");
        Connect(canvas, result, "result", cte, "query");

        Assert.True(await canvas.EnterCteEditorAsync(cte));
        Assert.True(canvas.IsInCteEditor);

        NodeViewModel editedNode = canvas.SpawnNode(NodeDefinitionRegistry.Get(NodeType.Equals), new Point(420, 220));
        Assert.True(canvas.IsDirty);

        int diagnosticsBefore = canvas.Diagnostics.SnapshotEntries().Count;

        QueryTemplate template = QueryTemplateLibrary.All.First(t => t.Name == "Simple SELECT");
        canvas.LoadTemplate(template);

        Assert.True(canvas.IsInCteEditor);
        Assert.Contains(canvas.Nodes, node => node.Id == editedNode.Id);
        Assert.True(canvas.Diagnostics.SnapshotEntries().Count > diagnosticsBefore);
    }

    private static void Connect(
        CanvasViewModel canvas,
        NodeViewModel fromNode,
        string fromPin,
        NodeViewModel toNode,
        string toPin)
    {
        PinViewModel from = fromNode.OutputPins.First(p => p.Name == fromPin);
        PinViewModel to = toNode.InputPins.First(p => p.Name == toPin);
        canvas.Connections.Add(new ConnectionViewModel(from, from.AbsolutePosition, to.AbsolutePosition)
        {
            ToPin = to,
        });
    }
}
