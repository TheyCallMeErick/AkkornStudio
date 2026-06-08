using AkkornStudio.Core;

namespace AkkornStudio.Metadata;

// ═════════════════════════════════════════════════════════════════════════════
// COLUMN-LEVEL METADATA
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Extended column model, richer than the basic <see cref="ColumnSchema"/>
/// returned by the orchestrator. Includes inferred semantic type for icon hints
/// in the TreeView.
/// </summary>
public record ColumnMetadata(
    string Name,
    string DataType,
    string NativeType, // raw provider type, e.g. "nvarchar", "int4", "longtext"
    bool IsNullable,
    bool IsPrimaryKey,
    bool IsForeignKey,
    bool IsUnique,
    bool IsIndexed,
    int OrdinalPosition,
    string? DefaultValue = null,
    int? MaxLength = null,
    int? Precision = null,
    int? Scale = null,
    string? Comment = null
)
{
    /// <summary>True when the column is an identity / auto-increment column.</summary>
    public bool IsAutoIncrement { get; init; }

    /// <summary>The generation expression for a computed/generated column, or null.</summary>
    public string? GeneratedExpression { get; init; }

    /// <summary>True when the column is a generated/computed column.</summary>
    public bool IsGenerated => !string.IsNullOrWhiteSpace(GeneratedExpression);

    /// <summary>Explicit column collation, or null when it inherits the default.</summary>
    public string? Collation { get; init; }

    /// <summary>Semantic category inferred from type name — used for TreeView icons.</summary>
    public ColumnSemanticType SemanticType => InferSemanticType(NativeType);

    private static ColumnSemanticType InferSemanticType(string nativeType)
    {
        string t = nativeType.ToLowerInvariant();
        if (
            t.Contains("int")
            || t.Contains("numeric")
            || t.Contains("decimal")
            || t.Contains("float")
            || t.Contains("double")
            || t.Contains("real")
            || t.Contains("money")
            || t.Contains("number")
        )
            return ColumnSemanticType.Numeric;

        if (
            t.Contains("char")
            || t.Contains("text")
            || t.Contains("clob")
            || t.Contains("string")
            || t.Contains("nvar")
        )
            return ColumnSemanticType.Text;

        if (
            t.Contains("date")
            || t.Contains("time")
            || t.Contains("timestamp")
            || t.Contains("interval")
        )
            return ColumnSemanticType.DateTime;

        if (t.Contains("bool") || t.Contains("bit") || t.Contains("tinyint"))
            return ColumnSemanticType.Boolean;

        if (t.Contains("uuid") || t.Contains("uniqueidentifier") || t.Contains("guid"))
            return ColumnSemanticType.Guid;

        if (t.Contains("json") || t.Contains("xml") || t.Contains("jsonb"))
            return ColumnSemanticType.Document;

        if (
            t.Contains("blob")
            || t.Contains("binary")
            || t.Contains("varbinary")
            || t.Contains("bytes")
            || t.Contains("image")
        )
            return ColumnSemanticType.Binary;

        if (t.Contains("geometry") || t.Contains("geography") || t.Contains("point"))
            return ColumnSemanticType.Spatial;

        return ColumnSemanticType.Other;
    }
}

public enum ColumnSemanticType
{
    Numeric,
    Text,
    DateTime,
    Boolean,
    Guid,
    Document,
    Binary,
    Spatial,
    Other,
}

/// <summary>Best-effort extra column attributes fetched separately from the main column query.</summary>
public readonly record struct ColumnAttributes(bool IsAutoIncrement, string? GeneratedExpression, string? Collation);

