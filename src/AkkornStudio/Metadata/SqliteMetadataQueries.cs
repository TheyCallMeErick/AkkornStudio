using System.Data;
using AkkornStudio.Core;

namespace AkkornStudio.Metadata;

/// <summary>
/// SQLite metadata provider using sqlite_master system table and PRAGMA statements.
/// Compatible with SQLite 3.0+.
/// </summary>
public sealed class SqliteMetadataQueries : IMetadataQueryProvider
{
    public string GetTablesQuery() => @"
        SELECT 'main' as TABLE_SCHEMA, name as TABLE_NAME
        FROM sqlite_master
        WHERE type='table'
          AND name NOT LIKE 'sqlite_%'
        ORDER BY name
    ";

    public string GetColumnsQuery() => @"
        -- PRAGMA table_info returns: cid, name, type, notnull, dflt_value, pk
        SELECT
            name,
            type,
            CASE WHEN notnull = 0 THEN 'YES' ELSE 'NO' END as is_nullable,
            NULL as max_length,
            CASE WHEN pk > 0 THEN 1 ELSE 0 END as is_pk,
            NULL as fk_table
        FROM pragma_table_info(@table)
        ORDER BY cid
    ";

    public string GetPrimaryKeysQuery() => @"
        SELECT name, 1 as constraint_name
        FROM pragma_table_info(@table)
        WHERE pk > 0
        ORDER BY pk
    ";

    public string GetForeignKeysQuery() => @"
        SELECT
            from as id,
            table as fk_table_name,
            from as from_column,
            to as to_column
        FROM pragma_foreign_key_list(@table)
    ";

    public IReadOnlyList<(string Schema, string Table)> ParseTables(DataTable dt)
    {
        var result = new List<(string, string)>();
        foreach (DataRow row in dt.Rows)
        {
            result.Add((
                "main", // SQLite has one implicit 'main' schema
                row.Field<string>(1) ?? string.Empty
            ));
        }
        return result;
    }

    public IReadOnlyList<ColumnSchema> ParseColumns(DataTable dt)
    {
        var columns = new List<ColumnSchema>();
        foreach (DataRow row in dt.Rows)
        {
            columns.Add(new ColumnSchema(
                Name: row.Field<string>(0) ?? string.Empty,
                DataType: NormalizeSqliteType(row.Field<string>(1)),
                IsNullable: row.Field<string>(2) == "YES",
                MaxLength: ParseMaxLength(row),
                IsPrimaryKey: row.Field<int>(4) == 1,
                IsForeignKey: false, // Foreign key detection handled separately
                ForeignKeyTable: null
            ));
        }
        return columns;
    }

    private static int? ParseMaxLength(DataRow row)
    {
        if (row.IsNull(3))
            return null;

        long raw = row.Field<long>(3);

        if (raw < int.MinValue || raw > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"SQLite max_length '{raw}' is outside supported Int32 range.");
        }

        return (int)raw;
    }

    /// <summary>
    /// SQLite has dynamic typing with type affinity. Normalize common type names.
    /// </summary>
    private static string NormalizeSqliteType(string? sqliteType)
    {
        if (string.IsNullOrEmpty(sqliteType))
            return "TEXT"; // SQLite defaults to TEXT

        string upper = sqliteType.ToUpperInvariant();

        // Type affinity logic: https://sqlite.org/datatype3.html#type_affinity
        return upper switch
        {
            _ when upper.Contains("INT") => "INTEGER",
            _ when upper.Contains("CHAR") || upper.Contains("CLOB") || upper.Contains("TEXT") => "TEXT",
            _ when upper.Contains("BLOB") => "BLOB",
            _ when upper.Contains("REAL") || upper.Contains("FLOA") || upper.Contains("DOUB") => "REAL",
            _ => upper
        };
    }
}
