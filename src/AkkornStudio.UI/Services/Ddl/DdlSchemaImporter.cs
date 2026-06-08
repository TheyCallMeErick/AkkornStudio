using Avalonia;
using System.Text.Json;
using System.Text.RegularExpressions;
using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.Nodes;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.Serialization;
using AkkornStudio.UI.ViewModels.Canvas;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Services.Ddl;

public sealed record DdlImportResult(
    int TableCount,
    int ColumnCount,
    int ForeignKeyCount,
    int IndexCount,
    IReadOnlyList<string>? Warnings = null
);

public sealed record DdlPartialImportResult(
    bool TableAdded,
    int AddedNodeCount,
    int AddedConnectionCount,
    int AddedForeignKeys
);

/// <summary>
/// Imports the connected database schema into the DDL canvas.
/// </summary>
public sealed class DdlSchemaImporter
{
    public DdlImportResult Import(DbMetadata metadata, CanvasViewModel canvas)
    {
        if (metadata is null)
            throw new ArgumentNullException(nameof(metadata));

        if (canvas is null)
            throw new ArgumentNullException(nameof(canvas));

        List<TableMetadata> tables = metadata
            .AllTables
            .Where(t => t.Kind == TableKind.Table)
            .OrderBy(t => t.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<TableMetadata> views = metadata
            .AllTables
            .Where(t => t.Kind is TableKind.View or TableKind.MaterializedView)
            .OrderBy(t => t.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var warnings = new List<string>();

        if (tables.Count == 0 && views.Count == 0)
        {
            canvas.ReplaceGraph([], []);
            return new DdlImportResult(0, 0, 0, 0);
        }

        var nodes = new List<NodeViewModel>();
        var connections = new List<ConnectionViewModel>();
        var tableNodes = new Dictionary<string, NodeViewModel>(StringComparer.OrdinalIgnoreCase);
        var sequenceNodes = new Dictionary<string, NodeViewModel>(StringComparer.OrdinalIgnoreCase);
        var columnNodesByTable = new Dictionary<string, Dictionary<string, NodeViewModel>>(StringComparer.OrdinalIgnoreCase);

        const double tableSpacingX = 520;
        const double tableSpacingY = 340;
        const int maxRowsPerColumn = 4;

        int tableIndex = 0;
        int totalColumns = 0;
        int totalForeignKeys = 0;
        int totalIndexes = 0;

        int sequenceIndex = 0;
        foreach (SequenceMetadata sequence in metadata.AllSequences.OrderBy(s => s.Schema, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            int gridColumn = sequenceIndex / maxRowsPerColumn;
            int gridRow = sequenceIndex % maxRowsPerColumn;

            double baseX = 120 + (gridColumn * tableSpacingX);
            double baseY = 80 + (gridRow * 180);

            NodeViewModel sequenceNode = NewNode(NodeType.SequenceDefinition, baseX, baseY);
            sequenceNode.Parameters["Schema"] = sequence.Schema;
            sequenceNode.Parameters["SequenceName"] = sequence.Name;
            sequenceNode.Parameters["StartValue"] = sequence.StartValue?.ToString() ?? string.Empty;
            sequenceNode.Parameters["Increment"] = sequence.Increment?.ToString() ?? string.Empty;
            sequenceNode.Parameters["MinValue"] = sequence.MinValue?.ToString() ?? string.Empty;
            sequenceNode.Parameters["MaxValue"] = sequence.MaxValue?.ToString() ?? string.Empty;
            sequenceNode.Parameters["Cycle"] = sequence.Cycle.GetValueOrDefault() ? "true" : "false";
            sequenceNode.Parameters["Cache"] = sequence.Cache?.ToString() ?? string.Empty;

            NodeViewModel createSequenceOutputNode = NewNode(NodeType.CreateSequenceOutput, baseX + 280, baseY);
            Connect(sequenceNode, "seq", createSequenceOutputNode, "seq", connections);

            nodes.Add(sequenceNode);
            nodes.Add(createSequenceOutputNode);
            sequenceNodes[sequence.FullName] = sequenceNode;
            sequenceIndex++;
        }

        foreach (TableMetadata table in tables)
        {
            int gridColumn = tableIndex / maxRowsPerColumn;
            int gridRow = tableIndex % maxRowsPerColumn;

            double baseX = 120 + (gridColumn * tableSpacingX);
            double baseY = 80 + (gridRow * tableSpacingY);

            NodeViewModel tableNode = NewNode(NodeType.TableDefinition, baseX, baseY);
            tableNode.Parameters["SchemaName"] = table.Schema;
            tableNode.Parameters["TableName"] = table.Name;
            tableNode.Parameters["IfNotExists"] = "true";
            tableNode.Parameters["Comment"] = table.Comment ?? string.Empty;

            NodeViewModel createOutputNode = NewNode(NodeType.CreateTableOutput, baseX + 280, baseY);
            Connect(tableNode, "table", createOutputNode, "table", connections);

            nodes.Add(tableNode);
            nodes.Add(createOutputNode);
            tableNodes[table.FullName] = tableNode;

            var tableColumnNodes = new Dictionary<string, NodeViewModel>(StringComparer.OrdinalIgnoreCase);
            columnNodesByTable[table.FullName] = tableColumnNodes;

            int columnOffset = 0;
            foreach (ColumnMetadata column in table.Columns.OrderBy(c => c.OrdinalPosition))
            {
                NodeViewModel columnNode = NewNode(NodeType.ColumnDefinition, baseX - 300, baseY + (columnOffset * 86));
                columnNode.Parameters["ColumnName"] = column.Name;
                columnNode.Parameters["DataType"] = NormalizeDataType(column);
                columnNode.Parameters["IsNullable"] = column.IsNullable ? "true" : "false";
                columnNode.Parameters["Comment"] = column.Comment ?? string.Empty;

                if (TryResolveSequenceDefault(metadata.Provider, column.DefaultValue, out string? sequenceFullName)
                    && !string.IsNullOrWhiteSpace(sequenceFullName)
                    && sequenceNodes.TryGetValue(sequenceFullName, out NodeViewModel? sequenceNode))
                {
                    Connect(sequenceNode, "seq", columnNode, "sequence", connections);
                }

                if (TryBuildEnumTypeNode(metadata.Provider, column, table, baseX, baseY + (columnOffset * 86), out NodeViewModel? enumTypeNode, out NodeViewModel? typeOutputNode))
                {
                    nodes.Add(enumTypeNode!);
                    if (typeOutputNode is not null)
                    {
                        nodes.Add(typeOutputNode);
                        Connect(enumTypeNode!, "type_def", typeOutputNode, "type_def", connections);
                    }
                    columnNode.Parameters["DataType"] = "ENUM";
                    Connect(enumTypeNode!, "type_def", columnNode, "type_def", connections);
                }

                nodes.Add(columnNode);
                Connect(columnNode, "column", tableNode, "column", connections);

                tableColumnNodes[column.Name] = columnNode;
                columnOffset++;
                totalColumns++;
            }

            IReadOnlyList<ColumnMetadata> pkColumns = table.Columns
                .Where(c => c.IsPrimaryKey)
                .OrderBy(c => c.OrdinalPosition)
                .ToList();

            if (pkColumns.Count > 0)
            {
                NodeViewModel pkNode = NewNode(NodeType.PrimaryKeyConstraint, baseX - 40, baseY - 120);
                pkNode.Parameters["ConstraintName"] = $"PK_{table.Name}";
                nodes.Add(pkNode);

                Connect(pkNode, "pk", tableNode, "constraint", connections);
                foreach (ColumnMetadata pkCol in pkColumns)
                {
                    if (tableColumnNodes.TryGetValue(pkCol.Name, out NodeViewModel? colNode))
                        Connect(colNode, "column", pkNode, "column", connections);
                }
            }

            foreach (IndexMetadata uniqueIndex in table.Indexes.Where(i => i.IsUnique && !i.IsPrimaryKey))
            {
                NodeViewModel uqNode = NewNode(NodeType.UniqueConstraint, baseX + 40, baseY - 120 - (totalIndexes % 3 * 72));
                uqNode.Parameters["ConstraintName"] = uniqueIndex.Name;
                nodes.Add(uqNode);

                Connect(uqNode, "uq", tableNode, "constraint", connections);
                foreach (string colName in uniqueIndex.Columns)
                {
                    if (tableColumnNodes.TryGetValue(colName, out NodeViewModel? colNode))
                        Connect(colNode, "column", uqNode, "column", connections);
                }

                totalIndexes++;
            }

            tableIndex++;
        }

        var viewNodes = new Dictionary<string, NodeViewModel>(StringComparer.OrdinalIgnoreCase);
        int viewIndex = 0;
        foreach (TableMetadata view in views)
        {
            int gridColumn = (tableIndex + viewIndex) / maxRowsPerColumn;
            int gridRow = (tableIndex + viewIndex) % maxRowsPerColumn;

            double baseX = 120 + (gridColumn * tableSpacingX);
            double baseY = 80 + (gridRow * tableSpacingY);

            AddViewSubgraph(metadata.Provider, view, new Point(baseX, baseY), nodes, connections, viewNodes);

            warnings.Add(
                string.Format(
                    L(
                        "ddlImporter.warning.viewSelectNotReconstructable",
                        "View '{0}': view SELECT cannot be reconstructed visually - edit it manually in the subcanvas."
                    ),
                    view.FullName
                )
            );
            viewIndex++;
        }

        var fkGroups = metadata.AllForeignKeys
            .GroupBy(f => $"{f.ChildSchema}.{f.ChildTable}::{f.ConstraintName}", StringComparer.OrdinalIgnoreCase)
            .ToList();

        int fkVisualIndex = 0;
        foreach (IGrouping<string, ForeignKeyRelation> group in fkGroups)
        {
            List<ForeignKeyRelation> relations = group.OrderBy(r => r.OrdinalPosition).ToList();
            if (relations.Count == 0)
                continue;

            ForeignKeyRelation first = relations[0];
            string childTableFullName = Qualify(first.ChildSchema, first.ChildTable);
            string parentTableFullName = Qualify(first.ParentSchema, first.ParentTable);

            if (!tableNodes.TryGetValue(childTableFullName, out NodeViewModel? childTableNode))
                continue;

            if (!columnNodesByTable.TryGetValue(childTableFullName, out Dictionary<string, NodeViewModel>? childColumns))
                continue;

            NodeViewModel fkNode = NewNode(
                NodeType.ForeignKeyConstraint,
                childTableNode.Position.X + 120,
                childTableNode.Position.Y + 140 + (fkVisualIndex % 3 * 72)
            );

            fkNode.Parameters["ConstraintName"] = first.ConstraintName;
            fkNode.Parameters["OnDelete"] = ToReferentialAction(first.OnDelete);
            fkNode.Parameters["OnUpdate"] = ToReferentialAction(first.OnUpdate);

            nodes.Add(fkNode);
            Connect(fkNode, "fk", childTableNode, "constraint", connections);

            foreach (ForeignKeyRelation relation in relations)
            {
                if (childColumns.TryGetValue(relation.ChildColumn, out NodeViewModel? childColumnNode))
                    Connect(childColumnNode, "column", fkNode, "child_column", connections);

                if (columnNodesByTable.TryGetValue(parentTableFullName, out Dictionary<string, NodeViewModel>? parentColumns)
                    && parentColumns.TryGetValue(relation.ParentColumn, out NodeViewModel? parentColumnNode))
                {
                    Connect(parentColumnNode, "column", fkNode, "parent_column", connections);
                }
            }

            fkVisualIndex++;
            totalForeignKeys++;
        }

        canvas.Provider = metadata.Provider;
        ApplyAutoLayout(nodes, connections, new Point(120, 80));
        canvas.ReplaceGraph(nodes, connections);

        return new DdlImportResult(tables.Count, totalColumns, totalForeignKeys, totalIndexes, warnings);
    }

    public DdlPartialImportResult ImportTable(
        DbMetadata metadata,
        string fullTableName,
        CanvasViewModel canvas,
        Point? suggestedOrigin = null
    )
    {
        if (metadata is null)
            throw new ArgumentNullException(nameof(metadata));

        if (canvas is null)
            throw new ArgumentNullException(nameof(canvas));

        TableMetadata? table = metadata.FindTable(fullTableName);
        if (table is null)
            throw new InvalidOperationException(
                string.Format(
                    L("ddlImporter.error.tableNotFoundInMetadata", "Table '{0}' was not found in current metadata."),
                    fullTableName
                )
            );

        var nodes = canvas.Nodes.ToList();
        var connections = canvas.Connections.ToList();
        var tableNodes = BuildExistingTableNodeMap(nodes);
        var viewNodes = BuildExistingViewNodeMap(nodes);
        var sequenceNodes = BuildExistingSequenceNodeMap(nodes);
        var columnNodesByTable = BuildExistingColumnNodeMap(nodes, connections);

        bool isView = table.Kind is TableKind.View or TableKind.MaterializedView;

        if (!isView && tableNodes.ContainsKey(table.FullName))
            return new DdlPartialImportResult(false, 0, 0, 0);

        if (isView && viewNodes.ContainsKey(table.FullName))
            return new DdlPartialImportResult(false, 0, 0, 0);

        Point origin = suggestedOrigin ?? ComputeSuggestedOrigin(nodes);

        int beforeNodes = nodes.Count;
        int beforeConnections = connections.Count;

        int fkAdded = 0;
        if (isView)
        {
            AddViewSubgraph(metadata.Provider, table, origin, nodes, connections, viewNodes);
        }
        else
        {
            AddTableSubgraph(metadata.Provider, metadata, table, origin, nodes, connections, tableNodes, sequenceNodes, columnNodesByTable, out int _);
            fkAdded = AddForeignKeysForChildTable(metadata, table.FullName, nodes, connections, tableNodes, columnNodesByTable);
        }

        ApplyAutoLayout(nodes, connections, new Point(120, 80));

        canvas.Provider = metadata.Provider;
        canvas.ReplaceGraph(nodes, connections);

        return new DdlPartialImportResult(
            true,
            nodes.Count - beforeNodes,
            connections.Count - beforeConnections,
            fkAdded
        );
    }

    private static Dictionary<string, NodeViewModel> BuildExistingTableNodeMap(IEnumerable<NodeViewModel> nodes)
    {
        var map = new Dictionary<string, NodeViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (NodeViewModel node in nodes.Where(n => n.Type == NodeType.TableDefinition))
        {
            string schema = node.Parameters.TryGetValue("SchemaName", out string? schemaName)
                ? schemaName ?? string.Empty
                : string.Empty;
            string table = node.Parameters.TryGetValue("TableName", out string? tableName)
                ? tableName ?? string.Empty
                : string.Empty;

            if (string.IsNullOrWhiteSpace(table))
                continue;

            map[Qualify(schema, table)] = node;
        }

        return map;
    }

    private static Dictionary<string, NodeViewModel> BuildExistingViewNodeMap(IEnumerable<NodeViewModel> nodes)
    {
        var map = new Dictionary<string, NodeViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (NodeViewModel node in nodes.Where(n => n.Type == NodeType.ViewDefinition))
        {
            string schema = node.Parameters.TryGetValue("Schema", out string? schemaName)
                ? schemaName ?? string.Empty
                : string.Empty;
            string view = node.Parameters.TryGetValue("ViewName", out string? viewName)
                ? viewName ?? string.Empty
                : string.Empty;

            if (string.IsNullOrWhiteSpace(view))
                continue;

            map[Qualify(schema, view)] = node;
        }

        return map;
    }

    private static Dictionary<string, Dictionary<string, NodeViewModel>> BuildExistingColumnNodeMap(
        IEnumerable<NodeViewModel> nodes,
        IEnumerable<ConnectionViewModel> connections)
    {
        _ = nodes;
        var result = new Dictionary<string, Dictionary<string, NodeViewModel>>(StringComparer.OrdinalIgnoreCase);

        foreach (ConnectionViewModel connection in connections)
        {
            if (connection.ToPin is null)
                continue;

            if (connection.ToPin.Name != "column")
                continue;

            NodeViewModel toNode = connection.ToPin.Owner;
            NodeViewModel fromNode = connection.FromPin.Owner;
            if (toNode.Type != NodeType.TableDefinition || fromNode.Type != NodeType.ColumnDefinition)
                continue;

            string schema = toNode.Parameters.TryGetValue("SchemaName", out string? schemaName)
                ? schemaName ?? string.Empty
                : string.Empty;
            string table = toNode.Parameters.TryGetValue("TableName", out string? tableName)
                ? tableName ?? string.Empty
                : string.Empty;
            string full = Qualify(schema, table);

            if (!result.TryGetValue(full, out Dictionary<string, NodeViewModel>? tableCols))
            {
                tableCols = new Dictionary<string, NodeViewModel>(StringComparer.OrdinalIgnoreCase);
                result[full] = tableCols;
            }

            if (!fromNode.Parameters.TryGetValue("ColumnName", out string? colName) || string.IsNullOrWhiteSpace(colName))
                continue;

            tableCols[colName] = fromNode;
        }

        return result;
    }

    private static Dictionary<string, NodeViewModel> BuildExistingSequenceNodeMap(IEnumerable<NodeViewModel> nodes)
    {
        var map = new Dictionary<string, NodeViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (NodeViewModel node in nodes.Where(n => n.Type == NodeType.SequenceDefinition))
        {
            string schema = node.Parameters.TryGetValue("Schema", out string? schemaName)
                ? schemaName ?? string.Empty
                : string.Empty;
            string name = node.Parameters.TryGetValue("SequenceName", out string? sequenceName)
                ? sequenceName ?? string.Empty
                : string.Empty;

            if (string.IsNullOrWhiteSpace(name))
                continue;

            map[Qualify(schema, name)] = node;
        }

        return map;
    }

    private static Point ComputeSuggestedOrigin(IEnumerable<NodeViewModel> nodes)
    {
        double maxX = nodes.Any() ? nodes.Max(n => n.Position.X) : 120;
        return new Point(maxX + 520, 80);
    }

    private void AddTableSubgraph(
        DatabaseProvider provider,
        DbMetadata metadata,
        TableMetadata table,
        Point origin,
        ICollection<NodeViewModel> nodes,
        ICollection<ConnectionViewModel> connections,
        IDictionary<string, NodeViewModel> tableNodes,
        IReadOnlyDictionary<string, NodeViewModel> sequenceNodes,
        IDictionary<string, Dictionary<string, NodeViewModel>> columnNodesByTable,
        out int uniqueIndexesAdded)
    {
        NodeViewModel tableNode = NewNode(NodeType.TableDefinition, origin.X, origin.Y);
        tableNode.Parameters["SchemaName"] = table.Schema;
        tableNode.Parameters["TableName"] = table.Name;
        tableNode.Parameters["IfNotExists"] = "true";
        tableNode.Parameters["Comment"] = table.Comment ?? string.Empty;

        NodeViewModel createOutputNode = NewNode(NodeType.CreateTableOutput, origin.X + 280, origin.Y);
        Connect(tableNode, "table", createOutputNode, "table", connections);

        nodes.Add(tableNode);
        nodes.Add(createOutputNode);
        tableNodes[table.FullName] = tableNode;

        var tableColumnNodes = new Dictionary<string, NodeViewModel>(StringComparer.OrdinalIgnoreCase);
        columnNodesByTable[table.FullName] = tableColumnNodes;

        int columnOffset = 0;
        foreach (ColumnMetadata column in table.Columns.OrderBy(c => c.OrdinalPosition))
        {
            NodeViewModel columnNode = NewNode(NodeType.ColumnDefinition, origin.X - 300, origin.Y + (columnOffset * 86));
            columnNode.Parameters["ColumnName"] = column.Name;
            columnNode.Parameters["DataType"] = NormalizeDataType(column);
            columnNode.Parameters["IsNullable"] = column.IsNullable ? "true" : "false";
            columnNode.Parameters["Comment"] = column.Comment ?? string.Empty;

            if (TryResolveSequenceDefault(metadata.Provider, column.DefaultValue, out string? sequenceFullName)
                && !string.IsNullOrWhiteSpace(sequenceFullName)
                && sequenceNodes.TryGetValue(sequenceFullName, out NodeViewModel? sequenceNode))
            {
                Connect(sequenceNode, "seq", columnNode, "sequence", connections);
            }

            if (TryBuildEnumTypeNode(provider, column, table, origin.X, origin.Y + (columnOffset * 86), out NodeViewModel? enumTypeNode, out NodeViewModel? typeOutputNode))
            {
                nodes.Add(enumTypeNode!);
                if (typeOutputNode is not null)
                {
                    nodes.Add(typeOutputNode);
                    Connect(enumTypeNode!, "type_def", typeOutputNode, "type_def", connections);
                }
                columnNode.Parameters["DataType"] = "ENUM";
                Connect(enumTypeNode!, "type_def", columnNode, "type_def", connections);
            }

            nodes.Add(columnNode);
            Connect(columnNode, "column", tableNode, "column", connections);

            tableColumnNodes[column.Name] = columnNode;
            columnOffset++;
        }

        IReadOnlyList<ColumnMetadata> pkColumns = table.Columns
            .Where(c => c.IsPrimaryKey)
            .OrderBy(c => c.OrdinalPosition)
            .ToList();

        if (pkColumns.Count > 0)
        {
            NodeViewModel pkNode = NewNode(NodeType.PrimaryKeyConstraint, origin.X - 40, origin.Y - 120);
            pkNode.Parameters["ConstraintName"] = $"PK_{table.Name}";
            nodes.Add(pkNode);

            Connect(pkNode, "pk", tableNode, "constraint", connections);
            foreach (ColumnMetadata pkCol in pkColumns)
            {
                if (tableColumnNodes.TryGetValue(pkCol.Name, out NodeViewModel? colNode))
                    Connect(colNode, "column", pkNode, "column", connections);
            }
        }

        uniqueIndexesAdded = 0;
        foreach (IndexMetadata uniqueIndex in table.Indexes.Where(i => i.IsUnique && !i.IsPrimaryKey))
        {
            NodeViewModel uqNode = NewNode(NodeType.UniqueConstraint, origin.X + 40, origin.Y - 120 - (uniqueIndexesAdded % 3 * 72));
            uqNode.Parameters["ConstraintName"] = uniqueIndex.Name;
            nodes.Add(uqNode);

            Connect(uqNode, "uq", tableNode, "constraint", connections);
            foreach (string colName in uniqueIndex.Columns)
            {
                if (tableColumnNodes.TryGetValue(colName, out NodeViewModel? colNode))
                    Connect(colNode, "column", uqNode, "column", connections);
            }

            uniqueIndexesAdded++;
        }
    }

    private static void AddViewSubgraph(
        DatabaseProvider provider,
        TableMetadata view,
        Point origin,
        ICollection<NodeViewModel> nodes,
        ICollection<ConnectionViewModel> connections,
        IDictionary<string, NodeViewModel> viewNodes)
    {
        NodeViewModel viewNode = NewNode(NodeType.ViewDefinition, origin.X, origin.Y);
        viewNode.Parameters["Schema"] = view.Schema;
        viewNode.Parameters["ViewName"] = view.Name;
        viewNode.Parameters["OrReplace"] = "false";
        viewNode.Parameters["IsMaterialized"] = view.Kind == TableKind.MaterializedView ? "true" : "false";
        string selectSql = BuildViewSeedSelectSql(provider, view);
        viewNode.Parameters["SelectSql"] = selectSql;

        NodeGraph seedGraph = BuildViewSeedSubgraph(selectSql);
        viewNode.Parameters[CanvasSerializer.ViewSubgraphParameterKey] = JsonSerializer.Serialize(seedGraph);
        viewNode.Parameters[CanvasSerializer.ViewFromTableParameterKey] = $"({selectSql}) view_src";

        NodeViewModel createViewOutputNode = NewNode(NodeType.CreateViewOutput, origin.X + 280, origin.Y);
        Connect(viewNode, "view", createViewOutputNode, "view", connections);

        nodes.Add(viewNode);
        nodes.Add(createViewOutputNode);
        viewNodes[view.FullName] = viewNode;
    }

    private static NodeGraph BuildViewSeedSubgraph(string selectSql)
    {
        const string queryNodeId = "view_query";
        const string outputNodeId = "view_output";

        return new NodeGraph
        {
            Nodes =
            [
                new NodeInstance(
                    queryNodeId,
                    NodeType.Subquery,
                    PinLiterals: new Dictionary<string, string>(),
                    Parameters: new Dictionary<string, string>
                    {
                        ["query"] = selectSql,
                        ["alias"] = "view_src",
                    }),
                new NodeInstance(
                    outputNodeId,
                    NodeType.ResultOutput,
                    PinLiterals: new Dictionary<string, string>(),
                    Parameters: new Dictionary<string, string>())
            ],
            Connections = [],
            SelectOutputs = []
        };
    }

    private static string BuildViewSeedSelectSql(DatabaseProvider provider, TableMetadata view)
    {
        IReadOnlyList<ColumnMetadata> columns = view.Columns
            .OrderBy(static column => column.OrdinalPosition)
            .ToList();

        if (columns.Count == 0)
            return "SELECT 1 AS placeholder";

        string projection = string.Join(
            ", ",
            columns.Select(column => $"NULL AS {QuoteIdentifier(provider, column.Name)}"));
        return $"SELECT {projection}";
    }

    private static string QuoteIdentifier(DatabaseProvider provider, string identifier)
    {
        string safeIdentifier = string.IsNullOrWhiteSpace(identifier)
            ? "column"
            : identifier.Trim();

        return provider switch
        {
            DatabaseProvider.SqlServer => $"[{safeIdentifier.Replace("]", "]]", StringComparison.Ordinal)}]",
            DatabaseProvider.MySql => $"`{safeIdentifier.Replace("`", "``", StringComparison.Ordinal)}`",
            _ => $"\"{safeIdentifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"",
        };
    }

