using AkkornStudio.Metadata;

namespace AkkornStudio.Ddl.Compare;

/// <summary>How risky a single schema difference is to apply.</summary>
public enum SchemaDiffSeverity
{
    Info,
    Low,
    Medium,
    High,
}

/// <summary>Logical grouping of a difference, used for filtering and content toggles.</summary>
public enum SchemaDiffCategory
{
    Table,
    Column,
    PrimaryKey,
    Unique,
    Index,
    Check,
    ForeignKey,
    ExternalDependency,
}

/// <summary>Direction of the change relative to the target (the side that gets altered).</summary>
public enum SchemaDiffChangeKind
{
    /// <summary>Present in source, missing in target — the target must gain it.</summary>
    AddToTarget,

    /// <summary>Present in target, missing in source — the target must drop it.</summary>
    RemoveFromTarget,

    /// <summary>Present in both but different — the target must be altered.</summary>
    AlterInTarget,
}

/// <summary>
/// A single, structured schema difference. Carries enough typed data
/// (<see cref="Operation"/>) for SQL to be generated lazily by <see cref="SyncScriptGenerator"/>,
/// independent of any display strings.
/// </summary>
public sealed record SchemaDifference
{
    public required string Id { get; init; }
    public required SchemaDiffCategory Category { get; init; }
    public required SchemaDiffChangeKind ChangeKind { get; init; }

    /// <summary>Name of the affected object (column, constraint, index or FK).</summary>
    public required string ObjectName { get; init; }

    public required string SourceDescription { get; init; }
    public required string TargetDescription { get; init; }
    public required SchemaDiffSeverity Severity { get; init; }
    public required bool IsDestructive { get; init; }

    /// <summary>Structured operation describing how to converge the target to the source.</summary>
    public required ISchemaSyncOperation Operation { get; init; }
}

/// <summary>Result of comparing a source table against a target table.</summary>
public sealed record TableDiff(
    IReadOnlyList<SchemaDifference> Differences,
    IReadOnlyList<string> Warnings)
{
    public static TableDiff Empty { get; } = new([], []);

    public bool IsEmpty => Differences.Count == 0;
}

/// <summary>Options that influence the generated DDL (not the comparison itself).</summary>
public sealed record SchemaSyncOptions(bool ExistenceSafe)
{
    /// <summary>Existence guards on by default (IF EXISTS / IF NOT EXISTS where supported).</summary>
    public static SchemaSyncOptions Default { get; } = new(ExistenceSafe: true);
}

// ─────────────────────────────────────────────────────────────────────────────
// Structured sync operations
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Marker for a structured operation that converges the target to the source.</summary>
public interface ISchemaSyncOperation;

public sealed record AddColumnOperation(ColumnMetadata Column, string ResolvedType) : ISchemaSyncOperation;

public sealed record DropColumnOperation(string ColumnName) : ISchemaSyncOperation;

public sealed record AlterColumnOperation(string ColumnName, string DataType, bool IsNullable) : ISchemaSyncOperation;

public sealed record SetColumnDefaultOperation(string ColumnName, string? DefaultValue) : ISchemaSyncOperation;

public sealed record SetColumnCommentOperation(string ColumnName, string? Comment) : ISchemaSyncOperation;

/// <summary>
/// A column attribute (identity, generated expression, collation) that differs but cannot be
/// safely auto-altered in most engines (usually needs a table rebuild). Surfaced informationally.
/// </summary>
public sealed record ColumnAttributeNote(string ColumnName, string Attribute) : ISchemaSyncOperation;

public sealed record RecreatePrimaryKeyOperation(
    bool DropExisting,
    string? DropConstraintName,
    string? AddConstraintName,
    IReadOnlyList<string> Columns) : ISchemaSyncOperation;

public sealed record AddUniqueOperation(string? Name, IReadOnlyList<string> Columns) : ISchemaSyncOperation;

public sealed record DropUniqueOperation(string Name) : ISchemaSyncOperation;

public sealed record CreateIndexOperation(string Name, IReadOnlyList<string> Columns) : ISchemaSyncOperation;

public sealed record DropIndexOperation(string Name) : ISchemaSyncOperation;

public sealed record AddCheckOperation(string Name, string Expression) : ISchemaSyncOperation;

public sealed record DropCheckOperation(string Name) : ISchemaSyncOperation;

public sealed record AddForeignKeyOperation(CompositeForeignKey ForeignKey) : ISchemaSyncOperation;

public sealed record DropForeignKeyOperation(string ConstraintName) : ISchemaSyncOperation;

/// <summary>Create a whole table that exists in the source but not the target.</summary>
public sealed record CreateTableOperation(Metadata.TableMetadata Table) : ISchemaSyncOperation;

/// <summary>Drop a whole table that exists in the target but not the source.</summary>
public sealed record DropTableOperation(string Schema, string Table) : ISchemaSyncOperation;

/// <summary>A difference that has no executable SQL (e.g. external impact, informational).</summary>
public sealed record InformationalOperation : ISchemaSyncOperation;

/// <summary>
/// A foreign key regrouped from the per-column-pair <see cref="ForeignKeyRelation"/> rows
/// (which the metadata layer stores one-per-column) into a single multi-column constraint.
/// </summary>
public sealed record CompositeForeignKey(
    string ConstraintName,
    IReadOnlyList<string> ChildColumns,
    string ParentSchema,
    string ParentTable,
    IReadOnlyList<string> ParentColumns,
    ReferentialAction OnDelete,
    ReferentialAction OnUpdate);
