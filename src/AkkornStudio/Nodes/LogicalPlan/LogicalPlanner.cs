using AkkornStudio.Core;

namespace AkkornStudio.Nodes.LogicalPlan;

public sealed class LogicalPlanner(NodeGraph graph, EmitContext emitContext)
{
    private readonly NodeGraph _graph = graph ?? throw new ArgumentNullException(nameof(graph));
    private readonly EmitContext _emitContext = emitContext ?? throw new ArgumentNullException(nameof(emitContext));
    private readonly AliasGenerator _aliasGenerator = new();
    private readonly Dictionary<string, string> _aliasesByNodeId = new(StringComparer.OrdinalIgnoreCase);
    private static readonly NodeType[] DatasetSourceTypes =
    [
        NodeType.TableSource,
        NodeType.CteSource,
        NodeType.Subquery,
    ];

    public LogicalOutput Plan()
    {
        NodeInstance outputSink = ResolveSingleOutputSink();
        LogicalNode source = BuildOutputSource(outputSink);
        IReadOnlyList<LogicalOrderBinding> orderBy = BuildOrderBindings(outputSink);
        ValidateCteContracts(outputSink);

        return new LogicalOutput(
            source,
            Ctes: [],
            orderBy,
            Distinct: _graph.Distinct,
            Limit: _graph.Limit,
            Offset: _graph.Offset
        );
    }

    private NodeInstance ResolveSingleOutputSink()
    {
        List<NodeInstance> outputNodes = _graph.Nodes
            .Where(n => n.Type == NodeType.ResultOutput)
            .Where(n => !IsCteSubgraphOutputNode(n.Id))
            .ToList();

        if (outputNodes.Count == 1)
            return outputNodes[0];

        throw new PlanningException(
            nodeId: outputNodes.FirstOrDefault()?.Id ?? string.Empty,
            PlannerErrorKind.OutputSourceAmbiguous,
            outputNodes.Count == 0
                ? "No top-level ResultOutput node found."
                : "Multiple top-level ResultOutput nodes found."
        );
    }

    private LogicalNode BuildOutputSource(NodeInstance outputSink)
    {
        HashSet<string> referencedDataSources = CollectReferencedDataSourceIds(outputSink);
        if (TryBuildJoinSourceForOutput(outputSink, referencedDataSources, out LogicalNode? joinSource))
            return joinSource!;

        if (referencedDataSources.Count == 1)
        {
            string sourceId = referencedDataSources.Single();
            return BuildDataSourceNode(sourceId);
        }

        if (referencedDataSources.Count > 1)
            return BuildJoinSourceForOutput(outputSink, referencedDataSources);

        throw new PlanningException(
            outputSink.Id,
            PlannerErrorKind.DatasetNotReachableFromOutput,
            $"Output sink '{outputSink.Id}' does not reference any reachable dataset source."
        );
    }