    private int AddForeignKeysForChildTable(
        DbMetadata metadata,
        string childTableFullName,
        ICollection<NodeViewModel> nodes,
        ICollection<ConnectionViewModel> connections,
        IReadOnlyDictionary<string, NodeViewModel> tableNodes,
        IReadOnlyDictionary<string, Dictionary<string, NodeViewModel>> columnNodesByTable)
    {
        if (!tableNodes.TryGetValue(childTableFullName, out NodeViewModel? childTableNode))
            return 0;

        if (!columnNodesByTable.TryGetValue(childTableFullName, out Dictionary<string, NodeViewModel>? childColumns))
            return 0;

        var fkGroups = metadata.AllForeignKeys
            .Where(fk => Qualify(fk.ChildSchema, fk.ChildTable).Equals(childTableFullName, StringComparison.OrdinalIgnoreCase))
            .GroupBy(f => $"{f.ChildSchema}.{f.ChildTable}::{f.ConstraintName}", StringComparer.OrdinalIgnoreCase)
            .ToList();

        int added = 0;
        foreach (IGrouping<string, ForeignKeyRelation> group in fkGroups)
        {
            List<ForeignKeyRelation> relations = group.OrderBy(r => r.OrdinalPosition).ToList();
            if (relations.Count == 0)
                continue;

            ForeignKeyRelation first = relations[0];
            string parentTableFullName = Qualify(first.ParentSchema, first.ParentTable);
            if (!tableNodes.TryGetValue(parentTableFullName, out NodeViewModel? _))
                continue;

            if (!columnNodesByTable.TryGetValue(parentTableFullName, out Dictionary<string, NodeViewModel>? parentColumns))
                continue;

            NodeViewModel fkNode = NewNode(
                NodeType.ForeignKeyConstraint,
                childTableNode.Position.X + 120,
                childTableNode.Position.Y + 140 + (added % 3 * 72)
            );

            fkNode.Parameters["ConstraintName"] = first.ConstraintName;
            fkNode.Parameters["OnDelete"] = ToReferentialAction(first.OnDelete);
            fkNode.Parameters["OnUpdate"] = ToReferentialAction(first.OnUpdate);

            nodes.Add(fkNode);
            Connect(fkNode, "fk", childTableNode, "constraint", connections);

            foreach (ForeignKeyRelation relation in relations)
            {
                if (childColumns.TryGetValue(relation.ChildColumn, out NodeViewModel? childColumnNode))
                    Connect(childColumnNode, "column", fkNode, "child_column", connections);

                if (parentColumns.TryGetValue(relation.ParentColumn, out NodeViewModel? parentColumnNode))
                    Connect(parentColumnNode, "column", fkNode, "parent_column", connections);
            }

            added++;
        }

        return added;
    }

