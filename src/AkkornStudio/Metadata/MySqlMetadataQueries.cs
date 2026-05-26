using System.Data;
using AkkornStudio.Core;

namespace AkkornStudio.Metadata;

/// <summary>
/// MySQL INFORMATION_SCHEMA provider for metadata queries.
/// Compatible with MySQL 5.7+ and MariaDB 10.3+.
/// </summary>
public sealed class MySqlMetadataQueries : IMetadataQueryProvider
{
    public string GetTablesQuery() => @"
        SELECT TABLE_SCHEMA, TABLE_NAME
        FROM   INFORMATION_SCHEMA.TABLES
        WHERE  TABLE_TYPE = 'BASE TABLE'
          AND  TABLE_SCHEMA NOT IN ('mysql', 'information_schema', 'performance_schema', 'sys')
          AND  (CREATE_OPTIONS IS NULL OR UPPER(CREATE_OPTIONS) NOT LIKE '%TEMPORARY%')
        ORDER  BY TABLE_SCHEMA, TABLE_NAME
    ";

    public string GetColumnsQuery() => @"
        SELECT
            c.COLUMN_NAME,
            c.COLUMN_TYPE,
            CASE WHEN c.IS_NULLABLE = 'YES' THEN 'YES' ELSE 'NO' END AS is_nullable,
            c.CHARACTER_MAXIMUM_LENGTH,
            CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS is_pk,
            fk_ref.REFERENCED_TABLE_NAME AS fk_table
        FROM INFORMATION_SCHEMA.COLUMNS c
        LEFT JOIN (
            SELECT COLUMN_NAME
            FROM   INFORMATION_SCHEMA.KEY_COLUMN_USAGE
            WHERE  TABLE_SCHEMA = @schema AND TABLE_NAME = @table AND CONSTRAINT_NAME = 'PRIMARY'
        ) pk ON pk.COLUMN_NAME = c.COLUMN_NAME
        LEFT JOIN (
            SELECT
                COLUMN_NAME,
                GROUP_CONCAT(DISTINCT REFERENCED_TABLE_NAME ORDER BY REFERENCED_TABLE_NAME SEPARATOR ',') AS REFERENCED_TABLE_NAME
            FROM   INFORMATION_SCHEMA.KEY_COLUMN_USAGE
            WHERE  TABLE_SCHEMA = @schema AND TABLE_NAME = @table AND REFERENCED_TABLE_NAME IS NOT NULL
            GROUP  BY COLUMN_NAME
        ) fk_ref ON fk_ref.COLUMN_NAME = c.COLUMN_NAME
        WHERE  c.TABLE_SCHEMA = @schema AND c.TABLE_NAME = @table
        ORDER  BY c.ORDINAL_POSITION
    ";

    public string GetPrimaryKeysQuery() => @"
        SELECT COLUMN_NAME, CONSTRAINT_NAME
        FROM   INFORMATION_SCHEMA.KEY_COLUMN_USAGE
        WHERE  TABLE_SCHEMA = @schema AND TABLE_NAME = @table AND CONSTRAINT_NAME = 'PRIMARY'
    ";

    public string GetForeignKeysQuery() => @"
        SELECT
            COLUMN_NAME,
            REFERENCED_TABLE_SCHEMA AS fk_table_schema,
            REFERENCED_TABLE_NAME AS fk_table_name,
            REFERENCED_COLUMN_NAME AS fk_column_name
        FROM   INFORMATION_SCHEMA.KEY_COLUMN_USAGE
        WHERE  TABLE_SCHEMA = @schema AND TABLE_NAME = @table AND REFERENCED_TABLE_NAME IS NOT NULL
    ";

    public IReadOnlyList<(string Schema, string Table)> ParseTables(DataTable dt)
    {
        var result = new List<(string, string)>();
        foreach (DataRow row in dt.Rows)
        {
            result.Add((
                row.Field<string>(0) ?? string.Empty,
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
                DataType: row.Field<string>(1) ?? string.Empty,
                IsNullable: row.Field<string>(2) == "YES",
                MaxLength: row.IsNull(3) ? null : (int?)row.Field<int?>(3),
                IsPrimaryKey: row.Field<int>(4) == 1,
                IsForeignKey: !row.IsNull(5),
                ForeignKeyTable: row.IsNull(5) ? null : row.Field<string>(5)
            ));
        }
        return columns;
    }
}