    private bool TryBuildJoinSourceForOutput(
        NodeInstance outputSink,
        IReadOnlySet<string> referencedDataSources,
        out LogicalNode? joinSource)
    {
        joinSource = null;
        List<NodeInstance> joinCandidates = _graph.Nodes
            .Where(n => n.Type is NodeType.Join or NodeType.RowSetJoin)
            .ToList();

        foreach (NodeInstance join in joinCandidates)
        {
            Connection? left = _graph.GetSingleInputConnection(join.Id, "left");
            Connection? right = _graph.GetSingleInputConnection(join.Id, "right");
            if (left is null || right is null)
                continue;

            string? leftSourceId = ResolveDataSourceNodeId(left.FromNodeId);
            string? rightSourceId = ResolveDataSourceNodeId(right.FromNodeId);
            if (leftSourceId is null || rightSourceId is null)
                continue;

            // Join is relevant when it references at least one dataset used by output.
            if (referencedDataSources.Count > 0
                && !referencedDataSources.Contains(leftSourceId)
                && !referencedDataSources.Contains(rightSourceId))
            {
                continue;
            }

            LogicalNode leftNode = BuildDataSourceNode(leftSourceId);
            LogicalNode rightNode = BuildDataSourceNode(rightSourceId);
            JoinKind kind = ResolveJoinKind(join);
            ISqlExpression condition = ResolveJoinCondition(join, left, right, kind);
            joinSource = new LogicalJoin(join.Id, leftNode, rightNode, kind, condition);
            return true;
        }

        foreach (NodeInstance join in joinCandidates)
        {
            if (!join.Parameters.TryGetValue("right_source", out string? rightSourceRaw)
                || string.IsNullOrWhiteSpace(rightSourceRaw))
            {
                continue;
            }

            string? leftSourceId = referencedDataSources.Count == 1 ? referencedDataSources.Single() : null;
            if (string.IsNullOrWhiteSpace(leftSourceId))
                continue;

            LogicalNode leftNode = BuildDataSourceNode(leftSourceId);
            (string rightTable, string rightAlias) = ParseRightSource(rightSourceRaw!);
            LogicalNode rightNode = new LogicalScan(
                NodeId: $"{join.Id}::right",
                Alias: rightAlias,
                TableFullName: rightTable,
                Schema: []);

            JoinKind kind = ResolveJoinKind(join);
            ISqlExpression condition = ResolveJoinConditionFromParameters(join, leftSourceId, rightAlias, kind);
            joinSource = new LogicalJoin(join.Id, leftNode, rightNode, kind, condition);
            return true;
        }

        return false;
    }

    private static (string Table, string Alias) ParseRightSource(string rightSource)
    {
        string[] tokens = rightSource.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string table = tokens[0];
        string alias = tokens.Length >= 2 ? tokens[^1] : DeriveAliasSeedFromTableName(table);
        return (table, alias);
    }

    private ISqlExpression ResolveJoinConditionFromParameters(
        NodeInstance joinNode,
        string leftSourceId,
        string rightAlias,
        JoinKind kind)
    {
        Connection? explicitCondition = _graph.GetSingleInputConnection(joinNode.Id, "condition");
        if (explicitCondition is not null)
            return ResolveComparisonOrBooleanExpression(explicitCondition);

        if (kind == JoinKind.Cross)
            return new LiteralExpr("TRUE", PinDataType.Boolean);

        string? leftExprRaw = joinNode.Parameters.GetValueOrDefault("left_expr");
        string? rightExprRaw = joinNode.Parameters.GetValueOrDefault("right_expr");
        if (string.IsNullOrWhiteSpace(leftExprRaw) || string.IsNullOrWhiteSpace(rightExprRaw))
            throw new PlanningException(
                joinNode.Id,
                PlannerErrorKind.JoinWithoutCondition,
                $"Join node '{joinNode.Id}' requires an explicit condition for join type '{kind}'.");

        string opRaw = joinNode.Parameters.GetValueOrDefault("operator") ?? "=";
        if (!TryParseJoinOperator(opRaw, out ComparisonOperator op))
        {
            throw new PlanningException(
                joinNode.Id,
                PlannerErrorKind.InvalidJoinOperator,
                $"Join node '{joinNode.Id}' contains unsupported operator '{opRaw}'.");
        }

        ISqlExpression left = ParseColumnExpression(leftExprRaw!, fallbackAlias: ResolveDatasetAlias(_graph.NodeMap[leftSourceId]));
        ISqlExpression right = ParseColumnExpression(rightExprRaw!, fallbackAlias: rightAlias);
        return new ComparisonExpr(left, op, right);
    }

    private static bool TryParseJoinOperator(string rawOperator, out ComparisonOperator op)
    {
        string normalized = (rawOperator ?? string.Empty).Trim().ToUpperInvariant();
        op = normalized switch
        {
            "=" => ComparisonOperator.Eq,
            "<>" or "!=" => ComparisonOperator.Neq,
            ">" => ComparisonOperator.Gt,
            ">=" => ComparisonOperator.Gte,
            "<" => ComparisonOperator.Lt,
            "<=" => ComparisonOperator.Lte,
            "LIKE" => ComparisonOperator.Like,
            "NOT LIKE" => ComparisonOperator.NotLike,
            _ => default,
        };

        return normalized is "=" or "<>" or "!=" or ">" or ">=" or "<" or "<=" or "LIKE" or "NOT LIKE";
    }

