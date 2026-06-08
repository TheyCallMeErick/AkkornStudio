using System.Text.Json;
using AkkornStudio.Nodes;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Serialization;

public static partial class CanvasSerializer
{
    private static SavedViewSubgraph? BuildViewSubgraph(
        NodeViewModel node,
        Dictionary<string, string> parameters
    )
    {
        if (node.Type != NodeType.ViewDefinition)
            return null;

        parameters.TryGetValue(ViewSubgraphParameterKey, out string? payload);
        parameters.TryGetValue(ViewEditorCanvasParameterKey, out string? editorCanvasJson);

        bool hasCompiledGraph = !string.IsNullOrWhiteSpace(payload);
        bool hasEditorCanvas = !string.IsNullOrWhiteSpace(editorCanvasJson);
        if (!hasCompiledGraph && !hasEditorCanvas)
            return null;

        // Keep malformed payloads in Parameters for diagnostics/fallback compatibility.
        if (hasCompiledGraph)
        {
            try
            {
                using JsonDocument _ = JsonDocument.Parse(payload!);
            }
            catch
            {
                if (!hasEditorCanvas)
                    return null;

                payload = null;
            }
        }

        parameters.Remove(ViewSubgraphParameterKey);
        parameters.Remove(ViewEditorCanvasParameterKey);
        parameters.TryGetValue(ViewFromTableParameterKey, out string? fromTable);
        return new SavedViewSubgraph(
            string.IsNullOrWhiteSpace(payload) ? null : payload,
            string.IsNullOrWhiteSpace(fromTable) ? null : fromTable.Trim(),
            string.IsNullOrWhiteSpace(editorCanvasJson) ? null : editorCanvasJson);
    }

    private static SavedCteSubgraph? BuildCteSubgraph(
        NodeViewModel node,
        IEnumerable<NodeViewModel> allNodes,
        IEnumerable<ConnectionViewModel> allConnections
    )
    {
        if (node.Type != NodeType.CteDefinition)
            return null;

        if (node.Parameters.TryGetValue(CteSubgraphParameterKey, out string? payload)
            && !string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                SavedCteSubgraph? persisted = JsonSerializer.Deserialize<SavedCteSubgraph>(payload);
                if (persisted is not null)
                    return persisted;
            }
            catch
            {
                // Fall back to graph extraction when payload is malformed.
            }
        }

        ConnectionViewModel? queryWire = allConnections.FirstOrDefault(c =>
            c.ToPin?.Owner == node
            && c.ToPin.Name == "query"
            && c.FromPin.Owner.Type == NodeType.ResultOutput
        );
        if (queryWire?.FromPin.Owner is not NodeViewModel resultOutput)
            return null;

        HashSet<string> upstream = CollectUpstreamNodeIds(resultOutput, allConnections, includeCteDefinitions: false);

        var scopedNodes = allNodes.Where(n => upstream.Contains(n.Id)).ToList();
        var scopedIds = scopedNodes.Select(n => n.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scopedConnections = allConnections.Where(c =>
            c.ToPin is not null
            && scopedIds.Contains(c.FromPin.Owner.Id)
            && scopedIds.Contains(c.ToPin!.Owner.Id)
        );

        return new SavedCteSubgraph(
            Nodes: [.. scopedNodes.Select(n => SerialiseNode(n, scopedNodes, scopedConnections, includeCteSubgraph: false))],
            Connections: [.. scopedConnections
                .Select(SerialiseConnection)
                .Where(c => c is not null)
                .Select(c => c!)],
            ResultOutputNodeId: resultOutput.Id
        );
    }

