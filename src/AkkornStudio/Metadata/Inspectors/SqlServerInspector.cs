using System.Data.Common;
using Microsoft.Data.SqlClient;
using AkkornStudio.Core;

namespace AkkornStudio.Metadata.Inspectors;

/// <summary>
/// SQL Server inspector.
/// Uses sys.* catalog views — richer and faster than INFORMATION_SCHEMA on SQL Server.
///
/// Foreign-key mapping uses sys.foreign_keys + sys.foreign_key_columns, which
/// gives composite-key ordinal positions and referential actions in one pass.
/// </summary>
public sealed class SqlServerInspector(ConnectionConfig config) : BaseInspector(config)
{
    public override DatabaseProvider Provider => DatabaseProvider.SqlServer;

    protected override async Task<DbConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new SqlConnection(Config.BuildConnectionString());
        await conn.OpenAsync(ct);
        return conn;
    }

    // ── Server version ────────────────────────────────────────────────────────

    protected override async Task<string> GetServerVersionAsync(
        DbConnection conn,
        CancellationToken ct
    )
    {
        await using DbCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT @@VERSION";
        string raw = (await cmd.ExecuteScalarAsync(ct))?.ToString() ?? string.Empty;
        // @@VERSION is a multi-line string; return just the first line
        return raw.Split('\n', 2)[0].Trim();
    }

    // ── Tables + Views ────────────────────────────────────────────────────────

    protected override async Task<
        IReadOnlyList<(string, string, TableKind, long?, string?)>
    > FetchAllTablesAsync(DbConnection conn, CancellationToken ct)
    {
        // sys.dm_db_partition_stats gives fast row-count estimates without locking
        const string sql = """
            SELECT
                s.name                                        AS [schema],
                o.name                                        AS [table],
                o.type_desc                                   AS [type],
                                SUM(p.row_count)                              AS [est_rows],
                                table_desc.comment                            AS [table_comment]
            FROM sys.objects o
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            LEFT JOIN sys.dm_db_partition_stats p
                  ON p.object_id = o.object_id
                 AND p.index_id  IN (0, 1)   -- heap or clustered
                        OUTER APPLY (
                                SELECT TOP(1) CAST(ep.value AS nvarchar(max)) AS comment
                                FROM sys.extended_properties ep
                                WHERE ep.major_id = o.object_id
                                    AND ep.minor_id = 0
                                    AND ep.name = N'MS_Description'
                        ) AS table_desc
            WHERE o.type IN ('U', 'V')        -- U = user table, V = view
              AND o.is_ms_shipped = 0
                        GROUP BY s.name, o.name, o.type_desc, table_desc.comment
            ORDER BY s.name, o.name
            """;

        await using DbCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        var result = new List<(string, string, TableKind, long?, string?)>();
        await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            TableKind kind = reader.GetString(2) == "VIEW" ? TableKind.View : TableKind.Table;
            long? rows = reader.IsDBNull(3) ? (long?)null : reader.GetInt64(3);
            string? comment = reader.IsDBNull(4) ? null : reader.GetString(4);
            result.Add((reader.GetString(0), reader.GetString(1), kind, rows, comment));
        }

        return result;
    }

    // ── Columns ───────────────────────────────────────────────────────────────

    protected override async Task<IReadOnlyList<ColumnMetadata>> FetchColumnsAsync(
        DbConnection conn,
        string schema,
        string table,
        CancellationToken ct
    )
    {
        const string sql = """
            SELECT
                c.column_id                                     AS ordinal,
                c.name                                          AS col_name,
                tp.name                                         AS native_type,
                c.is_nullable,
                c.max_length,
                c.precision,
                c.scale,
                dc.definition                                   AS default_val,
                -- PK?
                CASE WHEN pk.column_id IS NOT NULL THEN 1 ELSE 0 END AS is_pk,
                -- FK?
                CASE WHEN fk.parent_column_id IS NOT NULL THEN 1 ELSE 0 END AS is_fk,
                -- Unique (non-PK unique index)?
                CASE WHEN uq.column_id IS NOT NULL THEN 1 ELSE 0 END AS is_unique,
                -- Any index?
                CASE WHEN ix.column_id IS NOT NULL THEN 1 ELSE 0 END AS is_indexed
                ,CAST(col_desc.value AS nvarchar(max))               AS col_comment
            FROM sys.columns c
            JOIN sys.objects  o  ON o.object_id  = c.object_id
            JOIN sys.schemas  s  ON s.schema_id  = o.schema_id
            JOIN sys.types    tp ON tp.user_type_id = c.user_type_id
            LEFT JOIN sys.default_constraints dc ON dc.parent_object_id = c.object_id
                                                 AND dc.parent_column_id = c.column_id
            LEFT JOIN sys.extended_properties col_desc ON col_desc.major_id = c.object_id
                                                      AND col_desc.minor_id = c.column_id
                                                      AND col_desc.name = N'MS_Description'
            -- PK detection
            LEFT JOIN (
                SELECT ic.object_id, ic.column_id
                FROM sys.index_columns ic
                JOIN sys.indexes       i  ON i.object_id = ic.object_id
                                          AND i.index_id = ic.index_id
                WHERE i.is_primary_key = 1
            ) pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
            -- FK detection
            LEFT JOIN (
                SELECT DISTINCT fkc.parent_object_id, fkc.parent_column_id
                FROM sys.foreign_key_columns fkc
            ) fk ON fk.parent_object_id = c.object_id AND fk.parent_column_id = c.column_id
            -- Unique index detection
            LEFT JOIN (
                SELECT DISTINCT ic.object_id, ic.column_id
                FROM sys.index_columns ic
                JOIN sys.indexes       i  ON i.object_id = ic.object_id
                                          AND i.index_id = ic.index_id
                WHERE i.is_unique = 1 AND i.is_primary_key = 0
            ) uq ON uq.object_id = c.object_id AND uq.column_id = c.column_id
            -- Any index detection
            LEFT JOIN (
                SELECT DISTINCT ic.object_id, ic.column_id
                FROM sys.index_columns ic
            ) ix ON ix.object_id = c.object_id AND ix.column_id = c.column_id
            WHERE s.name = @schema
              AND o.name = @table
              AND o.type IN ('U', 'V')
            ORDER BY c.column_id
            """;

        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", table);

        var columns = new List<ColumnMetadata>();
        await using SqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            string nativeType = reader.GetString(2);
            // SQL Server stores max_length in bytes; for nvarchar divide by 2
            short rawMaxLen = reader.GetInt16(4);
            int? maxLen = rawMaxLen == -1 ? null : (int?)rawMaxLen; // -1 = MAX

            columns.Add(
                new ColumnMetadata(
                    Name: reader.GetString(1),
                    DataType: NormalizeType(nativeType),
                    NativeType: nativeType,
                    IsNullable: reader.GetBoolean(3),
                    MaxLength: maxLen,
                    Precision: (int)reader.GetByte(5),
                    Scale: (int)reader.GetByte(6),
                    DefaultValue: reader.IsDBNull(7) ? null : reader.GetString(7),
                    IsPrimaryKey: reader.GetInt32(8) == 1,
                    IsForeignKey: reader.GetInt32(9) == 1,
                    IsUnique: reader.GetInt32(10) == 1,
                    IsIndexed: reader.GetInt32(11) == 1,
                    OrdinalPosition: reader.GetInt32(0),
                    Comment: reader.IsDBNull(12) ? null : reader.GetString(12)
                )
            );
        }

        return columns;
    }

    // ── Check constraints (best-effort) ─────────────────────────────────────────

    protected override async Task<IReadOnlyList<CheckConstraintMetadata>> FetchCheckConstraintsAsync(
        DbConnection conn,
        string schema,
        string table,
        CancellationToken ct
    )
    {
        const string sql = """
            SELECT cc.name, cc.definition
            FROM sys.check_constraints cc
            JOIN sys.objects o ON o.object_id = cc.parent_object_id
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE s.name = @schema
              AND o.name = @table
            """;

        try
        {
            await using var cmd = (SqlCommand)conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", table);

            var checks = new List<CheckConstraintMetadata>();
            await using SqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(1))
                    continue;

                checks.Add(new CheckConstraintMetadata(reader.GetString(0), reader.GetString(1).Trim()));
            }

            return checks;
        }
        catch
        {
            return [];
        }
    }

    // ── Column attributes (best-effort) ──────────────────────────────────────────

    protected override async Task<IReadOnlyDictionary<string, ColumnAttributes>> FetchColumnAttributesAsync(
        DbConnection conn,
        string schema,
        string table,
        CancellationToken ct
    )
    {
        const string sql = """
            SELECT c.name, c.is_identity, cc.definition, c.collation_name
            FROM sys.columns c
            JOIN sys.objects o ON o.object_id = c.object_id
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
            WHERE s.name = @schema
              AND o.name = @table
            """;

        var map = new Dictionary<string, ColumnAttributes>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var cmd = (SqlCommand)conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", table);

            await using SqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                string name = reader.GetString(0);
                bool isIdentity = !reader.IsDBNull(1) && reader.GetBoolean(1);
                string? gen = reader.IsDBNull(2) ? null : reader.GetString(2);
                string? collation = reader.IsDBNull(3) ? null : reader.GetString(3);

                map[name] = new ColumnAttributes(isIdentity, gen, collation);
            }
        }
        catch
        {
            return map;
        }

        return map;
    }

    // ── Indexes ───────────────────────────────────────────────────────────────

    protected override async Task<IReadOnlyList<IndexMetadata>> FetchIndexesAsync(
        DbConnection conn,
        string schema,
        string table,
        CancellationToken ct
    )
    {
        const string sql = """
            SELECT
                i.name                  AS idx_name,
                i.is_unique,
                i.type_desc,            -- CLUSTERED / NONCLUSTERED / HEAP
                i.is_primary_key,
                STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal) AS cols
            FROM sys.indexes      i
            JOIN sys.objects      o   ON o.object_id = i.object_id
            JOIN sys.schemas      s   ON s.schema_id = o.schema_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id
                                     AND ic.index_id  = i.index_id
                                     AND ic.is_included_column = 0
            JOIN sys.columns      c   ON c.object_id  = i.object_id
                                     AND c.column_id  = ic.column_id
            WHERE s.name = @schema
              AND o.name = @table
              AND i.type > 0           -- exclude heap pseudo-index
            GROUP BY i.name, i.is_unique, i.type_desc, i.is_primary_key
            ORDER BY i.is_primary_key DESC, i.name
            """;

        await using var cmd = (SqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", table);

        var indexes = new List<IndexMetadata>();
        await using SqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            indexes.Add(
                new IndexMetadata(
                    Name: reader.GetString(0),
                    IsUnique: reader.GetBoolean(1),
                    IsClustered: reader.GetString(2) == "CLUSTERED",
                    IsPrimaryKey: reader.GetBoolean(3),
                    Columns: reader.GetString(4).Split(',')
                )
            );
        }

        return indexes;
    }

    // ── Foreign Keys — sys.foreign_keys ──────────────────────────────────────

    protected override async Task<IReadOnlyList<ForeignKeyRelation>> FetchForeignKeysAsync(
        DbConnection conn,
        CancellationToken ct
    )
    {
        // This is the canonical SQL Server approach: sys.foreign_keys gives
        // constraint metadata, sys.foreign_key_columns gives column pairs,
        // and the schema/name are resolved via sys.objects + sys.schemas.
        const string sql = """
            SELECT
                fk.name                                           AS constraint_name,
                sp.name                                           AS child_schema,
                tp.name                                           AS child_table,
                cp.name                                           AS child_column,
                sr.name                                           AS parent_schema,
                tr.name                                           AS parent_table,
                cr.name                                           AS parent_column,
                fk.delete_referential_action_desc                 AS on_delete,
                fk.update_referential_action_desc                 AS on_update,
                fkc.constraint_column_id                          AS ordinal
            FROM sys.foreign_keys            fk
            JOIN sys.foreign_key_columns     fkc  ON fkc.constraint_object_id = fk.object_id
            -- Child side
            JOIN sys.tables   tp  ON tp.object_id = fk.parent_object_id
            JOIN sys.schemas  sp  ON sp.schema_id = tp.schema_id
            JOIN sys.columns  cp  ON cp.object_id = fk.parent_object_id
                                  AND cp.column_id = fkc.parent_column_id
            -- Parent (referenced) side
            JOIN sys.tables   tr  ON tr.object_id = fk.referenced_object_id
            JOIN sys.schemas  sr  ON sr.schema_id = tr.schema_id
            JOIN sys.columns  cr  ON cr.object_id = fk.referenced_object_id
                                  AND cr.column_id = fkc.referenced_column_id
            ORDER BY sp.name, tp.name, fk.name, fkc.constraint_column_id
            """;

        await using DbCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        var relations = new List<ForeignKeyRelation>();
        await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            relations.Add(
                new ForeignKeyRelation(
                    ConstraintName: reader.GetString(0),
                    ChildSchema: reader.GetString(1),
                    ChildTable: reader.GetString(2),
                    ChildColumn: reader.GetString(3),
                    ParentSchema: reader.GetString(4),
                    ParentTable: reader.GetString(5),
                    ParentColumn: reader.GetString(6),
                    OnDelete: ParseReferentialAction(reader.GetString(7)),
                    OnUpdate: ParseReferentialAction(reader.GetString(8)),
                    OrdinalPosition: reader.GetInt32(9)
                )
            );
        }

        return relations;
    }

    protected override async Task<IReadOnlyList<SequenceMetadata>> FetchSequencesAsync(
        DbConnection conn,
        CancellationToken ct
    )
    {
        const string sql = """
            SELECT
                s.name AS schema_name,
                seq.name AS sequence_name,
                CAST(seq.start_value AS bigint) AS start_value,
                CAST(seq.increment AS bigint) AS increment_value,
                CAST(seq.minimum_value AS bigint) AS min_value,
                CAST(seq.maximum_value AS bigint) AS max_value,
                CAST(seq.is_cycling AS bit) AS is_cycling,
                CAST(seq.cache_size AS int) AS cache_size
            FROM sys.sequences seq
            JOIN sys.schemas s ON s.schema_id = seq.schema_id
            ORDER BY s.name, seq.name
            """;

        await using DbCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        var sequences = new List<SequenceMetadata>();
        await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            sequences.Add(
                new SequenceMetadata(
                    Schema: reader.GetString(0),
                    Name: reader.GetString(1),
                    StartValue: reader.IsDBNull(2) ? null : reader.GetInt64(2),
                    Increment: reader.IsDBNull(3) ? null : reader.GetInt64(3),
                    MinValue: reader.IsDBNull(4) ? null : reader.GetInt64(4),
                    MaxValue: reader.IsDBNull(5) ? null : reader.GetInt64(5),
                    Cycle: reader.IsDBNull(6) ? null : reader.GetBoolean(6),
                    Cache: reader.IsDBNull(7) ? null : reader.GetInt32(7)
                )
            );
        }

        return sequences;
    }

    // ── Type normalisation ────────────────────────────────────────────────────

    private static string NormalizeType(string native) =>
        native.ToLowerInvariant() switch
        {
            "int" => "integer",
            "bigint" => "bigint",
            "smallint" => "smallint",
            "tinyint" => "tinyint",
            "bit" => "boolean",
            "decimal" or "numeric" => "decimal",
            "float" or "real" => "float",
            "money" or "smallmoney" => "money",
            "char" or "nchar" => "char",
            "varchar" or "nvarchar" => "varchar",
            "text" or "ntext" => "text",
            "uniqueidentifier" => "uuid",
            "datetime" or "datetime2" or "smalldatetime" => "datetime",
            "date" => "date",
            "time" => "time",
            "datetimeoffset" => "timestamptz",
            "varbinary" or "binary" or "image" => "binary",
            "xml" => "xml",
            _ => native.ToLowerInvariant(),
        };
}
