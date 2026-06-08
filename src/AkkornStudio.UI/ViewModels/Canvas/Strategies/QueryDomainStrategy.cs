using System.Text.Json;
using Avalonia;
using AkkornStudio.Metadata;
using AkkornStudio.Nodes;
using AkkornStudio.UI.Serialization;

namespace AkkornStudio.UI.ViewModels.Canvas.Strategies;

public sealed class QueryDomainStrategy : ICanvasDomainStrategy, ICanvasDomainStrategyExt
{
    public QueryDomainStrategy(
        Func<bool>? isDdlModeActiveResolver = null,
        Action<TableMetadata, Point>? importDdlTableAction = null
    )
    {
        _ = isDdlModeActiveResolver;
        _ = importDdlTableAction;
    }

    public string DomainName => "Query";

    public bool CanEnterSubEditor(NodeViewModel node)
        => node.Type is NodeType.CteDefinition
            or NodeType.ViewDefinition
            or NodeType.Subquery
            or NodeType.SubqueryReference
            or NodeType.SubqueryDefinition;

    public Task<CanvasSnapshot?> GetSubEditorSeedAsync(NodeViewModel node)
    {
        _ = node;
        return Task.FromResult<CanvasSnapshot?>(new(BuildSeedCanvasJson()));
    }

    public void OnConnectionEstablished(
        ConnectionViewModel connection,
        IEnumerable<ConnectionViewModel> allConnections,
        IEnumerable<NodeViewModel> allNodes
    )
    {
        _ = connection;
        ResyncCteSources(allConnections, allNodes);
    }

    public void OnConnectionRemoved(
        ConnectionViewModel connection,
        IEnumerable<ConnectionViewModel> allConnections,
        IEnumerable<NodeViewModel> allNodes
    )
    {
        _ = connection;
        ResyncCteSources(allConnections, allNodes);
    }

    public void OnNodeAdded(NodeViewModel node, IEnumerable<ConnectionViewModel> allConnections)
    {
        _ = node;
        _ = allConnections;
    }

    public void OnParameterChanged(
        NodeViewModel node,
        string paramName,
        IEnumerable<ConnectionViewModel> allConnections,
        IEnumerable<NodeViewModel> allNodes)
    {
        _ = node;
        _ = paramName;
        ResyncCteSources(allConnections, allNodes);
    }

    public IReadOnlyList<NodeViewModel> GetOutputNodes(IEnumerable<NodeViewModel> nodes)
        => [.. nodes.Where(IsQueryOutputNode)];

    public IReadOnlyList<NodeSuggestion> GetConnectionSuggestions(
        PinViewModel sourcePinViewModel,
        IEnumerable<NodeViewModel> canvasNodes
    )
    {
        bool hasCandidateTable = canvasNodes.Any(n => n.IsTableSource);
        if (!hasCandidateTable || sourcePinViewModel.EffectiveDataType != PinDataType.ColumnSet)
            return [];

        return [new NodeSuggestion(NodeType.Join, "Column set can flow into a JOIN node")];
    }

    public bool TryHandleSchemaTableInsert(
        TableMetadata table,
        Point position,
        Func<bool>? isDdlModeActiveResolver,
        Action<TableMetadata, Point>? importDdlTableAction,
        Action spawnQueryTableNode
    )
    {
        _ = table;
        _ = position;
        _ = isDdlModeActiveResolver;
        _ = importDdlTableAction;

        spawnQueryTableNode();
        return true;
    }

    public (List<SavedNode> Nodes, List<SavedConnection> Connections) ExtractCteEditableSubgraph(
        NodeViewModel cteNode,
        IEnumerable<NodeViewModel> allNodes,
        IEnumerable<ConnectionViewModel> allConnections
    )
    {
        if (TryReadPersistedCteSubgraph(cteNode, out SavedCteSubgraph? persisted) && persisted is not null)
            return (persisted.Nodes, persisted.Connections);

        var connections = allConnections.ToList();
        ConnectionViewModel? queryWire = connections.FirstOrDefault(c =>
            c.ToPin?.Owner == cteNode
            && c.ToPin.Name == "query"
            && c.FromPin.Owner.IsResultOutput
        );

        if (queryWire?.FromPin.Owner is not NodeViewModel resultOutput)
            return ([], []);

        HashSet<string> upstreamIds = CollectUpstreamNodeIds(
            resultOutput,
            connections,
            includeSubEditorDefinitions: false
        );
        List<NodeViewModel> subgraphNodes = allNodes
            .Where(n => upstreamIds.Contains(n.Id) && n != cteNode)
            .ToList();

        List<ConnectionViewModel> subgraphConnections = connections
            .Where(c =>
                c.ToPin is not null
                && upstreamIds.Contains(c.FromPin.Owner.Id)
                && upstreamIds.Contains(c.ToPin.Owner.Id)
            )
            .ToList();

        return CanvasSerializer.SerialiseSubgraph(subgraphNodes, subgraphConnections);
    }