    private static ISqlExpression ParseColumnExpression(string raw, string fallbackAlias)
    {
        string[] parts = raw.Trim().Trim('"', '`').Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return new ColumnExpr(fallbackAlias, "id", PinDataType.ColumnRef);

        if (parts.Length == 1)
            return new ColumnExpr(fallbackAlias, parts[0].Trim('"', '`'), PinDataType.ColumnRef);

        string alias = parts.Length >= 2 ? parts[^2].Trim('"', '`') : fallbackAlias;
        string column = parts[^1].Trim('"', '`');
        return new ColumnExpr(alias, column, PinDataType.ColumnRef);
    }

    private LogicalNode BuildJoinSourceForOutput(
        NodeInstance outputSink,
        IReadOnlySet<string> referencedDataSources
    )
    {
        List<NodeInstance> joinCandidates = _graph.Nodes
            .Where(n => n.Type is NodeType.Join or NodeType.RowSetJoin)
            .ToList();

        foreach (NodeInstance join in joinCandidates)
        {
            Connection? left = _graph.GetSingleInputConnection(join.Id, "left");
            Connection? right = _graph.GetSingleInputConnection(join.Id, "right");
            if (left is null || right is null)
                continue;

            string? leftSourceId = ResolveDataSourceNodeId(left.FromNodeId);
            string? rightSourceId = ResolveDataSourceNodeId(right.FromNodeId);
            if (leftSourceId is null || rightSourceId is null)
                continue;

            if (
                !referencedDataSources.Contains(leftSourceId)
                || !referencedDataSources.Contains(rightSourceId)
            )
            {
                continue;
            }

            LogicalNode leftNode = BuildDataSourceNode(leftSourceId);
            LogicalNode rightNode = BuildDataSourceNode(rightSourceId);
            JoinKind kind = ResolveJoinKind(join);
            ISqlExpression condition = ResolveJoinCondition(join, left, right, kind);
            return new LogicalJoin(join.Id, leftNode, rightNode, kind, condition);
        }

        throw new PlanningException(
            outputSink.Id,
            PlannerErrorKind.OutputSourceAmbiguous,
            "Output references multiple datasets but no explicit JOIN node can resolve a single source root."
        );
    }

    private ISqlExpression ResolveJoinCondition(
        NodeInstance joinNode,
        Connection left,
        Connection right,
        JoinKind kind
    )
    {
        Connection? explicitCondition = _graph.GetSingleInputConnection(joinNode.Id, "condition");
        if (explicitCondition is not null)
            return ResolveComparisonOrBooleanExpression(explicitCondition);

        if (kind == JoinKind.Cross)
            return new LiteralExpr("TRUE", PinDataType.Boolean);

        throw new PlanningException(
            joinNode.Id,
            PlannerErrorKind.JoinWithoutCondition,
            $"Join node '{joinNode.Id}' requires an explicit condition for join type '{kind}'."
        );
    }

    private ISqlExpression ResolveComparisonOrBooleanExpression(Connection conditionConnection)
    {
        if (!_graph.NodeMap.TryGetValue(conditionConnection.FromNodeId, out NodeInstance? sourceNode))
            throw new PlanningException(
                conditionConnection.FromNodeId,
                PlannerErrorKind.JoinWithoutCondition,
                $"Condition source node '{conditionConnection.FromNodeId}' not found."
            );

        return BuildBooleanExpression(sourceNode.Id);
    }

