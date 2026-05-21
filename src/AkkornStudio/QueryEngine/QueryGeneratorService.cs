using System.Linq;
using SqlKata;
using SqlKata.Compilers;
using AkkornStudio.Core;
using AkkornStudio.Nodes;
using AkkornStudio.Nodes.LogicalPlan;
using AkkornStudio.Registry;

namespace AkkornStudio.QueryEngine;

// ─── Final compiled output ────────────────────────────────────────────────────

/// <summary>
/// The final product of the query generator — SQL string + named bindings.
/// Passed to <see cref="IDbOrchestrator.ExecutePreviewAsync"/>.
/// </summary>
public sealed record GeneratedQuery(
    string Sql,
    IReadOnlyDictionary<string, object?> Bindings,
    string DebugTree // human-readable expression tree for the canvas debug panel
);

public sealed record QueryGenerationStructure(
    string? FromTable = null,
    IReadOnlyList<JoinDefinition>? Joins = null);

// ─── Service ──────────────────────────────────────────────────────────────────

/// <summary>
/// Bridges the <see cref="NodeGraph"/> / <see cref="NodeGraphCompiler"/> world
/// with the SqlKata <see cref="Compiler"/> world.
///
/// Pipeline:
/// <code>
///   NodeGraph
///     → NodeGraphCompiler  (resolves expression trees)
///     → CompiledNodeGraph  (ISqlExpression trees per SELECT/WHERE/ORDER/GROUP)
///     → QueryGeneratorService.Generate()
///     → SqlKata Query       (structural: FROM, JOINs, paging)
///     + raw expression strings injected via SelectRaw / WhereRaw
///     → Compiler.Compile()
///     → GeneratedQuery { Sql, Bindings }
/// </code>
/// </summary>
public sealed class QueryGeneratorService(DatabaseProvider provider, ISqlFunctionRegistry registry)
{
    private readonly DatabaseProvider _provider = provider;
    private readonly ISqlFunctionRegistry _registry = registry;
    private readonly Compiler _compiler = CreateCompiler(provider);
    private readonly Providers.Dialects.ISqlDialect _dialect =
        new ProviderRegistry(DefaultProviderRegistrations.CreateAll()).GetDialect(provider);
    private readonly EmitContext _emitCtx = new EmitContext(provider, registry);

    public QueryGeneratorService(
        DatabaseProvider provider,
        ISqlFunctionRegistry registry,
        IProviderRegistry providerRegistry
    )
        : this(provider, registry)
    {
        ArgumentNullException.ThrowIfNull(providerRegistry);
        _dialect = providerRegistry.GetDialect(provider);
        _emitCtx = new EmitContext(provider, registry, providerRegistry);
    }

    public static QueryGeneratorService Create(DatabaseProvider provider) =>
        Create(provider, new ProviderRegistry(DefaultProviderRegistrations.CreateAll()));

    public static QueryGeneratorService Create(DatabaseProvider provider, IProviderRegistry providerRegistry)
    {
        ArgumentNullException.ThrowIfNull(providerRegistry);
        ISqlFunctionRegistry registry = providerRegistry.CreateFunctionRegistry(provider);
        return new QueryGeneratorService(provider, registry, providerRegistry);
    }

    // ── Main entry point ──────────────────────────────────────────────────────

    /// <summary>
    /// Compiles a <see cref="NodeGraph"/> plus optional structural overrides into
    /// a provider-correct SQL string.
    /// </summary>
    public GeneratedQuery Generate(
        NodeGraph graph,
        QueryGenerationStructure? structure = null,
        SetOperationDefinition? setOperation = null
    )
    {
        bool structureProvided = structure is not null;
        bool needsPlannedFromTable = string.IsNullOrWhiteSpace(structure?.FromTable);
        bool needsPlannedJoins = !structureProvided;
        (string? PlannedFromTable, IReadOnlyList<JoinDefinition>? PlannedJoins) plannedStructure =
            (null, null);
        if (needsPlannedFromTable || needsPlannedJoins)
            plannedStructure = TryBuildPlannedStructure(graph);

        string? overrideFromTable = structure?.FromTable;
        IReadOnlyList<JoinDefinition>? overrideJoins = structure?.Joins;
        string effectiveFromTable = string.IsNullOrWhiteSpace(overrideFromTable)
            ? plannedStructure.PlannedFromTable ?? string.Empty
            : overrideFromTable;
        IReadOnlyList<JoinDefinition>? effectiveJoins = overrideJoins
            ?? (structureProvided ? [] : plannedStructure.PlannedJoins);
        if (string.IsNullOrWhiteSpace(effectiveFromTable))
        {
            throw new InvalidOperationException(
                "Unable to resolve FROM source from graph. Connect an explicit dataset source to Result Output.");
        }

        var nodeCompiler = new NodeGraphCompiler(graph, _emitCtx, allowLegacyBindings: false);
        CompiledNodeGraph compiled = nodeCompiler.Compile();
        IReadOnlyList<WhereBinding> whereMeta = ResolveWhereMetaBindings(graph);
        Query query = BuildSqlKataQuery(effectiveFromTable, compiled, effectiveJoins, whereMeta);
        ApplyCtes(query, graph.Ctes);

        SqlResult result = _compiler.Compile(query);
        string finalSql = ApplyRecursiveCtePrefix(result.Sql, graph.Ctes);
        finalSql = ApplyQualifyClause(finalSql, compiled.QualifyExprs);
        finalSql = ApplySetOperation(finalSql, setOperation);
        finalSql = ApplyPivotOperation(finalSql, graph.PivotMode, graph.PivotConfig);
        finalSql = ApplyQueryHints(finalSql, graph.QueryHints);

        return new GeneratedQuery(finalSql, result.NamedBindings, BuildDebugTree(compiled));
    }

