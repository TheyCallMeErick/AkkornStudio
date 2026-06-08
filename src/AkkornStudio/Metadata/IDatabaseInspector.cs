using System.Data.Common;
using AkkornStudio.Core;

namespace AkkornStudio.Metadata;

// ─── Inspector interface ──────────────────────────────────────────────────────

/// <summary>
/// Deep database introspection contract.
/// Implementations query provider-specific system catalogs and return a
/// normalised <see cref="DbMetadata"/> object.
/// </summary>
public interface IDatabaseInspector
{
    DatabaseProvider Provider { get; }

    /// <summary>
    /// Builds the complete <see cref="DbMetadata"/> for the connected database,
    /// including tables, columns, indexes, FK graph and row-count estimates.
    /// </summary>
    Task<DbMetadata> InspectAsync(CancellationToken ct = default);

    /// <summary>
    /// Re-inspects a single table in-place (useful for live-refresh when the
    /// user right-clicks a node on the canvas).
    /// </summary>
    Task<TableMetadata> InspectTableAsync(
        string schema,
        string table,
        CancellationToken ct = default
    );

    /// <summary>
    /// Fetches only the FK graph — fast path for Auto-Join without a full reload.
    /// </summary>
    Task<IReadOnlyList<ForeignKeyRelation>> GetForeignKeysAsync(CancellationToken ct = default);
}

// ─── Abstract base with shared normalisation helpers ─────────────────────────

public abstract class BaseInspector(ConnectionConfig config) : IDatabaseInspector
{
    protected readonly ConnectionConfig Config = config;
    public abstract DatabaseProvider Provider { get; }

    // ── Template method ───────────────────────────────────────────────────────

    public async Task<DbMetadata> InspectAsync(CancellationToken ct = default)
    {
        await using DbConnection conn = await OpenAsync(ct);

        string version = await GetServerVersionAsync(conn, ct);
        IReadOnlyList<(string Schema, string Name, TableKind Kind, long? RowCount, string? Comment)> rawTables =
            await FetchAllTablesAsync(conn, ct);
        IReadOnlyList<ForeignKeyRelation> allFks = await FetchForeignKeysAsync(conn, ct);
        IReadOnlyList<SequenceMetadata> sequences = await FetchSequencesAsync(conn, ct);

        // Index FKs by table for O(1) lookup during table assembly
        ILookup<string, ForeignKeyRelation> fksByChild = allFks.ToLookup(
            r => r.ChildFullTable,
            StringComparer.OrdinalIgnoreCase
        );
        ILookup<string, ForeignKeyRelation> fksByParent = allFks.ToLookup(
            r => r.ParentFullTable,
            StringComparer.OrdinalIgnoreCase
        );

        var tableMetaList = new List<TableMetadata>();

        foreach ((string schema, string name, TableKind kind, long? rowCount, string? tableComment) in rawTables)
        {
            ct.ThrowIfCancellationRequested();

            IReadOnlyList<ColumnMetadata> columns = await FetchColumnsAsync(conn, schema, name, ct);
            columns = await ApplyColumnAttributesAsync(conn, schema, name, columns, ct);
            IReadOnlyList<IndexMetadata> indexes = await FetchIndexesAsync(conn, schema, name, ct);
            IReadOnlyList<CheckConstraintMetadata> checks = await FetchCheckConstraintsAsync(conn, schema, name, ct);

            string fullName = string.IsNullOrEmpty(schema) ? name : $"{schema}.{name}";
            tableMetaList.Add(
                new TableMetadata(
                    Schema: schema,
                    Name: name,
                    Kind: kind,
                    EstimatedRowCount: rowCount,
                    Columns: columns,
                    Indexes: indexes,
                    OutboundForeignKeys: fksByChild[fullName].ToList(),
                    InboundForeignKeys: fksByParent[fullName].ToList(),
                    Comment: tableComment
                )
                {
                    CheckConstraints = checks,
                }
            );
        }

        var schemas = tableMetaList
            .GroupBy(t => t.Schema, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key)
            .Select(g => new SchemaMetadata(g.Key, g.OrderBy(t => t.Name).ToList()))
            .ToList();

        return new DbMetadata(
            DatabaseName: Config.Database,
            Provider: Provider,
            ServerVersion: version,
            CapturedAt: DateTimeOffset.UtcNow,
            Schemas: schemas,
            AllForeignKeys: allFks,
            Sequences: sequences
        );
    }

