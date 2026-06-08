using System.ComponentModel;
using System.Reflection;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.ViewModels.Canvas;

public sealed class CanvasViewModelDisposeHardeningTests
{
    [Fact]
    public void Dispose_UnsubscribesTrackedNodeHandler_EvenWhenNodeIsNoLongerInNodesCollection()
    {
        var vm = new CanvasViewModel();
        vm.InitializeDemoNodes();

        NodeViewModel detachedNode = vm.Nodes.First();
        vm.Nodes.Remove(detachedNode);

        int calls = 0;
        PropertyChangedEventHandler staleHandler = (_, _) => calls++;
        detachedNode.PropertyChanged += staleHandler;

        Dictionary<NodeViewModel, PropertyChangedEventHandler> handlers = GetNodeValidationHandlers(vm);
        handlers[detachedNode] = staleHandler;

        vm.Dispose();

        detachedNode.IsSelected = !detachedNode.IsSelected;
        Assert.Equal(0, calls);
        Assert.Empty(handlers);
    }

    private static Dictionary<NodeViewModel, PropertyChangedEventHandler> GetNodeValidationHandlers(CanvasViewModel vm)
    {
        FieldInfo? field = typeof(CanvasViewModel).GetField(
            "_nodeValidationHandlers",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        return Assert.IsType<Dictionary<NodeViewModel, PropertyChangedEventHandler>>(field!.GetValue(vm));
    }
}