    private ISqlExpression BuildBooleanExpression(string nodeId)
    {
        if (!_graph.NodeMap.TryGetValue(nodeId, out NodeInstance? node))
            return new LiteralExpr("TRUE", PinDataType.Boolean);

        if (TryMapComparisonOperator(node.Type, out ComparisonOperator comparisonOperator))
        {
            Connection? leftConn = _graph.GetSingleInputConnection(node.Id, "left");
            Connection? rightConn = _graph.GetSingleInputConnection(node.Id, "right");
            if (leftConn is null || rightConn is null)
                return new LiteralExpr("TRUE", PinDataType.Boolean);

            ISqlExpression leftExpr = BuildExpressionFromConnection(leftConn);
            ISqlExpression rightExpr = BuildExpressionFromConnection(rightConn);
            return new ComparisonExpr(leftExpr, comparisonOperator, rightExpr);
        }

        if (node.Type is NodeType.And or NodeType.Or)
        {
            IReadOnlyList<Connection> inputs = _graph.Connections
                .Where(c =>
                    c.ToNodeId == node.Id
                    && (c.ToPinName.Equals("conditions", StringComparison.OrdinalIgnoreCase)
                        || c.ToPinName.StartsWith("cond_", StringComparison.OrdinalIgnoreCase)))
                .ToList();
            LogicOperator op = node.Type == NodeType.And ? LogicOperator.And : LogicOperator.Or;
            return new LogicGateExpr(op, inputs.Select(c => BuildBooleanExpression(c.FromNodeId)).ToList());
        }

        if (node.Type == NodeType.Not)
        {
            Connection? input = _graph.GetSingleInputConnection(node.Id, "condition")
                ?? _graph.GetSingleInputConnection(node.Id, "input");
            return input is null
                ? new LiteralExpr("TRUE", PinDataType.Boolean)
                : new NotExpr(BuildBooleanExpression(input.FromNodeId));
        }

        return new LiteralExpr("TRUE", PinDataType.Boolean);
    }

    private ISqlExpression BuildExpressionFromConnection(Connection connection)
    {
        if (_graph.NodeMap.TryGetValue(connection.FromNodeId, out NodeInstance? source)
            && source.Type is NodeType.ValueNumber or NodeType.ValueString or NodeType.ValueBoolean)
        {
            return source.Type switch
            {
                NodeType.ValueNumber when source.Parameters.TryGetValue("value", out string? n)
                    && double.TryParse(n, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsed)
                    => new NumberLiteralExpr(parsed),
                NodeType.ValueBoolean when source.Parameters.TryGetValue("value", out string? b)
                    => new LiteralExpr(string.Equals(b, "true", StringComparison.OrdinalIgnoreCase) ? "TRUE" : "FALSE", PinDataType.Boolean),
                _ => new StringLiteralExpr(source.Parameters.GetValueOrDefault("value") ?? string.Empty),
            };
        }

        return BuildColumnRefExpression(connection);
    }

    private static bool TryMapComparisonOperator(NodeType nodeType, out ComparisonOperator op)
    {
        op = nodeType switch
        {
            NodeType.Equals => ComparisonOperator.Eq,
            NodeType.NotEquals => ComparisonOperator.Neq,
            NodeType.GreaterThan => ComparisonOperator.Gt,
            NodeType.GreaterOrEqual => ComparisonOperator.Gte,
            NodeType.LessThan => ComparisonOperator.Lt,
            NodeType.LessOrEqual => ComparisonOperator.Lte,
            NodeType.Like => ComparisonOperator.Like,
            NodeType.NotLike => ComparisonOperator.NotLike,
            _ => ComparisonOperator.Eq,
        };

        return nodeType is NodeType.Equals
            or NodeType.NotEquals
            or NodeType.GreaterThan
            or NodeType.GreaterOrEqual
            or NodeType.LessThan
            or NodeType.LessOrEqual
            or NodeType.Like
            or NodeType.NotLike;
    }

