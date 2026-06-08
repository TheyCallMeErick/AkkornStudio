using System.Collections.Specialized;
using Avalonia;
using AkkornStudio.Nodes;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.UndoRedo.Commands;

namespace AkkornStudio.Tests.Unit.ViewModels.UndoRedo.Commands;

public sealed class ConnectionAndNodeCommandHardeningTests
{
    [Fact]
    public void AddConnectionCommand_Description_UsesPinNames()
    {
        NodeViewModel source = new("public.orders", [("id", PinDataType.Integer)], new Point(0, 0));
        NodeViewModel sum = new(NodeDefinitionRegistry.Get(NodeType.Sum), new Point(220, 0));
        PinViewModel from = source.OutputPins.Single(p => p.Name == "id");
        PinViewModel to = sum.InputPins.Single(p => p.Name == "value");
        var connection = new ConnectionViewModel(from, from.AbsolutePosition, to.AbsolutePosition) { ToPin = to };
        var sut = new AddConnectionCommand(connection);

        Assert.Contains("Connect", sut.Description, StringComparison.Ordinal);
        Assert.Contains("id", sut.Description, StringComparison.Ordinal);
        Assert.Contains("value", sut.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void AddConnectionCommand_ExecuteAndUndo_WithDisplacedConnection_ReplacesAndRestores()
    {
        using var canvas = new CanvasViewModel();

        NodeViewModel sourceA = new("public.orders", [("id_a", PinDataType.Integer)], new Point(0, 0));
        NodeViewModel sourceB = new("public.customers", [("id_b", PinDataType.Integer)], new Point(0, 120));
        NodeViewModel sum = new(NodeDefinitionRegistry.Get(NodeType.Sum), new Point(220, 0));
        canvas.Nodes.Add(sourceA);
        canvas.Nodes.Add(sourceB);
        canvas.Nodes.Add(sum);

        PinViewModel to = sum.InputPins.Single(p => p.Name == "value");
        PinViewModel fromA = sourceA.OutputPins.Single(p => p.Name == "id_a");
        PinViewModel fromB = sourceB.OutputPins.Single(p => p.Name == "id_b");

        var displaced = new ConnectionViewModel(fromA, fromA.AbsolutePosition, to.AbsolutePosition) { ToPin = to };
        canvas.Connections.Add(displaced);
        fromA.IsConnected = true;
        to.IsConnected = true;

        var created = new ConnectionViewModel(fromB, fromB.AbsolutePosition, to.AbsolutePosition) { ToPin = to };
        var sut = new AddConnectionCommand(created, displaced);

        sut.Execute(canvas);

        Assert.DoesNotContain(displaced, canvas.Connections);
        Assert.Contains(created, canvas.Connections);
        Assert.True(fromB.IsConnected);
        Assert.True(to.IsConnected);

        sut.Undo(canvas);

        Assert.DoesNotContain(created, canvas.Connections);
        Assert.Contains(displaced, canvas.Connections);
        Assert.True(fromA.IsConnected);
        Assert.True(to.IsConnected);
    }

    [Fact]
    public void AddConnectionCommand_WhenAddThrowsAfterDisplace_RollsBackToDisplacedConnection()
    {
        using var canvas = new CanvasViewModel();

        NodeViewModel sourceA = new("public.orders", [("id_a", PinDataType.Integer)], new Point(0, 0));
        NodeViewModel sourceB = new("public.customers", [("id_b", PinDataType.Integer)], new Point(0, 120));
        NodeViewModel sum = new(NodeDefinitionRegistry.Get(NodeType.Sum), new Point(220, 0));
        canvas.Nodes.Add(sourceA);
        canvas.Nodes.Add(sourceB);
        canvas.Nodes.Add(sum);

        PinViewModel to = sum.InputPins.Single(p => p.Name == "value");
        PinViewModel fromA = sourceA.OutputPins.Single(p => p.Name == "id_a");
        PinViewModel fromB = sourceB.OutputPins.Single(p => p.Name == "id_b");

        var displaced = new ConnectionViewModel(fromA, fromA.AbsolutePosition, to.AbsolutePosition) { ToPin = to };
        canvas.Connections.Add(displaced);
        fromA.IsConnected = true;
        to.IsConnected = true;

        var created = new ConnectionViewModel(fromB, fromB.AbsolutePosition, to.AbsolutePosition) { ToPin = to };
        var sut = new AddConnectionCommand(created, displaced);

        NotifyCollectionChangedEventHandler handler = (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Add
                && args.NewItems?.OfType<ConnectionViewModel>().Any(c => ReferenceEquals(c, created)) == true)
            {
                throw new InvalidOperationException("Injected failure during add.");
            }
        };

        canvas.Connections.CollectionChanged += handler;
        try
        {
            Assert.Throws<InvalidOperationException>(() => sut.Execute(canvas));
        }
        finally
        {
            canvas.Connections.CollectionChanged -= handler;
        }

        Assert.Contains(displaced, canvas.Connections);
        Assert.DoesNotContain(created, canvas.Connections);
        Assert.True(fromA.IsConnected);
        Assert.True(to.IsConnected);
        Assert.False(fromB.IsConnected);
    }

    [Fact]
    public void AddConnectionCommand_WithNullToPin_CoversNullBranchesInExecuteAndUndo()
    {
        using var canvas = new CanvasViewModel();

        NodeViewModel source = new("public.orders", [("id", PinDataType.Integer)], new Point(0, 0));
        canvas.Nodes.Add(source);
        PinViewModel from = source.OutputPins.Single(p => p.Name == "id");
        var connection = new ConnectionViewModel(from, from.AbsolutePosition, new Point(10, 10));
        var sut = new AddConnectionCommand(connection);

        sut.Execute(canvas);
        Assert.Contains(connection, canvas.Connections);
        Assert.True(from.IsConnected);

        sut.Undo(canvas);
        Assert.DoesNotContain(connection, canvas.Connections);
        Assert.False(from.IsConnected);
    }

    [Fact]
    public void AddConnectionCommand_Description_WithNullToPin_StillFormatsText()
    {
        NodeViewModel source = new("public.orders", [("id", PinDataType.Integer)], new Point(0, 0));
        PinViewModel from = source.OutputPins.Single(p => p.Name == "id");
        var connection = new ConnectionViewModel(from, from.AbsolutePosition, new Point(10, 10));
        var sut = new AddConnectionCommand(connection);

        Assert.Contains("Connect id", sut.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void AddConnectionCommand_WhenAddThrowsWithoutDisplaced_CompensatesToOriginalState()
    {
        using var canvas = new CanvasViewModel();

        NodeViewModel source = new("public.orders", [("id", PinDataType.Integer)], new Point(0, 0));
        NodeViewModel sum = new(NodeDefinitionRegistry.Get(NodeType.Sum), new Point(220, 0));
        canvas.Nodes.Add(source);
        canvas.Nodes.Add(sum);

        PinViewModel from = source.OutputPins.Single(p => p.Name == "id");
        PinViewModel to = sum.InputPins.Single(p => p.Name == "value");
        var created = new ConnectionViewModel(from, from.AbsolutePosition, to.AbsolutePosition) { ToPin = to };
        var sut = new AddConnectionCommand(created);

        NotifyCollectionChangedEventHandler handler = (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Add
                && args.NewItems?.OfType<ConnectionViewModel>().Any(c => ReferenceEquals(c, created)) == true)
            {
                throw new InvalidOperationException("Injected failure during add without displaced.");
            }
        };

        canvas.Connections.CollectionChanged += handler;
        try
        {
            Assert.Throws<InvalidOperationException>(() => sut.Execute(canvas));
        }
        finally
        {
            canvas.Connections.CollectionChanged -= handler;
        }

        Assert.DoesNotContain(created, canvas.Connections);
        Assert.False(from.IsConnected);
        Assert.False(to.IsConnected);
    }

    [Fact]
    public void AddConnectionCommand_WhenCompensationOperationsThrow_SwallowsAndRethrowsOriginal()
    {
        using var canvas = new CanvasViewModel();

        NodeViewModel sourceA = new("public.orders", [("id_a", PinDataType.Integer)], new Point(0, 0));
        NodeViewModel sourceB = new("public.customers", [("id_b", PinDataType.Integer)], new Point(0, 120));
        NodeViewModel sum = new(NodeDefinitionRegistry.Get(NodeType.Sum), new Point(220, 0));
        canvas.Nodes.Add(sourceA);
        canvas.Nodes.Add(sourceB);
        canvas.Nodes.Add(sum);

        PinViewModel to = sum.InputPins.Single(p => p.Name == "value");
        PinViewModel fromA = sourceA.OutputPins.Single(p => p.Name == "id_a");
        PinViewModel fromB = sourceB.OutputPins.Single(p => p.Name == "id_b");

        var displaced = new ConnectionViewModel(fromA, fromA.AbsolutePosition, to.AbsolutePosition) { ToPin = to };
        canvas.Connections.Add(displaced);
        fromA.IsConnected = true;
        to.IsConnected = true;

        var created = new ConnectionViewModel(fromB, fromB.AbsolutePosition, to.AbsolutePosition) { ToPin = to };
        var sut = new AddConnectionCommand(created, displaced);

        NotifyCollectionChangedEventHandler handler = (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Add
                && args.NewItems?.OfType<ConnectionViewModel>().Any(c => ReferenceEquals(c, created)) == true)
            {
                throw new InvalidOperationException("Injected primary add failure.");
            }

            if (args.Action == NotifyCollectionChangedAction.Remove
                && args.OldItems?.OfType<ConnectionViewModel>().Any(c => ReferenceEquals(c, created)) == true)
            {
                throw new InvalidOperationException("Injected compensation remove failure.");
            }

            if (args.Action == NotifyCollectionChangedAction.Add
                && args.NewItems?.OfType<ConnectionViewModel>().Any(c => ReferenceEquals(c, displaced)) == true)
            {
                throw new InvalidOperationException("Injected compensation restore failure.");
            }
        };

        canvas.Connections.CollectionChanged += handler;
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => sut.Execute(canvas));
            Assert.Equal("Injected primary add failure.", ex.Message);
        }
        finally
        {
            canvas.Connections.CollectionChanged -= handler;
        }
    }

    [Fact]
    public void DeleteNodeCommand_Description_UsesNodeTitle()
    {
        NodeViewModel source = new("public.orders", [("id", PinDataType.Integer)], new Point(0, 0));
        var sut = new DeleteNodeCommand(source);

        Assert.Contains("Delete", sut.Description, StringComparison.Ordinal);
        Assert.Contains(source.Title, sut.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void DeleteNodeCommand_ExecuteAndUndo_RemovesAndRestoresNodeWithConnections()
    {
        using var canvas = new CanvasViewModel();

        NodeViewModel source = new("public.orders", [("id", PinDataType.Integer)], new Point(0, 0));
        NodeViewModel sum = new(NodeDefinitionRegistry.Get(NodeType.Sum), new Point(220, 0));
        canvas.Nodes.Add(source);
        canvas.Nodes.Add(sum);

        PinViewModel from = source.OutputPins.Single(p => p.Name == "id");
        PinViewModel to = sum.InputPins.Single(p => p.Name == "value");
        canvas.ConnectPins(from, to);
        ConnectionViewModel connection = Assert.Single(canvas.Connections);

        var sut = new DeleteNodeCommand(source);
        sut.Execute(canvas);

        Assert.DoesNotContain(source, canvas.Nodes);
        Assert.DoesNotContain(connection, canvas.Connections);

        sut.Undo(canvas);

        Assert.Contains(source, canvas.Nodes);
        Assert.Contains(connection, canvas.Connections);
    }

    [Fact]
    public void DeleteNodeCommand_WhenNodeRemovalThrows_RestoresRemovedConnections()
    {
        using var canvas = new CanvasViewModel();

        NodeViewModel source = new("public.orders", [("id", PinDataType.Integer)], new Point(0, 0));
        NodeViewModel sum = new(NodeDefinitionRegistry.Get(NodeType.Sum), new Point(220, 0));
        canvas.Nodes.Add(source);
        canvas.Nodes.Add(sum);

        PinViewModel from = source.OutputPins.Single(p => p.Name == "id");
        PinViewModel to = sum.InputPins.Single(p => p.Name == "value");
        canvas.ConnectPins(from, to);
        ConnectionViewModel initialConnection = Assert.Single(canvas.Connections);

        var sut = new DeleteNodeCommand(source);

        NotifyCollectionChangedEventHandler handler = (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Remove
                && args.OldItems?.OfType<NodeViewModel>().Any(n => ReferenceEquals(n, source)) == true)
            {
                throw new InvalidOperationException("Injected failure during node removal.");
            }
        };

        canvas.Nodes.CollectionChanged += handler;
        try
        {
            Assert.Throws<InvalidOperationException>(() => sut.Execute(canvas));
        }
        finally
        {
            canvas.Nodes.CollectionChanged -= handler;
        }

        Assert.Contains(source, canvas.Nodes);
        Assert.Contains(initialConnection, canvas.Connections);
        Assert.Single(canvas.Connections);
    }

    [Fact]
    public void DeleteNodeCommand_WhenWireRemovalThrowsBeforeNodeRemoval_DoesNotDuplicateNodeInCatchPath()
    {
        using var canvas = new CanvasViewModel();

        NodeViewModel source = new("public.orders", [("id", PinDataType.Integer), ("customer_id", PinDataType.Integer)], new Point(0, 0));
        NodeViewModel columnList = new(NodeDefinitionRegistry.Get(NodeType.ColumnList), new Point(220, 0));
        canvas.Nodes.Add(source);
        canvas.Nodes.Add(columnList);

        PinViewModel columns = columnList.InputPins.Single(p => p.Name == "columns");
        canvas.ConnectPins(source.OutputPins.Single(p => p.Name == "id"), columns);
        canvas.ConnectPins(source.OutputPins.Single(p => p.Name == "customer_id"), columns);
        List<ConnectionViewModel> wires = [.. canvas.Connections.Where(c => c.FromPin.Owner == source)];
        Assert.Equal(2, wires.Count);

        bool injected = false;
        NotifyCollectionChangedEventHandler handler = (_, args) =>
        {
            if (injected || args.Action != NotifyCollectionChangedAction.Remove)
                return;

            if (args.OldItems?[0] is ConnectionViewModel removed && ReferenceEquals(removed, wires[0]))
            {
                injected = true;
                if (canvas.Connections.Contains(wires[1]))
                    canvas.Connections.Remove(wires[1]);
                if (!canvas.Connections.Contains(wires[0]))
                    canvas.Connections.Add(wires[0]);
                throw new InvalidOperationException("Injected failure before node removal.");
            }
        };

        var sut = new DeleteNodeCommand(source);
        canvas.Connections.CollectionChanged += handler;
        try
        {
            Assert.Throws<InvalidOperationException>(() => sut.Execute(canvas));
        }
        finally
        {
            canvas.Connections.CollectionChanged -= handler;
        }

        Assert.Equal(1, canvas.Nodes.Count(n => ReferenceEquals(n, source)));
    }

    [Fact]
    public void DeleteNodeCommand_WhenNodeIsNotInCanvas_AndRemovalFails_DoesNotAddNodeInCatchPath()
    {
        using var canvas = new CanvasViewModel();

        // source is intentionally not part of canvas.Nodes
        NodeViewModel source = new("public.orders", [("id", PinDataType.Integer)], new Point(0, 0));
        NodeViewModel sum = new(NodeDefinitionRegistry.Get(NodeType.Sum), new Point(220, 0));
        canvas.Nodes.Add(sum);

        PinViewModel from = source.OutputPins.Single(p => p.Name == "id");
        PinViewModel to = sum.InputPins.Single(p => p.Name == "value");
        var connection = new ConnectionViewModel(from, from.AbsolutePosition, to.AbsolutePosition)
        {
            ToPin = to,
        };
        canvas.Connections.Add(connection);

        NotifyCollectionChangedEventHandler handler = (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Remove
                && args.OldItems?.OfType<ConnectionViewModel>().Any(c => ReferenceEquals(c, connection)) == true)
            {
                throw new InvalidOperationException("Injected failure with non-canvas source node.");
            }
        };

        var sut = new DeleteNodeCommand(source);
        canvas.Connections.CollectionChanged += handler;
        try
        {
            Assert.Throws<InvalidOperationException>(() => sut.Execute(canvas));
        }
        finally
        {
            canvas.Connections.CollectionChanged -= handler;
        }

        Assert.DoesNotContain(source, canvas.Nodes);
    }

    [Fact]
    public void DeleteConnectionCommand_Description_IsStable()
    {
        NodeViewModel source = new("public.orders", [("id", PinDataType.Integer)], new Point(0, 0));
        ConnectionViewModel connection = new(
            source.OutputPins.Single(p => p.Name == "id"),
            new Point(0, 0),
            new Point(0, 0));
        var sut = new DeleteConnectionCommand(connection);

        Assert.Equal("Delete connection", sut.Description);
    }

    [Fact]
    public void DeleteNodeCommand_ConnectionFilter_CoversFromOwnerToOwnerAndNullToPinCases()
    {
        using var canvas = new CanvasViewModel();

        NodeViewModel sourceA = new("public.orders", [("id_a", PinDataType.Integer)], new Point(0, 0));
        NodeViewModel sourceB = new("public.customers", [("id_b", PinDataType.Integer)], new Point(0, 120));
        NodeViewModel target = new(NodeDefinitionRegistry.Get(NodeType.Sum), new Point(220, 0));
        NodeViewModel other = new(NodeDefinitionRegistry.Get(NodeType.Sum), new Point(220, 140));
        canvas.Nodes.Add(sourceA);
        canvas.Nodes.Add(sourceB);
        canvas.Nodes.Add(target);
        canvas.Nodes.Add(other);

        PinViewModel targetInput = target.InputPins.Single(p => p.Name == "value");
        PinViewModel otherInput = other.InputPins.Single(p => p.Name == "value");

        var connFromOwner = new ConnectionViewModel(
            sourceA.OutputPins.Single(p => p.Name == "id_a"),
            new Point(0, 0),
            new Point(10, 10))
        {
            ToPin = targetInput,
        };
        var connToOwner = new ConnectionViewModel(
            sourceB.OutputPins.Single(p => p.Name == "id_b"),
            new Point(0, 0),
            new Point(10, 10))
        {
            ToPin = targetInput,
        };
        var connNullToPin = new ConnectionViewModel(
            sourceB.OutputPins.Single(p => p.Name == "id_b"),
            new Point(0, 0),
            new Point(0, 0));
        var connUnrelated = new ConnectionViewModel(
            sourceB.OutputPins.Single(p => p.Name == "id_b"),
            new Point(0, 0),
            new Point(10, 10))
        {
            ToPin = otherInput,
        };

        canvas.Connections.Add(connFromOwner);
        canvas.Connections.Add(connToOwner);
        canvas.Connections.Add(connNullToPin);
        canvas.Connections.Add(connUnrelated);

        var sut = new DeleteNodeCommand(target);
        sut.Execute(canvas);

        Assert.DoesNotContain(target, canvas.Nodes);
        Assert.DoesNotContain(connFromOwner, canvas.Connections);
        Assert.DoesNotContain(connToOwner, canvas.Connections);
        Assert.Contains(connNullToPin, canvas.Connections);
        Assert.Contains(connUnrelated, canvas.Connections);
    }

    [Fact]
    public void DeleteConnectionCommand_ExecuteAndUndo_UpdatesPinConnectivity()
    {
        using var canvas = new CanvasViewModel();

        NodeViewModel source = new("public.orders", [("id", PinDataType.Integer)], new Point(0, 0));
        NodeViewModel sum = new(NodeDefinitionRegistry.Get(NodeType.Sum), new Point(220, 0));
        canvas.Nodes.Add(source);
        canvas.Nodes.Add(sum);

        PinViewModel from = source.OutputPins.Single(p => p.Name == "id");
        PinViewModel to = sum.InputPins.Single(p => p.Name == "value");
        canvas.ConnectPins(from, to);
        ConnectionViewModel connection = Assert.Single(canvas.Connections);

        var sut = new DeleteConnectionCommand(connection);
        sut.Execute(canvas);

        Assert.Empty(canvas.Connections);
        Assert.False(from.IsConnected);
        Assert.False(to.IsConnected);

        sut.Undo(canvas);

        Assert.Contains(connection, canvas.Connections);
        Assert.True(from.IsConnected);
        Assert.True(to.IsConnected);
    }

    [Fact]
    public void DeleteConnectionCommand_WhenCollectionRemoveThrows_RecomputesPinStateFromCurrentGraph()
    {
        using var canvas = new CanvasViewModel();

        NodeViewModel source = new("public.orders", [("id", PinDataType.Integer)], new Point(0, 0));
        NodeViewModel sum = new(NodeDefinitionRegistry.Get(NodeType.Sum), new Point(220, 0));
        canvas.Nodes.Add(source);
        canvas.Nodes.Add(sum);

        PinViewModel from = source.OutputPins.Single(p => p.Name == "id");
        PinViewModel to = sum.InputPins.Single(p => p.Name == "value");
        canvas.ConnectPins(from, to);
        ConnectionViewModel connection = Assert.Single(canvas.Connections);

        var sut = new DeleteConnectionCommand(connection);

        NotifyCollectionChangedEventHandler handler = (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Remove
                && args.OldItems?.OfType<ConnectionViewModel>().Any(c => ReferenceEquals(c, connection)) == true)
            {
                throw new InvalidOperationException("Injected failure during connection removal.");
            }
        };

        canvas.Connections.CollectionChanged += handler;
        try
        {
            Assert.Throws<InvalidOperationException>(() => sut.Execute(canvas));
        }
        finally
        {
            canvas.Connections.CollectionChanged -= handler;
        }

        Assert.DoesNotContain(connection, canvas.Connections);
        Assert.False(from.IsConnected);
        Assert.False(to.IsConnected);
    }

    [Fact]
    public void DeleteConnectionCommand_WhenOtherConnectionsRemain_EvaluatesConnectivityPredicates()
    {
        using var canvas = new CanvasViewModel();

        NodeViewModel source = new("public.orders", [("id", PinDataType.Integer), ("customer_id", PinDataType.Integer)], new Point(0, 0));
        NodeViewModel columnList = new(NodeDefinitionRegistry.Get(NodeType.ColumnList), new Point(220, 0));
        canvas.Nodes.Add(source);
        canvas.Nodes.Add(columnList);

        PinViewModel columns = columnList.InputPins.Single(p => p.Name == "columns");
        canvas.ConnectPins(source.OutputPins.Single(p => p.Name == "id"), columns);
        canvas.ConnectPins(source.OutputPins.Single(p => p.Name == "customer_id"), columns);

        ConnectionViewModel removedConnection = canvas.Connections.Single(c => c.FromPin.Name == "id");
        var sut = new DeleteConnectionCommand(removedConnection);

        sut.Execute(canvas);

        Assert.DoesNotContain(removedConnection, canvas.Connections);
        Assert.True(columns.IsConnected);
    }

    [Fact]
    public void AddNodeCommand_Description_UsesNodeTitle()
    {
        NodeViewModel node = new("public.orders", [("id", PinDataType.Integer)], new Point(0, 0));
        var sut = new AddNodeCommand(node);

        Assert.Contains("Add", sut.Description, StringComparison.Ordinal);
        Assert.Contains(node.Title, sut.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void AddNodeCommand_ExecuteAndUndo_AddsNodeAndRemovesConnectedWires()
    {
        using var canvas = new CanvasViewModel();

        NodeViewModel source = new("public.orders", [("id", PinDataType.Integer)], new Point(0, 0));
        NodeViewModel target = new(NodeDefinitionRegistry.Get(NodeType.Sum), new Point(440, 0));
        canvas.Nodes.Add(source);
        canvas.Nodes.Add(target);

        NodeViewModel added = new(NodeDefinitionRegistry.Get(NodeType.Sum), new Point(220, 0));
        var sut = new AddNodeCommand(added);

        sut.Execute(canvas);
        Assert.Contains(added, canvas.Nodes);

        var incoming = new ConnectionViewModel(
            source.OutputPins.Single(p => p.Name == "id"),
            new Point(0, 0),
            new Point(0, 0))
        {
            ToPin = added.InputPins.Single(p => p.Name == "value"),
        };
        var outgoing = new ConnectionViewModel(
            added.OutputPins.First(),
            new Point(0, 0),
            new Point(0, 0))
        {
            ToPin = target.InputPins.Single(p => p.Name == "value"),
        };
        var dangling = new ConnectionViewModel(
            source.OutputPins.Single(p => p.Name == "id"),
            new Point(0, 0),
            new Point(10, 10));
        canvas.Connections.Add(incoming);
        canvas.Connections.Add(outgoing);
        canvas.Connections.Add(dangling);

        sut.Undo(canvas);

        Assert.DoesNotContain(added, canvas.Nodes);
        Assert.DoesNotContain(incoming, canvas.Connections);
        Assert.DoesNotContain(outgoing, canvas.Connections);
        Assert.Contains(dangling, canvas.Connections);
    }

    [Fact]
    public void AddNodeCommand_WhenDifferentNodeWithSameIdExists_ThrowsAndDoesNotMutateCanvas()
    {
        using var canvas = new CanvasViewModel();

        NodeViewModel existing = new("public.orders", [("id", PinDataType.Integer)], new Point(0, 0));
        canvas.Nodes.Add(existing);

        NodeViewModel duplicateId = new("public.customers", [("customer_id", PinDataType.Integer)], new Point(220, 0))
        {
            Id = existing.Id,
        };
        var sut = new AddNodeCommand(duplicateId);

        var ex = Assert.Throws<InvalidOperationException>(() => sut.Execute(canvas));
        Assert.Contains(existing.Id, ex.Message, StringComparison.Ordinal);
        Assert.Contains(existing, canvas.Nodes);
        Assert.DoesNotContain(duplicateId, canvas.Nodes);
        Assert.Single(canvas.Nodes);
    }

    [Fact]
    public void AddNodeCommand_WhenNodeAlreadyInCanvas_DoesNotDuplicateEntry()
    {
        using var canvas = new CanvasViewModel();

        NodeViewModel node = new("public.orders", [("id", PinDataType.Integer)], new Point(0, 0));
        canvas.Nodes.Add(node);
        var sut = new AddNodeCommand(node);

        sut.Execute(canvas);

        Assert.Single(canvas.Nodes);
        Assert.Same(node, canvas.Nodes[0]);
    }
}
