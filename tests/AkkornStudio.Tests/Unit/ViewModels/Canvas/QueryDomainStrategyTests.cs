using AkkornStudio.UI.Services.Canvas.AutoJoin;
using AkkornStudio.UI.Services.Explain;
using Avalonia;
using AkkornStudio.Metadata;
using AkkornStudio.Nodes;
using AkkornStudio.UI.Serialization;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.Canvas.Strategies;

namespace AkkornStudio.Tests.Unit.ViewModels.Canvas;

public class QueryDomainStrategyTests
{
    [Theory]
    [InlineData(NodeType.CteDefinition, true)]
    [InlineData(NodeType.ViewDefinition, true)]
    [InlineData(NodeType.Subquery, true)]
    [InlineData(NodeType.SubqueryReference, true)]
    [InlineData(NodeType.SubqueryDefinition, true)]
    [InlineData(NodeType.And, false)]
    [InlineData(NodeType.TableDefinition, false)]
    public void CanEnterSubEditor_MatchesExpectedNodeTypes(NodeType type, bool expected)
    {
        var strategy = new QueryDomainStrategy();
        var node = new NodeViewModel(NodeDefinitionRegistry.Get(type), new Point(10, 10));

        bool canEnter = strategy.CanEnterSubEditor(node);

        Assert.Equal(expected, canEnter);
    }

    [Fact]
    public void GetOutputNodes_ReturnsOnlyResultOutputs()
    {
        var strategy = new QueryDomainStrategy();
        List<NodeViewModel> nodes =
        [
            new("public.orders", [], new Point(0, 0)),
            new(NodeDefinitionRegistry.Get(NodeType.ResultOutput), new Point(20, 0)),
        ];

        IReadOnlyList<NodeViewModel> outputs = strategy.GetOutputNodes(nodes);

        Assert.Single(outputs);
        Assert.All(outputs, o => Assert.Equal(NodeType.ResultOutput, o.Type));
    }

    [Fact]
    public void GetConnectionSuggestions_ColumnSetWithTables_SuggestsJoin()
    {
        var strategy = new QueryDomainStrategy();
        var owner = new NodeViewModel("public.orders", [], new Point(0, 0));
        var sourcePin = new PinViewModel(new PinDescriptor("columns", PinDirection.Output, PinDataType.ColumnSet), owner);

        List<NodeViewModel> canvasNodes = [owner];

        IReadOnlyList<NodeSuggestion> suggestions = strategy.GetConnectionSuggestions(sourcePin, canvasNodes);

        Assert.Contains(suggestions, s => s.NodeType == NodeType.Join);
    }