    /// <summary>
    /// Overload that accepts a pre-resolved <see cref="CompiledNodeGraph"/>
    /// (e.g. from the canvas preview panel that already ran the compiler).
    /// </summary>
    public GeneratedQuery Generate(
        string fromTable,
        CompiledNodeGraph compiled,
        IReadOnlyList<JoinDefinition>? joins = null,
        IReadOnlyList<WhereBinding>? whereMeta = null,
        SetOperationDefinition? setOperation = null,
        string? pivotMode = null,
        string? pivotConfig = null,
        string? queryHints = null
    )
    {
        Query query = BuildSqlKataQuery(fromTable, compiled, joins, whereMeta ?? []);

        SqlResult result = _compiler.Compile(query);
        string sql = ApplyQualifyClause(result.Sql, compiled.QualifyExprs);
        sql = ApplySetOperation(sql, setOperation);
        sql = ApplyPivotOperation(sql, pivotMode, pivotConfig);
        sql = ApplyQueryHints(sql, queryHints);
        return new GeneratedQuery(sql, result.NamedBindings, BuildDebugTree(compiled));
    }

    private (Query Query, CompiledNodeGraph Compiled) BuildQueryFromGraph(NodeGraph graph)
    {
        (string? PlannedFromTable, IReadOnlyList<JoinDefinition>? PlannedJoins) plannedStructure =
            TryBuildPlannedStructure(graph);

        string? effectiveFromTable = plannedStructure.PlannedFromTable;
        if (string.IsNullOrWhiteSpace(effectiveFromTable))
        {
            throw new InvalidOperationException(
                "Unable to resolve FROM source from graph. Connect an explicit dataset source to Result Output.");
        }

        IReadOnlyList<JoinDefinition> effectiveJoins = plannedStructure.PlannedJoins ?? [];

        var nodeCompiler = new NodeGraphCompiler(graph, _emitCtx, allowLegacyBindings: false);
        CompiledNodeGraph compiled = nodeCompiler.Compile();
        IReadOnlyList<WhereBinding> whereMeta = ResolveWhereMetaBindings(graph);
        Query query = BuildSqlKataQuery(effectiveFromTable, compiled, effectiveJoins, whereMeta);

        ApplyCtes(query, graph.Ctes);
        return (query, compiled);
    }

    private (string? PlannedFromTable, IReadOnlyList<JoinDefinition>? PlannedJoins) TryBuildPlannedStructure(
        NodeGraph graph)
    {
        try
        {
            LogicalOutput plan = new LogicalPlanner(graph, _emitCtx).Plan();
            if (TryMapPlanSourceToSqlStructure(plan.Source, out string? fromTable, out IReadOnlyList<JoinDefinition>? joins))
                return (fromTable, joins);

            return (null, null);
        }
        catch (PlanningException planningException)
        {
            throw new InvalidOperationException(
                $"Logical planner validation failed ({planningException.Kind}): {planningException.Message}",
                planningException);
        }
    }

    private bool TryMapPlanSourceToSqlStructure(
        LogicalNode source,
        out string? fromTable,
        out IReadOnlyList<JoinDefinition>? joins)
    {
        switch (source)
        {
            case LogicalScan scan:
                fromTable = ToFromOrTarget(scan);
                joins = [];
                return true;

            case LogicalJoin join:
                return TryMapJoinTree(join, out fromTable, out joins);

            default:
                fromTable = null;
                joins = null;
                return false;
        }
    }

    private bool TryMapJoinTree(
        LogicalJoin root,
        out string? fromTable,
        out IReadOnlyList<JoinDefinition>? joins)
    {
        if (!TryFlattenLeftDeepJoin(root, out LogicalScan? baseScan, out List<(LogicalScan Right, JoinDefinition Join)> flattened))
        {
            fromTable = null;
            joins = null;
            return false;
        }

        fromTable = ToFromOrTarget(baseScan!);
        joins = flattened.Select(x => x.Join).ToList();
        return true;
    }

