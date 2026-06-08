using AkkornStudio.Nodes;

namespace AkkornStudio.UI.Services.QueryPreview;

internal sealed class QueryExecutionParameterContextExtractor
{
    public IReadOnlyDictionary<string, QueryExecutionParameterContext> Extract(
        string sqlTemplate,
        NodeGraph graph)
    {
        IReadOnlyList<QueryParameterPlaceholder> placeholders = QueryParameterPlaceholderParser.Parse(sqlTemplate);
        if (placeholders.Count == 0)
            return new Dictionary<string, QueryExecutionParameterContext>(StringComparer.OrdinalIgnoreCase);

        List<QueryExecutionParameterContext> contexts = [];
        contexts.AddRange(ExtractPredicateBindings(graph.WhereConditions, graph));
        contexts.AddRange(ExtractPredicateBindings(graph.Havings.Select(static binding => new WhereBinding(binding.NodeId, binding.PinName)).ToList(), graph));
        contexts.AddRange(ExtractPredicateBindings(graph.Qualifies.Select(static binding => new WhereBinding(binding.NodeId, binding.PinName)).ToList(), graph));

        Dictionary<string, QueryExecutionParameterContext> mapped = new(StringComparer.OrdinalIgnoreCase);
        int count = Math.Min(placeholders.Count, contexts.Count);
        for (int index = 0; index < count; index++)
        {
            QueryParameterPlaceholder placeholder = placeholders[index];
            QueryExecutionParameterContext context = contexts[index] with
            {
                BindingLabel = placeholder.Kind == QueryParameterPlaceholderKind.Named
                    ? placeholder.Token
                    : contexts[index].BindingLabel,
            };
            mapped[QueryParameterPlaceholderParser.GetStorageKey(placeholder)] = context;
        }

        return mapped;
    }

    private static IEnumerable<QueryExecutionParameterContext> ExtractPredicateBindings(
        IReadOnlyList<WhereBinding> bindings,
        NodeGraph graph)
    {
        foreach (WhereBinding binding in bindings)
        {
            if (!graph.NodeMap.TryGetValue(binding.NodeId, out NodeInstance? node))
                continue;

            foreach (QueryExecutionParameterContext context in ExtractNodeContexts(node, graph))
                yield return context;
        }
    }

    private static IEnumerable<QueryExecutionParameterContext> ExtractNodeContexts(
        NodeInstance node,
        NodeGraph graph)
    {
        switch (node.Type)
        {
            case NodeType.Equals:
            case NodeType.NotEquals:
            case NodeType.GreaterThan:
            case NodeType.GreaterOrEqual:
            case NodeType.LessThan:
            case NodeType.LessOrEqual:
                if (HasLiteralLikeInput(node, graph, "right"))
                {
                    QueryExecutionParameterContext? context = ResolveColumnContext(node, graph, "left");
                    if (context is not null)
                        yield return context;
                }
                yield break;
            case NodeType.Like:
            case NodeType.NotLike:
                if (node.Parameters.TryGetValue("pattern", out string? pattern)
                    && !string.IsNullOrWhiteSpace(pattern))
                {
                    QueryExecutionParameterContext? context = ResolveColumnContext(node, graph, "text");
                    if (context is not null)
                        yield return context;
                }
                yield break;
            case NodeType.Between:
            case NodeType.NotBetween:
                QueryExecutionParameterContext? betweenContext = ResolveColumnContext(node, graph, "value");
                if (betweenContext is null)
                    yield break;

                if (HasLiteralLikeInput(node, graph, "low"))
                    yield return betweenContext;
                if (HasLiteralLikeInput(node, graph, "high"))
                    yield return betweenContext;
                yield break;
            case NodeType.And:
            case NodeType.Or:
                foreach (AkkornStudio.Nodes.Connection connection in graph.Connections.Where(connection =>
                             connection.ToNodeId == node.Id
                             && (connection.ToPinName.Equals("conditions", StringComparison.OrdinalIgnoreCase)
                                 || connection.ToPinName.StartsWith("cond_", StringComparison.OrdinalIgnoreCase))))
                {
                    if (!graph.NodeMap.TryGetValue(connection.FromNodeId, out NodeInstance? upstream) || upstream is null)
                        continue;

                    foreach (QueryExecutionParameterContext context in ExtractNodeContexts(upstream, graph))
                        yield return context;
                }
                yield break;
        }
    }