    private static void ApplyAutoLayout(IReadOnlyList<NodeViewModel> nodes, IReadOnlyList<ConnectionViewModel> connections, Point origin)
    {
        Dictionary<NodeViewModel, Point> layout = NodeLayoutManager.ComputeAutoLayout(
            nodes,
            connections,
            IsDdlSinkNode,
            origin
        );

        foreach (KeyValuePair<NodeViewModel, Point> item in layout)
            item.Key.Position = item.Value;
    }

    private static bool IsDdlSinkNode(NodeViewModel node) =>
        node.Type is NodeType.CreateTableOutput or NodeType.CreateTypeOutput or NodeType.CreateSequenceOutput or NodeType.AlterTableOutput or NodeType.CreateIndexOutput or NodeType.CreateViewOutput or NodeType.AlterViewOutput;

    private static NodeViewModel NewNode(NodeType type, double x, double y)
    {
        return new NodeViewModel(NodeDefinitionRegistry.Get(type), new Point(x, y));
    }

    private static void Connect(
        NodeViewModel fromNode,
        string fromPinName,
        NodeViewModel toNode,
        string toPinName,
        ICollection<ConnectionViewModel> sink)
    {
        PinViewModel? fromPin = fromNode.FindPin(fromPinName, PinDirection.Output);
        PinViewModel? toPin = toNode.FindPin(toPinName, PinDirection.Input);

        if (fromPin is null || toPin is null)
            return;

        sink.Add(new ConnectionViewModel(fromPin, default, default) { ToPin = toPin });
    }

