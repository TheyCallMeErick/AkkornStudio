namespace AkkornStudio.Nodes;

// ═════════════════════════════════════════════════════════════════════════════
// RUNTIME NODE INSTANCE  (one per placed node on the canvas)
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// A placed node on the canvas. Serialised to/from the canvas state JSON.
/// </summary>
public sealed record NodeInstance(
    string Id,
    NodeType Type,
    /// <summary>
    /// Literal values for input pins that are NOT wired via a Connection
    /// (e.g. a constant entered in the property panel for the "right" pin of Equals).
    /// Key = pin name, Value = raw SQL literal string.
    /// </summary>
    IReadOnlyDictionary<string, string> PinLiterals,
    /// <summary>
    /// Node-level parameters (not pins) — e.g. ROUND precision = "2".
    /// Key = parameter name, Value = string representation.
    /// </summary>
    IReadOnlyDictionary<string, string> Parameters,
    /// <summary>Optional display alias for SELECT output pins.</summary>
    string? Alias = null,
    /// <summary>Only used for TableSource nodes: "schema.table"</summary>
    string? TableFullName = null,
    /// <summary>Only used for TableSource nodes: column name → output pin name</summary>
    IReadOnlyDictionary<string, string>? ColumnPins = null,
    /// <summary>Only used for TableSource nodes: output pin name → structural pin type</summary>
    IReadOnlyDictionary<string, PinDataType>? ColumnPinTypes = null
);

// ═════════════════════════════════════════════════════════════════════════════
// CONNECTION  (directed edge: one output pin → one input pin)
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// A wire between two canvas nodes.
/// AllowMultiple pins (AND, OR) can have multiple Connection objects pointing
/// to the same ToNodeId + ToPinName.
/// </summary>
public sealed record Connection(
    string FromNodeId,
    string FromPinName,
    string ToNodeId,
    string ToPinName
);

// ═════════════════════════════════════════════════════════════════════════════
// OUTPUT BINDING  (how node output pins map to SELECT / WHERE)
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Binds a node's output pin to the SELECT list.
/// The canvas draws a wire from any expression node to the SelectOutput sink.
/// </summary>
public sealed record SelectBinding(string NodeId, string PinName, string? Alias = null);

/// <summary>
/// Binds a boolean node's output pin to the WHERE clause.
/// Multiple WhereBindings are AND-ed together by default.
/// </summary>
public sealed record WhereBinding(
    string NodeId,
    string PinName,
    string LogicOp = "AND" // AND | OR — how to combine with the previous condition
);

/// <summary>
/// Binds a node output to ORDER BY.
/// </summary>
public sealed record OrderBinding(string NodeId, string PinName, bool Descending = false);

/// <summary>
/// Binds a node output to GROUP BY.
/// </summary>
public sealed record GroupByBinding(string NodeId, string PinName);

/// <summary>
/// Binds a boolean/aggregate expression to the HAVING clause.
/// Multiple HavingBindings are AND-ed by default.
/// </summary>
public sealed record HavingBinding(string NodeId, string PinName);

/// <summary>
/// Binds a boolean expression to post-window filtering (QUALIFY semantics).
/// Multiple QualifyBindings are AND-ed by default.
/// </summary>
public sealed record QualifyBinding(string NodeId, string PinName);

/// <summary>
/// Defines a CTE to prepend to the query with WITH name AS (...).
/// </summary>
public sealed record CteBinding(
    string Name,
    string FromTable,
    NodeGraph Graph,
    bool Recursive = false
);

// ═════════════════════════════════════════════════════════════════════════════
// NODE GRAPH  (complete canvas state → compiler input)
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// The complete canvas graph. The <see cref="NodeGraphCompiler"/> walks this
/// structure to produce a <see cref="CompiledNodeGraph"/>.
///
/// The canvas serialises one NodeGraph per query and calls Compile() to get SQL.
/// </summary>
public sealed class NodeGraph
{
    private IReadOnlyList<NodeInstance> _nodes = [];
    private Dictionary<string, NodeInstance>? _nodeMap;

    public IReadOnlyList<NodeInstance> Nodes
    {
        get => _nodes;
        init
        {
            if (_nodes is System.Collections.Specialized.INotifyCollectionChanged previousNotifyingCollection)
                previousNotifyingCollection.CollectionChanged -= HandleNodesCollectionChanged;

            _nodes = value ?? throw new ArgumentNullException(nameof(value));

            if (_nodes is System.Collections.Specialized.INotifyCollectionChanged notifyingCollection)
                notifyingCollection.CollectionChanged += HandleNodesCollectionChanged;

            _nodeMap = null;
        }
    }
    public IReadOnlyList<Connection> Connections { get; init; } = [];

    // ── Common table expressions ─────────────────────────────────────────────
    public IReadOnlyList<CteBinding> Ctes { get; init; } = [];

