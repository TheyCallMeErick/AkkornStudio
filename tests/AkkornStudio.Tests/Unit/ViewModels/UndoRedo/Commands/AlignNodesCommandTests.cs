using Avalonia;
using AkkornStudio.Nodes;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.UndoRedo.Commands;

namespace AkkornStudio.Tests.Unit.ViewModels.UndoRedo.Commands;

public sealed class AlignNodesCommandTests
{
    [Fact]
    public void Constructor_WithNullNodes_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AlignNodesCommand(null!, AlignMode.Left));
    }

    [Fact]
    public void Constructor_WithInvalidMode_Throws()
    {
        IReadOnlyList<NodeViewModel> nodes = [CreateTableNode("a", 0, 0, 1)];
        Assert.Throws<ArgumentOutOfRangeException>(() => new AlignNodesCommand(nodes, (AlignMode)999));
    }

    [Fact]
    public void ExecuteUndo_WithEmptyNodes_NoOp()
    {
        using var canvas = new CanvasViewModel();
        var sut = new AlignNodesCommand([], AlignMode.Left);

        sut.Execute(canvas);
        sut.Undo(canvas);

        Assert.Equal("Align nodes", sut.Description);
    }

    [Theory]
    [InlineData(AlignMode.Left)]
    [InlineData(AlignMode.Right)]
    [InlineData(AlignMode.Top)]
    [InlineData(AlignMode.Bottom)]
    [InlineData(AlignMode.CenterH)]
    [InlineData(AlignMode.CenterV)]
    [InlineData(AlignMode.DistributeH)]
    [InlineData(AlignMode.DistributeV)]
    public void ExecuteUndo_AllModes_RestoresOriginalPositions(AlignMode mode)
    {
        using var canvas = new CanvasViewModel();
        NodeViewModel n1 = CreateTableNode("n1", 10, 30, 1);
        NodeViewModel n2 = CreateTableNode("n2", 140, 80, 3);
        NodeViewModel n3 = CreateTableNode("n3", 310, 180, 5);
        n1.Width = 200;
        n2.Width = 0; // covers width fallback branch
        n3.Width = double.NaN; // covers non-finite width branch

        IReadOnlyList<NodeViewModel> nodes = [n1, n2, n3];
        Point[] before = [.. nodes.Select(n => n.Position)];
        var sut = new AlignNodesCommand(nodes, mode);

        sut.Execute(canvas);

        bool moved = false;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].Position == before[i])
                continue;

            moved = true;
            break;
        }

        Assert.True(moved);

        sut.Undo(canvas);
        for (int i = 0; i < nodes.Count; i++)
            Assert.Equal(before[i], nodes[i].Position);
    }

    [Fact]
    public void BottomAlignment_UsesPerNodeEstimatedHeight()
    {
        using var canvas = new CanvasViewModel();
        NodeViewModel shortNode = CreateTableNode("short", 50, 10, 1);
        NodeViewModel tallNode = CreateTableNode("tall", 120, 40, 6);
        IReadOnlyList<NodeViewModel> nodes = [shortNode, tallNode];
        var sut = new AlignNodesCommand(nodes, AlignMode.Bottom);

        sut.Execute(canvas);

        double shortBottom = shortNode.Position.Y + EstimateHeight(shortNode);
        double tallBottom = tallNode.Position.Y + EstimateHeight(tallNode);
        Assert.Equal(shortBottom, tallBottom, 6);
    }

    [Fact]
    public void CenterHAlignment_UsesPerNodeEstimatedHeight()
    {
        using var canvas = new CanvasViewModel();
        NodeViewModel shortNode = CreateTableNode("short", 0, 20, 1);
        NodeViewModel tallNode = CreateTableNode("tall", 220, 190, 6);
        IReadOnlyList<NodeViewModel> nodes = [shortNode, tallNode];
        var sut = new AlignNodesCommand(nodes, AlignMode.CenterH);

        sut.Execute(canvas);

        double shortCenter = shortNode.Position.Y + (EstimateHeight(shortNode) / 2.0);
        double tallCenter = tallNode.Position.Y + (EstimateHeight(tallNode) / 2.0);
        Assert.Equal(shortCenter, tallCenter, 6);
    }

    private static NodeViewModel CreateTableNode(string name, double x, double y, int columns)
    {
        var cols = Enumerable.Range(1, columns).Select(i => ($"c{i}", PinDataType.Integer)).ToArray();
        return new NodeViewModel($"public.{name}", cols, new Point(x, y));
    }

    private static double EstimateHeight(NodeViewModel node)
    {
        int pinRows = Math.Max(node.InputPins.Count, node.OutputPins.Count);
        return 74 + (pinRows * 22);
    }
}
