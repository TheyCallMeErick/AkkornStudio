namespace AkkornStudio.Ddl.Compare;

/// <summary>
/// A neutral, provider-agnostic snapshot of a table's rows. The comparison engine works on this
/// instead of <c>DataTable</c> so it stays pure and testable; the UI adapter converts the
/// orchestrator's preview result into this shape.
/// </summary>
public sealed record DataRowSet(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows)
{
    public static DataRowSet Empty { get; } = new([], []);
}

/// <summary>How a single row differs between source and target.</summary>
public enum RowDifferenceKind
{
    /// <summary>Present in source, missing in target — the target must gain it (INSERT).</summary>
    InsertIntoTarget,

    /// <summary>Present in target, missing in source — the target must drop it (DELETE).</summary>
    DeleteFromTarget,

    /// <summary>Present in both (same key) but with differing cell values (UPDATE).</summary>
    UpdateInTarget,

    /// <summary>Present in both with identical values.</summary>
    Unchanged,
}

/// <summary>One row-level difference. Cells are keyed by column name over the shared column set.</summary>
public sealed record RowDifference
{
    public required RowDifferenceKind Kind { get; init; }

    /// <summary>The matching key as a display string (e.g. "id=42" or composite).</summary>
    public required string KeyDisplay { get; init; }

    /// <summary>Source cells by column (null for <see cref="RowDifferenceKind.DeleteFromTarget"/>).</summary>
    public required IReadOnlyDictionary<string, object?> SourceValues { get; init; }

    /// <summary>Target cells by column (null for <see cref="RowDifferenceKind.InsertIntoTarget"/>).</summary>
    public required IReadOnlyDictionary<string, object?> TargetValues { get; init; }

    /// <summary>Columns whose values differ (only populated for <see cref="RowDifferenceKind.UpdateInTarget"/>).</summary>
    public required IReadOnlyList<string> ChangedColumns { get; init; }
}

/// <summary>Result of comparing two row sets of the same logical table.</summary>
public sealed record DataComparison(
    IReadOnlyList<string> Columns,
    IReadOnlyList<string> KeyColumns,
    IReadOnlyList<RowDifference> Differences,
    IReadOnlyList<string> Warnings)
{
    public static DataComparison Empty { get; } = new([], [], [], []);

    public int InsertCount => Differences.Count(static d => d.Kind == RowDifferenceKind.InsertIntoTarget);

    public int DeleteCount => Differences.Count(static d => d.Kind == RowDifferenceKind.DeleteFromTarget);

    public int UpdateCount => Differences.Count(static d => d.Kind == RowDifferenceKind.UpdateInTarget);

    public int UnchangedCount => Differences.Count(static d => d.Kind == RowDifferenceKind.Unchanged);

    /// <summary>True when source and target hold the same rows (only unchanged rows, no warnings aside).</summary>
    public bool IsInSync => InsertCount == 0 && DeleteCount == 0 && UpdateCount == 0;
}

/// <summary>Options that influence the generated DML (not the comparison itself).</summary>
public sealed record DataSyncOptions(bool CommentDestructive)
{
    /// <summary>Destructive DELETEs are emitted but commented out by default, for review.</summary>
    public static DataSyncOptions Default { get; } = new(CommentDestructive: true);
}