    private bool TryFlattenLeftDeepJoin(
        LogicalJoin node,
        out LogicalScan? baseScan,
        out List<(LogicalScan Right, JoinDefinition Join)> flattened)
    {
        flattened = [];
        baseScan = null;

        if (node.Right is not LogicalScan rightScan)
            return false;

        if (!TryBuildJoinDefinition(node, rightScan, out JoinDefinition? currentJoin))
            return false;

        flattened.Add((rightScan, currentJoin!));

        if (node.Left is LogicalScan leftScan)
        {
            baseScan = leftScan;
            flattened.Reverse();
            return true;
        }

        if (node.Left is not LogicalJoin leftJoin)
            return false;

        if (!TryFlattenLeftDeepJoin(leftJoin, out baseScan, out List<(LogicalScan Right, JoinDefinition Join)> leftFlattened))
            return false;

        leftFlattened.AddRange(flattened);
        flattened = leftFlattened;
        return true;
    }

    private bool TryBuildJoinDefinition(LogicalJoin join, LogicalScan rightScan, out JoinDefinition? definition)
    {
        definition = null;

        string joinType = join.Kind switch
        {
            JoinKind.Left => "LEFT",
            JoinKind.Right => "RIGHT",
            JoinKind.Full => "FULL",
            JoinKind.Cross => "CROSS",
            _ => "INNER",
        };

        string target = ToFromOrTarget(rightScan);
        if (join.Condition is ComparisonExpr cmp
            && cmp.Left is ColumnExpr leftColumn
            && cmp.Right is ColumnExpr rightColumn)
        {
            definition = new JoinDefinition(
                target,
                $"{leftColumn.TableAlias}.{leftColumn.ColumnName}",
                $"{rightColumn.TableAlias}.{rightColumn.ColumnName}",
                Type: joinType,
                Operator: MapComparisonOperator(cmp.Op));
            return true;
        }

        definition = new JoinDefinition(
            target,
            "1",
            "1",
            Type: joinType,
            Operator: "=",
            OnRaw: join.Condition.Emit(_emitCtx));
        return true;
    }

    private static string ToFromOrTarget(LogicalScan scan)
    {
        if (string.IsNullOrWhiteSpace(scan.Alias))
            return scan.TableFullName;

        return $"{scan.TableFullName} AS {scan.Alias}";
    }

    private static string MapComparisonOperator(ComparisonOperator op)
    {
        return op switch
        {
            ComparisonOperator.Eq => "=",
            ComparisonOperator.Neq => "<>",
            ComparisonOperator.Gt => ">",
            ComparisonOperator.Gte => ">=",
            ComparisonOperator.Lt => "<",
            ComparisonOperator.Lte => "<=",
            ComparisonOperator.Like => "LIKE",
            ComparisonOperator.NotLike => "NOT LIKE",
            _ => "=",
        };
    }

    private Query BuildSqlKataQuery(
        string fromTable,
        CompiledNodeGraph compiled,
        IReadOnlyList<JoinDefinition>? joins,
        IReadOnlyList<WhereBinding> whereMeta
    )
    {
        Query query = IsSubqueryFromSource(fromTable)
            ? new Query().FromRaw(fromTable)
            : new Query(fromTable);
        ApplyJoins(query, joins);
        ApplySelects(query, compiled.SelectExprs);
        ValidateAggregateGroupByContract(compiled);
        if (compiled.Distinct)
            query.Distinct();
        ApplyWheres(query, compiled.WhereExprs, whereMeta);
        ApplyOrders(query, compiled.OrderExprs);
        IReadOnlyList<ISqlExpression> groupByExprs = ResolveGroupByExpressions(compiled);
        ApplyGroupBys(query, groupByExprs);
        ApplyHavings(query, compiled.HavingExprs, groupByExprs);
        if (compiled.Limit.HasValue)
            query.Limit(compiled.Limit.Value);
        if (compiled.Offset.HasValue)
            query.Offset(compiled.Offset.Value);
        return query;
    }

    private static bool IsSubqueryFromSource(string fromTable)
    {
        if (string.IsNullOrWhiteSpace(fromTable))
            return false;

        return fromTable.TrimStart().StartsWith("(", StringComparison.Ordinal);
    }