    private static bool HasLiteralLikeInput(NodeInstance node, NodeGraph graph, string pinName)
    {
        AkkornStudio.Nodes.Connection? wire = graph.GetSingleInputConnection(node.Id, pinName);
        if (wire is not null)
        {
            return graph.NodeMap.TryGetValue(wire.FromNodeId, out NodeInstance? source)
                   && source.Type is NodeType.ValueNumber
                       or NodeType.ValueString
                       or NodeType.ValueDateTime
                       or NodeType.ValueBoolean;
        }

        return node.PinLiterals.ContainsKey(pinName);
    }

    private static QueryExecutionParameterContext? ResolveColumnContext(
        NodeInstance node,
        NodeGraph graph,
        string pinName)
    {
        AkkornStudio.Nodes.Connection? wire = graph.GetSingleInputConnection(node.Id, pinName);
        if (wire is null)
            return null;

        return ResolveSourcePin(graph, wire.FromNodeId, wire.FromPinName);
    }

    private static QueryExecutionParameterContext? ResolveSourcePin(
        NodeGraph graph,
        string nodeId,
        string pinName)
    {
        if (!graph.NodeMap.TryGetValue(nodeId, out NodeInstance? node))
            return null;

        if (node.Type is NodeType.TableSource or NodeType.CteSource)
        {
            string? tableRef = ResolveTableReference(node);
            string sourceReference = !string.IsNullOrWhiteSpace(tableRef)
                ? $"{tableRef}.{pinName}"
                : $"{(node.Alias ?? node.Id)}.{pinName}";

            return new QueryExecutionParameterContext(
                SourceReference: sourceReference,
                TableRef: tableRef,
                ColumnName: pinName,
                ContextLabel: $"Origem estrutural: {sourceReference}",
                ExpressionKind: "column",
                SourceCount: 1,
                SourceReferences: [sourceReference]);
        }

        string? passthroughPin = ResolvePassthroughInputPin(node.Type, pinName);
        if (!string.IsNullOrWhiteSpace(passthroughPin))
        {
            QueryExecutionParameterContext? passthroughContext = ResolveColumnContext(node, graph, passthroughPin!);
            if (passthroughContext is null)
                return null;

            return passthroughContext with
            {
                ExpressionKind = ResolveExpressionKind(node.Type, passthroughContext.ExpressionKind),
            };
        }

        if (node.Type == NodeType.WindowFunction)
            return ResolveWindowContext(node, graph);
        if (node.Type == NodeType.StringAgg)
            return ResolveAggregateContext(node, graph, "aggregate-string", ("value", "Agregacao textual sobre"), ("order_by", "ordenada por"));
        if (node.Type is NodeType.Sum or NodeType.Avg or NodeType.Min or NodeType.Max or NodeType.CountDistinct or NodeType.CountStar)
            return ResolveAggregateContext(node, graph, "aggregate", ("value", "Agregado sobre"));
        if (node.Type == NodeType.NullFill)
            return ResolveConditionalContext(node, graph, "conditional", ("value", "Condicional sobre"), ("fallback", "fallback de"));
        if (node.Type == NodeType.EmptyFill)
            return ResolveConditionalContext(node, graph, "conditional", ("value", "Condicional sobre"), ("fallback", "preenchido por"));
        if (node.Type == NodeType.ValueMap)
            return ResolveConditionalContext(node, graph, "conditional", ("value", "Mapeamento de valor sobre"));

        IReadOnlyList<string>? compositePins = ResolveCompositeInputPins(node.Type);
        if (compositePins is not null)
            return ResolveCompositeContext(node, graph, compositePins, ResolveExpressionKind(node.Type, null));

        return null;
    }

    private static string? ResolvePassthroughInputPin(NodeType nodeType, string pinName)
    {
        return nodeType switch
        {
            NodeType.Alias => "expression",
            NodeType.ScalarFromColumn or NodeType.Cast or NodeType.ColumnRefCast => "value",
            NodeType.Upper or NodeType.Lower or NodeType.Trim or NodeType.StringLength
                or NodeType.Substring or NodeType.RegexMatch or NodeType.RegexReplace
                or NodeType.RegexExtract or NodeType.Replace => "text",
            NodeType.Round or NodeType.Abs or NodeType.Ceil or NodeType.Floor
                or NodeType.DatePart or NodeType.DateFormat => "value",
            NodeType.DateAdd => "date",
            NodeType.JsonExtract or NodeType.JsonValue or NodeType.JsonArrayLength => "json",
            NodeType.TableSource or NodeType.CteSource when pinName.Equals("result", StringComparison.OrdinalIgnoreCase) => null,
            _ => null,
        };
    }

