using AkkornStudio.UI.ViewModels;
using System.Reflection;

namespace AkkornStudio.Tests.Unit.ViewModels.Canvas;

public sealed class WireSelectionStateTests
{
    [Fact]
    public void SelectConnection_EnsuresExclusiveWireSelection_AndClearsNodeSelection()
    {
        var vm = new CanvasViewModel();
        vm.InitializeDemoNodes();

        vm.Nodes[0].IsSelected = true;
        ConnectionViewModel selected = vm.Connections.First(c => c.ToPin is not null);
        ConnectionViewModel other = vm.Connections.First(c => !ReferenceEquals(c, selected) && c.ToPin is not null);

        vm.SelectConnection(selected);

        Assert.Same(selected, vm.SelectedConnection);
        Assert.True(selected.IsSelected);
        Assert.False(other.IsSelected);
        Assert.DoesNotContain(vm.Nodes, n => n.IsSelected);
    }

    [Fact]
    public void DeselectAll_ClearsSelectedConnection()
    {
        var vm = new CanvasViewModel();
        vm.InitializeDemoNodes();

        ConnectionViewModel selected = vm.Connections.First(c => c.ToPin is not null);
        vm.SelectConnection(selected);

        vm.DeselectAll();

        Assert.Null(vm.SelectedConnection);
        Assert.DoesNotContain(vm.Connections, c => c.IsSelected);
    }

    [Fact]
    public void DeleteSelectedConnection_RemovesWire_AndResetsSelection()
    {
        var vm = new CanvasViewModel();
        vm.InitializeDemoNodes();

        ConnectionViewModel selected = vm.Connections.First(c => c.ToPin is not null);
        vm.SelectConnection(selected);

        bool deleted = vm.DeleteSelectedConnection();

        Assert.True(deleted);
        Assert.Null(vm.SelectedConnection);
        Assert.DoesNotContain(vm.Connections, c => ReferenceEquals(c, selected));
    }

    [Fact]
    public void ClearConnectionSelection_ClearsOrphanSelectedBreakpointWireOutsideConnections()
    {
        var vm = new CanvasViewModel();
        vm.InitializeDemoNodes();

        ConnectionViewModel orphanWire = vm.Connections.First(c => c.ToPin is not null);
        orphanWire.IsSelected = true;

        vm.Connections.Remove(orphanWire);
        SetPrivateField<ConnectionViewModel?>(vm, "_selectedConnection", null);
        SetPrivateField(vm, "_selectedBreakpointConnection", orphanWire);
        SetPrivateField(vm, "_selectedBreakpointIndex", 0);

        vm.ClearConnectionSelection();

        Assert.False(orphanWire.IsSelected);
        Assert.Null(vm.SelectedConnection);
        Assert.Null(vm.SelectedBreakpointConnection);
        Assert.Equal(-1, vm.SelectedBreakpointIndex);
    }

    private static void SetPrivateField<T>(CanvasViewModel vm, string fieldName, T value)
    {
        FieldInfo? field = typeof(CanvasViewModel).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(vm, value);
    }
}
