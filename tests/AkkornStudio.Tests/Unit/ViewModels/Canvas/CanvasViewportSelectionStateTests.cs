using Avalonia;
using AkkornStudio.UI.Controls;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.ViewModels.Canvas;

public sealed class CanvasViewportSelectionStateTests
{
    private static NodeViewModel Node(string name, double x, double y, double width = 220)
    {
        return new NodeViewModel($"public.{name}", Array.Empty<(string n, DataType t)>(), new Point(x, y))
        {
            Width = width,
        };
    }

    [Fact]
    public void TrySelectInRegion_SelectsIntersectingNodes()
    {
        var vm = new CanvasViewModel();
        var inside = Node("orders", 100, 100);
        var outside = Node("users", 500, 500);
        vm.Nodes.Add(inside);
        vm.Nodes.Add(outside);

        bool selected = ((ICanvasViewportSelectionState)vm).TrySelectInRegion(new Rect(90, 90, 260, 180));

        Assert.True(selected);
        Assert.True(inside.IsSelected);
        Assert.False(outside.IsSelected);
    }

    [Fact]
    public void TryGetSelectionFrame_ReturnsBoundsForSelectedNodes()
    {
        var vm = new CanvasViewModel();
        var first = Node("orders", 100, 100, width: 220);
        var second = Node("users", 400, 180, width: 260);
        first.IsSelected = true;
        second.IsSelected = true;
        vm.Nodes.Add(first);
        vm.Nodes.Add(second);

        bool ok = ((ICanvasViewportSelectionState)vm).TryGetSelectionFrame(10, out Rect frame);

        Assert.True(ok);
        Assert.Equal(90, frame.X);
        Assert.Equal(90, frame.Y);
        Assert.Equal(580, frame.Width);
        Assert.Equal(230, frame.Height);
    }
}
