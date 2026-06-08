using System.Data.Common;
using MySqlConnector;
using AkkornStudio.Core;

namespace AkkornStudio.Metadata.Inspectors;

/// <summary>
/// MySQL / MariaDB inspector.
/// Uses information_schema exclusively — no proprietary extensions needed.
/// Row-count estimates come from information_schema.TABLES.TABLE_ROWS,
/// which MySQL updates via ANALYZE TABLE or background stats.
/// </summary>
public sealed class MySqlInspector(ConnectionConfig config) : BaseInspector(config)
{
    public override DatabaseProvider Provider => DatabaseProvider.MySql;

    protected override async Task<DbConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new MySqlConnection(Config.BuildConnectionString());
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
        cmd.CommandText = "SELECT VERSION()";
        return (await cmd.ExecuteScalarAsync(ct))?.ToString() ?? "unknown";
    }

    // ── Tables + Views ────────────────────────────────────────────────────────

    protected override async Task<
        IReadOnlyList<(string, string, TableKind, long?, string?)>
    > FetchAllTablesAsync(DbConnection conn, CancellationToken ct)
    {
        const string sql = """
            SELECT
                TABLE_SCHEMA,
                TABLE_NAME,
                TABLE_TYPE,
                TABLE_ROWS,     -- estimated; null for views
                TABLE_COMMENT
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = @db
            ORDER BY TABLE_NAME
            """;

        await using var cmd = (MySqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@db", Config.Database);

        var result = new List<(string, string, TableKind, long?, string?)>();
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
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
                c.ORDINAL_POSITION,
                c.COLUMN_NAME,
                c.DATA_TYPE,
                c.COLUMN_TYPE,            -- includes length/precision, e.g. varchar(255)
                c.IS_NULLABLE,
                c.CHARACTER_MAXIMUM_LENGTH,
                c.NUMERIC_PRECISION,
                c.NUMERIC_SCALE,
                c.COLUMN_DEFAULT,
                c.COLUMN_KEY,             -- PRI / UNI / MUL
                c.EXTRA,                  -- auto_increment etc
                c.COLUMN_COMMENT
            FROM information_schema.COLUMNS c
            WHERE c.TABLE_SCHEMA = @schema
              AND c.TABLE_NAME   = @table
            ORDER BY c.ORDINAL_POSITION
            """;

        await using var cmd = (MySqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", table);

        // FK column list for this table (pre-fetch to mark columns)
        HashSet<string> fkColumns = await GetFkColumnNamesAsync(conn, schema, table, ct);

        var columns = new List<ColumnMetadata>();
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            string name = reader.GetString(1);
            string columnKey = reader.GetString(9);
            string nativeType = reader.GetString(2);

            columns.Add(
                new ColumnMetadata(
                    OrdinalPosition: reader.GetInt32(0),
                    Name: name,
                    DataType: NormalizeType(nativeType),
                    NativeType: reader.GetString(3), // full e.g. "varchar(255)"
                    IsNullable: reader.GetString(4) == "YES",
                    MaxLength: reader.IsDBNull(5) ? null : (int?)reader.GetInt64(5),
                    Precision: reader.IsDBNull(6) ? null : (int?)reader.GetInt64(6),
                    Scale: reader.IsDBNull(7) ? null : (int?)reader.GetInt64(7),
                    DefaultValue: reader.IsDBNull(8) ? null : reader.GetString(8),
                    IsPrimaryKey: columnKey == "PRI",
                    IsUnique: columnKey == "UNI",
                    IsIndexed: columnKey is "PRI" or "UNI" or "MUL",
                    IsForeignKey: fkColumns.Contains(name, StringComparer.OrdinalIgnoreCase),
                    Comment: reader.IsDBNull(11) ? null : reader.GetString(11)
                )
            );
        }

        return columns;
    }

    private static async Task<HashSet<string>> GetFkColumnNamesAsync(
        DbConnection conn,
        string schema,
        string table,
        CancellationToken ct
    )
    {
        const string sql = """
            SELECT COLUMN_NAME
            FROM   information_schema.KEY_COLUMN_USAGE
            WHERE  TABLE_SCHEMA            = @schema
              AND  TABLE_NAME              = @table
              AND  REFERENCED_TABLE_NAME IS NOT NULL
            """;

        await using var cmd = (MySqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", table);

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            set.Add(reader.GetString(0));

        return set;
    }

    // ── Check constraints (best-effort, MySQL 8.0.16+) ───────────────────────────

    protected override async Task<IReadOnlyList<CheckConstraintMetadata>> FetchCheckConstraintsAsync(
        DbConnection conn,
        string schema,
        string table,
        CancellationToken ct
    )
    {
        const string sql = """
            SELECT cc.CONSTRAINT_NAME, cc.CHECK_CLAUSE
            FROM information_schema.CHECK_CONSTRAINTS cc
            JOIN information_schema.TABLE_CONSTRAINTS tc
              ON tc.CONSTRAINT_SCHEMA = cc.CONSTRAINT_SCHEMA
             AND tc.CONSTRAINT_NAME   = cc.CONSTRAINT_NAME
            WHERE tc.TABLE_SCHEMA = @schema
              AND tc.TABLE_NAME   = @table
            """;

        try
        {
            await using var cmd = (MySqlCommand)conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", table);

            var checks = new List<CheckConstraintMetadata>();
            await using MySqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
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
            SELECT COLUMN_NAME, EXTRA, GENERATION_EXPRESSION, COLLATION_NAME
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = @schema
              AND TABLE_NAME   = @table
            """;

        var map = new Dictionary<string, ColumnAttributes>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var cmd = (MySqlCommand)conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", table);

            await using MySqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                string name = reader.GetString(0);
                string extra = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                string? gen = reader.IsDBNull(2) ? null : reader.GetString(2);
                string? collation = reader.IsDBNull(3) ? null : reader.GetString(3);

                bool isAutoIncrement = extra.Contains("auto_increment", StringComparison.OrdinalIgnoreCase);
                if (string.IsNullOrWhiteSpace(gen))
                    gen = null;

                map[name] = new ColumnAttributes(isAutoIncrement, gen, collation);
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
                INDEX_NAME,
                NON_UNIQUE,
                GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX) AS cols
            FROM information_schema.STATISTICS
            WHERE TABLE_SCHEMA = @schema
              AND TABLE_NAME   = @table
            GROUP BY INDEX_NAME, NON_UNIQUE
            ORDER BY INDEX_NAME
            """;

        await using var cmd = (MySqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", table);

        var indexes = new List<IndexMetadata>();
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            string idxName = reader.GetString(0);
            indexes.Add(
                new IndexMetadata(
                    Name: idxName,
                    IsUnique: reader.GetInt32(1) == 0, // NON_UNIQUE = 0 means unique
                    IsClustered: idxName == "PRIMARY",
                    IsPrimaryKey: idxName == "PRIMARY",
                    Columns: reader.GetString(2).Split(',')
                )
            );
        }

        return indexes;
    }

    // ── Foreign Keys — information_schema ─────────────────────────────────────

    protected override async Task<IReadOnlyList<ForeignKeyRelation>> FetchForeignKeysAsync(
        DbConnection conn,
        CancellationToken ct
    )
    {
        // Join KEY_COLUMN_USAGE with REFERENTIAL_CONSTRAINTS to get the
        // referential actions (ON DELETE / ON UPDATE) alongside column pairs.
        const string sql = """
            SELECT
                kcu.CONSTRAINT_NAME,
                kcu.TABLE_SCHEMA         AS child_schema,
                kcu.TABLE_NAME           AS child_table,
                kcu.COLUMN_NAME          AS child_column,
                kcu.REFERENCED_TABLE_SCHEMA  AS parent_schema,
                kcu.REFERENCED_TABLE_NAME    AS parent_table,
                kcu.REFERENCED_COLUMN_NAME   AS parent_column,
                rc.DELETE_RULE           AS on_delete,
                rc.UPDATE_RULE           AS on_update,
                kcu.ORDINAL_POSITION     AS ordinal
            FROM information_schema.KEY_COLUMN_USAGE kcu
            JOIN information_schema.REFERENTIAL_CONSTRAINTS rc
                 ON  rc.CONSTRAINT_NAME   = kcu.CONSTRAINT_NAME
                 AND rc.CONSTRAINT_SCHEMA = kcu.TABLE_SCHEMA
            WHERE kcu.TABLE_SCHEMA             = @db
              AND kcu.REFERENCED_TABLE_NAME IS NOT NULL
            ORDER BY kcu.TABLE_NAME, kcu.CONSTRAINT_NAME, kcu.ORDINAL_POSITION
            """;

        await using var cmd = (MySqlCommand)conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@db", Config.Database);

        var relations = new List<ForeignKeyRelation>();
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
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

    protected override Task<IReadOnlyList<SequenceMetadata>> FetchSequencesAsync(
        DbConnection conn,
        CancellationToken ct
    )
    {
        _ = conn;
        _ = ct;
        return Task.FromResult<IReadOnlyList<SequenceMetadata>>([]);
    }

    // ── Type normalisation ────────────────────────────────────────────────────

    private static string NormalizeType(string native) =>
        native.ToLowerInvariant() switch
        {
            "tinyint" => "tinyint",
            "smallint" => "smallint",
            "mediumint" => "integer",
            "int" => "integer",
            "bigint" => "bigint",
            "decimal" or "numeric" => "decimal",
            "float" => "float",
            "double" => "double",
            "bit" => "boolean",
            "char" => "char",
            "varchar" => "varchar",
            "tinytext" or "text" or "mediumtext" or "longtext" => "text",
            "date" => "date",
            "time" => "time",
            "datetime" => "datetime",
            "timestamp" => "timestamp",
            "year" => "year",
            "tinyblob" or "blob" or "mediumblob" or "longblob" => "binary",
            "binary" or "varbinary" => "binary",
            "json" => "json",
            "enum" => "enum",
            "set" => "set",
            _ => native.ToLowerInvariant(),
        };
}
