using AkkornStudio.UI.Services.Canvas.AutoJoin;
using AkkornStudio.UI.Services.Explain;
using System.Collections.ObjectModel;
using Avalonia;
using AkkornStudio.Nodes;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.ViewModels.Canvas;

public class CanvasAutoJoinControllerOrchestrationTests
{
    [Fact]
    public void RunSelectedAutoJoin_NoSuggestions_OpensManualDialogAndWarning()
    {
        var ctx = CreateControllerContext(
            CreateTable("public.alpha", ("a_text", PinDataType.Text), ("id", PinDataType.Number)),
            CreateTable("public.beta", ("b_flag", PinDataType.Boolean), ("id", PinDataType.Number))
        );
        Assert.True(ctx.Controller.HasTwoSelectedTableSources);

        ctx.Controller.RunSelectedAutoJoin();

        Assert.True(ctx.ManualJoinDialog.IsVisible);
        Assert.True(ctx.Toasts.IsVisible);
        Assert.Equal(ToastSeverity.Warning, ctx.Toasts.Severity);
        Assert.DoesNotContain(ctx.Nodes, n => n.Type == NodeType.Join);
    }

    [Fact]
    public void RunSelectedAutoJoin_SingleSuggestion_AppliesJoinAndShowsSuccess()
    {
        var ctx = CreateControllerContext(
            CreateTable("public.orders", ("id", PinDataType.Number), ("customer_id", PinDataType.Number)),
            CreateTable("public.customers", ("id", PinDataType.Number))
        );
        Assert.True(ctx.Controller.HasTwoSelectedTableSources);

        ctx.Controller.RunSelectedAutoJoin();

        Assert.Single(ctx.Nodes, n => n.Type == NodeType.Join);
        Assert.True(ctx.Toasts.IsVisible);
        Assert.Equal(ToastSeverity.Success, ctx.Toasts.Severity);
        Assert.False(ctx.ManualJoinDialog.IsVisible);
    }

    [Fact]
    public void RunSelectedAutoJoin_WhenDuplicateJoinDetected_FallsBackToManualAndWarning()
    {
        var ctx = CreateControllerContext(
            CreateTable("public.orders", ("id", PinDataType.Number), ("customer_id", PinDataType.Number)),
            CreateTable("public.customers", ("id", PinDataType.Number))
        );
        Assert.True(ctx.Controller.HasTwoSelectedTableSources);

        ctx.Controller.RunSelectedAutoJoin();
        ctx.Controller.RunSelectedAutoJoin();

        Assert.Single(ctx.Nodes, n => n.Type == NodeType.Join);
        Assert.True(ctx.ManualJoinDialog.IsVisible);
        Assert.Equal(ToastSeverity.Warning, ctx.Toasts.Severity);
    }

    [Fact]
    public void RunSelectedAutoJoin_MultipleSuggestions_OpensManualDialogWithoutApplying()
    {
        var ctx = CreateControllerContext(
            CreateTable("public.orders", ("id", PinDataType.Number), ("customer_id", PinDataType.Number), ("region_id", PinDataType.Number)),
            CreateTable("public.customers", ("id", PinDataType.Number), ("customer_id", PinDataType.Number), ("region_id", PinDataType.Number))
        );
        Assert.True(ctx.Controller.HasTwoSelectedTableSources);

        ctx.Controller.RunSelectedAutoJoin();

        Assert.True(ctx.ManualJoinDialog.IsVisible);
        Assert.Equal(ToastSeverity.Warning, ctx.Toasts.Severity);
        Assert.DoesNotContain(ctx.Nodes, n => n.Type == NodeType.Join);
    }

    [Fact]
    public void RunSelectedAutoJoin_WhenSuggestionAnalysisThrows_FallsBackToManualDialogAndUnavailableWarning()
    {
        var ctx = CreateControllerContext(
            CreateTable("public.orders", ("id", PinDataType.Number), ("customer_id", PinDataType.Number)),
            CreateTable("public.customers", ("id", PinDataType.Number)),
            suggestionService: new ThrowingSuggestionService()
        );
        Assert.True(ctx.Controller.HasTwoSelectedTableSources);

        ctx.Controller.RunSelectedAutoJoin();

        Assert.True(ctx.ManualJoinDialog.IsVisible);
        Assert.True(ctx.Toasts.IsVisible);
        Assert.Equal(ToastSeverity.Warning, ctx.Toasts.Severity);
        Assert.DoesNotContain(ctx.Nodes, n => n.Type == NodeType.Join);
    }

    private static (CanvasAutoJoinController Controller, ObservableCollection<NodeViewModel> Nodes, ObservableCollection<ConnectionViewModel> Connections, ManualJoinDialogViewModel ManualJoinDialog, ToastCenterViewModel Toasts) CreateControllerContext(
        NodeViewModel first,
        NodeViewModel second,
        ICanvasAutoJoinSuggestionService? suggestionService = null)
    {
        first.IsSelected = true;
        second.IsSelected = true;

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
            suggestionService,
            null,
            (Func<NodeDefinition, Point, NodeViewModel>)SpawnNode,
            (Action<PinViewModel, PinViewModel>)ConnectPins,
            () => { });

        return (controller, nodes, connections, manualJoinDialog, toasts);
    }

    private static NodeViewModel CreateTable(string fullName, params (string name, PinDataType type)[] cols)
        => new(fullName, cols, new Point(100, 100));

    private sealed class ThrowingSuggestionService : ICanvasAutoJoinSuggestionService
    {
        public IReadOnlyList<JoinSuggestion> AnalyzeNewTable(string newTableFullName, IReadOnlyCollection<NodeViewModel> nodes)
            => throw new InvalidOperationException("suggestion analysis failed");

        public IReadOnlyList<JoinSuggestion> AnalyzeAllTables(IReadOnlyCollection<NodeViewModel> nodes)
            => throw new InvalidOperationException("suggestion analysis failed");

        public IReadOnlyList<JoinSuggestion> AnalyzePair(NodeViewModel left, NodeViewModel right, IReadOnlyCollection<NodeViewModel> nodes)
            => throw new InvalidOperationException("suggestion analysis failed");
    }
}