    private static string ApplySetOperation(string sql, SetOperationDefinition? setOperation)
    {
        if (setOperation is null)
            return sql;

        if (string.IsNullOrWhiteSpace(setOperation.QuerySql))
            return sql;

        string? normalizedOperator = NormalizeSetOperator(setOperation.Operator);
        if (normalizedOperator is null)
        {
            throw new InvalidOperationException(
                $"Unsupported set operation operator '{setOperation.Operator}'. Expected one of: UNION, UNION ALL, INTERSECT, EXCEPT."
            );
        }

        string left = TrimTrailingSemicolon(sql);
        string right = TrimTrailingSemicolon(setOperation.QuerySql);

        if (string.IsNullOrWhiteSpace(right))
            return sql;

        return $"{left}\n{normalizedOperator}\n{right}";
    }

    private string ApplyQualifyClause(string sql, IReadOnlyList<ISqlExpression> qualifyExprs)
    {
        if (qualifyExprs.Count == 0)
            return sql;

        var predicates = qualifyExprs
            .Select(e => NormalizeWherePredicate(e.Emit(_emitCtx)))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (predicates.Count == 0)
            return sql;

        string predicate = string.Join(" AND ", predicates);
        string inner = TrimTrailingSemicolon(sql);

        // Emulate QUALIFY across providers by filtering over a projected subquery.
        return $"SELECT * FROM ({inner}) AS _qualify\nWHERE {predicate}";
    }

    private string ApplyQueryHints(string sql, string? queryHints)
        => _dialect.ApplyQueryHints(sql, queryHints);

    private string ApplyPivotOperation(string sql, string? pivotMode, string? pivotConfig)
    {
        if (_provider != DatabaseProvider.SqlServer)
            return sql;

        string mode = (pivotMode ?? "NONE").Trim().ToUpperInvariant();
        if (mode is not ("PIVOT" or "UNPIVOT"))
            return sql;

        string config = (pivotConfig ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(config))
            return sql;

        if (!IsSafePivotConfig(config))
        {
            throw new InvalidOperationException(
                "Unsafe PIVOT/UNPIVOT configuration detected. Only simple pivot clauses are allowed."
            );
        }

        string baseSql = TrimTrailingSemicolon(sql);
        string sourceAlias = mode == "PIVOT" ? "_pivot_src" : "_unpivot_src";
        string targetAlias = mode == "PIVOT" ? "_pivot" : "_unpivot";
        return $"SELECT * FROM ({baseSql}) AS {sourceAlias}\n{mode} ({config}) AS {targetAlias}";
    }

    private static bool IsSafePivotConfig(string config)
    {
        if (config.Contains(';', StringComparison.Ordinal))
            return false;
        if (config.Contains("--", StringComparison.Ordinal))
            return false;
        if (config.Contains("/*", StringComparison.Ordinal) || config.Contains("*/", StringComparison.Ordinal))
            return false;
        if (config.Contains('\'') || config.Contains('"') || config.Contains('`'))
            return false;

        int depth = 0;
        foreach (char c in config)
        {
            if (c == '(')
                depth++;
            else if (c == ')')
            {
                depth--;
                if (depth < 0)
                    return false;
            }
        }

        return depth == 0;
    }

    private static string? NormalizeSetOperator(string? @operator)
    {
        if (string.IsNullOrWhiteSpace(@operator))
            return null;

        return @operator.Trim().ToUpperInvariant() switch
        {
            "UNION" => "UNION",
            "UNION ALL" => "UNION ALL",
            "INTERSECT" => "INTERSECT",
            "EXCEPT" => "EXCEPT",
            _ => null,
        };
    }

    private static string TrimTrailingSemicolon(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return string.Empty;

        return sql.Trim().TrimEnd(';').TrimEnd();
    }

    private void ApplyCtes(Query query, IReadOnlyList<CteBinding> ctes)
    {
        foreach (CteBinding cte in OrderCtesByDependencies(ctes))
        {
            (Query cteQuery, _) = BuildQueryFromGraph(
                cte.Graph);
            query.With(cte.Name, cteQuery);
        }
    }

    private IReadOnlyList<WhereBinding> ResolveWhereMetaBindings(NodeGraph graph)
    {
        List<NodeInstance> outputCandidates = graph.Nodes
            .Where(n => n.Type == NodeType.ResultOutput)
            .Where(n => !graph.Connections.Any(c =>
                c.FromNodeId == n.Id
                && c.FromPinName.Equals("result", StringComparison.OrdinalIgnoreCase)
                && c.ToPinName.Equals("query", StringComparison.OrdinalIgnoreCase)
                && graph.NodeMap.TryGetValue(c.ToNodeId, out NodeInstance? target)
                && target.Type == NodeType.CteDefinition))
            .ToList();

        NodeInstance? outputSink = outputCandidates.Count == 1 ? outputCandidates[0] : null;

        if (outputSink is null)
            return [];

        List<WhereBinding> wiredBindings = graph.Connections
            .Where(c =>
                c.ToNodeId == outputSink.Id
                && c.ToPinName.Equals("where", StringComparison.OrdinalIgnoreCase))
            .Select(c => new WhereBinding(c.FromNodeId, c.FromPinName))
            .ToList();

        if (wiredBindings.Count > 0)
            return wiredBindings;

        return [];
    }

