using Avalonia;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.ViewModels.Canvas;

public sealed class WireBreakpointEditingTests
{
    [Fact]
    public void InsertWireBreakpoint_AddsPointAtRequestedIndex()
    {
        var vm = new CanvasViewModel();
        vm.InitializeDemoNodes();
        ConnectionViewModel wire = vm.Connections.First(c => c.ToPin is not null);
        wire.RoutingMode = CanvasWireRoutingMode.Orthogonal;

        bool inserted = vm.InsertWireBreakpoint(wire, insertIndex: 0, new Point(240, 180));

        Assert.True(inserted);
        Assert.Single(wire.Breakpoints);
        Assert.Equal(new Point(240, 180), wire.Breakpoints[0].Position);
    }

    [Fact]
    public void RemoveWireBreakpoint_RemovesExistingPoint()
    {
        var vm = new CanvasViewModel();
        vm.InitializeDemoNodes();
        ConnectionViewModel wire = vm.Connections.First(c => c.ToPin is not null);
        wire.RoutingMode = CanvasWireRoutingMode.Orthogonal;
        wire.SetBreakpoints([new WireBreakpoint(new Point(200, 180))]);

        bool removed = vm.RemoveWireBreakpoint(wire, 0);

        Assert.True(removed);
        Assert.Empty(wire.Breakpoints);
    }

    [Fact]
    public void CommitWireBreakpointDrag_RegistersBeforeAndAfter_ForUndoRedo()
    {
        var vm = new CanvasViewModel();
        vm.InitializeDemoNodes();
        ConnectionViewModel wire = vm.Connections.First(c => c.ToPin is not null);
        wire.RoutingMode = CanvasWireRoutingMode.Orthogonal;
        wire.SetBreakpoints([new WireBreakpoint(new Point(200, 180))]);

        bool committed = vm.CommitWireBreakpointDrag(
            wire,
            index: 0,
            initialPosition: new Point(200, 180),
            finalPosition: new Point(260, 220));

        Assert.True(committed);
        Assert.Equal(new Point(260, 220), wire.Breakpoints[0].Position);

        vm.UndoRedo.Undo();
        Assert.Equal(new Point(200, 180), wire.Breakpoints[0].Position);

        vm.UndoRedo.Redo();
        Assert.Equal(new Point(260, 220), wire.Breakpoints[0].Position);
    }

    [Fact]
    public void SelectWireBreakpoint_SelectsWireAndExposesSelectedHandle()
    {
        var vm = new CanvasViewModel();
        vm.InitializeDemoNodes();
        ConnectionViewModel wire = vm.Connections.First(c => c.ToPin is not null);
        wire.RoutingMode = CanvasWireRoutingMode.Orthogonal;
        wire.SetBreakpoints([new WireBreakpoint(new Point(210, 190))]);

        bool selected = vm.SelectWireBreakpoint(wire, 0);

        Assert.True(selected);
        Assert.Same(wire, vm.SelectedConnection);
        Assert.Same(wire, vm.SelectedBreakpointConnection);
        Assert.Equal(0, vm.SelectedBreakpointIndex);
        Assert.True(vm.HasSelectedBreakpoint);
    }

    [Fact]
    public void RemoveWireBreakpoint_ClearsSelectedHandle_WhenRemovingSelectedIndex()
    {
        var vm = new CanvasViewModel();
        vm.InitializeDemoNodes();
        ConnectionViewModel wire = vm.Connections.First(c => c.ToPin is not null);
        wire.RoutingMode = CanvasWireRoutingMode.Orthogonal;
        wire.SetBreakpoints(
        [
            new WireBreakpoint(new Point(180, 170)),
            new WireBreakpoint(new Point(260, 220)),
        ]);
        vm.SelectWireBreakpoint(wire, 1);

        bool removed = vm.RemoveWireBreakpoint(wire, 1);

        Assert.True(removed);
        Assert.Single(wire.Breakpoints);
        Assert.False(vm.HasSelectedBreakpoint);
        Assert.Null(vm.SelectedBreakpointConnection);
        Assert.Equal(-1, vm.SelectedBreakpointIndex);
    }