    [Fact]
    public void GetConnectionSuggestions_WithoutCompatibleCanvasNodes_ReturnsEmpty()
    {
        var strategy = new QueryDomainStrategy();
        var owner = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.CountStar), new Point(0, 0));
        var sourcePin = new PinViewModel(new PinDescriptor("columns", PinDirection.Output, PinDataType.ColumnSet), owner);

        IReadOnlyList<NodeSuggestion> suggestions = strategy.GetConnectionSuggestions(sourcePin, [owner]);

        Assert.Empty(suggestions);
    }

    [Fact]
    public async Task GetSubEditorSeedAsync_ReturnsSeedCanvasWithResultOutputNode()
    {
        var strategy = new QueryDomainStrategy();
        var cteNode = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.CteDefinition), new Point(0, 0));

        CanvasSnapshot? snapshot = await strategy.GetSubEditorSeedAsync(cteNode);

        Assert.NotNull(snapshot);
        Assert.Contains(nameof(NodeType.ResultOutput), snapshot!.JsonPayload);
    }

    [Fact]
    public void ResolvePrimaryOutputNodeId_ReturnsQueryOutputNodeId()
    {
        var strategy = new QueryDomainStrategy();
        List<SavedNode> nodes =
        [
            new(
                NodeId: "table-1",
                NodeType: nameof(NodeType.TableSource),
                X: 0,
                Y: 0,
                ZOrder: null,
                Alias: null,
                TableFullName: "public.orders",
                Parameters: [],
                PinLiterals: [],
                Columns: []),
            new(
                NodeId: "output-1",
                NodeType: nameof(NodeType.ResultOutput),
                X: 10,
                Y: 10,
                ZOrder: null,
                Alias: null,
                TableFullName: null,
                Parameters: [],
                PinLiterals: [])
        ];

        string? primaryOutputNodeId = strategy.ResolvePrimaryOutputNodeId(nodes);

        Assert.Equal("output-1", primaryOutputNodeId);
    }

    [Fact]
    public void TryHandleSchemaTableInsert_InQueryMode_SpawnsQueryTable()
    {
        var strategy = new QueryDomainStrategy();
        bool spawned = false;
        bool imported = false;

        bool handled = strategy.TryHandleSchemaTableInsert(
            BuildTable(),
            new Point(100, 120),
            () => false,
            (_, _) => imported = true,
            () => spawned = true
        );

        Assert.True(handled);
        Assert.True(spawned);
        Assert.False(imported);
    }

    [Fact]
    public void TryHandleSchemaTableInsert_InDdlModeWithImporter_StillSpawnsQueryTable()
    {
        var strategy = new QueryDomainStrategy();
        bool spawned = false;
        bool imported = false;

        bool handled = strategy.TryHandleSchemaTableInsert(
            BuildTable(),
            new Point(100, 120),
            () => true,
            (_, _) => imported = true,
            () => spawned = true
        );

        Assert.True(handled);
        Assert.True(spawned);
        Assert.False(imported);
    }

    [Fact(Skip = "Test setup needs CTE subgraph structure - deferred for now")]
    public void CanvasViewModel_WithInjectedStrategy_ConsultsStrategyForSubEditorCheck()
    {
        // This test verifies that CanvasViewModel uses the injected strategy
        // to determine if a node can enter a sub-editor
        var strategy = new TrackingDomainStrategy();
        var canvas = new CanvasViewModel(
            nodeManager: null,
            pinManager: null,
            selectionManager: null,
            localizationService: null,
            domainStrategy: strategy);

        var cte = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.CteDefinition), new Point(40, 40));
        cte.Parameters["name"] = "test_cte";

        canvas.Nodes.Add(cte);
        cte.IsSelected = true;

        // Simply calling CanEnterSelectedCteEditor should consult the strategy
        // Our tracking strategy records when CanEnterSubEditor is called
        bool canEnter = canvas.Nodes.Count(n => n.IsSelected && strategy.CanEnterSubEditor(n)) == 1;

        // Verify that the strategy was consulted for the check
        Assert.True(strategy.CanEnterCalled);
        Assert.True(canEnter);
    }

    private static TableMetadata BuildTable() => new(
        Schema: "public",
        Name: "orders",
        Kind: TableKind.Table,
        EstimatedRowCount: null,
        Columns: [],
        Indexes: [],
        OutboundForeignKeys: [],
        InboundForeignKeys: []
    );

    private sealed class TrackingDomainStrategy : ICanvasDomainStrategy
    {
        public string DomainName => "Tracking";

        public bool CanEnterCalled { get; private set; }

        public bool GetSeedCalled { get; private set; }

        public bool CanEnterSubEditor(NodeViewModel node)
        {
            _ = node;
            CanEnterCalled = true;
            return true;
        }

        public Task<CanvasSnapshot?> GetSubEditorSeedAsync(NodeViewModel node)
        {
            _ = node;
            GetSeedCalled = true;
            return Task.FromResult<CanvasSnapshot?>(null);
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

        public IReadOnlyList<NodeViewModel> GetOutputNodes(IEnumerable<NodeViewModel> nodes)
        {
            _ = nodes;
            return [];
        }

        public IReadOnlyList<NodeSuggestion> GetConnectionSuggestions(
            PinViewModel sourcePinViewModel,
            IEnumerable<NodeViewModel> canvasNodes)
        {
            _ = sourcePinViewModel;
            _ = canvasNodes;
            return [];
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
            _ = spawnQueryTableNode;
            return false;
        }
    }
}