    private static IReadOnlyList<string> ResolveWindowFunctionPins(NodeInstance node, NodeGraph graph)
    {
        List<string> pins = ["value", "default"];
        pins.AddRange(graph.Connections
            .Where(connection => connection.ToNodeId == node.Id
                && (connection.ToPinName.StartsWith("partition_", StringComparison.OrdinalIgnoreCase)
                    || connection.ToPinName.StartsWith("order_", StringComparison.OrdinalIgnoreCase)))
            .Select(connection => connection.ToPinName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase));
        return pins;
    }

    private static IReadOnlyList<string>? ResolveCompositeInputPins(NodeType nodeType)
    {
        return nodeType switch
        {
            NodeType.Concat => ["a", "b"],
            NodeType.Add or NodeType.Subtract or NodeType.Multiply or NodeType.Divide or NodeType.Modulo => ["a", "b", "left", "right"],
            NodeType.NullFill or NodeType.EmptyFill => ["value", "fallback"],
            NodeType.DateDiff => ["start", "end"],
            NodeType.Sum or NodeType.Avg or NodeType.Min or NodeType.Max or NodeType.CountDistinct => ["value"],
            NodeType.StringAgg => ["value", "order_by"],
            NodeType.WindowFunction => ["value", "default", "partition_1", "partition_2", "partition_3", "partition_4", "order_1", "order_2", "order_3", "order_4"],
            _ => null,
        };
    }

    private static QueryExecutionParameterContext? ResolveCompositeContext(
        NodeInstance node,
        NodeGraph graph,
        IReadOnlyList<string> pinNames,
        string? expressionKind)
    {
        List<QueryExecutionParameterContext> contexts = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (string pinName in pinNames)
        {
            AkkornStudio.Nodes.Connection? wire = graph.GetSingleInputConnection(node.Id, pinName);
            if (wire is null)
                continue;

            QueryExecutionParameterContext? context = ResolveSourcePin(graph, wire.FromNodeId, wire.FromPinName);
            if (context is null)
                continue;

            string dedupeKey = context.SourceReference
                ?? context.ContextLabel
                ?? $"{context.TableRef}.{context.ColumnName}";
            if (!seen.Add(dedupeKey))
                continue;

            contexts.Add(context);
        }

        if (contexts.Count == 0)
            return null;

        if (contexts.Count == 1)
        {
            QueryExecutionParameterContext single = contexts[0];
            return single with
            {
                ExpressionKind = expressionKind ?? single.ExpressionKind,
            };
        }

        string joinedSources = string.Join(", ", contexts
            .Select(static context => context.SourceReference)
            .Where(static source => !string.IsNullOrWhiteSpace(source)));
        string contextLabel = string.IsNullOrWhiteSpace(joinedSources)
            ? "Origens estruturais compostas"
            : $"Origens estruturais: {joinedSources}";
        string[] sourceReferences = contexts
            .Select(static context => context.SourceReference)
            .Where(static source => !string.IsNullOrWhiteSpace(source))
            .Cast<string>()
            .ToArray();

        QueryExecutionParameterContext primary = contexts[0];
        return primary with
        {
            TableRef = null,
            ColumnName = null,
            ContextLabel = contextLabel,
            ExpressionKind = expressionKind ?? "composite",
            SourceCount = contexts.Count,
            SourceReferences = sourceReferences,
        };
    }

    private static QueryExecutionParameterContext? ResolveWindowContext(
        NodeInstance node,
        NodeGraph graph)
    {
        List<QueryExecutionParameterContext> valueContexts = ResolveWindowRoleContexts(node, graph, "value", "default");
        List<QueryExecutionParameterContext> partitionContexts = ResolveWindowRoleContexts(node, graph, "partition_");
        List<QueryExecutionParameterContext> orderContexts = ResolveWindowRoleContexts(node, graph, "order_");

        List<QueryExecutionParameterContext> allContexts = [];
        AddDistinctContexts(allContexts, valueContexts);
        AddDistinctContexts(allContexts, partitionContexts);
        AddDistinctContexts(allContexts, orderContexts);

        if (allContexts.Count == 0)
            return null;

        QueryExecutionParameterContext primary = valueContexts.FirstOrDefault() ?? allContexts[0];
        string contextLabel = BuildWindowContextLabel(valueContexts, partitionContexts, orderContexts);
        string[] sourceReferences = allContexts
            .Select(static context => context.SourceReference)
            .Where(static source => !string.IsNullOrWhiteSpace(source))
            .Cast<string>()
            .ToArray();

        return primary with
        {
            TableRef = allContexts.Count == 1 ? primary.TableRef : null,
            ColumnName = allContexts.Count == 1 ? primary.ColumnName : null,
            ContextLabel = contextLabel,
            ExpressionKind = "window",
            SourceCount = allContexts.Count,
            SourceReferences = sourceReferences,
        };
    }