    private static HashSet<string> CollectUpstreamNodeIds(
        NodeViewModel sinkNode,
        IEnumerable<ConnectionViewModel> allConnections,
        bool includeCteDefinitions
    )
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { sinkNode.Id };
        var queue = new Queue<string>();
        queue.Enqueue(sinkNode.Id);

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            foreach (ConnectionViewModel conn in allConnections.Where(c => c.ToPin?.Owner.Id == current))
            {
                NodeViewModel fromOwner = conn.FromPin.Owner;
                if (!includeCteDefinitions && fromOwner.Type == NodeType.CteDefinition)
                    continue;

                if (visited.Add(fromOwner.Id))
                    queue.Enqueue(fromOwner.Id);
            }
        }

        return visited;
    }

    private static void MaterializeCteSubgraphs(
        SavedCanvas saved,
        CanvasViewModel vm,
        Dictionary<string, NodeViewModel> nodeMap,
        List<string> warnings,
        IReadOnlyDictionary<string, IReadOnlyList<(string Name, PinDataType Type)>>? columnLookup
    )
    {
        foreach (SavedNode cteNode in saved.Nodes.Where(n => n.CteSubgraph is not null))
        {
            if (!nodeMap.TryGetValue(cteNode.NodeId, out NodeViewModel? cteVm))
                continue;

            if (cteVm.Type != NodeType.CteDefinition)
                continue;

            bool hasQueryWire = vm.Connections.Any(c =>
                c.ToPin?.Owner == cteVm
                && c.ToPin.Name == "query"
                && c.FromPin.Owner.Type == NodeType.ResultOutput
            );
            if (hasQueryWire)
                continue;

            SavedCteSubgraph subgraph = cteNode.CteSubgraph!;
            if (subgraph.Nodes.Count == 0)
                continue;
            if (TryGetCteSubgraphDepth(subgraph, out int depth) && depth > MaxCteSubgraphDepth)
            {
                warnings.Add(
                    $"Skipped persisted CTE subgraph for node '{cteNode.NodeId}' because nesting depth {depth} exceeds limit {MaxCteSubgraphDepth}.");
                continue;
            }

            var localIdMap = new Dictionary<string, NodeViewModel>(StringComparer.Ordinal);
            foreach (SavedNode subNode in subgraph.Nodes)
            {
                (NodeViewModel? subVm, string? skipReason) = BuildNodeVm(subNode, columnLookup);
                if (subVm is null)
                {
                    warnings.Add($"Skipped CTE subgraph node '{subNode.NodeType}' for '{cteNode.NodeId}': {skipReason ?? "Unknown error"}.");
                    continue;
                }

                while (nodeMap.ContainsKey(subVm.Id))
                    subVm.Id = Guid.NewGuid().ToString("N")[..8];

                nodeMap[subVm.Id] = subVm;
                localIdMap[subNode.NodeId] = subVm;
                vm.Nodes.Add(subVm);
            }

            foreach (SavedConnection sc in subgraph.Connections)
            {
                if (!localIdMap.TryGetValue(sc.FromNodeId, out NodeViewModel? fromNode))
                    continue;
                if (!localIdMap.TryGetValue(sc.ToNodeId, out NodeViewModel? toNode))
                    continue;

                if (!TryResolvePins(fromNode, sc.FromPinName, toNode, sc.ToPinName, out PinViewModel? fromPin, out PinViewModel? toPin))
                    continue;

                TryConnect(vm.Connections, fromPin!, toPin!);
            }

            NodeViewModel? resultVm = null;
            if (!string.IsNullOrWhiteSpace(subgraph.ResultOutputNodeId))
                localIdMap.TryGetValue(subgraph.ResultOutputNodeId, out resultVm);

            resultVm ??= localIdMap.Values.FirstOrDefault(n => n.Type == NodeType.ResultOutput);
            if (resultVm is null)
                continue;

            PinViewModel? resultPin = resultVm.OutputPins.FirstOrDefault(p => p.Name == "result");
            PinViewModel? queryPin = cteVm.InputPins.FirstOrDefault(p => p.Name == "query");
            if (resultPin is null || queryPin is null)
                continue;

            if (!TryConnect(vm.Connections, resultPin, queryPin))
                continue;

            warnings.Add($"Materialized persisted CTE subgraph for node '{cteVm.Id}'.");
        }
    }

    private static bool TryGetCteSubgraphDepth(SavedCteSubgraph subgraph, out int depth)
    {
        depth = 0;
        var stack = new Stack<(SavedCteSubgraph Graph, int Level)>();
        stack.Push((subgraph, 1));

        while (stack.Count > 0)
        {
            (SavedCteSubgraph current, int level) = stack.Pop();
            if (level > depth)
                depth = level;

            foreach (SavedNode node in current.Nodes)
            {
                if (node.CteSubgraph is not null)
                    stack.Push((node.CteSubgraph, level + 1));
            }
        }

        return true;
    }
}