    private static IReadOnlyList<CteBinding> OrderCtesByDependencies(IReadOnlyList<CteBinding> ctes)
    {
        if (ctes.Count == 0)
            return ctes;

        var byName = new Dictionary<string, CteBinding>(StringComparer.OrdinalIgnoreCase);
        var originalOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < ctes.Count; i++)
        {
            CteBinding cte = ctes[i];
            if (!byName.TryAdd(cte.Name, cte))
                throw new InvalidOperationException($"Duplicate CTE name '{cte.Name}'.");
            originalOrder[cte.Name] = i;
        }

        var adjacency = byName.Values.ToDictionary(
            c => c.Name,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase
        );
        var inDegree = byName.Values.ToDictionary(
            c => c.Name,
            _ => 0,
            StringComparer.OrdinalIgnoreCase
        );

        foreach (CteBinding cte in byName.Values)
        {
            foreach (string dep in GetCteDependencies(cte, byName.Keys))
            {
                if (dep.Equals(cte.Name, StringComparison.OrdinalIgnoreCase))
                {
                    if (!cte.Recursive)
                    {
                        throw new InvalidOperationException(
                            $"CTE '{cte.Name}' references itself but is not marked recursive."
                        );
                    }

                    continue;
                }

                // dependency -> current edge
                if (adjacency[dep].Add(cte.Name))
                    inDegree[cte.Name]++;
            }
        }

        var ready = inDegree
            .Where(kv => kv.Value == 0)
            .Select(kv => kv.Key)
            .OrderBy(name => originalOrder[name])
            .ToList();

        var sortedNames = new List<string>(ctes.Count);
        while (ready.Count > 0)
        {
            string name = ready[0];
            ready.RemoveAt(0);
            sortedNames.Add(name);

            foreach (string next in adjacency[name])
            {
                inDegree[next]--;
                if (inDegree[next] == 0)
                {
                    ready.Add(next);
                    ready.Sort((a, b) => originalOrder[a].CompareTo(originalOrder[b]));
                }
            }
        }

        if (sortedNames.Count != ctes.Count)
        {
            IEnumerable<string> cycle = inDegree.Where(kv => kv.Value > 0).Select(kv => kv.Key);
            throw new InvalidOperationException(
                "Cycle detected between CTE definitions: " + string.Join(", ", cycle)
            );
        }