    private static string NormalizeDataType(ColumnMetadata column)
    {
        string native = column.NativeType.Trim();
        if (string.IsNullOrWhiteSpace(native))
            native = column.DataType.Trim();

        string lower = native.ToLowerInvariant();

        if (lower.Contains("bigint"))
            return "BIGINT";

        if (lower.Contains("smallint"))
            return "SMALLINT";

        if (lower.Contains("int") || lower.Contains("serial"))
            return "INT";

        if (lower.Contains("numeric") || lower.Contains("decimal"))
        {
            if (column.Precision.HasValue && column.Scale.HasValue)
                return $"DECIMAL({column.Precision.Value},{column.Scale.Value})";

            if (column.Precision.HasValue)
                return $"DECIMAL({column.Precision.Value})";

            return "DECIMAL";
        }

        if (lower.Contains("double") || lower.Contains("float") || lower.Contains("real"))
            return "DOUBLE";

        if (lower.Contains("boolean") || lower.Contains("bool") || lower.Contains("bit"))
            return "BOOLEAN";

        if (lower.Contains("timestamp") || lower.Contains("datetime"))
            return "TIMESTAMP";

        if (lower.Contains("date"))
            return "DATE";

        if (lower.Contains("time"))
            return "TIME";

        if (lower.Contains("uuid") || lower.Contains("uniqueidentifier"))
            return "UUID";

        if (lower.Contains("json"))
            return "JSON";

        if (lower.Contains("char") || lower.Contains("text"))
        {
            if (column.MaxLength.HasValue && column.MaxLength.Value > 0 && lower.Contains("char"))
                return $"VARCHAR({column.MaxLength.Value})";

            return lower.Contains("text") ? "TEXT" : "VARCHAR";
        }

        return native.ToUpperInvariant();
    }

