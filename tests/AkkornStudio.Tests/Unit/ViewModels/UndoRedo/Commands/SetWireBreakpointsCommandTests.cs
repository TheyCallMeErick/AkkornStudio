using System.ComponentModel;
using Avalonia;
using AkkornStudio.Nodes;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.UndoRedo.Commands;

namespace AkkornStudio.Tests.Unit.ViewModels.UndoRedo.Commands;

public sealed class SetWireBreakpointsCommandTests
{
    [Fact]
    public void Constructor_WithNullWire_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SetWireBreakpointsCommand(
            null!,
            [],
            [],
            "set breakpoints"));
    }

    [Fact]
    public void Constructor_WithNullBefore_Throws()
    {
        (_, ConnectionViewModel wire) = CreateCanvasAndWire();
        Assert.Throws<ArgumentNullException>(() => new SetWireBreakpointsCommand(
            wire,
            null!,
            [],
            "set breakpoints"));
    }

    [Fact]
    public void Constructor_WithNullAfter_Throws()
    {
        (_, ConnectionViewModel wire) = CreateCanvasAndWire();
        Assert.Throws<ArgumentNullException>(() => new SetWireBreakpointsCommand(
            wire,
            [],
            null!,
            "set breakpoints"));
    }

    [Fact]
    public void Constructor_WithNullDescription_Throws()
    {
        (_, ConnectionViewModel wire) = CreateCanvasAndWire();
        Assert.Throws<ArgumentNullException>(() => new SetWireBreakpointsCommand(
            wire,
            [],
            [],
            null!));
    }

    [Fact]
    public void ExecuteUndo_WhenSuccessful_UpdatesBreakpointsAndMarksCanvasDirty()
    {
        using var canvas = new CanvasViewModel();
        ConnectionViewModel wire = CreateWire(canvas);
        var before = Array.Empty<WireBreakpoint>();
        WireBreakpoint[] after = [new(new Point(10, 20)), new(new Point(30, 40))];
        var sut = new SetWireBreakpointsCommand(wire, before, after, "set breakpoints");

        canvas.IsDirty = false;
        sut.Execute(canvas);

        Assert.Equal(after, wire.Breakpoints);
        Assert.True(canvas.IsDirty);

        canvas.IsDirty = false;
        sut.Undo(canvas);

        Assert.Equal(before, wire.Breakpoints);
        Assert.True(canvas.IsDirty);
        Assert.Equal("set breakpoints", sut.Description);
    }

    [Fact]
    public void Execute_WhenWireIsNotInCanvas_IsNoOpAndDoesNotMarkDirty()
    {
        using var canvas = new CanvasViewModel();
        ConnectionViewModel wire = CreateWire(canvas);
        canvas.Connections.Remove(wire);
        WireBreakpoint[] before = [];
        WireBreakpoint[] after = [new(new Point(10, 10))];
        var sut = new SetWireBreakpointsCommand(wire, before, after, "set breakpoints");

        canvas.IsDirty = false;
        sut.Execute(canvas);
        Assert.False(canvas.IsDirty);
        Assert.Empty(wire.Breakpoints);

        sut.Undo(canvas);
        Assert.False(canvas.IsDirty);
        Assert.Empty(wire.Breakpoints);
    }

    [Fact]
    public void Execute_WhenSetBreakpointsThrows_RollsBackAndRethrows()
    {
        using var canvas = new CanvasViewModel();
        ConnectionViewModel wire = CreateWire(canvas);
        var seed = new SetWireBreakpointsCommand(wire, [], [new(new Point(1, 1))], "seed");
        seed.Execute(canvas);
        IReadOnlyList<WireBreakpoint> previous = [.. wire.Breakpoints];

        WireBreakpoint[] after = [new(new Point(8, 8))];
        var sut = new SetWireBreakpointsCommand(wire, previous, after, "set breakpoints");

        int throwCount = 0;
        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName == nameof(ConnectionViewModel.Breakpoints) && throwCount++ == 0)
                throw new InvalidOperationException("Injected breakpoint change failure.");
        };

        wire.PropertyChanged += handler;
        canvas.IsDirty = false;
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => sut.Execute(canvas));
            Assert.Equal("Injected breakpoint change failure.", ex.Message);
        }
        finally
        {
            wire.PropertyChanged -= handler;
        }

        Assert.Equal(previous, wire.Breakpoints);
        Assert.False(canvas.IsDirty);
    }

    [Fact]
    public void Execute_WhenRollbackAlsoThrows_KeepsOriginalFailure()
    {
        using var canvas = new CanvasViewModel();
        ConnectionViewModel wire = CreateWire(canvas);
        var sut = new SetWireBreakpointsCommand(
            wire,
            [],
            [new(new Point(9, 9))],
            "set breakpoints");

        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName == nameof(ConnectionViewModel.Breakpoints))
                throw new InvalidOperationException("Injected breakpoint change failure.");
        };

        wire.PropertyChanged += handler;
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => sut.Execute(canvas));
            Assert.Equal("Injected breakpoint change failure.", ex.Message);
        }
        finally
        {
            wire.PropertyChanged -= handler;
        }
    }

    private static (CanvasViewModel Canvas, ConnectionViewModel Wire) CreateCanvasAndWire()
    {
        var canvas = new CanvasViewModel();
        return (canvas, CreateWire(canvas));
    }

    private static ConnectionViewModel CreateWire(CanvasViewModel canvas)
    {
        NodeViewModel source = new("public.orders", [("id", PinDataType.Integer)], new Point(0, 0));
        NodeViewModel target = new(NodeDefinitionRegistry.Get(NodeType.Sum), new Point(220, 0));
        canvas.Nodes.Add(source);
        canvas.Nodes.Add(target);

        PinViewModel from = source.OutputPins.Single(p => p.Name == "id");
        PinViewModel to = target.InputPins.Single(p => p.Name == "value");
        canvas.ConnectPins(from, to);
        return Assert.Single(canvas.Connections);
    }
}