    public async Task<TableMetadata> InspectTableAsync(
        string schema,
        string table,
        CancellationToken ct = default
    )
    {
        await using DbConnection conn = await OpenAsync(ct);
        IReadOnlyList<ColumnMetadata> columns = await FetchColumnsAsync(conn, schema, table, ct);
        columns = await ApplyColumnAttributesAsync(conn, schema, table, columns, ct);
        IReadOnlyList<IndexMetadata> indexes = await FetchIndexesAsync(conn, schema, table, ct);
        IReadOnlyList<CheckConstraintMetadata> checks = await FetchCheckConstraintsAsync(conn, schema, table, ct);
        IReadOnlyList<ForeignKeyRelation> allFks = await FetchForeignKeysAsync(conn, ct);
        string fullName = string.IsNullOrEmpty(schema) ? table : $"{schema}.{table}";

        return new TableMetadata(
            Schema: schema,
            Name: table,
            Kind: TableKind.Table,
            EstimatedRowCount: null,
            Columns: columns,
            Indexes: indexes,
            OutboundForeignKeys: allFks
                .Where(r => r.ChildFullTable.Equals(fullName, StringComparison.OrdinalIgnoreCase))
                .ToList(),
            InboundForeignKeys: allFks
                .Where(r => r.ParentFullTable.Equals(fullName, StringComparison.OrdinalIgnoreCase))
                .ToList(),
            Comment: null
        )
        {
            CheckConstraints = checks,
        };
    }

    public async Task<IReadOnlyList<ForeignKeyRelation>> GetForeignKeysAsync(
        CancellationToken ct = default
    )
    {
        await using DbConnection conn = await OpenAsync(ct);
        return await FetchForeignKeysAsync(conn, ct);
    }

    // ── Abstract hooks (each provider implements these) ───────────────────────

    protected abstract Task<DbConnection> OpenAsync(CancellationToken ct);

    protected abstract Task<string> GetServerVersionAsync(DbConnection conn, CancellationToken ct);

    /// <summary>Returns (schema, table, kind, estimatedRows) tuples.</summary>
    protected abstract Task<
        IReadOnlyList<(string Schema, string Name, TableKind Kind, long? RowCount, string? Comment)>
    > FetchAllTablesAsync(DbConnection conn, CancellationToken ct);

    protected abstract Task<IReadOnlyList<ColumnMetadata>> FetchColumnsAsync(
        DbConnection conn,
        string schema,
        string table,
        CancellationToken ct
    );

    protected abstract Task<IReadOnlyList<IndexMetadata>> FetchIndexesAsync(
        DbConnection conn,
        string schema,
        string table,
        CancellationToken ct
    );

    protected abstract Task<IReadOnlyList<ForeignKeyRelation>> FetchForeignKeysAsync(
        DbConnection conn,
        CancellationToken ct
    );

    protected abstract Task<IReadOnlyList<SequenceMetadata>> FetchSequencesAsync(
        DbConnection conn,
        CancellationToken ct
    );

    /// <summary>
    /// Fetches table-level CHECK constraints. Best-effort: providers override this and must
    /// swallow catalog errors (returning empty) so introspection never breaks if the query
    /// is unsupported on a given server version. Base returns none.
    /// </summary>
    protected virtual Task<IReadOnlyList<CheckConstraintMetadata>> FetchCheckConstraintsAsync(
        DbConnection conn,
        string schema,
        string table,
        CancellationToken ct
    ) => Task.FromResult<IReadOnlyList<CheckConstraintMetadata>>([]);

    /// <summary>
    /// Fetches extra per-column attributes (identity, generated expression, collation) keyed by
    /// column name. Best-effort: providers override and must swallow catalog errors (returning an
    /// empty map) so introspection never breaks. Base returns none.
    /// </summary>
    protected virtual Task<IReadOnlyDictionary<string, ColumnAttributes>> FetchColumnAttributesAsync(
        DbConnection conn,
        string schema,
        string table,
        CancellationToken ct
    ) => Task.FromResult<IReadOnlyDictionary<string, ColumnAttributes>>(
        new Dictionary<string, ColumnAttributes>(StringComparer.OrdinalIgnoreCase));

    private async Task<IReadOnlyList<ColumnMetadata>> ApplyColumnAttributesAsync(
        DbConnection conn,
        string schema,
        string table,
        IReadOnlyList<ColumnMetadata> columns,
        CancellationToken ct
    )
    {
        IReadOnlyDictionary<string, ColumnAttributes> attributes =
            await FetchColumnAttributesAsync(conn, schema, table, ct);
        if (attributes.Count == 0)
            return columns;

        return columns
            .Select(column => attributes.TryGetValue(column.Name, out ColumnAttributes attr)
                ? column with
                {
                    IsAutoIncrement = attr.IsAutoIncrement,
                    GeneratedExpression = attr.GeneratedExpression,
                    Collation = attr.Collation,
                }
                : column)
            .ToList();
    }

    // ── Shared normalisation ──────────────────────────────────────────────────

    protected static ReferentialAction ParseReferentialAction(string? raw) =>
        raw?.ToUpperInvariant() switch
        {
            "CASCADE" => ReferentialAction.Cascade,
            "SET NULL" => ReferentialAction.SetNull,
            "SET DEFAULT" => ReferentialAction.SetDefault,
            "RESTRICT" => ReferentialAction.Restrict,
            _ => ReferentialAction.NoAction,
        };
}