    private static bool TryBuildEnumTypeNode(
        DatabaseProvider provider,
        ColumnMetadata column,
        TableMetadata table,
        double anchorX,
        double anchorY,
        out NodeViewModel? enumTypeNode,
        out NodeViewModel? typeOutputNode)
    {
        enumTypeNode = null;
        typeOutputNode = null;

        if (!TryParseEnumValues(column.NativeType, out IReadOnlyList<string> values))
            return false;

        string defaultTypeName = $"{table.Name}_{column.Name}_enum".ToLowerInvariant();
        enumTypeNode = NewNode(NodeType.EnumTypeDefinition, anchorX - 520, anchorY);
        enumTypeNode.Parameters["SchemaName"] = string.IsNullOrWhiteSpace(table.Schema) ? "public" : table.Schema;
        enumTypeNode.Parameters["TypeName"] = defaultTypeName;
        enumTypeNode.Parameters["EnumValues"] = string.Join(",", values);

        if (provider == DatabaseProvider.Postgres)
            typeOutputNode = NewNode(NodeType.CreateTypeOutput, anchorX - 260, anchorY);

        return true;
    }

    private static bool TryParseEnumValues(string nativeType, out IReadOnlyList<string> values)
    {
        values = [];
        if (string.IsNullOrWhiteSpace(nativeType))
            return false;

        Match match = Regex.Match(nativeType, @"enum\s*\((?<values>.*)\)", RegexOptions.IgnoreCase);
        if (!match.Success)
            return false;

        string raw = match.Groups["values"].Value;
        string[] parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        values =
        [
            .. parts
                .Select(v => v.Trim().Trim('\'', '"'))
                .Where(v => !string.IsNullOrWhiteSpace(v)),
        ];

        return values.Count > 0;
    }