    private static QueryExecutionParameterContext? ResolveAggregateContext(
        NodeInstance node,
        NodeGraph graph,
        string expressionKind,
        params (string PinPrefix, string Label)[] roles)
    {
        List<(string Label, IReadOnlyList<QueryExecutionParameterContext> Contexts)> groupedContexts = [];
        List<QueryExecutionParameterContext> allContexts = [];

        foreach ((string pinPrefix, string label) in roles)
        {
            List<QueryExecutionParameterContext> roleContexts = ResolveWindowRoleContexts(node, graph, pinPrefix);
            if (roleContexts.Count == 0)
                continue;

            groupedContexts.Add((label, roleContexts));
            AddDistinctContexts(allContexts, roleContexts);
        }

        if (allContexts.Count == 0)
            return null;

        QueryExecutionParameterContext primary = groupedContexts.FirstOrDefault().Contexts?.FirstOrDefault() ?? allContexts[0];
        string contextLabel = BuildRoleBasedContextLabel(groupedContexts, "Origem estrutural de agregacao");
        string[] sourceReferences = allContexts
            .Select(static context => context.SourceReference)
            .Where(static source => !string.IsNullOrWhiteSpace(source))
            .Cast<string>()
            .ToArray();

        return primary with
        {
            TableRef = allContexts.Count == 1 ? primary.TableRef : null,
            ColumnName = allContexts.Count == 1 ? primary.ColumnName : null,
            ContextLabel = contextLabel,
            ExpressionKind = expressionKind,
            SourceCount = allContexts.Count,
            SourceReferences = sourceReferences,
        };
    }

    private static QueryExecutionParameterContext? ResolveConditionalContext(
        NodeInstance node,
        NodeGraph graph,
        string expressionKind,
        params (string PinPrefix, string Label)[] roles)
    {
        List<(string Label, IReadOnlyList<QueryExecutionParameterContext> Contexts)> groupedContexts = [];
        List<QueryExecutionParameterContext> allContexts = [];

        foreach ((string pinPrefix, string label) in roles)
        {
            List<QueryExecutionParameterContext> roleContexts = ResolveWindowRoleContexts(node, graph, pinPrefix);
            if (roleContexts.Count == 0)
                continue;

            groupedContexts.Add((label, roleContexts));
            AddDistinctContexts(allContexts, roleContexts);
        }

        if (allContexts.Count == 0)
            return null;

        QueryExecutionParameterContext primary = groupedContexts.FirstOrDefault().Contexts?.FirstOrDefault() ?? allContexts[0];
        string contextLabel = BuildRoleBasedContextLabel(groupedContexts, "Origem estrutural condicional");
        string[] sourceReferences = allContexts
            .Select(static context => context.SourceReference)
            .Where(static source => !string.IsNullOrWhiteSpace(source))
            .Cast<string>()
            .ToArray();

        return primary with
        {
            TableRef = allContexts.Count == 1 ? primary.TableRef : null,
            ColumnName = allContexts.Count == 1 ? primary.ColumnName : null,
            ContextLabel = contextLabel,
            ExpressionKind = expressionKind,
            SourceCount = allContexts.Count,
            SourceReferences = sourceReferences,
        };
    }

    private static List<QueryExecutionParameterContext> ResolveWindowRoleContexts(
        NodeInstance node,
        NodeGraph graph,
        params string[] pinPrefixes)
    {
        List<QueryExecutionParameterContext> contexts = [];

        foreach (AkkornStudio.Nodes.Connection connection in graph.Connections.Where(connection =>
                     connection.ToNodeId == node.Id
                     && pinPrefixes.Any(prefix => prefix.EndsWith('_')
                         ? connection.ToPinName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                         : connection.ToPinName.Equals(prefix, StringComparison.OrdinalIgnoreCase))))
        {
            QueryExecutionParameterContext? context = ResolveSourcePin(graph, connection.FromNodeId, connection.FromPinName);
            if (context is null)
                continue;

            AddDistinctContexts(contexts, [context]);
        }

        return contexts;
    }

