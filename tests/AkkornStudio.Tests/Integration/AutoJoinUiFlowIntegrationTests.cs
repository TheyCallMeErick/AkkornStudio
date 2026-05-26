using System.Collections.ObjectModel;
using Avalonia;



namespace Integration;

public class AutoJoinUiFlowIntegrationTests
{
    [Fact]
    public void TriggerAutoJoinAnalysis_WhenSuggestionsExist_DetailsActionOpensPrefilledManualDialog()
    {
        var ctx = CreateControllerContext(
            CreateTable("public.orders", ("id", PinDataType.Number), ("customer_id", PinDataType.Number)),
            CreateTable("public.customers", ("id", PinDataType.Number))
        );

        ctx.Controller.TriggerAutoJoinAnalysis("public.orders");

        Assert.True(ctx.Toasts.IsVisible);
        Assert.True(ctx.Toasts.HasDetailsAction);

        ctx.Toasts.ShowDetailsCommand.Execute(null);

        Assert.True(ctx.ManualJoinDialog.IsVisible);
        Assert.Contains(ctx.ManualJoinDialog.LeftTableLabel, new[] { "public.orders", "public.customers" });
        Assert.Contains(ctx.ManualJoinDialog.RightTableLabel, new[] { "public.orders", "public.customers" });
        Assert.NotEqual(ctx.ManualJoinDialog.LeftTableLabel, ctx.ManualJoinDialog.RightTableLabel);

        string? leftCol = ctx.ManualJoinDialog.SelectedLeftColumn?.Name;
        string? rightCol = ctx.ManualJoinDialog.SelectedRightColumn?.Name;
        Assert.False(string.IsNullOrWhiteSpace(leftCol));
        Assert.False(string.IsNullOrWhiteSpace(rightCol));
        Assert.Contains(leftCol!, new[] { "id", "customer_id" }, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(rightCol!, new[] { "id", "customer_id" }, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunSelectedAutoJoin_NoSuggestion_ManualDialogConfirmCreatesJoinAndSuccessToast()
    {
        var ctx = CreateControllerContext(
            CreateTable("public.alpha", ("a_text", PinDataType.Text), ("id", PinDataType.Number)),
            CreateTable("public.beta", ("b_flag", PinDataType.Boolean), ("id", PinDataType.Number))
        );

        ctx.First.IsSelected = true;
        ctx.Second.IsSelected = true;

        ctx.Controller.RunSelectedAutoJoin();

        Assert.True(ctx.ManualJoinDialog.IsVisible);
        Assert.Equal(ToastSeverity.Warning, ctx.Toasts.Severity);

        ctx.ManualJoinDialog.SelectedLeftColumn = ctx.ManualJoinDialog.LeftColumns
            .First(c => c.Name.Equals("id", StringComparison.OrdinalIgnoreCase));
        ctx.ManualJoinDialog.SelectedRightColumn = ctx.ManualJoinDialog.RightColumns
            .First(c => c.Name.Equals("id", StringComparison.OrdinalIgnoreCase));

        Assert.True(ctx.ManualJoinDialog.ConfirmCommand.CanExecute(null));
        ctx.ManualJoinDialog.ConfirmCommand.Execute(null);

        Assert.False(ctx.ManualJoinDialog.IsVisible);
        Assert.Equal(ToastSeverity.Success, ctx.Toasts.Severity);
        Assert.Single(ctx.Nodes, n => n.Type == NodeType.Join);
        Assert.Equal(2, ctx.Connections.Count(c => c.ToPin?.Owner?.Type == NodeType.Join));
    }

    [Fact]
    public void RunSelectedAutoJoin_AfterSuggestionsToastDismissed_ReopensCachedSuggestionsWithoutReaddingTable()
    {
        var ctx = CreateControllerContext(
            CreateTable("public.orders", ("id", PinDataType.Number), ("customer_id", PinDataType.Number)),
            CreateTable("public.customers", ("id", PinDataType.Number))
        );

        ctx.Controller.TriggerAutoJoinAnalysis("public.orders");
        Assert.True(ctx.Toasts.IsVisible);

        ctx.Toasts.DismissCommand.Execute(null);
        Assert.False(ctx.Toasts.IsVisible);

        ctx.Controller.RunSelectedAutoJoin();

        Assert.True(ctx.ManualJoinDialog.IsVisible);
        Assert.Contains(ctx.ManualJoinDialog.LeftTableLabel, new[] { "public.orders", "public.customers" });
        Assert.Contains(ctx.ManualJoinDialog.RightTableLabel, new[] { "public.orders", "public.customers" });
    }

    private static (CanvasAutoJoinController Controller, NodeViewModel First, NodeViewModel Second, ObservableCollection<NodeViewModel> Nodes, ObservableCollection<ConnectionViewModel> Connections, ManualJoinDialogViewModel ManualJoinDialog, ToastCenterViewModel Toasts) CreateControllerContext(
        NodeViewModel first,
        NodeViewModel second)
    {
        var nodes = new ObservableCollection<NodeViewModel> { first, second };
        var connections = new ObservableCollection<ConnectionViewModel>();
        var autoJoin = new AutoJoinOverlayViewModel();
        var manualJoinDialog = new ManualJoinDialogViewModel(LocalizationService.Instance);
        var toasts = new ToastCenterViewModel();

        NodeViewModel SpawnNode(NodeDefinition def, Point pos)
        {
            var node = new NodeViewModel(def, pos)
            {
                ZOrder = nodes.Count == 0 ? 0 : nodes.Max(n => n.ZOrder) + 1,
            };
            nodes.Add(node);
            return node;
        }

        void ConnectPins(PinViewModel from, PinViewModel to)
        {
            var conn = new ConnectionViewModel(from, new Point(0, 0), new Point(0, 0))
            {
                ToPin = to,
            };
            connections.Add(conn);
        }

        var controller = new CanvasAutoJoinController(
            nodes,
            connections,
            autoJoin,
            manualJoinDialog,
            toasts,
            LocalizationService.Instance,
            null,
            null,
            null,
            SpawnNode,
            ConnectPins,
            () => { });

        return (controller, first, second, nodes, connections, manualJoinDialog, toasts);
    }

    private static NodeViewModel CreateTable(string fullName, params (string name, PinDataType type)[] cols)
        => new(fullName, cols, new Point(120, 120));
}