    [Fact]
    public void InsertWireBreakpoint_ShiftsSelectedIndex_WhenInsertedBeforeSelectedBreakpoint()
    {
        var vm = new CanvasViewModel();
        vm.InitializeDemoNodes();
        ConnectionViewModel wire = vm.Connections.First(c => c.ToPin is not null);
        wire.RoutingMode = CanvasWireRoutingMode.Orthogonal;
        wire.SetBreakpoints(
        [
            new WireBreakpoint(new Point(220, 180)),
            new WireBreakpoint(new Point(280, 220)),
        ]);
        vm.SelectWireBreakpoint(wire, 1);

        bool inserted = vm.InsertWireBreakpoint(wire, insertIndex: 1, new Point(250, 200));

        Assert.True(inserted);
        Assert.Equal(3, wire.Breakpoints.Count);
        Assert.Equal(2, vm.SelectedBreakpointIndex);
        Assert.True(vm.HasSelectedBreakpoint);
    }

    [Fact]
    public void DeleteSelectedWireBreakpoint_RemovesSelectedHandle()
    {
        var vm = new CanvasViewModel();
        vm.InitializeDemoNodes();
        ConnectionViewModel wire = vm.Connections.First(c => c.ToPin is not null);
        wire.RoutingMode = CanvasWireRoutingMode.Orthogonal;
        wire.SetBreakpoints(
        [
            new WireBreakpoint(new Point(200, 180)),
            new WireBreakpoint(new Point(260, 220)),
        ]);
        vm.SelectWireBreakpoint(wire, 0);

        bool deleted = vm.DeleteSelectedWireBreakpoint();

        Assert.True(deleted);
        Assert.Single(wire.Breakpoints);
        Assert.Equal(new Point(260, 220), wire.Breakpoints[0].Position);
        Assert.False(vm.HasSelectedBreakpoint);
    }

    [Fact]
    public void DeleteConnection_ClearsSelectedBreakpointState_ForThatWire()
    {
        var vm = new CanvasViewModel();
        vm.InitializeDemoNodes();
        ConnectionViewModel wire = vm.Connections.First(c => c.ToPin is not null);
        wire.RoutingMode = CanvasWireRoutingMode.Orthogonal;
        wire.SetBreakpoints([new WireBreakpoint(new Point(220, 190))]);
        vm.SelectWireBreakpoint(wire, 0);

        vm.DeleteConnection(wire);

        Assert.False(vm.HasSelectedBreakpoint);
        Assert.Null(vm.SelectedBreakpointConnection);
        Assert.Equal(-1, vm.SelectedBreakpointIndex);
    }

    [Fact]
    public void Breakpoints_ExposesReadOnlyListView_NotMutableListInstance()
    {
        var vm = new CanvasViewModel();
        vm.InitializeDemoNodes();
        ConnectionViewModel wire = vm.Connections.First(c => c.ToPin is not null);
        wire.SetBreakpoints([new WireBreakpoint(new Point(220, 190))]);

        Assert.IsNotType<List<WireBreakpoint>>(wire.Breakpoints);
    }

    [Fact]
    public void Breakpoints_ReadOnlyListRejectsMutationAttempts()
    {
        var vm = new CanvasViewModel();
        vm.InitializeDemoNodes();
        ConnectionViewModel wire = vm.Connections.First(c => c.ToPin is not null);
        wire.SetBreakpoints([new WireBreakpoint(new Point(220, 190))]);

        IList<WireBreakpoint> breakpoints = Assert.IsAssignableFrom<IList<WireBreakpoint>>(wire.Breakpoints);
        Assert.Throws<NotSupportedException>(() => breakpoints.Add(new WireBreakpoint(new Point(260, 230))));
    }
}