    private LogicalNode BuildDataSourceNode(string nodeId)
    {
        if (!_graph.NodeMap.TryGetValue(nodeId, out NodeInstance? node))
        {
            throw new PlanningException(
                nodeId,
                PlannerErrorKind.DatasetNotReachableFromOutput,
                $"Dataset node '{nodeId}' does not exist in graph."
            );
        }

        if (!DatasetSourceTypes.Contains(node.Type))
        {
            throw new PlanningException(
                nodeId,
                PlannerErrorKind.DatasetNotReachableFromOutput,
                $"Node '{nodeId}' is not yet supported as a dataset source by LogicalPlanner."
            );
        }

        string tableFullName = node.Type switch
        {
            NodeType.CteSource => node.Parameters.GetValueOrDefault("cte_name")
                ?? throw new PlanningException(
                    node.Id,
                    PlannerErrorKind.DatasetNotReachableFromOutput,
                    $"CteSource '{node.Id}' has no cte_name."),
            NodeType.Subquery => BuildSubquerySource(node),
            _ => node.TableFullName
                ?? node.Parameters.GetValueOrDefault("table_full_name")
                ?? node.Parameters.GetValueOrDefault("table")
                ?? node.Parameters.GetValueOrDefault("source_table")
                ?? node.Parameters.GetValueOrDefault("from_table")
                ?? throw new PlanningException(
                    node.Id,
                    PlannerErrorKind.DatasetNotReachableFromOutput,
                    $"TableSource '{node.Id}' has no table_full_name."
                )
        };

        string alias = ResolveDatasetAlias(node);
        IReadOnlyList<LogicalColumn> schema = BuildSchema(node);
        return new LogicalScan(node.Id, alias, tableFullName, schema);
    }

    private string ResolveDatasetAlias(NodeInstance tableNode)
    {
        if (_aliasesByNodeId.TryGetValue(tableNode.Id, out string? cached))
            return cached;

        string? explicitAlias = tableNode.Alias;
        if (string.IsNullOrWhiteSpace(explicitAlias))
        {
            explicitAlias = tableNode.Parameters.TryGetValue("alias", out string? fromParameter)
                && !string.IsNullOrWhiteSpace(fromParameter)
                ? fromParameter.Trim()
                : null;
        }

        string resolvedAlias;
        if (!string.IsNullOrWhiteSpace(explicitAlias))
        {
            string generated = _aliasGenerator.GenerateFor(explicitAlias.Trim());
            if (!generated.Equals(explicitAlias.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new PlanningException(
                    tableNode.Id,
                    PlannerErrorKind.DuplicateAlias,
                    $"Duplicate alias '{explicitAlias}' detected in planning scope."
                );
            }

            resolvedAlias = generated;
        }
        else
        {
            string? aliasSeed = tableNode.Type switch
            {
                NodeType.Subquery => "subq",
                _ => tableNode.TableFullName,
            };

            string fallbackAlias = DeriveAliasSeedFromTableName(aliasSeed);
            resolvedAlias = _aliasGenerator.GenerateFor(fallbackAlias);
        }

        _aliasesByNodeId[tableNode.Id] = resolvedAlias;
        return resolvedAlias;
    }

    private static string DeriveAliasSeedFromTableName(string? tableFullName)
    {
        if (string.IsNullOrWhiteSpace(tableFullName))
            return "ds";

        string normalized = tableFullName.Trim().Trim('"').Trim('`').Trim('[', ']');
        string[] parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string seed = parts.Length > 0 ? parts[^1] : "ds";
        return string.IsNullOrWhiteSpace(seed) ? "ds" : seed;
    }

    private static IReadOnlyList<LogicalColumn> BuildSchema(NodeInstance node)
    {
        if (node.ColumnPinTypes is null || node.ColumnPinTypes.Count == 0)
            return [];

        return node.ColumnPinTypes
            .Select(kv => new LogicalColumn(kv.Key, kv.Value))
            .ToList();
    }

    private HashSet<string> CollectReferencedDataSourceIds(NodeInstance outputSink)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (SelectBinding binding in ResolveSelectBindings(outputSink))
            CollectReferencedDataSourcesFromNode(binding.NodeId, result, visited);

        foreach (WhereBinding binding in ResolveWhereBindings(outputSink))
            CollectReferencedDataSourcesFromNode(binding.NodeId, result, visited);

        foreach (HavingBinding binding in ResolveHavingBindings(outputSink))
            CollectReferencedDataSourcesFromNode(binding.NodeId, result, visited);

        foreach (QualifyBinding binding in ResolveQualifyBindings(outputSink))
            CollectReferencedDataSourcesFromNode(binding.NodeId, result, visited);

        foreach (OrderBinding binding in ResolveOrderBindings(outputSink))
            CollectReferencedDataSourcesFromNode(binding.NodeId, result, visited);

        foreach (GroupByBinding binding in ResolveGroupByBindings(outputSink))
            CollectReferencedDataSourcesFromNode(binding.NodeId, result, visited);

        return result;
    }

