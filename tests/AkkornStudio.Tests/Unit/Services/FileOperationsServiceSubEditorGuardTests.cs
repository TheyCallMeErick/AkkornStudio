using System.Runtime.Serialization;
using Avalonia;
using Avalonia.Controls;
using AkkornStudio.Nodes;
using AkkornStudio.UI.Serialization;
using AkkornStudio.UI.Services;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.Services;

public sealed class FileOperationsServiceSubEditorGuardTests
{
    [Fact]
    public async Task OpenPathAsync_WhenSubEditorHasUnsavedChanges_DoesNotReplaceCanvasState()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"akkorn-file-open-guard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        string savedPath = Path.Combine(tempRoot, "workspace.vsaq");

        try
        {
            var persistedCanvas = new CanvasViewModel();
            persistedCanvas.SpawnNode(NodeDefinitionRegistry.Get(NodeType.Equals), new Point(30, 30));
            await CanvasSerializer.SaveToFileAsync(savedPath, persistedCanvas);

            var activeCanvas = new CanvasViewModel();
            NodeViewModel cteNode = BuildBasicCteEditableGraph(activeCanvas);
            Assert.True(await activeCanvas.EnterCteEditorAsync(cteNode));
            NodeViewModel editedNode = activeCanvas.SpawnNode(NodeDefinitionRegistry.Get(NodeType.Equals), new Point(420, 220));
            Assert.True(activeCanvas.IsInCteEditor);
            Assert.True(activeCanvas.IsDirty);

#pragma warning disable SYSLIB0050
            var window = (Window)FormatterServices.GetUninitializedObject(typeof(Window));
#pragma warning restore SYSLIB0050
            var fileOps = new FileOperationsService(window, activeCanvas);

            await fileOps.OpenPathAsync(savedPath);

            Assert.True(activeCanvas.IsInCteEditor);
            Assert.Null(activeCanvas.CurrentFilePath);
            Assert.Contains(activeCanvas.Nodes, node => node.Id == editedNode.Id);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static NodeViewModel BuildBasicCteEditableGraph(CanvasViewModel canvas)
    {
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
        return cte;
    }

    private static void Connect(
        CanvasViewModel canvas,
        NodeViewModel fromNode,
        string fromPin,
        NodeViewModel toNode,
        string toPin)
    {
        PinViewModel from = fromNode.OutputPins.First(pin => pin.Name == fromPin);
        PinViewModel to = toNode.InputPins.First(pin => pin.Name == toPin);
        canvas.Connections.Add(new ConnectionViewModel(from, from.AbsolutePosition, to.AbsolutePosition)
        {
            ToPin = to,
        });
    }
}
