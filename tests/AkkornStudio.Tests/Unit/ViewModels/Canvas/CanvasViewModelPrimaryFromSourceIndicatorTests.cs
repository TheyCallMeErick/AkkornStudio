using Avalonia;
using AkkornStudio.Nodes;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.ViewModels.Canvas;

public sealed class CanvasViewModelPrimaryFromSourceIndicatorTests
{
    [Fact]
    public void Nodes_WhenNoResultOutput_MarksFirstTableSourceAsPrimaryFrom()
    {
        var canvas = new CanvasViewModel();

        NodeViewModel tableA = new("public.orders", [("id", PinDataType.Number)], new Point(0, 0));
        NodeViewModel tableB = new("public.customers", [("id", PinDataType.Number)], new Point(240, 0));

        canvas.Nodes.Add(tableA);
        canvas.Nodes.Add(tableB);

        Assert.True(tableA.IsPrimaryFromSource);
        Assert.False(tableB.IsPrimaryFromSource);
    }

    [Fact]
    public void Connections_WhenResultOutputHasUpstreamSource_MarksConnectedTableAsPrimaryFrom()
    {
        var canvas = new CanvasViewModel();

        NodeViewModel tableA = new("public.orders", [("id", PinDataType.Number)], new Point(0, 0));
        NodeViewModel tableB = new("public.customers", [("id", PinDataType.Number)], new Point(240, 0));
        NodeViewModel result = new(NodeDefinitionRegistry.Get(NodeType.ResultOutput), new Point(520, 0));

        canvas.Nodes.Add(tableA);
        canvas.Nodes.Add(tableB);
        canvas.Nodes.Add(result);

        PinViewModel from = tableB.OutputPins.First(pin => pin.Name == "id");
        PinViewModel to = result.InputPins.First(pin => pin.Name == "column");
        canvas.ConnectPins(from, to);

        Assert.False(tableA.IsPrimaryFromSource);
        Assert.True(tableB.IsPrimaryFromSource);
        Assert.False(result.IsPrimaryFromSource);
    }

    [Fact]
    public void Nodes_WhenOnlyCteSourceExists_MarksCteAsPrimaryFrom()
    {
        var canvas = new CanvasViewModel();

        NodeViewModel cteSource = new(NodeDefinitionRegistry.Get(NodeType.CteSource), new Point(0, 0));
        NodeViewModel transform = new(NodeDefinitionRegistry.Get(NodeType.Equals), new Point(220, 0));

        canvas.Nodes.Add(cteSource);
        canvas.Nodes.Add(transform);

        Assert.True(cteSource.IsPrimaryFromSource);
        Assert.False(transform.IsPrimaryFromSource);
    }

    [Fact]
    public void Connections_WhenAllResultOutputsAreInternal_FallsBackToFirstTableSourceDeterministically()
    {
        var canvas = new CanvasViewModel();

        NodeViewModel tableA = new("public.orders", [("id", PinDataType.Number)], new Point(0, 0));
        NodeViewModel tableB = new("public.customers", [("id", PinDataType.Number)], new Point(240, 0));
        NodeViewModel resultForB = new(NodeDefinitionRegistry.Get(NodeType.ResultOutput), new Point(520, 0));
        NodeViewModel resultForA = new(NodeDefinitionRegistry.Get(NodeType.ResultOutput), new Point(520, 180));
        NodeViewModel cteA = new(NodeDefinitionRegistry.Get(NodeType.CteDefinition), new Point(760, 180));
        NodeViewModel cteB = new(NodeDefinitionRegistry.Get(NodeType.CteDefinition), new Point(760, 0));

        canvas.Nodes.Add(tableA);
        canvas.Nodes.Add(tableB);
        canvas.Nodes.Add(resultForB);
        canvas.Nodes.Add(resultForA);
        canvas.Nodes.Add(cteA);
        canvas.Nodes.Add(cteB);

        Connect(canvas, tableB, "id", resultForB, "column");
        Connect(canvas, resultForB, "result", cteB, "query");
        Connect(canvas, tableA, "id", resultForA, "column");
        Connect(canvas, resultForA, "result", cteA, "query");

        Assert.True(tableA.IsPrimaryFromSource);
        Assert.False(tableB.IsPrimaryFromSource);
    }

    [Fact]
    public void Connections_WhenMultipleExternalResultOutputsExist_FallsBackToFirstTableSourceInsteadOfArbitraryOutput()
    {
        var canvas = new CanvasViewModel();

        NodeViewModel tableA = new("public.orders", [("id", PinDataType.Number)], new Point(0, 0));
        NodeViewModel tableB = new("public.customers", [("id", PinDataType.Number)], new Point(240, 0));
        NodeViewModel resultForB = new(NodeDefinitionRegistry.Get(NodeType.ResultOutput), new Point(520, 0));
        NodeViewModel resultForA = new(NodeDefinitionRegistry.Get(NodeType.ResultOutput), new Point(520, 180));

        canvas.Nodes.Add(tableA);
        canvas.Nodes.Add(tableB);
        canvas.Nodes.Add(resultForB);
        canvas.Nodes.Add(resultForA);

        Connect(canvas, tableB, "id", resultForB, "column");
        Connect(canvas, tableA, "id", resultForA, "column");

        Assert.True(tableA.IsPrimaryFromSource);
        Assert.False(tableB.IsPrimaryFromSource);
    }

    private static void Connect(CanvasViewModel canvas, NodeViewModel fromNode, string fromPin, NodeViewModel toNode, string toPin)
    {
        PinViewModel from = fromNode.OutputPins.First(pin => pin.Name == fromPin);
        PinViewModel to = toNode.InputPins.First(pin => pin.Name == toPin);
        canvas.ConnectPins(from, to);
    }
}
