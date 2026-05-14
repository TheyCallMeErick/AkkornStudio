namespace AkkornStudio.UI.Services.QueryPreview;

internal sealed class QueryCompilationSourceResolver(
    CanvasViewModel canvas,
    Func<NodeViewModel, IReadOnlyDictionary<string, string>, string?> resolveCteSourceReference)
{
    private readonly CanvasViewModel _canvas = canvas;
    private readonly Func<NodeViewModel, IReadOnlyDictionary<string, string>, string?> _resolveCteSourceReference = resolveCteSourceReference;

    public (string FromTable, string? Warning) ResolveFromTable(
        IReadOnlyList<NodeViewModel> tableNodes,
        IReadOnlyList<NodeViewModel> cteSourceNodes,
        IReadOnlyList<NodeViewModel> subqueryNodes,
        IReadOnlyDictionary<string, string> cteDefinitionNamesById)
    {
        NodeViewModel? resultOutputNode = ResolvePrimaryResultOutputNode();
        (IReadOnlyList<NodeViewModel> upstreamTables, IReadOnlyList<NodeViewModel> upstreamCtes, IReadOnlyList<NodeViewModel> upstreamSubqueries) =
            resultOutputNode is null
                ? ([], [], [])
                : FilterUpstreamSources(resultOutputNode, tableNodes, cteSourceNodes, subqueryNodes);

        NodeViewModel? preferredUpstream = ResolvePreferredSource(upstreamTables, upstreamCtes, upstreamSubqueries);
        if (preferredUpstream is not null)
        {
            if (preferredUpstream.Type == NodeType.TableSource)
                return (BuildTableFromReference(preferredUpstream), null);

            if (preferredUpstream.Type == NodeType.CteSource)
            {
                string? cteReference = _resolveCteSourceReference(preferredUpstream, cteDefinitionNamesById);
                if (!string.IsNullOrWhiteSpace(cteReference))
                    return (cteReference, null);
            }

            if (preferredUpstream.Type is NodeType.Subquery or NodeType.SubqueryReference)
            {
                (string? subqueryFrom, string? warning) = ResolveSubqueryFromSource(preferredUpstream);
                if (!string.IsNullOrWhiteSpace(subqueryFrom))
                    return (subqueryFrom, warning);
            }
        }

        if (upstreamTables.Count > 0)
            return (BuildTableFromReference(upstreamTables[0]), null);

        if (upstreamCtes.Count > 0)
        {
            NodeViewModel cte = upstreamCtes[0];
            string? cteReference = _resolveCteSourceReference(cte, cteDefinitionNamesById);
            if (!string.IsNullOrWhiteSpace(cteReference))
                return (cteReference, null);
        }

        if (upstreamSubqueries.Count > 0)
        {
            (string? subqueryFrom, string? warning) = ResolveSubqueryFromSource(upstreamSubqueries[0]);
            if (!string.IsNullOrWhiteSpace(subqueryFrom))
                return (subqueryFrom, warning);

            return ("cte_name", warning);
        }

        if (tableNodes.Count > 0)
            return (BuildTableFromReference(tableNodes[0]), null);

        if (cteSourceNodes.Count > 0)
        {
            NodeViewModel cte = cteSourceNodes[0];
            string? cteReference = _resolveCteSourceReference(cte, cteDefinitionNamesById);
            if (!string.IsNullOrWhiteSpace(cteReference))
                return (cteReference, null);
        }

        if (subqueryNodes.Count > 0)
        {
            (string? subqueryFrom, string? warning) = ResolveSubqueryFromSource(subqueryNodes[0]);
            if (!string.IsNullOrWhiteSpace(subqueryFrom))
                return (subqueryFrom, warning);

            return ("cte_name", warning);
        }

        return ("cte_name", null);
    }

    private static NodeViewModel? ResolvePreferredSource(
        IReadOnlyList<NodeViewModel> upstreamTables,
        IReadOnlyList<NodeViewModel> upstreamCtes,
        IReadOnlyList<NodeViewModel> upstreamSubqueries)
    {
        return upstreamTables.FirstOrDefault(static node => node.IsPrimaryFromSource)
            ?? upstreamCtes.FirstOrDefault(static node => node.IsPrimaryFromSource)
            ?? upstreamSubqueries.FirstOrDefault(static node => node.IsPrimaryFromSource);
    }

    private static string BuildTableFromReference(NodeViewModel tableNode)
    {
        string source = !string.IsNullOrWhiteSpace(tableNode.Subtitle)
            ? tableNode.Subtitle!.Trim()
            : tableNode.Title.Trim();

        string? alias = ResolveExplicitTableAlias(tableNode);
        return string.IsNullOrWhiteSpace(alias) ? source : $"{source} {alias}";
    }

    private static string? ResolveExplicitTableAlias(NodeViewModel tableNode)
    {
        if (!string.IsNullOrWhiteSpace(tableNode.Alias))
            return tableNode.Alias!.Trim();

        if (tableNode.Parameters.TryGetValue("alias", out string? aliasParam)
            && !string.IsNullOrWhiteSpace(aliasParam))
        {
            return aliasParam.Trim();
        }

        return null;
    }

    private (IReadOnlyList<NodeViewModel> Tables, IReadOnlyList<NodeViewModel> Ctes, IReadOnlyList<NodeViewModel> Subqueries) FilterUpstreamSources(
        NodeViewModel resultOutputNode,
        IReadOnlyList<NodeViewModel> tableNodes,
        IReadOnlyList<NodeViewModel> cteSourceNodes,
        IReadOnlyList<NodeViewModel> subqueryNodes)
    {
        HashSet<string> upstreamIds = CollectUpstreamNodeIds(resultOutputNode);
        List<NodeViewModel> tables = tableNodes.Where(n => upstreamIds.Contains(n.Id)).ToList();
        List<NodeViewModel> ctes = cteSourceNodes.Where(n => upstreamIds.Contains(n.Id)).ToList();
        List<NodeViewModel> subqueries = subqueryNodes.Where(n => upstreamIds.Contains(n.Id)).ToList();
        return (tables, ctes, subqueries);
    }

    private HashSet<string> CollectUpstreamNodeIds(NodeViewModel sinkNode)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { sinkNode.Id };
        var queue = new Queue<string>();
        queue.Enqueue(sinkNode.Id);

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            foreach (ConnectionViewModel conn in _canvas.Connections.Where(c => c.ToPin?.Owner.Id == current))
            {
                string fromOwnerId = conn.FromPin.Owner.Id;
                if (visited.Add(fromOwnerId))
                    queue.Enqueue(fromOwnerId);
            }
        }

        return visited;
    }

    private NodeViewModel? ResolvePrimaryResultOutputNode()
    {
        IReadOnlyList<NodeViewModel> outputs = _canvas.Nodes
            .Where(n => n.Type == NodeType.ResultOutput)
            .ToList();
        if (outputs.Count == 0)
            return null;
        if (outputs.Count == 1)
            return outputs[0];

        NodeViewModel? externalSink = outputs.FirstOrDefault(output =>
            !_canvas.Connections.Any(c =>
                c.FromPin.Owner == output
                && c.ToPin is not null
                && c.ToPin.Owner.Type == NodeType.CteDefinition
                && c.ToPin.Name.Equals("query", StringComparison.OrdinalIgnoreCase)));

        return externalSink ?? outputs[0];
    }

    private (string? FromSource, string? Warning) ResolveSubqueryFromSource(NodeViewModel subqueryNode)
    {
        string? query = QueryGraphHelpers.ResolveTextInput(_canvas, subqueryNode, "query_text");

        if (string.IsNullOrWhiteSpace(query))
            return (null, "Subquery source is missing query SQL. Add a SELECT or WITH query.");

        string body = query.Trim().TrimEnd(';');
        if (!QueryGraphHelpers.LooksLikeSelectStatement(body))
        {
            return (
                null,
                "Subquery source must start with SELECT, WITH, or a parenthesized SELECT. Ignoring Subquery source."
            );
        }

        if (!(body.StartsWith("(", StringComparison.Ordinal) && body.EndsWith(")", StringComparison.Ordinal)))
            body = $"({body})";

        string? alias = QueryGraphHelpers.ResolveTextInput(_canvas, subqueryNode, "alias_text");
        if (string.IsNullOrWhiteSpace(alias)
            && subqueryNode.Parameters.TryGetValue("alias", out string? aliasParam)
            && !string.IsNullOrWhiteSpace(aliasParam))
        {
            alias = aliasParam;
        }

        string? warning = null;
        if (string.IsNullOrWhiteSpace(alias))
        {
            alias = "subq";
            warning = "Subquery source alias is required. Defaulting alias to 'subq'.";
        }
        else
        {
            alias = alias.Trim();
            if (alias.Contains(' ', StringComparison.Ordinal))
            {
                warning = "Subquery source alias cannot contain spaces. Defaulting alias to 'subq'.";
                alias = "subq";
            }
        }

        return ($"{body} {alias}", warning);
    }
}