    // ── Output bindings ───────────────────────────────────────────────────────
    public IReadOnlyList<SelectBinding> SelectOutputs { get; init; } = [];
    public IReadOnlyList<WhereBinding> WhereConditions { get; init; } = [];
    public IReadOnlyList<HavingBinding> Havings { get; init; } = [];
    public IReadOnlyList<QualifyBinding> Qualifies { get; init; } = [];
    public string? QueryHints { get; init; }
    public string? PivotMode { get; init; }
    public string? PivotConfig { get; init; }
    public IReadOnlyList<OrderBinding> OrderBys { get; init; } = [];
    public IReadOnlyList<GroupByBinding> GroupBys { get; init; } = [];
    public bool Distinct { get; init; }

    // ── Pagination ────────────────────────────────────────────────────────────
    public int? Limit { get; init; }
    public int? Offset { get; init; }

    // ── Derived lookups (computed on first access) ────────────────────────────

    public IReadOnlyDictionary<string, NodeInstance> NodeMap
    {
        get
        {
            // For non-observable mutable lists, rebuild on every access to avoid stale cache.
            if (_nodes is not System.Collections.Specialized.INotifyCollectionChanged)
                return _nodes.ToDictionary(n => n.Id);

            return _nodeMap ??= _nodes.ToDictionary(n => n.Id);
        }
    }

    private void HandleNodesCollectionChanged(
        object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs args
    ) => _nodeMap = null;

    /// <summary>
    /// Returns all Connection objects that wire INTO the given node's input pin.
    /// For AllowMultiple pins (AND, OR) this returns multiple connections.
    /// </summary>
    public IReadOnlyList<Connection> GetInputConnections(string nodeId, string pinName) =>
        Connections.Where(c => c.ToNodeId == nodeId && c.ToPinName == pinName).ToList();

    /// <summary>
    /// Returns the single connection that feeds the given node's input pin.
    /// Returns null if the pin has a literal value instead.
    /// Throws if more than one connection exists (use GetInputConnections for multi-pins).
    /// </summary>
    public Connection? GetSingleInputConnection(string nodeId, string pinName)
    {
        var matches = Connections
            .Where(c => c.ToNodeId == nodeId && c.ToPinName == pinName)
            .ToList();

        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"Pin '{pinName}' on node '{nodeId}' has {matches.Count} connections; "
                    + $"expected at most 1. Use GetInputConnections for multi-input pins."
            ),
        };
    }

    // ── Topological sort ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns nodes in dependency order (sources first, sinks last).
    /// Used by the compiler to ensure every node's inputs are resolved
    /// before the node itself is compiled.
    /// Throws <see cref="InvalidOperationException"/> if a cycle is detected.
    /// </summary>
    public IReadOnlyList<NodeInstance> TopologicalOrder()
    {
        // Kahn's algorithm on the node graph
        var inDegree = Nodes.ToDictionary(n => n.Id, _ => 0);

        // Count in-edges (each connection increases target node's in-degree)
        foreach (Connection conn in Connections)
        {
            if (!inDegree.ContainsKey(conn.ToNodeId))
                inDegree[conn.ToNodeId] = 0;
            inDegree[conn.ToNodeId]++;
        }

        var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));

        var sorted = new List<NodeInstance>();
        IReadOnlyDictionary<string, NodeInstance> nodeMap = NodeMap;

        while (queue.Count > 0)
        {
            string id = queue.Dequeue();
            if (nodeMap.TryGetValue(id, out NodeInstance? node))
                sorted.Add(node);

            // Find all nodes that depend on 'id' (i.e. 'id' feeds their inputs)
            foreach (Connection? outgoing in Connections.Where(c => c.FromNodeId == id))
            {
                inDegree[outgoing.ToNodeId]--;
                if (inDegree[outgoing.ToNodeId] == 0)
                    queue.Enqueue(outgoing.ToNodeId);
            }
        }

        if (sorted.Count != Nodes.Count)
            throw new InvalidOperationException(
                "Cycle detected in the node graph. Remove circular connections."
            );

        return sorted;
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// COMPILED NODE GRAPH  (output of the compiler)
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// The resolved expression tree — ready to feed into <see cref="QueryEngine.QueryBuilderService"/>.
/// </summary>
public sealed record CompiledNodeGraph(
    IReadOnlyList<(ISqlExpression Expr, string? Alias)> SelectExprs,
    IReadOnlyList<ISqlExpression> WhereExprs,
    IReadOnlyList<ISqlExpression> HavingExprs,
    IReadOnlyList<ISqlExpression> QualifyExprs,
    IReadOnlyList<(ISqlExpression Expr, bool Desc)> OrderExprs,
    IReadOnlyList<ISqlExpression> GroupByExprs,
    bool Distinct,
    int? Limit,
    int? Offset
);
