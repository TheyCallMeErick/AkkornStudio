using System.Data;
using AkkornStudio.Core;

namespace AkkornStudio.Metadata;

/// <summary>
/// SQL Server INFORMATION_SCHEMA provider for metadata queries.
/// Compatible with SQL Server 2012+ and Azure SQL.
/// </summary>
public sealed class SqlServerMetadataQueries : IMetadataQueryProvider
{
    public string GetTablesQuery() => @"
        SELECT TABLE_SCHEMA, TABLE_NAME
        FROM   INFORMATION_SCHEMA.TABLES
        WHERE  TABLE_TYPE = 'BASE TABLE'
          AND  TABLE_SCHEMA NOT IN ('sys', 'INFORMATION_SCHEMA')
          AND  TABLE_CATALOG NOT IN ('master', 'model', 'msdb', 'tempdb', 'Resource')
        ORDER  BY TABLE_SCHEMA, TABLE_NAME
    ";

    public string GetColumnsQuery() => @"
        SELECT
            c.COLUMN_NAME,
            c.DATA_TYPE,
            c.IS_NULLABLE,
            c.CHARACTER_MAXIMUM_LENGTH,
            CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS IS_PK,
            fk_ref.TABLE_NAME AS FK_TABLE
        FROM INFORMATION_SCHEMA.COLUMNS c
        LEFT JOIN (
            SELECT ku.TABLE_SCHEMA, ku.TABLE_NAME, ku.COLUMN_NAME
            FROM   INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
            JOIN   INFORMATION_SCHEMA.KEY_COLUMN_USAGE  ku
                   ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
                   AND tc.TABLE_SCHEMA = ku.TABLE_SCHEMA
            WHERE  tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
        ) pk ON pk.TABLE_SCHEMA = c.TABLE_SCHEMA
             AND pk.TABLE_NAME  = c.TABLE_NAME
             AND pk.COLUMN_NAME = c.COLUMN_NAME
        LEFT JOIN (
            SELECT
                ku.TABLE_SCHEMA, ku.TABLE_NAME, ku.COLUMN_NAME,
                rku.TABLE_NAME AS FK_REF_TABLE
            FROM   INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc
            JOIN   INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                   ON rc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
            JOIN   INFORMATION_SCHEMA.KEY_COLUMN_USAGE rku
                   ON rc.UNIQUE_CONSTRAINT_NAME = rku.CONSTRAINT_NAME
                      AND ku.ORDINAL_POSITION = rku.ORDINAL_POSITION
        ) fk_ref ON fk_ref.TABLE_SCHEMA = c.TABLE_SCHEMA
                 AND fk_ref.TABLE_NAME  = c.TABLE_NAME
                 AND fk_ref.COLUMN_NAME = c.COLUMN_NAME
        WHERE c.TABLE_SCHEMA = @schema
          AND c.TABLE_NAME   = @table
        ORDER BY c.ORDINAL_POSITION
    ";

    public string GetPrimaryKeysQuery() => @"
        SELECT COLUMN_NAME, CONSTRAINT_NAME
        FROM   INFORMATION_SCHEMA.KEY_COLUMN_USAGE
        WHERE  TABLE_SCHEMA = @schema
          AND  TABLE_NAME   = @table
          AND  CONSTRAINT_NAME IN (
                  SELECT CONSTRAINT_NAME
                  FROM   INFORMATION_SCHEMA.TABLE_CONSTRAINTS
                  WHERE  TABLE_SCHEMA = @schema
                    AND  TABLE_NAME   = @table
                    AND  CONSTRAINT_TYPE = 'PRIMARY KEY'
              )
    ";

    public string GetForeignKeysQuery() => @"
        SELECT
            ku.COLUMN_NAME,
            rku.TABLE_SCHEMA AS FK_TABLE_SCHEMA,
            rku.TABLE_NAME AS FK_TABLE_NAME,
            rku.COLUMN_NAME AS FK_COLUMN_NAME
        FROM   INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc
        JOIN   INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
               ON rc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
               AND ku.TABLE_SCHEMA = @schema
               AND ku.TABLE_NAME = @table
        JOIN   INFORMATION_SCHEMA.KEY_COLUMN_USAGE rku
               ON rc.UNIQUE_CONSTRAINT_NAME = rku.CONSTRAINT_NAME
                  AND ku.ORDINAL_POSITION = rku.ORDINAL_POSITION
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
