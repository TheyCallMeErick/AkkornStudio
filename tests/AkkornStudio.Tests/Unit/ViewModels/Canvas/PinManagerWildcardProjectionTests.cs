using System.Collections.Specialized;
using AkkornStudio.UI.Services.Canvas.AutoJoin;
using AkkornStudio.UI.Services.Explain;
using Avalonia;
using AkkornStudio.Nodes;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.ViewModels.Canvas;

public class PinManagerWildcardProjectionTests
{
    [Fact]
    public void ConnectPins_WildcardToColumnList_RemovesSameTableColumnWiresAndKeepsOnlyWildcard()
    {
        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();

        NodeViewModel orders = new("public.orders", [("id", PinDataType.Number), ("customer_id", PinDataType.Number)], new Point(0, 0));
        NodeViewModel columnList = new(NodeDefinitionRegistry.Get(NodeType.ColumnList), new Point(180, 0));

        canvas.Nodes.Add(orders);
        canvas.Nodes.Add(columnList);

        PinViewModel columnsPin = columnList.InputPins.First(p => p.Name == "columns");
        canvas.ConnectPins(orders.OutputPins.First(p => p.Name == "id"), columnsPin);
        canvas.ConnectPins(orders.OutputPins.First(p => p.Name == "customer_id"), columnsPin);

        Assert.Equal(2, canvas.Connections.Count(c =>
            c.ToPin?.Owner == columnList
            && c.ToPin.Name == "columns"
            && c.FromPin.Owner == orders));

        canvas.UndoRedo.Clear();
        canvas.ConnectPins(orders.OutputPins.First(p => p.Name == "*"), columnsPin);

        List<ConnectionViewModel> remaining = [.. canvas.Connections.Where(c =>
            c.ToPin?.Owner == columnList
            && c.ToPin.Name == "columns"
            && c.FromPin.Owner == orders)];

        Assert.Single(remaining);
        Assert.Equal("*", remaining[0].FromPin.Name);

        canvas.UndoRedo.Undo();

        List<ConnectionViewModel> restored = [.. canvas.Connections.Where(c =>
            c.ToPin?.Owner == columnList
            && c.ToPin.Name == "columns"
            && c.FromPin.Owner == orders)];

        Assert.Equal(2, restored.Count);
        Assert.DoesNotContain(restored, c => c.FromPin.Name == "*");
    }

    [Fact]
    public void ConnectPins_WildcardToColumnList_PreservesConnectionsFromOtherTables()
    {
        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();

        NodeViewModel orders = new("public.orders", [("id", PinDataType.Number)], new Point(0, 0));
        NodeViewModel customers = new("public.customers", [("id", PinDataType.Number)], new Point(0, 90));
        NodeViewModel columnList = new(NodeDefinitionRegistry.Get(NodeType.ColumnList), new Point(240, 0));

        canvas.Nodes.Add(orders);
        canvas.Nodes.Add(customers);
        canvas.Nodes.Add(columnList);

        PinViewModel columnsPin = columnList.InputPins.First(p => p.Name == "columns");
        canvas.ConnectPins(orders.OutputPins.First(p => p.Name == "id"), columnsPin);
        canvas.ConnectPins(customers.OutputPins.First(p => p.Name == "id"), columnsPin);

        canvas.ConnectPins(orders.OutputPins.First(p => p.Name == "*"), columnsPin);

        Assert.DoesNotContain(canvas.Connections, c =>
            c.ToPin?.Owner == columnList
            && c.FromPin.Owner == orders
            && c.FromPin.Name == "id");
        Assert.Contains(canvas.Connections, c =>
            c.ToPin?.Owner == columnList
            && c.FromPin.Owner == orders
            && c.FromPin.Name == "*");
        Assert.Contains(canvas.Connections, c =>
            c.ToPin?.Owner == columnList
            && c.FromPin.Owner == customers
            && c.FromPin.Name == "id");
    }

    [Fact]
    public void ConnectPins_WhenPruneMutationFails_RollsBackToOriginalConnections()
    {
        var canvas = new CanvasViewModel();
        canvas.Nodes.Clear();
        canvas.Connections.Clear();

        NodeViewModel orders = new("public.orders", [("id", PinDataType.Number), ("customer_id", PinDataType.Number)], new Point(0, 0));
        NodeViewModel columnList = new(NodeDefinitionRegistry.Get(NodeType.ColumnList), new Point(180, 0));

        canvas.Nodes.Add(orders);
        canvas.Nodes.Add(columnList);

        PinViewModel columnsPin = columnList.InputPins.First(p => p.Name == "columns");
        canvas.ConnectPins(orders.OutputPins.First(p => p.Name == "id"), columnsPin);
        canvas.ConnectPins(orders.OutputPins.First(p => p.Name == "customer_id"), columnsPin);

        List<ConnectionViewModel> before = [.. canvas.Connections.Where(c =>
            c.ToPin?.Owner == columnList
            && c.ToPin.Name == "columns"
            && c.FromPin.Owner == orders)];
        Assert.Equal(2, before.Count);

        bool injectedFailureThrown = false;
        NotifyCollectionChangedEventHandler handler = (_, args) =>
        {
            if (injectedFailureThrown)
                return;

            if (args.Action != NotifyCollectionChangedAction.Remove)
                return;

            if (args.OldItems is null || args.OldItems.Count == 0)
                return;

            if (args.OldItems[0] is ConnectionViewModel removed
                && removed.FromPin.Owner == orders
                && removed.FromPin.Name == "id")
            {
                injectedFailureThrown = true;
                throw new InvalidOperationException("Injected failure while pruning wildcard projection connections.");
            }
        };

        canvas.Connections.CollectionChanged += handler;
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                canvas.ConnectPins(orders.OutputPins.First(p => p.Name == "*"), columnsPin));
        }
        finally
        {
            canvas.Connections.CollectionChanged -= handler;
        }

        List<ConnectionViewModel> after = [.. canvas.Connections.Where(c =>
            c.ToPin?.Owner == columnList
            && c.ToPin.Name == "columns"
            && c.FromPin.Owner == orders)];

        Assert.Equal(2, after.Count);
        Assert.Contains(after, c => c.FromPin.Name == "id");
        Assert.Contains(after, c => c.FromPin.Name == "customer_id");
        Assert.DoesNotContain(after, c => c.FromPin.Name == "*");
    }
}