// ═════════════════════════════════════════════════════════════════════════════
// FOREIGN KEY RELATION  (bidirectional, provider-agnostic)
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// A single FK constraint, normalised from whatever catalog the provider uses
/// (sys.foreign_keys for SQL Server, information_schema for MySQL/Postgres).
///
/// A composite FK (multiple columns) is stored as one relation per column pair,
/// grouped by <see cref="ConstraintName"/> + <see cref="OrdinalPosition"/>.
/// </summary>
public record ForeignKeyRelation(
    string ConstraintName,
    // Child side (the table that owns the FK column)
    string ChildSchema,
    string ChildTable,
    string ChildColumn,
    // Parent side (the referenced / primary-key table)
    string ParentSchema,
    string ParentTable,
    string ParentColumn,
    // Referential actions
    ReferentialAction OnDelete,
    ReferentialAction OnUpdate,
    int OrdinalPosition = 1
)
{
    public string ChildFullTable => Qualify(ChildSchema, ChildTable);
    public string ParentFullTable => Qualify(ParentSchema, ParentTable);

    private static string Qualify(string schema, string table) =>
        string.IsNullOrEmpty(schema) ? table : $"{schema}.{table}";

    /// <summary>
    /// True when this relation connects <paramref name="tableA"/> and <paramref name="tableB"/>
    /// in either direction.
    /// </summary>
    public bool Involves(string tableA, string tableB) =>
        Involves(tableA, tableB, StringComparison.OrdinalIgnoreCase);

    public bool Involves(string tableA, string tableB, StringComparison comparison) =>
        (
            ChildFullTable.Equals(tableA, comparison)
            && ParentFullTable.Equals(tableB, comparison)
        )
        || (
            ChildFullTable.Equals(tableB, comparison)
            && ParentFullTable.Equals(tableA, comparison)
        );

    /// <summary>
    /// Returns the JOIN ON clause from the perspective of the child table.
    /// e.g.: "orders.customer_id = customers.id"
    /// </summary>
    public string ToJoinOnClause() =>
        $"{ChildFullTable}.{ChildColumn} = {ParentFullTable}.{ParentColumn}";
}

public enum ReferentialAction
{
    NoAction,
    Cascade,
    SetNull,
    SetDefault,
    Restrict,
}

// ═════════════════════════════════════════════════════════════════════════════
// INDEX METADATA
// ═════════════════════════════════════════════════════════════════════════════

public record IndexMetadata(
    string Name,
    bool IsUnique,
    bool IsClustered,
    bool IsPrimaryKey,
    IReadOnlyList<string> Columns
);

// ═════════════════════════════════════════════════════════════════════════════
// CHECK CONSTRAINT METADATA
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>A table-level CHECK constraint (name + boolean expression).</summary>
public record CheckConstraintMetadata(string Name, string Expression);

// ═════════════════════════════════════════════════════════════════════════════
// TABLE METADATA
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Full table descriptor used by the TreeView and the auto-join engine.
/// Holds columns, indexes and pre-resolved FK links in both directions.
/// </summary>
public record TableMetadata(
    string Schema,
    string Name,
    TableKind Kind,
    long? EstimatedRowCount,
    IReadOnlyList<ColumnMetadata> Columns,
    IReadOnlyList<IndexMetadata> Indexes,
    /// <summary>FK constraints where THIS table is the child (owns the FK column).</summary>
    IReadOnlyList<ForeignKeyRelation> OutboundForeignKeys,
    /// <summary>FK constraints where THIS table is the parent (is referenced).</summary>
    IReadOnlyList<ForeignKeyRelation> InboundForeignKeys,
    string? Comment = null
)
{
    /// <summary>Table-level CHECK constraints. Empty when the provider has none or doesn't report them.</summary>
    public IReadOnlyList<CheckConstraintMetadata> CheckConstraints { get; init; } = [];

    public string FullName => string.IsNullOrEmpty(Schema) ? Name : $"{Schema}.{Name}";

    public IReadOnlyList<ColumnMetadata> PrimaryKeyColumns =>
        Columns.Where(c => c.IsPrimaryKey).ToList();

    public IReadOnlyList<ColumnMetadata> ForeignKeyColumns =>
        Columns.Where(c => c.IsForeignKey).ToList();

    /// <summary>All tables this table directly references (parents).</summary>
    public IEnumerable<string> ReferencedTables =>
        OutboundForeignKeys.Select(r => r.ParentFullTable).Distinct();

    /// <summary>All tables that reference this table (children).</summary>
    public IEnumerable<string> ReferencingTables =>
        InboundForeignKeys.Select(r => r.ChildFullTable).Distinct();
}