    private void CollectReferencedDataSourcesFromNode(
        string nodeId,
        ISet<string> collector,
        ISet<string> visited
    )
    {
        if (!visited.Add(nodeId))
            return;

        if (!_graph.NodeMap.TryGetValue(nodeId, out NodeInstance? node))
            return;

        if (DatasetSourceTypes.Contains(node.Type))
        {
            collector.Add(node.Id);
            return;
        }

        foreach (Connection incoming in _graph.Connections.Where(c => c.ToNodeId == nodeId))
            CollectReferencedDataSourcesFromNode(incoming.FromNodeId, collector, visited);
    }

    private string? ResolveDataSourceNodeId(string nodeId)
    {
        if (!_graph.NodeMap.TryGetValue(nodeId, out NodeInstance? node))
            return null;

        if (DatasetSourceTypes.Contains(node.Type))
            return node.Id;

        Connection? upstream = _graph.Connections.FirstOrDefault(c => c.ToNodeId == nodeId);
        return upstream is null ? null : ResolveDataSourceNodeId(upstream.FromNodeId);
    }

    private ISqlExpression BuildColumnRefExpression(Connection connection)
    {
        if (!_graph.NodeMap.TryGetValue(connection.FromNodeId, out NodeInstance? source))
        {
            throw new PlanningException(
                connection.FromNodeId,
                PlannerErrorKind.UnconnectedColumnSource,
                $"Referenced node '{connection.FromNodeId}' not found while resolving column."
            );
        }

        if (!DatasetSourceTypes.Contains(source.Type))
        {
            throw new PlanningException(
                connection.FromNodeId,
                PlannerErrorKind.UnconnectedColumnSource,
                $"Node '{connection.FromNodeId}' ({source.Type}) is not a dataset source and cannot provide column '{connection.FromPinName}'."
            );
        }

        string alias = ResolveDatasetAlias(source);
        PinDataType pinType = source.ColumnPinTypes?.GetValueOrDefault(connection.FromPinName)
            ?? PinDataType.ColumnRef;
        return new ColumnExpr(alias, connection.FromPinName, pinType);
    }

    private IReadOnlyList<SelectBinding> ResolveSelectBindings(NodeInstance outputSink)
    {
        List<SelectBinding> wired = _graph.Connections
            .Where(c =>
                c.ToNodeId == outputSink.Id
                && (
                    c.ToPinName.Equals("column", StringComparison.OrdinalIgnoreCase)
                    || c.ToPinName.Equals("columns", StringComparison.OrdinalIgnoreCase)
                )
            )
            .Select(c => new SelectBinding(c.FromNodeId, c.FromPinName))
            .ToList();

        return wired;
    }

    private IReadOnlyList<WhereBinding> ResolveWhereBindings(NodeInstance outputSink)
    {
        List<WhereBinding> wired = _graph.Connections
            .Where(c =>
                c.ToNodeId == outputSink.Id
                && c.ToPinName.Equals("where", StringComparison.OrdinalIgnoreCase)
            )
            .Select(c => new WhereBinding(c.FromNodeId, c.FromPinName))
            .ToList();

        return wired;
    }

    private IReadOnlyList<HavingBinding> ResolveHavingBindings(NodeInstance outputSink)
    {
        List<HavingBinding> wired = _graph.Connections
            .Where(c =>
                c.ToNodeId == outputSink.Id
                && c.ToPinName.Equals("having", StringComparison.OrdinalIgnoreCase)
            )
            .Select(c => new HavingBinding(c.FromNodeId, c.FromPinName))
            .ToList();

        return wired;
    }