    public void RemoveExistingCteQuerySubgraph(
        NodeViewModel cteNode,
        System.Collections.ObjectModel.ObservableCollection<NodeViewModel> allNodes,
        System.Collections.ObjectModel.ObservableCollection<ConnectionViewModel> allConnections
    )
    {
        ConnectionViewModel? queryWire = allConnections.FirstOrDefault(c =>
            c.ToPin?.Owner == cteNode
            && c.ToPin.Name == "query"
            && c.FromPin.Owner.IsResultOutput
        );

        if (queryWire?.FromPin.Owner is not NodeViewModel resultOutput)
            return;

        HashSet<string> removeNodeIds = CollectUpstreamNodeIds(
            resultOutput,
            allConnections,
            includeSubEditorDefinitions: false
        );

        foreach (ConnectionViewModel conn in allConnections
                     .Where(c =>
                         c.ToPin is not null
                         && (
                             removeNodeIds.Contains(c.FromPin.Owner.Id)
                             || removeNodeIds.Contains(c.ToPin.Owner.Id)
                             || (
                                 c.FromPin.Owner == resultOutput
                                 && c.ToPin.Owner == cteNode
                                 && c.ToPin.Name == "query"
                             )
                         )
                     )
                     .ToList())
        {
            allConnections.Remove(conn);
        }

        foreach (NodeViewModel node in allNodes.Where(n => removeNodeIds.Contains(n.Id)).ToList())
            allNodes.Remove(node);
    }

    public string? ResolvePrimaryOutputNodeId(IEnumerable<SavedNode> nodes)
        => nodes.FirstOrDefault(n => IsQueryOutputNodeType(n.NodeType))?.NodeId;

    private static HashSet<string> CollectUpstreamNodeIds(
        NodeViewModel sinkNode,
        IEnumerable<ConnectionViewModel> allConnections,
        bool includeSubEditorDefinitions
    )
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { sinkNode.Id };
        var queue = new Queue<string>();
        queue.Enqueue(sinkNode.Id);

        List<ConnectionViewModel> connections = [.. allConnections];
        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            foreach (ConnectionViewModel conn in connections.Where(c => c.ToPin?.Owner.Id == current))
            {
                NodeViewModel fromOwner = conn.FromPin.Owner;
                if (!includeSubEditorDefinitions && fromOwner.Type == NodeType.CteDefinition)
                    continue;

                if (visited.Add(fromOwner.Id))
                    queue.Enqueue(fromOwner.Id);
            }
        }

        return visited;
    }

    private static bool TryReadPersistedCteSubgraph(NodeViewModel cteNode, out SavedCteSubgraph? subgraph)
    {
        subgraph = null;

        if (!cteNode.Parameters.TryGetValue(CanvasSerializer.CteSubgraphParameterKey, out string? payload)
            || string.IsNullOrWhiteSpace(payload))
            return false;

        try
        {
            subgraph = JsonSerializer.Deserialize<SavedCteSubgraph>(payload);
            return subgraph is not null;
        }
        catch
        {
            return false;
        }
    }

    private static void ResyncCteSources(
        IEnumerable<ConnectionViewModel> allConnections,
        IEnumerable<NodeViewModel> allNodes)
    {
        ConnectionViewModel[] connections = [.. allConnections];
        foreach (NodeViewModel node in allNodes.Where(n => n.Type == NodeType.CteSource))
            node.SyncCteSourceColumns(connections);
    }

    private static bool IsQueryOutputNode(NodeViewModel node)
        => node.IsResultOutput;

    private static bool IsQueryOutputNodeType(string nodeType)
        => string.Equals(nodeType, nameof(NodeType.ResultOutput), StringComparison.OrdinalIgnoreCase);

    private static string BuildSeedCanvasJson()
    {
        NodeViewModel seedResult = new(NodeDefinitionRegistry.Get(NodeType.ResultOutput), new Point(260, 140));
        (List<SavedNode> nodes, List<SavedConnection> connections) = CanvasSerializer.SerialiseSubgraph([seedResult], []);

        var saved = new SavedCanvas(
            Version: CanvasSerializer.CurrentCanvasSchemaVersion,
            DatabaseProvider: "Postgres",
            ConnectionName: "sub-editor",
            Zoom: 1.0,
            PanX: 0,
            PanY: 0,
            Nodes: nodes,
            Connections: connections,
            SelectBindings: [],
            WhereBindings: [],
            AppVersion: CanvasSerializer.AppVersion,
            CreatedAt: DateTime.UtcNow.ToString("o"),
            Description: "Query sub-editor"
        );

        return JsonSerializer.Serialize(saved);
    }
}