public enum TableKind
{
    Table,
    View,
    MaterializedView,
}

// ═════════════════════════════════════════════════════════════════════════════
// SEQUENCE METADATA
// ═════════════════════════════════════════════════════════════════════════════

public record SequenceMetadata(
    string Schema,
    string Name,
    long? StartValue,
    long? Increment,
    long? MinValue,
    long? MaxValue,
    bool? Cycle,
    int? Cache
)
{
    public string FullName => string.IsNullOrEmpty(Schema) ? Name : $"{Schema}.{Name}";
}

// ═════════════════════════════════════════════════════════════════════════════
// SCHEMA GROUP
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Groups tables under a schema name (TreeView first level).</summary>
public record SchemaMetadata(string Name, IReadOnlyList<TableMetadata> Tables)
{
    public int TableCount => Tables.Count(t => t.Kind == TableKind.Table);
    public int ViewCount => Tables.Count(t => t.Kind != TableKind.Table);
}

// ═════════════════════════════════════════════════════════════════════════════
// DB METADATA  (root aggregate — TreeView data-source)
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Provider-agnostic snapshot of an entire database, ready to bind to the
/// Avalonia TreeView.  Rebuilt on demand via <see cref="MetadataService"/>;
/// immutable after construction.
/// </summary>
public record DbMetadata(
    string DatabaseName,
    DatabaseProvider Provider,
    string ServerVersion,
    DateTimeOffset CapturedAt,
    IReadOnlyList<SchemaMetadata> Schemas,
    IReadOnlyList<ForeignKeyRelation> AllForeignKeys,
    IReadOnlyList<SequenceMetadata>? Sequences = null
)
{
    // ── Flat accessors (useful for the canvas and auto-join engine) ────────────

    private StringComparison IdentifierComparison =>
        Provider == DatabaseProvider.Postgres
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

    private StringComparer IdentifierComparer =>
        Provider == DatabaseProvider.Postgres
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;

    public IEnumerable<TableMetadata> AllTables => Schemas.SelectMany(s => s.Tables);

    public TableMetadata? FindTable(string fullName) =>
        AllTables.FirstOrDefault(t => t.FullName.Equals(fullName, IdentifierComparison));

    public IEnumerable<SequenceMetadata> AllSequences => Sequences ?? [];

    public TableMetadata? FindTable(string schema, string name) =>
        AllTables.FirstOrDefault(t =>
            t.Schema.Equals(schema, IdentifierComparison)
            && t.Name.Equals(name, IdentifierComparison)
        );

    /// <summary>
    /// Returns every FK relation that directly connects <paramref name="tableA"/>
    /// and <paramref name="tableB"/> in either direction.
    /// </summary>
    public IReadOnlyList<ForeignKeyRelation> GetRelationsBetween(string tableA, string tableB) =>
        AllForeignKeys.Where(r => r.Involves(tableA, tableB, IdentifierComparison)).ToList();

    /// <summary>
    /// Returns all FK relations that connect <paramref name="table"/> to any of
    /// the <paramref name="canvasTables"/> already on the canvas.
    /// </summary>
    public IReadOnlyList<ForeignKeyRelation> GetRelationsToCanvas(
        string table,
        IEnumerable<string> canvasTables
    )
    {
        var set = new HashSet<string>(canvasTables, IdentifierComparer);

        return AllForeignKeys
            .Where(r =>
                (
                    r.ChildFullTable.Equals(table, IdentifierComparison)
                    && set.Contains(r.ParentFullTable)
                )
                || (
                    r.ParentFullTable.Equals(table, IdentifierComparison)
                    && set.Contains(r.ChildFullTable)
                )
            )
            .ToList();
    }

    // ── Stats (status bar) ────────────────────────────────────────────────────

    public int TotalTables => AllTables.Count(t => t.Kind == TableKind.Table);
    public int TotalViews => AllTables.Count(t => t.Kind != TableKind.Table);
    public int TotalForeignKeys => AllForeignKeys.Count;
}