    private IReadOnlyList<QualifyBinding> ResolveQualifyBindings(NodeInstance outputSink)
    {
        List<QualifyBinding> wired = _graph.Connections
            .Where(c =>
                c.ToNodeId == outputSink.Id
                && c.ToPinName.Equals("qualify", StringComparison.OrdinalIgnoreCase)
            )
            .Select(c => new QualifyBinding(c.FromNodeId, c.FromPinName))
            .ToList();

        return wired;
    }

    private IReadOnlyList<OrderBinding> ResolveOrderBindings(NodeInstance outputSink)
    {
        List<OrderBinding> ascending = _graph.Connections
            .Where(c =>
                c.ToNodeId == outputSink.Id
                && c.ToPinName.Equals("order_by", StringComparison.OrdinalIgnoreCase)
            )
            .Select(c => new OrderBinding(c.FromNodeId, c.FromPinName, Descending: false))
            .ToList();

        List<OrderBinding> descending = _graph.Connections
            .Where(c =>
                c.ToNodeId == outputSink.Id
                && c.ToPinName.Equals("order_by_desc", StringComparison.OrdinalIgnoreCase)
            )
            .Select(c => new OrderBinding(c.FromNodeId, c.FromPinName, Descending: true))
            .ToList();

        if (ascending.Count == 0 && descending.Count == 0)
            return [];

        ascending.AddRange(descending);
        return ascending;
    }

    private IReadOnlyList<GroupByBinding> ResolveGroupByBindings(NodeInstance outputSink)
    {
        List<GroupByBinding> wired = _graph.Connections
            .Where(c =>
                c.ToNodeId == outputSink.Id
                && c.ToPinName.Equals("group_by", StringComparison.OrdinalIgnoreCase)
            )
            .Select(c => new GroupByBinding(c.FromNodeId, c.FromPinName))
            .ToList();

        return wired;
    }

    private IReadOnlyList<LogicalOrderBinding> BuildOrderBindings(NodeInstance outputSink)
    {
        IReadOnlyList<OrderBinding> orderBindings = ResolveOrderBindings(outputSink);
        if (orderBindings.Count == 0)
            return [];

        return orderBindings
            .Select(binding =>
                new LogicalOrderBinding(
                    BuildColumnRefExpression(new Connection(binding.NodeId, binding.PinName, outputSink.Id, "order_by")),
                    binding.Descending
                )
            )
            .ToList();
    }

    private static JoinKind ResolveJoinKind(NodeInstance joinNode)
    {
        string raw = joinNode.Parameters.TryGetValue("join_type", out string? value)
            ? value?.Trim().ToUpperInvariant() ?? "INNER"
            : "INNER";

        return raw switch
        {
            "LEFT" => JoinKind.Left,
            "RIGHT" => JoinKind.Right,
            "FULL" => JoinKind.Full,
            "CROSS" => JoinKind.Cross,
            _ => JoinKind.Inner,
        };
    }

    private bool IsCteSubgraphOutputNode(string nodeId)
    {
        return _graph.Connections.Any(c =>
            c.FromNodeId == nodeId
            && c.FromPinName.Equals("result", StringComparison.OrdinalIgnoreCase)
            && c.ToPinName.Equals("query", StringComparison.OrdinalIgnoreCase)
            && _graph.NodeMap.TryGetValue(c.ToNodeId, out NodeInstance? target)
            && target.Type == NodeType.CteDefinition
        );
    }

    private void ValidateCteContracts(NodeInstance outputSink)
    {
        HashSet<string> defined = CollectDefinedCteNames();
        if (defined.Count == 0)
            return;

        ValidateCteDependencyCycles(defined, outputSink.Id);
    }

    private HashSet<string> CollectDefinedCteNames()
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (CteBinding cte in _graph.Ctes)
        {
            if (!string.IsNullOrWhiteSpace(cte.Name))
                names.Add(cte.Name.Trim());
        }

        foreach (NodeInstance node in _graph.Nodes.Where(n => n.Type == NodeType.CteDefinition))
        {
            if (node.Parameters.TryGetValue("cte_name", out string? name) && !string.IsNullOrWhiteSpace(name))
                names.Add(name.Trim());
        }

