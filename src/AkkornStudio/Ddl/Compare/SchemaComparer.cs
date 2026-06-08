using AkkornStudio.Metadata;

namespace AkkornStudio.Ddl.Compare;

public enum TableChangeKind
{
    /// <summary>Present in source, missing in target — must be created.</summary>
    Added,

    /// <summary>Present in target, missing in source — must be dropped.</summary>
    Removed,

    /// <summary>Present in both with differing structure.</summary>
    Modified,
}

/// <summary>
/// One table's worth of a schema comparison. <see cref="Diff"/> holds the per-table
/// differences; for added/removed tables it carries a single create/drop operation.
/// </summary>
public sealed record TableComparison(
    string TargetSchema,
    string TableName,
    TableChangeKind Kind,
    TableDiff Diff);

/// <summary>Result of comparing every table in a source schema against a target schema.</summary>
public sealed record SchemaComparison(IReadOnlyList<TableComparison> Tables)
{
    public static SchemaComparison Empty { get; } = new([]);

    public int TableCount => Tables.Count;

    public bool IsEmpty => Tables.Count == 0;
}

/// <summary>
/// Pure comparison of two sets of tables (a whole schema). Reuses <see cref="TableComparer"/>
/// for tables present on both sides, and reports added/removed tables. No SQL, no UI.
/// </summary>
public sealed class SchemaComparer
{
    private readonly TableComparer _tableComparer = new();

    /// <summary>
    /// Compares all tables of one schema. Tables are matched by name and every change targets
    /// <paramref name="targetSchema"/> (source and target schemas may be named differently).
    /// </summary>
    public SchemaComparison Compare(
        IReadOnlyList<TableMetadata> sourceTables,
        IReadOnlyList<TableMetadata> targetTables,
        string targetSchema)
        => CompareCore(sourceTables, targetTables, static t => t.Name, _ => targetSchema);

    /// <summary>
    /// Compares every table in a whole database. Tables are matched by qualified name
    /// (schema.table) and each change targets the table's own schema.
    /// </summary>
    public SchemaComparison CompareDatabase(
        IReadOnlyList<TableMetadata> sourceTables,
        IReadOnlyList<TableMetadata> targetTables)
        => CompareCore(sourceTables, targetTables, static t => t.FullName, static t => t.Schema);

    private SchemaComparison CompareCore(
        IReadOnlyList<TableMetadata> sourceTables,
        IReadOnlyList<TableMetadata> targetTables,
        Func<TableMetadata, string> keySelector,
        Func<TableMetadata, string> targetSchemaSelector)
    {
        ArgumentNullException.ThrowIfNull(sourceTables);
        ArgumentNullException.ThrowIfNull(targetTables);

        Dictionary<string, TableMetadata> source = ToMap(sourceTables, keySelector);
        Dictionary<string, TableMetadata> target = ToMap(targetTables, keySelector);

        var comparisons = new List<TableComparison>();
        int sequence = 1;

        // Tables only in the target → DROP TABLE.
        foreach ((string key, TableMetadata targetTable) in target.OrderBy(static p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (source.ContainsKey(key))
                continue;

            string schema = targetSchemaSelector(targetTable);
            comparisons.Add(new TableComparison(
                schema,
                targetTable.Name,
                TableChangeKind.Removed,
                SingleOperationDiff(ref sequence, SchemaDiffChangeKind.RemoveFromTarget, targetTable.FullName, targetTable.FullName, TableComparer.NotPresent, isDestructive: true,
                    new DropTableOperation(schema, targetTable.Name))));
        }

        foreach ((string key, TableMetadata sourceTable) in source.OrderBy(static p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            string schema = targetSchemaSelector(sourceTable);
            if (!target.TryGetValue(key, out TableMetadata? targetTable))
            {
                // Only in the source → CREATE TABLE.
                comparisons.Add(new TableComparison(
                    schema,
                    sourceTable.Name,
                    TableChangeKind.Added,
                    SingleOperationDiff(ref sequence, SchemaDiffChangeKind.AddToTarget, sourceTable.FullName, TableComparer.NotPresent, sourceTable.FullName, isDestructive: false,
                        new CreateTableOperation(sourceTable))));
                continue;
            }

            TableDiff diff = _tableComparer.Compare(sourceTable, targetTable);
            if (!diff.IsEmpty)
                comparisons.Add(new TableComparison(targetSchemaSelector(targetTable), targetTable.Name, TableChangeKind.Modified, diff));
        }

        return new SchemaComparison(comparisons);
    }

    private static Dictionary<string, TableMetadata> ToMap(IReadOnlyList<TableMetadata> tables, Func<TableMetadata, string> keySelector)
    {
        var map = new Dictionary<string, TableMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (TableMetadata table in tables)
            map[keySelector(table)] = table;

        return map;
    }

    private static TableDiff SingleOperationDiff(
        ref int sequence,
        SchemaDiffChangeKind changeKind,
        string objectName,
        string sourceDescription,
        string targetDescription,
        bool isDestructive,
        ISchemaSyncOperation operation)
    {
        var difference = new SchemaDifference
        {
            Id = $"tablediff_{sequence++}",
            Category = SchemaDiffCategory.Table,
            ChangeKind = changeKind,
            ObjectName = objectName,
            SourceDescription = sourceDescription,
            TargetDescription = targetDescription,
            Severity = isDestructive ? SchemaDiffSeverity.High : SchemaDiffSeverity.Medium,
            IsDestructive = isDestructive,
            Operation = operation,
        };

        return new TableDiff([difference], []);
    }
}
