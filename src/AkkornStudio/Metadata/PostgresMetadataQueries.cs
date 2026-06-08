using System.Data;
using AkkornStudio.Core;

namespace AkkornStudio.Metadata;

/// <summary>
/// PostgreSQL information_schema provider for metadata queries.
/// Compatible with PostgreSQL 12+.
/// </summary>
public sealed class PostgresMetadataQueries : IMetadataQueryProvider
{
    public string GetTablesQuery() => @"
        SELECT table_schema, table_name
        FROM   information_schema.tables
        WHERE  table_type = 'BASE TABLE'
          AND  table_schema NOT IN ('pg_catalog', 'information_schema', 'pg_toast')
        ORDER  BY table_schema, table_name
    ";

    public string GetColumnsQuery() => @"
        SELECT
            c.column_name,
            c.data_type,
            CASE WHEN c.is_nullable = 'YES' THEN 'YES' ELSE 'NO' END AS is_nullable,
            c.character_maximum_length,
            CASE WHEN pk.column_name IS NOT NULL THEN 1 ELSE 0 END AS is_pk,
            fk_ref.fk_table AS fk_table
        FROM information_schema.columns c
        LEFT JOIN (
            SELECT a.attname AS column_name
            FROM   pg_constraint pk
            JOIN   pg_attribute a ON pk.conrelid = a.attrelid AND a.attnum = ANY(pk.conkey)
            JOIN   pg_class pk_table ON pk.conrelid = pk_table.oid
            JOIN   pg_namespace pk_schema ON pk_table.relnamespace = pk_schema.oid
            WHERE  pk.contype = 'p'
              AND  pk_schema.nspname = @schema
              AND  pk_table.relname = @table
        ) pk ON pk.column_name = c.column_name
        LEFT JOIN (
            SELECT DISTINCT
                child_n.nspname AS table_schema,
                child_c.relname AS table_name,
                a.attname AS column_name,
                parent_c.relname AS fk_table
            FROM   pg_constraint fk
            JOIN   pg_class child_c ON fk.conrelid = child_c.oid
            JOIN   pg_namespace child_n ON child_c.relnamespace = child_n.oid
            JOIN   pg_attribute a ON fk.conrelid = a.attrelid AND a.attnum = ANY(fk.conkey)
            JOIN   pg_class parent_c ON fk.confrelid = parent_c.oid
            WHERE  fk.contype = 'f'
              AND  child_n.nspname = @schema
              AND  child_c.relname = @table
        ) fk_ref ON fk_ref.table_schema = c.table_schema
                 AND fk_ref.table_name = c.table_name
                 AND fk_ref.column_name = c.column_name
        WHERE  c.table_schema = @schema
          AND  c.table_name = @table
        ORDER  BY c.ordinal_position
    ";

    public string GetPrimaryKeysQuery() => @"
        SELECT a.attname AS column_name, pk.conname AS constraint_name
        FROM   pg_constraint pk
        JOIN   pg_attribute a ON pk.conrelid = a.attrelid AND a.attnum = ANY(pk.conkey)
        JOIN   pg_class c ON pk.conrelid = c.oid
        JOIN   pg_namespace n ON c.relnamespace = n.oid
        WHERE  pk.contype = 'p'
          AND  n.nspname = @schema
          AND  c.relname = @table
    ";

    public string GetForeignKeysQuery() => @"
        SELECT
            a.attname AS column_name,
            fn.nspname AS fk_table_schema,
            fc.relname AS fk_table_name,
            fa.attname AS fk_column_name
        FROM   pg_constraint fk
        JOIN   pg_attribute a ON fk.conrelid = a.attrelid AND a.attnum = ANY(fk.conkey)
        JOIN   pg_class c ON fk.conrelid = c.oid
        JOIN   pg_namespace n ON c.relnamespace = n.oid
        JOIN   pg_class fc ON fk.confrelid = fc.oid
        JOIN   pg_namespace fn ON fc.relnamespace = fn.oid
        JOIN   pg_attribute fa ON fk.confrelid = fa.attrelid AND fa.attnum = ANY(fk.confkey)
        WHERE  fk.contype = 'f'
          AND  n.nspname = @schema
          AND  c.relname = @table
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
