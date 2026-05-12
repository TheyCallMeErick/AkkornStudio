using Avalonia;
using AkkornStudio.Nodes;
using AkkornStudio.UI.ViewModels;
using Xunit;

namespace AkkornStudio.Tests.Unit.ViewModels.Canvas;

public class NodeViewModelComparisonInlineSummaryTests
{
    [Fact]
    public void EqualsNode_WithNoInputs_ShowsLeftRightPlaceholders()
    {
        var canvas = new CanvasViewModel();
        var equalsNode = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.Equals), new Point(120, 80));

        canvas.Nodes.Add(equalsNode);

        Assert.True(equalsNode.HasComparisonInlineSummary);
        Assert.Equal("<left> = <right>", equalsNode.ComparisonInlineSummary);
    }

    [Fact]
    public void EqualsNode_WithLeftColumnAndRightLiteral_ShowsComparedValues()
    {
        var canvas = new CanvasViewModel();
        var orders = new NodeViewModel("public.orders", [("status", PinDataType.Text)], new Point(0, 0));
        var equalsNode = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.Equals), new Point(180, 40));
        equalsNode.PinLiterals["right"] = "COMPLETED";

        canvas.Nodes.Add(orders);
        canvas.Nodes.Add(equalsNode);
        canvas.ConnectPins(
            orders.FindPin("status", PinDirection.Output)!,
            equalsNode.FindPin("left", PinDirection.Input)!);

        Assert.Equal("orders.status = COMPLETED", equalsNode.ComparisonInlineSummary);
    }

    [Fact]
    public void EqualsNode_WithLongLiteral_TruncatesPreviewText()
    {
        var canvas = new CanvasViewModel();
        var equalsNode = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.Equals), new Point(120, 80));
        equalsNode.PinLiterals["right"] = "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor";

        canvas.Nodes.Add(equalsNode);

        Assert.True(equalsNode.HasComparisonInlineSummary);
        Assert.StartsWith("<left> = Lorem ipsum dolor sit amet", equalsNode.ComparisonInlineSummary);
        Assert.EndsWith("...", equalsNode.ComparisonInlineSummary);
    }
}
