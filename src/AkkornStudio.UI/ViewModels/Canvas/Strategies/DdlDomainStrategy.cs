using Avalonia;
using AkkornStudio.Metadata;
using AkkornStudio.Nodes;

namespace AkkornStudio.UI.ViewModels.Canvas.Strategies;

public sealed class DdlDomainStrategy : ICanvasDomainStrategy
{
    public string DomainName => "DDL";

    public bool CanEnterSubEditor(NodeViewModel node)
        => node.Type == NodeType.ViewDefinition;

    public Task<CanvasSnapshot?> GetSubEditorSeedAsync(NodeViewModel node)
    {
        _ = node;
        return Task.FromResult<CanvasSnapshot?>(null);
    }

    public void OnConnectionEstablished(
        ConnectionViewModel connection,
        IEnumerable<ConnectionViewModel> allConnections,
        IEnumerable<NodeViewModel> allNodes
    )
    {
        SyncAffectedTableDefinitions(connection, allConnections, allNodes);
    }

    public void OnConnectionRemoved(
        ConnectionViewModel connection,
        IEnumerable<ConnectionViewModel> allConnections,
        IEnumerable<NodeViewModel> allNodes
    )
    {
        SyncAffectedTableDefinitions(connection, allConnections, allNodes);
    }

    public void OnNodeAdded(NodeViewModel node, IEnumerable<ConnectionViewModel> allConnections)
    {
        if (!node.IsDdlTableDefinition())
            return;

        SyncTableProjectionRows(node, allConnections);
    }

    /// <summary>
    /// Re-syncs table previews when a connected ScalarType or EnumType parameter changes,
    /// so that the DataType badge on the table card reflects the new type spec.
    /// </summary>
    public void OnParameterChanged(
        NodeViewModel node,
        string paramName,
        IEnumerable<ConnectionViewModel> allConnections,
        IEnumerable<NodeViewModel> allNodes
    )
    {
        bool isScalarTypeParam = node.Type == NodeType.ScalarTypeDefinition
            && paramName is "TypeKind" or "Length" or "Precision" or "Scale";

        bool isEnumTypeParam = node.Type == NodeType.EnumTypeDefinition
            && string.Equals(paramName, "TypeName", StringComparison.OrdinalIgnoreCase);

        if (!isScalarTypeParam && !isEnumTypeParam)
            return;

        List<ConnectionViewModel> connections = [.. allConnections];

        // Collect every TableDefinition that is reachable through:
        // typeNode → type_def pin → ColumnDefinition → column pin → TableDefinition
        HashSet<NodeViewModel> tablesToSync = [];

        foreach (ConnectionViewModel typeConn in connections)
        {
            if (typeConn.FromPin.Owner != node)
                continue;
            if (typeConn.ToPin?.Name != "type_def")
                continue;

            NodeViewModel colNode = typeConn.ToPin.Owner;
            if (!colNode.IsDdlColumnDefinition())
                continue;

            foreach (ConnectionViewModel colConn in connections)
            {
                if (colConn.FromPin.Owner == colNode
                    && colConn.ToPin?.Owner.IsDdlTableDefinition() == true)
                {
                    tablesToSync.Add(colConn.ToPin.Owner);
                }
            }
        }

        foreach (NodeViewModel table in tablesToSync)
            SyncTableProjectionRows(table, connections);
    }

    public IReadOnlyList<NodeViewModel> GetOutputNodes(IEnumerable<NodeViewModel> nodes)
        =>
        [
            .. nodes.Where(n =>
                n.Type is NodeType.CreateTableOutput
                    or NodeType.CreateTypeOutput
                    or NodeType.CreateSequenceOutput
                    or NodeType.CreateTableAsOutput
                    or NodeType.CreateViewOutput
                    or NodeType.AlterViewOutput
                    or NodeType.AlterTableOutput
                    or NodeType.CreateIndexOutput
            ),
        ];

