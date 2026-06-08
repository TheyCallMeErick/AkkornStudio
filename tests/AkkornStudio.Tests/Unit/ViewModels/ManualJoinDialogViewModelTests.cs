using AkkornStudio.UI.Services.Benchmark;
using Avalonia;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.ViewModels;

public class ManualJoinDialogViewModelTests
{
    [Fact]
    public void Open_PicksReasonableDefaults_AndConfirmRaisesEvent()
    {
        var left = new NodeViewModel("public.orders", [("customer_id", PinDataType.Number)], new Point(0, 0));
        var right = new NodeViewModel("public.customers", [("id", PinDataType.Number)], new Point(100, 0));
        var vm = new ManualJoinDialogViewModel();

        ManualJoinRequest? received = null;
        vm.Confirmed += (_, req) => received = req;

        vm.Open(left, right);

        Assert.True(vm.IsVisible);
        Assert.Equal("customer_id", vm.SelectedLeftColumn?.Name);
        Assert.Equal("id", vm.SelectedRightColumn?.Name);
        Assert.Equal("INNER", vm.SelectedJoinType);

        vm.ConfirmCommand.Execute(null);

        Assert.False(vm.IsVisible);
        Assert.NotNull(received);
        Assert.Equal("customer_id", received!.LeftColumn);
        Assert.Equal("id", received.RightColumn);
    }

    [Fact]
    public void Open_FiltersRightColumnsByCompatiblePinType()
    {
        var canvas = new CanvasViewModel();
        var left = canvas.SpawnTableNode("public.orders", [("customer_id", PinDataType.Number)], new Point(0, 0));
        var right = canvas.SpawnTableNode("public.customers", [("name", PinDataType.Text), ("id", PinDataType.Number)], new Point(100, 0));
        var vm = new ManualJoinDialogViewModel();

        vm.Open(left, right);

        Assert.Contains(vm.RightColumns, c => c.Name == "id");
        Assert.DoesNotContain(vm.RightColumns, c => c.Name == "name");
        Assert.True(vm.HasCompatibleRightColumns);
    }

    [Fact]
    public void Open_WhenLeftIsNumber_FiltersOutIntegerNarrowingTargets()
    {
        var canvas = new CanvasViewModel();
        var left = canvas.SpawnTableNode("public.orders", [("customer_id", PinDataType.Number)], new Point(0, 0));
        var right = canvas.SpawnTableNode("public.customers", [("id_int", PinDataType.Integer), ("id_num", PinDataType.Number)], new Point(100, 0));
        var vm = new ManualJoinDialogViewModel();

        vm.Open(left, right);

        vm.SelectedLeftColumn = vm.LeftColumns.First(c => c.Name.Equals("customer_id", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(vm.RightColumns, c => c.Name.Equals("id_int", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(vm.RightColumns, c => c.Name.Equals("id_num", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Open_WhenLeftIsInteger_AllowsWiderNumericTargets()
    {
        var canvas = new CanvasViewModel();
        var left = canvas.SpawnTableNode("public.orders", [("customer_id", PinDataType.Integer)], new Point(0, 0));
        var right = canvas.SpawnTableNode("public.customers", [("id_num", PinDataType.Number), ("id_dec", PinDataType.Decimal)], new Point(100, 0));
        var vm = new ManualJoinDialogViewModel();

        vm.Open(left, right);

        vm.SelectedLeftColumn = vm.LeftColumns.First(c => c.Name.Equals("customer_id", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(vm.RightColumns, c => c.Name.Equals("id_num", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(vm.RightColumns, c => c.Name.Equals("id_dec", StringComparison.OrdinalIgnoreCase));
        Assert.True(vm.ConfirmCommand.CanExecute(null));
    }
}