    private static void AddDistinctContexts(
        List<QueryExecutionParameterContext> target,
        IReadOnlyList<QueryExecutionParameterContext> contexts)
    {
        foreach (QueryExecutionParameterContext context in contexts)
        {
            string dedupeKey = context.SourceReference
                ?? context.ContextLabel
                ?? $"{context.TableRef}.{context.ColumnName}";
            if (target.Any(existing =>
                    string.Equals(existing.SourceReference ?? existing.ContextLabel ?? $"{existing.TableRef}.{existing.ColumnName}",
                        dedupeKey,
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            target.Add(context);
        }
    }

    private static string BuildWindowContextLabel(
        IReadOnlyList<QueryExecutionParameterContext> valueContexts,
        IReadOnlyList<QueryExecutionParameterContext> partitionContexts,
        IReadOnlyList<QueryExecutionParameterContext> orderContexts)
    {
        return BuildRoleBasedContextLabel(
            [
                ("Janela sobre", valueContexts),
                ("particionada por", partitionContexts),
                ("ordenada por", orderContexts),
            ],
            "Origem estrutural de janela");
    }

    private static string JoinContextSources(IReadOnlyList<QueryExecutionParameterContext> contexts)
    {
        return string.Join(", ", contexts
            .Select(static context => context.SourceReference)
            .Where(static source => !string.IsNullOrWhiteSpace(source)));
    }

    private static string BuildRoleBasedContextLabel(
        IReadOnlyList<(string Label, IReadOnlyList<QueryExecutionParameterContext> Contexts)> groupedContexts,
        string fallback)
    {
        List<string> parts = [];

        foreach ((string label, IReadOnlyList<QueryExecutionParameterContext> contexts) in groupedContexts)
        {
            if (contexts.Count == 0)
                continue;

            string joinedSources = JoinContextSources(contexts);
            if (string.IsNullOrWhiteSpace(joinedSources))
                continue;

            parts.Add($"{label} {joinedSources}");
        }

        return parts.Count == 0
            ? fallback
            : string.Join(" | ", parts);
    }

    private static string? ResolveExpressionKind(NodeType nodeType, string? fallback)
    {
        return nodeType switch
        {
            NodeType.Sum or NodeType.Avg or NodeType.Min or NodeType.Max or NodeType.CountDistinct or NodeType.CountStar => "aggregate",
            NodeType.StringAgg => "aggregate-string",
            NodeType.WindowFunction => "window",
            NodeType.Concat => "concat",
            NodeType.Add or NodeType.Subtract or NodeType.Multiply or NodeType.Divide or NodeType.Modulo => "arithmetic",
            NodeType.DateDiff or NodeType.DateAdd or NodeType.DatePart or NodeType.DateFormat => "date-transform",
            NodeType.Upper or NodeType.Lower or NodeType.Trim or NodeType.Substring or NodeType.Replace or NodeType.RegexMatch or NodeType.RegexExtract or NodeType.RegexReplace => "string-transform",
            NodeType.NullFill or NodeType.EmptyFill or NodeType.ValueMap => "conditional",
            NodeType.Alias => "alias",
            NodeType.Cast or NodeType.ColumnRefCast or NodeType.ScalarFromColumn => "cast",
            NodeType.JsonExtract or NodeType.JsonValue or NodeType.JsonArrayLength => "json",
            _ => fallback,
        };
    }

    private static string? ResolveTableReference(NodeInstance node)
    {
        if (!string.IsNullOrWhiteSpace(node.TableFullName))
            return node.TableFullName;

        if (node.Parameters.TryGetValue("source_table", out string? sourceTable)
            && !string.IsNullOrWhiteSpace(sourceTable))
        {
            return sourceTable.Trim();
        }

        if (node.Parameters.TryGetValue("from_table", out string? fromTable)
            && !string.IsNullOrWhiteSpace(fromTable))
        {
            return fromTable.Trim();
        }

        if (node.Parameters.TryGetValue("cte_name", out string? cteName)
            && !string.IsNullOrWhiteSpace(cteName))
        {
            return cteName.Trim();
        }

        return null;
    }
}