    public IReadOnlyList<NodeSuggestion> GetConnectionSuggestions(
        PinViewModel sourcePinViewModel,
        IEnumerable<NodeViewModel> canvasNodes
    )
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
        Action spawnQueryTableNode
    )
    {
        _ = isDdlModeActiveResolver;
        _ = spawnQueryTableNode;
        if (importDdlTableAction is null)
            return false;

        importDdlTableAction(table, position);
        return true;
    }

    private static void SyncAffectedTableDefinitions(
        ConnectionViewModel connection,
        IEnumerable<ConnectionViewModel> allConnections,
        IEnumerable<NodeViewModel> allNodes
    )
    {
        HashSet<NodeViewModel> tables =
        [
            .. allNodes.Where(n => n.IsDdlTableDefinition()),
        ];

        if (connection.ToPin?.Owner.IsDdlTableDefinition() == true)
            _ = tables.Add(connection.ToPin.Owner);

        if (connection.FromPin.Owner.IsDdlTableDefinition())
            _ = tables.Add(connection.FromPin.Owner);

        foreach (NodeViewModel table in tables)
            SyncTableProjectionRows(table, allConnections);
    }

    private static void SyncTableProjectionRows(
        NodeViewModel tableDefinitionNode,
        IEnumerable<ConnectionViewModel> allConnections
    )
    {
        List<ConnectionViewModel> connections = [.. allConnections];
        HashSet<NodeViewModel> connectedConstraints =
        [
            .. connections
                .Where(c => c.ToPin?.Owner == tableDefinitionNode && c.ToPin.Name == "constraint")
                .Select(c => c.FromPin.Owner),
        ];

        IReadOnlyList<NodeViewModel> connectedColumnNodes =
        [
            .. connections
                .Where(c => c.ToPin?.Owner == tableDefinitionNode && c.ToPin.Name == "column")
                .Select(c => c.FromPin.Owner)
                .Where(n => n.IsDdlColumnDefinition())
                .Distinct(),
        ];

        List<DdlTableColumnRowViewModel> rows = [];
        foreach (NodeViewModel columnNode in connectedColumnNodes)
        {
            string columnName = columnNode.Parameters.TryGetValue("ColumnName", out string? configuredColumnName)
                && !string.IsNullOrWhiteSpace(configuredColumnName)
                ? configuredColumnName
                : columnNode.Title;

            string dataType = ResolveColumnDataType(columnNode, connections);
            columnNode.Parameters["ResolvedDataTypeDisplay"] = dataType;
            columnNode.RaiseParameterChanged("ResolvedDataTypeDisplay");

            bool isNullable = columnNode.Parameters.TryGetValue("IsNullable", out string? nullableText)
                && (string.Equals(nullableText, "true", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(nullableText, "1", StringComparison.OrdinalIgnoreCase));

            bool isPk = false;
            bool isFk = false;
            bool isUnique = false;

            foreach (NodeViewModel constraintNode in connectedConstraints)
            {
                bool touchesConstraint = connections.Any(c =>
                    c.FromPin.Owner == columnNode && c.ToPin?.Owner == constraintNode
                );
                if (!touchesConstraint)
                    continue;

                if (constraintNode.IsDdlPrimaryKeyConstraint())
                    isPk = true;
                else if (constraintNode.IsDdlForeignKeyConstraint())
                    isFk = true;
                else if (constraintNode.IsDdlUniqueConstraint())
                    isUnique = true;
            }

            rows.Add(new DdlTableColumnRowViewModel(columnName, dataType, isNullable, isPk, isFk, isUnique));
        }

        tableDefinitionNode.ReplaceTableDefinitionColumns(rows);
    }

    /// <summary>
    /// Resolves the display data type for a ColumnDefinition node.
    /// If the <c>type_def</c> pin is connected, the type is read from the connected node.
    /// Otherwise, falls back to the raw <c>DataType</c> parameter.
    /// </summary>
    private static string ResolveColumnDataType(
        NodeViewModel columnNode,
        List<ConnectionViewModel> connections
    )
    {
        ConnectionViewModel? typeDefConn = connections.FirstOrDefault(c =>
            c.ToPin?.Owner == columnNode && c.ToPin.Name == "type_def");

        if (typeDefConn is not null)
            return ResolveTypeDefLabel(typeDefConn.FromPin.Owner);

        return columnNode.Parameters.TryGetValue("DataType", out string? configured)
            && !string.IsNullOrWhiteSpace(configured)
            ? configured
            : "INT";
    }

    /// <summary>
    /// Formats the type label from a ScalarTypeDefinition or EnumTypeDefinition node
    /// for display in the table-definition ERD preview.
    /// </summary>
    private static string ResolveTypeDefLabel(NodeViewModel typeNode)
    {
        if (typeNode.Type == NodeType.ScalarTypeDefinition)
            return typeNode.ScalarTypeInlineLabel;

        if (typeNode.Type == NodeType.EnumTypeDefinition)
        {
            typeNode.Parameters.TryGetValue("TypeName", out string? enumName);
            return string.IsNullOrWhiteSpace(enumName) ? "ENUM" : enumName.Trim();
        }

        return "TYPE";
    }
}
