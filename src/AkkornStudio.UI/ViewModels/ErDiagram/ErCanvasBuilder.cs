using AkkornStudio.Metadata;
using Avalonia;

namespace AkkornStudio.UI.ViewModels.ErDiagram;

/// <summary>
/// Builds deterministic ER canvas state from database metadata.
/// </summary>
public static class ErCanvasBuilder
{
    private const double EntityWidth = 420;
    private const double HeaderHeight = 56;
    private const double TabsHeight = 28;
    // Border Padding="0,4" + txt-meta line ~16px = 24px (+ 1px border = ~25, use 24 for content)
    private const double ColumnsHeaderHeight = 24;
    // Row: Grid.Height=23 + Padding.Top/Bottom=2+2 + Margin.Top/Bottom=1+1 = 29px slot height
    private const double ColumnRowHeight = 29;
    private const double HorizontalGap = 60;
    private const double VerticalGap = 40;
    private const int MinEntitiesPerRow = 4;
    private const int MaxEntitiesPerRow = 12;
    private const double ReverseLaneOffset = 48;
    private const double ForwardLaneOffset = 24;
    // Center of the 14px pin column (Margin=0, so col starts at card edge, center = 7px)
    private const double PinInset = 7.0;

    public static ErCanvasViewModel Build(DbMetadata metadata, bool includeViews = false)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var canvas = new ErCanvasViewModel();
        canvas.SetIncludeViewsSilently(includeViews);