        return names;
    }

    private static string BuildSubquerySource(NodeInstance node)
    {
        string? subquerySql = node.Parameters.GetValueOrDefault("query")
            ?? node.Parameters.GetValueOrDefault("sql")
            ?? node.Parameters.GetValueOrDefault("raw_sql")
            ?? node.Parameters.GetValueOrDefault("statement");
        if (string.IsNullOrWhiteSpace(subquerySql))
        {
            throw new PlanningException(
                node.Id,
                PlannerErrorKind.DatasetNotReachableFromOutput,
                $"Subquery source '{node.Id}' has no query text.");
        }

        string trimmed = subquerySql.Trim().TrimEnd(';');
        if (!LooksLikeSelectStatement(trimmed))
        {
            throw new PlanningException(
                node.Id,
                PlannerErrorKind.DatasetNotReachableFromOutput,
                "Subquery source must start with SELECT, WITH, or a parenthesized SELECT. Ignoring Subquery source.");
        }

        if (!trimmed.StartsWith("(", StringComparison.Ordinal))
            trimmed = $"({trimmed})";

        return trimmed;
    }

    private static bool LooksLikeSelectStatement(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return false;

        string trimmed = sql.TrimStart();
        if (trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            return true;
        if (trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!trimmed.StartsWith("(", StringComparison.Ordinal))
            return false;

        string inner = trimmed[1..].TrimStart();
        return inner.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            || inner.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);
    }

    private void ValidateCteDependencyCycles(HashSet<string> knownCtes, string outputNodeId)
    {
        if (_graph.Ctes.Count == 0)
            return;

        Dictionary<string, HashSet<string>> adjacency = knownCtes.ToDictionary(
            keySelector: name => name,
            elementSelector: _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            comparer: StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> inDegree = knownCtes.ToDictionary(
            keySelector: name => name,
            elementSelector: _ => 0,
            comparer: StringComparer.OrdinalIgnoreCase);

        foreach (CteBinding cte in _graph.Ctes)
        {
            if (!knownCtes.Contains(cte.Name))
                continue;

            foreach (string dep in GetCteDependencies(cte, knownCtes))
            {
                if (dep.Equals(cte.Name, StringComparison.OrdinalIgnoreCase))
                {
                    if (!cte.Recursive)
                    {
                        throw new PlanningException(
                            outputNodeId,
                            PlannerErrorKind.CyclicDependency,
                            $"CTE '{cte.Name}' references itself but is not marked recursive."
                        );
                    }

                    continue;
                }

                if (adjacency[dep].Add(cte.Name))
                    inDegree[cte.Name]++;
            }
        }

        Queue<string> ready = new(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        int emitted = 0;
        while (ready.Count > 0)
        {
            string current = ready.Dequeue();
            emitted++;
            foreach (string next in adjacency[current])
            {
                inDegree[next]--;
                if (inDegree[next] == 0)
                    ready.Enqueue(next);
            }
        }

        if (emitted != knownCtes.Count)
        {
            throw new PlanningException(
                outputNodeId,
                PlannerErrorKind.CyclicDependency,
                "Cycle detected between CTE definitions."
            );
        }
    }

    private static IReadOnlyList<string> GetCteDependencies(
        CteBinding cte,
        IEnumerable<string> knownCteNames)
    {
        HashSet<string> known = new(knownCteNames, StringComparer.OrdinalIgnoreCase);
        HashSet<string> deps = new(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(cte.FromTable))
        {
            string fromToken = cte.FromTable.Trim()
                .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0]
                .Trim('"', '`', '[', ']');
            if (known.Contains(fromToken))
                deps.Add(fromToken);
        }

        foreach (NodeInstance node in cte.Graph.Nodes.Where(n => n.Type == NodeType.CteSource))
        {
            if (node.Parameters.TryGetValue("cte_name", out string? depName)
                && !string.IsNullOrWhiteSpace(depName)
                && known.Contains(depName.Trim()))
            {
                deps.Add(depName.Trim());
            }
        }

        return deps.ToList();
    }
}