        return sortedNames.Select(name => byName[name]).ToList();
    }

    private static IReadOnlyList<string> GetCteDependencies(
        CteBinding cte,
        IEnumerable<string> knownCteNames
    )
    {
        var known = new HashSet<string>(knownCteNames, StringComparer.OrdinalIgnoreCase);
        var deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(cte.FromTable))
        {
            string fromRef = cte.FromTable.Trim();
            string normalizedFromRef = NormalizeFromReferenceName(fromRef);
            if (known.Contains(normalizedFromRef))
                deps.Add(normalizedFromRef);
        }

        foreach (NodeInstance node in cte.Graph.Nodes)
        {
            if (node.Type != NodeType.CteSource)
                continue;

            if (
                node.Parameters.TryGetValue("cte_name", out string? dep)
                && !string.IsNullOrWhiteSpace(dep)
                && known.Contains(dep.Trim())
            )
            {
                deps.Add(dep.Trim());
            }
        }

        return deps.ToList();
    }

    private static string NormalizeFromReferenceName(string fromReference)
    {
        if (string.IsNullOrWhiteSpace(fromReference))
            return string.Empty;

        string firstToken = fromReference
            .Trim()
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0];

        if (firstToken.Length >= 2)
        {
            if (
                (firstToken[0] == '"' && firstToken[^1] == '"')
                || (firstToken[0] == '`' && firstToken[^1] == '`')
            )
            {
                return firstToken[1..^1];
            }

            if (firstToken[0] == '[' && firstToken[^1] == ']')
                return firstToken[1..^1];
        }

        return firstToken;
    }

    private string ApplyRecursiveCtePrefix(string sql, IReadOnlyList<CteBinding> ctes)
    {
        if (!ctes.Any(c => c.Recursive))
            return sql;

        // SQL Server recursive CTEs do not use the RECURSIVE keyword.
        if (_provider == DatabaseProvider.SqlServer)
            return sql;

        if (!sql.StartsWith("WITH ", StringComparison.OrdinalIgnoreCase))
            return sql;

        if (sql.StartsWith("WITH RECURSIVE ", StringComparison.OrdinalIgnoreCase))
            return sql;

        return "WITH RECURSIVE " + sql[5..];
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CLAUSE INJECTORS
    // ─────────────────────────────────────────────────────────────────────────

    private void ApplySelects(Query q, IReadOnlyList<(ISqlExpression Expr, string? Alias)> selects)
    {
        if (selects.Count == 0)
        {
            q.SelectRaw("*");
            return;
        }

        foreach ((ISqlExpression expr, string? alias) in selects)
        {
            string sql = expr.Emit(_emitCtx);
            q.SelectRaw(alias is null ? sql : $"{sql} AS {_emitCtx.QuoteIdentifier(alias)}");
        }
    }

    private static void ApplyJoins(Query q, IReadOnlyList<JoinDefinition>? joins)
    {
        if (joins is null)
            return;
        foreach (JoinDefinition j in joins)
        {
            string joinType = j.Type.ToUpperInvariant() switch
            {
                "LEFT" => "left join",
                "RIGHT" => "right join",
                "FULL" => "full join",
                "CROSS" => "cross join",
                _ => "join",
            };

            if (!string.IsNullOrWhiteSpace(j.OnRaw))
            {
                q.Join(j.TargetTable, x => x.WhereRaw(j.OnRaw), joinType);
                continue;
            }

            q.Join(
                j.TargetTable,
                j.LeftColumn,
                j.RightColumn,
                string.IsNullOrWhiteSpace(j.Operator) ? "=" : j.Operator,
                joinType
            );
        }
    }

    private void ApplyWheres(
        Query q,
        IReadOnlyList<ISqlExpression> whereExprs,
        IReadOnlyList<WhereBinding> bindings
    )
    {
        if (whereExprs.Count == 0)
            return;

        if (bindings.Count > 0 && bindings.Count < whereExprs.Count)
        {
            throw new InvalidOperationException(
                $"WHERE metadata mismatch: found {whereExprs.Count} predicate expression(s) but only {bindings.Count} logic binding(s)."
            );
        }

        var predicates = new List<(string Sql, string Op)>();
        for (int i = 0; i < whereExprs.Count; i++)
        {
            string op = i < bindings.Count ? bindings[i].LogicOp : "AND";
            string normalized = NormalizeWherePredicate(whereExprs[i].Emit(_emitCtx));
            if (string.IsNullOrWhiteSpace(normalized))
                continue;

            predicates.Add((normalized, op));
        }

        if (predicates.Count == 0)
            return;

        // When there's only one WHERE expression, inject it directly
        if (predicates.Count == 1)
        {
            q.WhereRaw(predicates[0].Sql);
            return;
        }

        // Multiple expressions: respect the LogicOp of each WhereBinding
        // Group consecutive OR bindings into sub-expressions, AND them together
        var andGroups = new List<List<string>>();
        var currentOr = new List<string>();

        foreach ((string sql, string op) in predicates)
        {
            if (op.Equals("OR", StringComparison.OrdinalIgnoreCase))
            {
                currentOr.Add(sql);
            }
            else
            {
                if (currentOr.Count > 0)
                {
                    andGroups.Add([.. currentOr]);
                    currentOr.Clear();
                }
                andGroups.Add([sql]);
            }
        }
        if (currentOr.Count > 0)
            andGroups.Add(currentOr);

        foreach (List<string> group in andGroups)
        {
            if (group.Count == 1)
            {
                q.WhereRaw(group[0]);
            }
            else
            {
                // Build (expr1 OR expr2 OR ...)
                string orClause = "(" + string.Join(" OR ", group) + ")";
                q.WhereRaw(orClause);
            }
        }
    }

    private string NormalizeWherePredicate(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return string.Empty;

        string trimmed = sql.Trim();
        if (trimmed.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        if (trimmed.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
        {
            return _provider == DatabaseProvider.Postgres ? "TRUE" : "1 = 1";
        }

        if (trimmed.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return _provider == DatabaseProvider.Postgres ? "FALSE" : "1 = 0";
        }

        return trimmed;
    }

    private void ApplyOrders(Query q, IReadOnlyList<(ISqlExpression Expr, bool Desc)> orders)
    {
        foreach ((ISqlExpression expr, bool desc) in orders)
        {
            string sql = expr.Emit(_emitCtx);
            if (_provider == DatabaseProvider.Postgres)
            {
                string direction = desc ? "DESC" : "ASC";
                string nulls = desc ? "NULLS FIRST" : "NULLS LAST";
                q.OrderByRaw($"{sql} {direction} {nulls}");
                continue;
            }

            // Normalize NULL ordering across providers that lack NULLS FIRST/LAST syntax.
            if (desc)
            {
                q.OrderByRaw($"CASE WHEN {sql} IS NULL THEN 0 ELSE 1 END ASC");
                q.OrderByRaw($"{sql} DESC");
            }
            else
            {
                q.OrderByRaw($"CASE WHEN {sql} IS NULL THEN 1 ELSE 0 END ASC");
                q.OrderByRaw($"{sql} ASC");
            }
        }
    }

    private void ApplyGroupBys(Query q, IReadOnlyList<ISqlExpression> groups)
    {
        foreach (ISqlExpression g in groups)
            q.GroupByRaw(g.Emit(_emitCtx));
    }

    private IReadOnlyList<ISqlExpression> ResolveGroupByExpressions(CompiledNodeGraph compiled)
        => compiled.GroupByExprs;

    private static void ValidateAggregateGroupByContract(CompiledNodeGraph compiled)
    {
        if (compiled.GroupByExprs.Count > 0)
            return;

        bool hasAggregateInSelect = compiled.SelectExprs.Any(binding => ContainsAggregate(binding.Expr));
        bool hasAggregateInHaving = compiled.HavingExprs.Any(ContainsAggregate);
        if (!hasAggregateInSelect && !hasAggregateInHaving)
            return;

        bool hasNonAggregateSelect = compiled.SelectExprs.Any(binding => !ContainsAggregate(binding.Expr));
        if (!hasNonAggregateSelect)
            return;

        throw new InvalidOperationException(
            "Aggregate expressions with non-aggregated selected columns require explicit GROUP BY connections.");
    }

    private static bool ContainsAggregate(ISqlExpression expr)
    {
        return expr switch
        {
            AggregateExpr => true,
            AliasExpr a => ContainsAggregate(a.Inner),
            CastExpr c => ContainsAggregate(c.Input),
            CaseExpr c => c.Whens.Any(w => ContainsAggregate(w.Condition) || ContainsAggregate(w.Result))
                || (c.Else is not null && ContainsAggregate(c.Else)),
            FunctionCallExpr f => IsAggregateFunctionName(f.FunctionName)
                || f.Args.Any(ContainsAggregate),
            RawSqlExpr raw => LooksLikeAggregateSql(raw.Sql),
            _ => false,
        };
    }

    private static bool IsAggregateFunctionName(string functionName) =>
        functionName.Equals("Count", StringComparison.OrdinalIgnoreCase)
            || functionName.Equals("CountStar", StringComparison.OrdinalIgnoreCase)
            || functionName.Equals("Sum", StringComparison.OrdinalIgnoreCase)
            || functionName.Equals("Avg", StringComparison.OrdinalIgnoreCase)
            || functionName.Equals("Min", StringComparison.OrdinalIgnoreCase)
            || functionName.Equals("Max", StringComparison.OrdinalIgnoreCase)
            || functionName.Equals("StringAgg", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeAggregateSql(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return false;

        string upper = sql.ToUpperInvariant();
        return upper.Contains("COUNT(", StringComparison.Ordinal)
            || upper.Contains("SUM(", StringComparison.Ordinal)
            || upper.Contains("AVG(", StringComparison.Ordinal)
            || upper.Contains("MIN(", StringComparison.Ordinal)
            || upper.Contains("MAX(", StringComparison.Ordinal)
            || upper.Contains("STRING_AGG(", StringComparison.Ordinal)
            || upper.Contains("GROUP_CONCAT(", StringComparison.Ordinal);
    }

    private void ApplyHavings(
        Query q,
        IReadOnlyList<ISqlExpression> havings,
        IReadOnlyList<ISqlExpression> groupBys
    )
    {
        ValidateHavingGroupByCompatibility(havings, groupBys);

        foreach (ISqlExpression h in havings)
            q.HavingRaw(h.Emit(_emitCtx));
    }

    private void ValidateHavingGroupByCompatibility(
        IReadOnlyList<ISqlExpression> havings,
        IReadOnlyList<ISqlExpression> groupBys
    )
    {
        if (havings.Count == 0 || groupBys.Count == 0)
            return;

        var groupedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ISqlExpression groupExpr in groupBys)
            CollectReferencedColumnsOutsideAggregates(groupExpr, insideAggregate: false, groupedColumns);

        foreach (ISqlExpression havingExpr in havings)
        {
            var requiredColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectReferencedColumnsOutsideAggregates(
                havingExpr,
                insideAggregate: false,
                requiredColumns
            );

            List<string> missing = requiredColumns
                .Where(column => !groupedColumns.Contains(column))
                .ToList();
            if (missing.Count == 0)
                continue;

            throw new InvalidOperationException(
                $"HAVING contains non-aggregated columns not present in GROUP BY: {string.Join(", ", missing)}."
            );
        }
    }

    private void CollectReferencedColumnsOutsideAggregates(
        ISqlExpression expr,
        bool insideAggregate,
        ISet<string> sink
    )
    {
        switch (expr)
        {
            case ColumnExpr col when !insideAggregate:
                sink.Add(col.Emit(_emitCtx));
                return;

            case AggregateExpr agg:
                if (agg.Inner is not null)
                    CollectReferencedColumnsOutsideAggregates(agg.Inner, insideAggregate: true, sink);
                return;

            case FunctionCallExpr f:
                bool aggregateFn = IsAggregateFunctionName(f.FunctionName);
                foreach (ISqlExpression arg in f.Args)
                {
                    CollectReferencedColumnsOutsideAggregates(
                        arg,
                        insideAggregate || aggregateFn,
                        sink
                    );
                }
                return;

            case AliasExpr a:
                CollectReferencedColumnsOutsideAggregates(a.Inner, insideAggregate, sink);
                return;

            case CastExpr c:
                CollectReferencedColumnsOutsideAggregates(c.Input, insideAggregate, sink);
                return;

            case ComparisonExpr c:
                CollectReferencedColumnsOutsideAggregates(c.Left, insideAggregate, sink);
                CollectReferencedColumnsOutsideAggregates(c.Right, insideAggregate, sink);
                return;

            case BetweenExpr b:
                CollectReferencedColumnsOutsideAggregates(b.Input, insideAggregate, sink);
                CollectReferencedColumnsOutsideAggregates(b.Lo, insideAggregate, sink);
                CollectReferencedColumnsOutsideAggregates(b.Hi, insideAggregate, sink);
                return;

            case LogicGateExpr logic:
                foreach (ISqlExpression operand in logic.Operands)
                    CollectReferencedColumnsOutsideAggregates(operand, insideAggregate, sink);
                return;

            case NotExpr n:
                CollectReferencedColumnsOutsideAggregates(n.Operand, insideAggregate, sink);
                return;

            case IsNullExpr n:
                CollectReferencedColumnsOutsideAggregates(n.Input, insideAggregate, sink);
                return;

            case CaseExpr c:
                foreach (WhenClause when in c.Whens)
                {
                    CollectReferencedColumnsOutsideAggregates(when.Condition, insideAggregate, sink);
                    CollectReferencedColumnsOutsideAggregates(when.Result, insideAggregate, sink);
                }
                if (c.Else is not null)
                    CollectReferencedColumnsOutsideAggregates(c.Else, insideAggregate, sink);
                return;

            default:
                return;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DEBUG TREE
    // ─────────────────────────────────────────────────────────────────────────

    private string BuildDebugTree(CompiledNodeGraph compiled)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("── SELECT ─────────────────────────────────────");
        if (compiled.Distinct)
            sb.AppendLine("  DISTINCT");
        if (compiled.SelectExprs.Count == 0)
            sb.AppendLine("  * (all columns)");
        foreach ((ISqlExpression expr, string? alias) in compiled.SelectExprs)
        {
            string sql = expr.Emit(_emitCtx);
            sb.AppendLine(alias is null ? $"  {sql}" : $"  {sql}  →  {alias}");
        }

        if (compiled.WhereExprs.Count > 0)
        {
            sb.AppendLine("── WHERE ──────────────────────────────────────");
            foreach (ISqlExpression expr in compiled.WhereExprs)
                sb.AppendLine($"  {expr.Emit(_emitCtx)}");
        }

        if (compiled.GroupByExprs.Count > 0)
        {
            sb.AppendLine("── GROUP BY ───────────────────────────────────");
            foreach (ISqlExpression expr in compiled.GroupByExprs)
                sb.AppendLine($"  {expr.Emit(_emitCtx)}");
        }

        if (compiled.HavingExprs.Count > 0)
        {
            sb.AppendLine("── HAVING ─────────────────────────────────────");
            foreach (ISqlExpression expr in compiled.HavingExprs)
                sb.AppendLine($"  {expr.Emit(_emitCtx)}");
        }

        if (compiled.QualifyExprs.Count > 0)
        {
            sb.AppendLine("── QUALIFY ────────────────────────────────────");
            foreach (ISqlExpression expr in compiled.QualifyExprs)
                sb.AppendLine($"  {expr.Emit(_emitCtx)}");
        }

        if (compiled.OrderExprs.Count > 0)
        {
            sb.AppendLine("── ORDER BY ───────────────────────────────────");
            foreach ((ISqlExpression expr, bool desc) in compiled.OrderExprs)
                sb.AppendLine($"  {expr.Emit(_emitCtx)} {(desc ? "DESC" : "ASC")}");
        }

        return sb.ToString();
    }

    // ── Compiler factory ──────────────────────────────────────────────────────

    private static Compiler CreateCompiler(DatabaseProvider p) =>
        p switch
        {
            DatabaseProvider.SqlServer => new SqlServerCompiler(),
            DatabaseProvider.MySql => new MySqlCompiler(),
            DatabaseProvider.Postgres => new PostgresCompiler(),
            DatabaseProvider.SQLite => new SqliteCompiler(),
            _ => throw new NotSupportedException($"No SqlKata compiler for {p}."),
        };
}