        IReadOnlyList<TableMetadata> eligibleTables = metadata.AllTables
            .Where(table => includeViews || table.Kind == TableKind.Table)
            .OrderBy(static table => table.Schema ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(static table => table.Name, StringComparer.Ordinal)
            .ToList();

        var entityById = new Dictionary<string, ErEntityNodeViewModel>(StringComparer.OrdinalIgnoreCase);
        var entities = new List<(ErEntityNodeViewModel Entity, double Height)>();
        foreach (TableMetadata table in eligibleTables)
        {
            ErEntityNodeViewModel entity = BuildEntity(table);
            double entityHeight = entity.NodeHeight;
            entities.Add((entity, entityHeight));
        }

        int columnCount = ResolveColumnCount(entities.Count);
        var columnBottom = new double[columnCount];

        for (int index = 0; index < entities.Count; index++)
        {
            int col = GetShortestColumnIndex(columnBottom);
            ErEntityNodeViewModel entity = entities[index].Entity;
            double entityHeight = entities[index].Height;

            entity.X = col * (EntityWidth + HorizontalGap);
            entity.Y = columnBottom[col];
            columnBottom[col] += entityHeight + VerticalGap;

            canvas.Entities.Add(entity);
            entityById[entity.Id] = entity;
        }

        IReadOnlyList<IGrouping<string, ForeignKeyRelation>> fkGroups = metadata.AllForeignKeys
            .OrderBy(static fk => fk.ChildSchema ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(static fk => fk.ChildTable, StringComparer.Ordinal)
            .ThenBy(static fk => fk.ParentSchema ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(static fk => fk.ParentTable, StringComparer.Ordinal)
            .ThenBy(static fk => fk.ConstraintName, StringComparer.Ordinal)
            .GroupBy(static fk => BuildCompositeGroupKey(fk), StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (IGrouping<string, ForeignKeyRelation> group in fkGroups)
        {
            ForeignKeyRelation fk = group
                .OrderBy(static item => item.OrdinalPosition)
                .ThenBy(static item => item.ChildColumn, StringComparer.Ordinal)
                .First();

            string childEntityId = BuildEntityId(fk.ChildSchema, fk.ChildTable);
            string parentEntityId = BuildEntityId(fk.ParentSchema, fk.ParentTable);
            if (!entityById.ContainsKey(childEntityId) || !entityById.ContainsKey(parentEntityId))
                continue;

            IReadOnlyList<string> childColumns = group
                .OrderBy(static item => item.OrdinalPosition)
                .Select(static item => item.ChildColumn)
                .ToList();
            IReadOnlyList<string> parentColumns = group
                .OrderBy(static item => item.OrdinalPosition)
                .Select(static item => item.ParentColumn)
                .ToList();

            canvas.Edges.Add(new ErRelationEdgeViewModel(
                constraintName: fk.ConstraintName,
                childEntityId: childEntityId,
                parentEntityId: parentEntityId,
                childColumns: childColumns,
                parentColumns: parentColumns,
                onDelete: fk.OnDelete,
                onUpdate: fk.OnUpdate));

            AppendEntityDependency(entityById, childEntityId, parentEntityId, childColumns, parentColumns, outbound: true);
            AppendEntityDependency(entityById, parentEntityId, childEntityId, parentColumns, childColumns, outbound: false);
        }

        RecomputeEdgeGeometry(canvas);

        return canvas;
    }

    public static void AutoLayout(ErCanvasViewModel canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        if (canvas.Entities.Count == 0)
            return;

        // Build adjacency for topology-aware ordering
        List<ErEntityNodeViewModel> ordered = BuildTopologicalOrder(canvas);

        int columnCount = ResolveColumnCount(ordered.Count);
        var columnBottom = new double[columnCount];
        for (int index = 0; index < ordered.Count; index++)
        {
            ErEntityNodeViewModel entity = ordered[index];
            int col = GetShortestColumnIndex(columnBottom);
            entity.X = col * (EntityWidth + HorizontalGap);
            entity.Y = columnBottom[col];
            columnBottom[col] += entity.NodeHeight + VerticalGap;
        }

        RecomputeEdgeGeometry(canvas);
    }

    private static List<ErEntityNodeViewModel> BuildTopologicalOrder(ErCanvasViewModel canvas)
    {
        // Group entities by how many inbound FK references they receive
        // Entities with 0 inbound (pure parents/root tables) go first
        var inboundCount = canvas.Entities.ToDictionary(
            entity => entity.Id,
            _ => 0,
            StringComparer.OrdinalIgnoreCase);

        foreach (ErRelationEdgeViewModel edge in canvas.Edges)
        {
            if (inboundCount.ContainsKey(edge.ParentEntityId))
                inboundCount[edge.ParentEntityId]++;
        }

        return canvas.Entities
            .OrderByDescending(entity => inboundCount.GetValueOrDefault(entity.Id, 0))
            .ThenBy(entity => entity.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ErEntityNodeViewModel BuildEntity(TableMetadata table)
    {
        IEnumerable<ErColumnRowViewModel> columns = table.Columns.Select(column =>
            new ErColumnRowViewModel(
                columnName: column.Name,
                dataType: string.IsNullOrWhiteSpace(column.DataType) ? column.NativeType : column.DataType,
                isNullable: column.IsNullable,
                isPrimaryKey: column.IsPrimaryKey,
                isForeignKey: column.IsForeignKey,
                isUnique: column.IsUnique,
                comment: column.Comment));

        return new ErEntityNodeViewModel(
            schema: table.Schema,
            name: table.Name,
            isView: table.Kind != TableKind.Table,
            estimatedRowCount: table.EstimatedRowCount,
            columns: columns,
            dependencies: [],
            createStatementSql: BuildCreateStatement(table));
    }

    private static string BuildEntityId(string schema, string name) =>
        string.IsNullOrWhiteSpace(schema) ? name : $"{schema}.{name}";

    private static string BuildCompositeGroupKey(ForeignKeyRelation fk) =>
        $"{fk.ConstraintName}|{fk.ChildSchema}|{fk.ChildTable}|{fk.ParentSchema}|{fk.ParentTable}";

    private static void AppendEntityDependency(
        IReadOnlyDictionary<string, ErEntityNodeViewModel> entityById,
        string sourceEntityId,
        string targetEntityId,
        IReadOnlyList<string> sourceColumns,
        IReadOnlyList<string> targetColumns,
        bool outbound)
    {
        if (!entityById.TryGetValue(sourceEntityId, out ErEntityNodeViewModel? sourceEntity))
            return;

        string sourceCols = sourceColumns.Count > 1
            ? $"({string.Join(", ", sourceColumns)})"
            : sourceColumns.FirstOrDefault() ?? "?";
        string targetCols = targetColumns.Count > 1
            ? $"({string.Join(", ", targetColumns)})"
            : targetColumns.FirstOrDefault() ?? "?";
        string cardinality = outbound ? "N:1" : "1:N";
        string dependency = outbound
            ? $"{cardinality} {sourceCols} -> {targetEntityId}.{targetCols}"
            : $"{cardinality} {sourceCols} <- {targetEntityId}.{targetCols}";

        if (sourceEntity.Dependencies.Any(existing => string.Equals(existing, dependency, StringComparison.OrdinalIgnoreCase)))
            return;

        sourceEntity.Dependencies.Add(dependency);
    }

    internal static void RecomputeEdgeGeometry(ErCanvasViewModel canvas)
    {
        var entityById = canvas.Entities.ToDictionary(entity => entity.Id, StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < canvas.Edges.Count; index++)
        {
            ErRelationEdgeViewModel edge = canvas.Edges[index];
            if (!entityById.TryGetValue(edge.ChildEntityId, out ErEntityNodeViewModel? child))
                continue;

            if (!entityById.TryGetValue(edge.ParentEntityId, out ErEntityNodeViewModel? parent))
                continue;

            // Self-referential FK: entity references itself
            if (ReferenceEquals(child, parent))
            {
                BuildSelfReferentialRoute(edge, child, index);
                continue;
            }

            double childHeight = child.NodeHeight;
            double parentHeight = parent.NodeHeight;
            bool childIsLeftOfParent = child.X < parent.X || (Math.Abs(child.X - parent.X) < 1 && child.Y <= parent.Y);
            edge.StartX = childIsLeftOfParent ? child.X + EntityWidth - PinInset : child.X + PinInset;
            edge.StartY = ResolveEdgeY(child, edge.ChildColumns, childHeight);
            edge.EndX = childIsLeftOfParent ? parent.X + PinInset : parent.X + EntityWidth - PinInset;
            edge.EndY = ResolveEdgeY(parent, edge.ParentColumns, parentHeight);
            edge.SetRoute(BuildOrthogonalRoute(edge, child, parent, index));
        }
    }

    private static void BuildSelfReferentialRoute(
        ErRelationEdgeViewModel edge,
        ErEntityNodeViewModel entity,
        int edgeIndex)
    {
        int stableHash = Math.Abs((edge.ConstraintName ?? $"{edge.ChildEntityId}-self").GetHashCode());
        int lane = (stableHash % 3) + 1;
        double loopExtent = EntityWidth / 2d + lane * 40d;

        double startY = ResolveEdgeY(entity, edge.ChildColumns, entity.NodeHeight);
        double endY = ResolveEdgeY(entity, edge.ParentColumns, entity.NodeHeight);

        // Exits from right side, loops right
        edge.StartX = entity.X + EntityWidth - PinInset;
        edge.StartY = startY;
        edge.EndX = entity.X + EntityWidth - PinInset;
        edge.EndY = endY;

        double loopX = entity.X + EntityWidth + loopExtent;
        edge.SetRoute([
            new Point(edge.StartX, startY),
            new Point(loopX, startY),
            new Point(loopX, endY),
            new Point(edge.EndX, endY),
        ]);
    }

    private static IReadOnlyList<Point> BuildOrthogonalRoute(
        ErRelationEdgeViewModel edge,
        ErEntityNodeViewModel child,
        ErEntityNodeViewModel parent,
        int edgeIndex)
    {
        double startX = edge.StartX;
        double startY = edge.StartY;
        double endX = edge.EndX;
        double endY = edge.EndY;
        int stableHash = Math.Abs((edge.ConstraintName ?? $"{edge.ChildEntityId}->{edge.ParentEntityId}").GetHashCode());
        int lane = (stableHash % 5) + 1;
        bool flowsLeftToRight = startX < endX;

        if (flowsLeftToRight)
        {
            // For forward routes, spread by alternating left/right of midpoint
            double gap = endX - startX;
            double midBase = startX + gap / 2d;
            // Odd lanes go right (+), even lanes go left (-) from midpoint
            double sign = (lane % 2 == 0) ? -1d : 1d;
            double midX = midBase + sign * ForwardLaneOffset * ((lane + 1) / 2);
            // Clamp midX to stay between start and end + some margin
            midX = Math.Clamp(midX, startX + 20d, endX - 20d);

            return
            [
                new Point(startX, startY),
                new Point(midX, startY),
                new Point(midX, endY),
                new Point(endX, endY),
            ];
        }

        // Reverse flow: route goes outside both nodes to the right
        double rightEdge = Math.Max(child.X + EntityWidth, parent.X + EntityWidth);
        double corridorX = rightEdge + ReverseLaneOffset * lane;
        return
        [
            new Point(startX, startY),
            new Point(corridorX, startY),
            new Point(corridorX, endY),
            new Point(endX, endY),
        ];
    }

    private static int GetShortestColumnIndex(IReadOnlyList<double> columnBottom)
    {
        int bestIndex = 0;
        double bestValue = columnBottom[0];
        for (int index = 1; index < columnBottom.Count; index++)
        {
            if (columnBottom[index] >= bestValue)
                continue;

            bestValue = columnBottom[index];
            bestIndex = index;
        }

        return bestIndex;
    }

    private static int ResolveColumnCount(int entityCount)
    {
        if (entityCount <= 0)
            return MinEntitiesPerRow;

        // Balance width/height for large schemas so initial fit does not collapse entities
        // into a visually "broken" vertical stack.
        int estimated = (int)Math.Ceiling(Math.Sqrt(entityCount));
        return Math.Clamp(estimated, MinEntitiesPerRow, MaxEntitiesPerRow);
    }

    private static string BuildCreateStatement(TableMetadata table)
    {
        string qualifiedName = string.IsNullOrWhiteSpace(table.Schema)
            ? table.Name
            : $"{table.Schema}.{table.Name}";
        string objectKeyword = table.Kind == TableKind.Table ? "TABLE" : "VIEW";

        if (table.Columns.Count == 0)
            return $"CREATE {objectKeyword} {qualifiedName};";

        if (table.Kind != TableKind.Table)
        {
            return string.Join(
                Environment.NewLine,
                $"CREATE VIEW {qualifiedName} AS",
                "SELECT",
                "  -- view definition unavailable from metadata snapshot",
                ";");
        }

        var lines = new List<string>(table.Columns.Count + 4)
        {
            $"CREATE TABLE {qualifiedName} ("
        };

        for (int index = 0; index < table.Columns.Count; index++)
        {
            ColumnMetadata column = table.Columns[index];
            string nullableSql = column.IsNullable ? "NULL" : "NOT NULL";
            string defaultSql = string.IsNullOrWhiteSpace(column.DefaultValue)
                ? string.Empty
                : $" DEFAULT {column.DefaultValue}";
            string suffix = index == table.Columns.Count - 1 ? string.Empty : ",";
            lines.Add($"  {column.Name} {column.DataType} {nullableSql}{defaultSql}{suffix}");
        }

        lines.Add(");");
        return string.Join(Environment.NewLine, lines);
    }

    private static double ResolveEdgeY(
        ErEntityNodeViewModel entity,
        IReadOnlyList<string> columns,
        double fallbackHeight)
    {
        if (columns.Count == 0)
            return entity.Y + (fallbackHeight / 2d);

        var indexes = new List<int>(columns.Count);
        foreach (string column in columns)
        {
            if (entity.TryGetVisibleColumnIndex(column, out int visibleIndex))
                indexes.Add(visibleIndex);
        }

        if (indexes.Count == 0)
            return entity.Y + (fallbackHeight / 2d);

        double averageIndex = indexes.Average();
        return entity.Y + HeaderHeight + TabsHeight + ColumnsHeaderHeight + ((averageIndex + 0.5d) * ColumnRowHeight);
    }
}
