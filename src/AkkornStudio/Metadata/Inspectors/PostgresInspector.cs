using System.Data.Common;
using Npgsql;
using AkkornStudio.Core;

namespace AkkornStudio.Metadata.Inspectors;

/// <summary>
/// PostgreSQL inspector.
///
/// Hybrid strategy:
/// - information_schema for FKs (standard, portable, includes referential actions)
/// - pg_catalog for tables, columns, indexes (richer metadata, faster queries)
/// - pg_class.reltuples for row-count estimates (updated by VACUUM/ANALYZE)
/// </summary>
public sealed class PostgresInspector(ConnectionConfig config) : BaseInspector(config)
{
    public override DatabaseProvider Provider => DatabaseProvider.Postgres;

    protected override async Task<DbConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new NpgsqlConnection(Config.BuildConnectionString());
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
        cmd.CommandText = "SELECT version()";
        return (await cmd.ExecuteScalarAsync(ct))?.ToString() ?? "unknown";
    }

    // ── Tables + Views ────────────────────────────────────────────────────────

    protected override async Task<
        IReadOnlyList<(string, string, TableKind, long?, string?)>
    > FetchAllTablesAsync(DbConnection conn, CancellationToken ct)
    {
        // pg_class.relkind: r = ordinary table, v = view, m = materialized view
        // reltuples is -1 when stats haven't been collected yet
        const string sql = """
            SELECT
                n.nspname                                                   AS schema,
                c.relname                                                   AS table_name,
                c.relkind                                                   AS kind,
                CASE WHEN c.reltuples < 0 THEN NULL
                     ELSE c.reltuples::bigint END                           AS est_rows
                 ,pg_catalog.obj_description(c.oid, 'pg_class')             AS table_comment
            FROM pg_catalog.pg_class     c
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('r', 'v', 'm')
              AND n.nspname NOT IN ('pg_catalog', 'information_schema', 'pg_toast')
              AND n.nspname NOT LIKE 'pg_temp%'
            ORDER BY n.nspname, c.relname
            """;

        await using DbCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        var result = new List<(string, string, TableKind, long?, string?)>();
        await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            TableKind kind = reader.GetChar(2) switch
            {
                'v' => TableKind.View,
                'm' => TableKind.MaterializedView,
                _ => TableKind.Table,
            };
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
                a.attnum                                                     AS ordinal,
                a.attname                                                    AS col_name,
                -- normalised type
                pg_catalog.format_type(a.atttypid, a.atttypmod)            AS data_type,
                -- base type name
                (SELECT typname FROM pg_type WHERE oid = a.atttypid)        AS native_type,
                NOT a.attnotnull                                             AS is_nullable,
                -- Max length: meaningful only for varchar; atttypmod-4 is the user-specified n
                CASE WHEN a.atttypmod > 4
                     THEN a.atttypmod - 4 ELSE NULL END                     AS max_length,
                information_schema._pg_numeric_precision(a.atttypid, a.atttypmod) AS precision,
                information_schema._pg_numeric_scale(a.atttypid, a.atttypmod)     AS scale,
                pg_catalog.pg_get_expr(ad.adbin, ad.adrelid)                AS default_val,
                -- PK?
                EXISTS (
                    SELECT 1 FROM pg_index i
                    WHERE  i.indrelid   = a.attrelid
                      AND  i.indisprimary
                      AND  a.attnum = ANY(i.indkey)
                )                                                            AS is_pk,
                -- FK?
                EXISTS (
                    SELECT 1 FROM pg_constraint c
                    WHERE  c.conrelid = a.attrelid
                      AND  c.contype  = 'f'
                      AND  a.attnum   = ANY(c.conkey)
                )                                                            AS is_fk,
                -- Unique (non-PK)?
                EXISTS (
                    SELECT 1 FROM pg_index i
                    WHERE  i.indrelid  = a.attrelid
                      AND  i.indisunique
                      AND  NOT i.indisprimary
                      AND  a.attnum    = ANY(i.indkey)
                )                                                            AS is_unique,
                -- Any index?
                EXISTS (
                    SELECT 1 FROM pg_index i
                    WHERE  i.indrelid = a.attrelid
                      AND  a.attnum   = ANY(i.indkey)
                                )                                                            AS is_indexed,
                                pg_catalog.col_description(a.attrelid, a.attnum)            AS col_comment
            FROM pg_catalog.pg_attribute  a
            JOIN pg_catalog.pg_class      cl ON cl.oid = a.attrelid
            JOIN pg_catalog.pg_namespace  n  ON n.oid  = cl.relnamespace
            LEFT JOIN pg_catalog.pg_attrdef ad ON ad.adrelid = a.attrelid
                                               AND ad.adnum   = a.attnum
            WHERE n.nspname  = @schema
              AND cl.relname = @table
              AND a.attnum   > 0
              AND NOT a.attisdropped
            ORDER BY a.attnum
            """;

        await using var cmd = (NpgsqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", table);

        var columns = new List<ColumnMetadata>();
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            columns.Add(
                new ColumnMetadata(
                    OrdinalPosition: reader.GetInt16(0),
                    Name: reader.GetString(1),
                    DataType: reader.GetString(2),
                    NativeType: reader.GetString(3),
                    IsNullable: reader.GetBoolean(4),
                    MaxLength: reader.IsDBNull(5) ? null : (int?)reader.GetInt32(5),
                    Precision: reader.IsDBNull(6) ? null : (int?)reader.GetInt32(6),
                    Scale: reader.IsDBNull(7) ? null : (int?)reader.GetInt32(7),
                    DefaultValue: reader.IsDBNull(8) ? null : reader.GetString(8),
                    IsPrimaryKey: reader.GetBoolean(9),
                    IsForeignKey: reader.GetBoolean(10),
                    IsUnique: reader.GetBoolean(11),
                    IsIndexed: reader.GetBoolean(12),
                    Comment: reader.IsDBNull(13) ? null : reader.GetString(13)
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
            SELECT con.conname, pg_get_constraintdef(con.oid)
            FROM pg_constraint con
            JOIN pg_class      rel ON rel.oid = con.conrelid
            JOIN pg_namespace  nsp ON nsp.oid = rel.relnamespace
            WHERE con.contype = 'c'
              AND nsp.nspname  = @schema
              AND rel.relname  = @table
            """;

        try
        {
            await using var cmd = (NpgsqlCommand)conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", table);

            var checks = new List<CheckConstraintMetadata>();
            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(1))
                    continue;

                checks.Add(new CheckConstraintMetadata(reader.GetString(0), StripCheckPrefix(reader.GetString(1))));
            }

            return checks;
        }
        catch
        {
            return [];
        }
    }

    // pg_get_constraintdef returns e.g. "CHECK ((amount > 0))"; keep the bare expression.
    private static string StripCheckPrefix(string definition)
    {
        string trimmed = definition.Trim();
        return trimmed.StartsWith("CHECK", StringComparison.OrdinalIgnoreCase)
            ? trimmed[5..].Trim()
            : trimmed;
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
            SELECT
                a.attname,
                (a.attidentity <> '')                                  AS is_identity,
                CASE WHEN a.attgenerated <> ''
                     THEN pg_get_expr(ad.adbin, ad.adrelid) END        AS gen_expr,
                co.collname                                            AS collation_name
            FROM pg_catalog.pg_attribute a
            JOIN pg_catalog.pg_class      cl ON cl.oid = a.attrelid
            JOIN pg_catalog.pg_namespace  n  ON n.oid  = cl.relnamespace
            LEFT JOIN pg_catalog.pg_attrdef ad ON ad.adrelid = a.attrelid AND ad.adnum = a.attnum
            LEFT JOIN pg_catalog.pg_collation co ON co.oid = a.attcollation
            WHERE n.nspname = @schema
              AND cl.relname = @table
              AND a.attnum > 0
              AND NOT a.attisdropped
            """;

        var map = new Dictionary<string, ColumnAttributes>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var cmd = (NpgsqlCommand)conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", table);

            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                string name = reader.GetString(0);
                bool isIdentity = !reader.IsDBNull(1) && reader.GetBoolean(1);
                string? gen = reader.IsDBNull(2) ? null : reader.GetString(2);
                string? collation = reader.IsDBNull(3) ? null : reader.GetString(3);
                if (string.Equals(collation, "default", StringComparison.OrdinalIgnoreCase))
                    collation = null;

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
                i.relname                                           AS idx_name,
                ix.indisunique,
                ix.indisclustered,
                ix.indisprimary,
                -- Expand attnum array to column names
                ARRAY(
                    SELECT a.attname
                    FROM   unnest(ix.indkey) WITH ORDINALITY AS u(attnum, pos)
                    JOIN   pg_attribute a ON a.attrelid = cl.oid
                                        AND a.attnum   = u.attnum
                    ORDER  BY u.pos
                )::text[]                                           AS col_names
            FROM pg_index     ix
            JOIN pg_class     cl ON cl.oid = ix.indrelid
            JOIN pg_class     i  ON i.oid  = ix.indexrelid
            JOIN pg_namespace n  ON n.oid  = cl.relnamespace
            WHERE n.nspname  = @schema
              AND cl.relname = @table
            ORDER BY ix.indisprimary DESC, i.relname
            """;

        await using var cmd = (NpgsqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", table);

        var indexes = new List<IndexMetadata>();
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            indexes.Add(
                new IndexMetadata(
                    Name: reader.GetString(0),
                    IsUnique: reader.GetBoolean(1),
                    IsClustered: reader.GetBoolean(2),
                    IsPrimaryKey: reader.GetBoolean(3),
                    Columns: reader.GetFieldValue<string[]>(4)
                )
            );
        }

        return indexes;
    }

    // ── Foreign Keys — information_schema (gives ON DELETE / ON UPDATE) ───────

    protected override async Task<IReadOnlyList<ForeignKeyRelation>> FetchForeignKeysAsync(
        DbConnection conn,
        CancellationToken ct
    )
    {
        // Using information_schema instead of pg_constraint because it directly
        // exposes DELETE_RULE and UPDATE_RULE without extra joins.
        const string sql = """
            SELECT
                tc.constraint_name,
                tc.table_schema             AS child_schema,
                tc.table_name               AS child_table,
                kcu.column_name             AS child_column,
                ccu.table_schema            AS parent_schema,
                ccu.table_name              AS parent_table,
                ccu.column_name             AS parent_column,
                rc.delete_rule              AS on_delete,
                rc.update_rule              AS on_update,
                kcu.ordinal_position        AS ordinal
            FROM information_schema.table_constraints         tc
            JOIN information_schema.key_column_usage          kcu
                 ON  kcu.constraint_name  = tc.constraint_name
                 AND kcu.constraint_schema = tc.constraint_schema
            JOIN information_schema.referential_constraints   rc
                 ON  rc.constraint_name   = tc.constraint_name
                 AND rc.constraint_schema = tc.constraint_schema
            JOIN information_schema.constraint_column_usage   ccu
                 ON  ccu.constraint_name  = rc.unique_constraint_name
                 AND ccu.constraint_schema = rc.unique_constraint_schema
            WHERE tc.constraint_type = 'FOREIGN KEY'
              AND tc.table_schema NOT IN ('pg_catalog', 'information_schema')
            ORDER BY tc.table_schema, tc.table_name, kcu.ordinal_position
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
                schemaname,
                sequencename,
                start_value,
                increment_by,
                min_value,
                max_value,
                cycle,
                cache_size
            FROM pg_sequences
            WHERE schemaname NOT IN ('pg_catalog', 'information_schema')
            ORDER BY schemaname, sequencename
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
                    Cache: reader.IsDBNull(7) ? null : reader.GetInt64(7) > int.MaxValue ? int.MaxValue : (int)reader.GetInt64(7)
                )
            );
        }

        return sequences;
    }
}
