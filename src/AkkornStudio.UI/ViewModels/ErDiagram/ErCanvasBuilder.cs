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
    private const double ColumnsHeaderHeight = 22;
    private const double ColumnRowHeight = 24;
    private const double HorizontalGap = 60;
    private const double VerticalGap = 40;
    private const int MinEntitiesPerRow = 4;
    private const int MaxEntitiesPerRow = 12;
    private const double ReverseLaneOffset = 48;
    private const double ForwardLaneOffset = 24;

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
        }

        RecomputeEdgeGeometry(canvas);

        return canvas;
    }

    public static void AutoLayout(ErCanvasViewModel canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        List<ErEntityNodeViewModel> ordered = canvas.Entities
            .OrderBy(static entity => entity.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ordered.Count == 0)
            return;

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

        IReadOnlyList<string> dependencies =
        [
            .. table.ReferencedTables
                .OrderBy(static t => t, StringComparer.OrdinalIgnoreCase)
                .Select(static t => $"Outbound -> {t}"),
            .. table.ReferencingTables
                .OrderBy(static t => t, StringComparer.OrdinalIgnoreCase)
                .Select(static t => $"Inbound <- {t}"),
        ];

        return new ErEntityNodeViewModel(
            schema: table.Schema,
            name: table.Name,
            isView: table.Kind != TableKind.Table,
            estimatedRowCount: table.EstimatedRowCount,
            columns: columns,
            dependencies: dependencies,
            createStatementSql: BuildCreateStatement(table));
    }

    private static string BuildEntityId(string schema, string name) =>
        string.IsNullOrWhiteSpace(schema) ? name : $"{schema}.{name}";

    private static string BuildCompositeGroupKey(ForeignKeyRelation fk) =>
        $"{fk.ConstraintName}|{fk.ChildSchema}|{fk.ChildTable}|{fk.ParentSchema}|{fk.ParentTable}";

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

            double childHeight = child.NodeHeight;
            double parentHeight = parent.NodeHeight;
            bool childIsLeftOfParent = child.X <= parent.X;
            edge.StartX = childIsLeftOfParent ? child.X + EntityWidth : child.X;
            edge.StartY = ResolveEdgeY(child, edge.ChildColumns, childHeight);
            edge.EndX = childIsLeftOfParent ? parent.X : parent.X + EntityWidth;
            edge.EndY = ResolveEdgeY(parent, edge.ParentColumns, parentHeight);
            edge.SetRoute(BuildOrthogonalRoute(edge, child, parent, index));
        }
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
        double laneOffset = (stableHash % 5) + 1;
        bool flowsLeftToRight = startX <= endX;

        if (flowsLeftToRight)
        {
            double midX = startX + ((endX - startX) / 2d) + (ForwardLaneOffset * laneOffset);
            return
            [
                new Point(startX, startY),
                new Point(midX, startY),
                new Point(midX, endY),
                new Point(endX, endY),
            ];
        }

        double corridorX = Math.Max(child.X + EntityWidth, parent.X + EntityWidth) + (ReverseLaneOffset * laneOffset);
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
