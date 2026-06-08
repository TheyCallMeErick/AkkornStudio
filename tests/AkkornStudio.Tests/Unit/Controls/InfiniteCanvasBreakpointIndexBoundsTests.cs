using Avalonia;
using AkkornStudio.UI.Controls;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.Controls;

public sealed class InfiniteCanvasBreakpointIndexBoundsTests
{
    [Fact]
    public void TryGetBreakpointPosition_ReturnsFalse_WhenIndexIsOutOfRange()
    {
        ConnectionViewModel wire = CreateOrthogonalWireWithTwoBreakpoints();

        bool resolved = InfiniteCanvas.TryGetBreakpointPosition(
            wire,
            breakpointIndex: 2,
            out Point position);

        Assert.False(resolved);
        Assert.Equal(default, position);
    }

    [Fact]
    public void TryGetBreakpointPosition_ReturnsTrue_WhenIndexIsValid()
    {
        ConnectionViewModel wire = CreateOrthogonalWireWithTwoBreakpoints();

        bool resolved = InfiniteCanvas.TryGetBreakpointPosition(
            wire,
            breakpointIndex: 1,
            out Point position);

        Assert.True(resolved);
        Assert.Equal(new Point(240, 220), position);
    }

    private static ConnectionViewModel CreateOrthogonalWireWithTwoBreakpoints()
    {
        var vm = new CanvasViewModel();
        vm.InitializeDemoNodes();

        ConnectionViewModel wire = vm.Connections.First(c => c.ToPin is not null);
        wire.RoutingMode = CanvasWireRoutingMode.Orthogonal;
        wire.SetBreakpoints(
        [
            new WireBreakpoint(new Point(200, 180)),
            new WireBreakpoint(new Point(240, 220)),
        ]);

        return wire;
    }
}