    private static string ToReferentialAction(ReferentialAction action)
    {
        return action switch
        {
            ReferentialAction.Cascade => "CASCADE",
            ReferentialAction.SetNull => "SET NULL",
            ReferentialAction.SetDefault => "SET DEFAULT",
            ReferentialAction.Restrict => "RESTRICT",
            _ => "NO ACTION",
        };
    }

    private static string L(string key, string fallback)
    {
        string value = LocalizationService.Instance[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    private static string Qualify(string schema, string table)
    {
        return string.IsNullOrWhiteSpace(schema) ? table : $"{schema}.{table}";
    }

    private static bool TryResolveSequenceDefault(DatabaseProvider provider, string? defaultExpression, out string? sequenceFullName)
    {
        sequenceFullName = null;
        if (string.IsNullOrWhiteSpace(defaultExpression))
            return false;

        if (provider == DatabaseProvider.Postgres)
        {
            Match match = Regex.Match(defaultExpression, @"nextval\('(?<name>[^']+)'", RegexOptions.IgnoreCase);
            if (!match.Success)
                return false;

            string value = match.Groups["name"].Value;
            if (value.EndsWith("::regclass", StringComparison.OrdinalIgnoreCase))
                value = value[..^10];

            value = value.Trim('"');
            sequenceFullName = value;
            return true;
        }

        if (provider == DatabaseProvider.SqlServer)
        {
            Match match = Regex.Match(defaultExpression, @"NEXT\s+VALUE\s+FOR\s+(?<name>[\[\]\w\.]+)", RegexOptions.IgnoreCase);
            if (!match.Success)
                return false;

            string raw = match.Groups["name"].Value;
            sequenceFullName = raw.Replace("[", string.Empty).Replace("]", string.Empty);
            return true;
        }

        return false;
    }
}
