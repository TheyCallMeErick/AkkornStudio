using Avalonia;
using AkkornStudio.Metadata;
using AkkornStudio.Nodes;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.Canvas.Strategies;

namespace AkkornStudio.Tests.Unit.ViewModels.Canvas;

public sealed class CanvasViewModelSchemaTableInsertHardeningTests
{
    [Fact]
    public void TryInsertSchemaTableNode_WhenDomainDoesNotHandle_ShowsWarningAndAddsDiagnostic()
    {
        var strategy = new StubSchemaInsertDomainStrategy
        {
            DomainName = "StubDomain",
            ShouldHandle = false,
        };
        var vm = new CanvasViewModel(
            nodeManager: null,
            pinManager: null,
            selectionManager: null,
            localizationService: null,
            domainStrategy: strategy);
        int diagnosticsBefore = vm.Diagnostics.SnapshotEntries().Count;

        bool inserted = vm.TryInsertSchemaTableNode("public.orders", new Point(40, 80));

        Assert.False(inserted);
        Assert.False(vm.IsDirty);
        Assert.True(vm.Toasts.IsVisible);
        Assert.Equal(ToastSeverity.Warning, vm.Toasts.Severity);
        Assert.Contains("public.orders", vm.Toasts.Details ?? string.Empty, StringComparison.Ordinal);

        IReadOnlyList<AppDiagnosticEntry> diagnostics = vm.Diagnostics.SnapshotEntries();
        Assert.True(diagnostics.Count > diagnosticsBefore);
        Assert.Contains(
            diagnostics,
            entry =>
                entry.Status == DiagnosticStatus.Warning
                && entry.Details.Contains("public.orders", StringComparison.Ordinal)
                && entry.Details.Contains("StubDomain", StringComparison.Ordinal));
    }

    [Fact]
    public void TryInsertSchemaTableNode_WhenDomainHandles_MarksCanvasAsDirty()
    {
        var strategy = new StubSchemaInsertDomainStrategy
        {
            DomainName = "StubDomain",
            ShouldHandle = true,
        };
        var vm = new CanvasViewModel(
            nodeManager: null,
            pinManager: null,
            selectionManager: null,
            localizationService: null,
            domainStrategy: strategy);

        bool inserted = vm.TryInsertSchemaTableNode("public.orders", new Point(40, 80));

        Assert.True(inserted);
        Assert.True(vm.IsDirty);
        Assert.Single(vm.Nodes);
    }

    private sealed class StubSchemaInsertDomainStrategy : ICanvasDomainStrategy
    {
        public string DomainName { get; set; } = "Stub";
        public bool ShouldHandle { get; set; }

        public bool CanEnterSubEditor(NodeViewModel node)
        {
            _ = node;
            return false;
        }

        public Task<CanvasSnapshot?> GetSubEditorSeedAsync(NodeViewModel node)
        {
            _ = node;
            return Task.FromResult<CanvasSnapshot?>(null);
        }

        public IReadOnlyList<NodeSuggestion> GetConnectionSuggestions(
            PinViewModel sourcePinViewModel,
            IEnumerable<NodeViewModel> canvasNodes)
        {
            _ = sourcePinViewModel;
            _ = canvasNodes;
            return [];
        }

        public IReadOnlyList<NodeViewModel> GetOutputNodes(IEnumerable<NodeViewModel> nodes)
        {
            _ = nodes;
            return [];
        }

        public void OnConnectionEstablished(
            ConnectionViewModel connection,
            IEnumerable<ConnectionViewModel> allConnections,
            IEnumerable<NodeViewModel> allNodes)
        {
            _ = connection;
            _ = allConnections;
            _ = allNodes;
        }

        public void OnConnectionRemoved(
            ConnectionViewModel connection,
            IEnumerable<ConnectionViewModel> allConnections,
            IEnumerable<NodeViewModel> allNodes)
        {
            _ = connection;
            _ = allConnections;
            _ = allNodes;
        }

        public void OnNodeAdded(NodeViewModel node, IEnumerable<ConnectionViewModel> allConnections)
        {
            _ = node;
            _ = allConnections;
        }

        public bool TryHandleSchemaTableInsert(
            TableMetadata table,
            Point position,
            Func<bool>? isDdlModeActiveResolver,
            Action<TableMetadata, Point>? importDdlTableAction,
            Action spawnQueryTableNode)
        {
            _ = table;
            _ = position;
            _ = isDdlModeActiveResolver;
            _ = importDdlTableAction;

            if (!ShouldHandle)
                return false;

            spawnQueryTableNode();
            return true;
        }
    }
}
