using System.Data;
using AkkornStudio.Metadata;

namespace AkkornStudio.Tests.Unit.Metadata;

public sealed class PostgresMetadataQueriesTests
{
    private readonly PostgresMetadataQueries _sut = new();

    [Fact]
    public void GetTablesQuery_FiltersSystemSchemasIncludingPgToast()
    {
        string sql = _sut.GetTablesQuery();

        Assert.Contains("information_schema.tables", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'pg_catalog'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'information_schema'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'pg_toast'", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetColumnsQuery_UsesValidPkAliases_AndRestrictsFkJoinBySchemaAndTable()
    {
        string sql = _sut.GetColumnsQuery();

        Assert.Contains("JOIN   pg_class pk_table", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("JOIN   pg_namespace pk_schema", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pk_schema.nspname = @schema", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pk_table.relname = @table", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pg_class_schema.relname", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pg_class_table.relname", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fk_ref.table_schema = c.table_schema", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fk_ref.table_name = c.table_name", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fk_ref.column_name = c.column_name", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fk_ref.fk_table AS fk_table", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE  fk.contype = 'f'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("child_n.nspname = @schema", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("child_c.relname = @table", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetPrimaryAndForeignKeyQueries_AreNonEmpty()
    {
        string pks = _sut.GetPrimaryKeysQuery();
        string fks = _sut.GetForeignKeysQuery();

        Assert.Contains("SELECT", pks, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SELECT", fks, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseTables_UsesStringFallbackForNulls()
    {
        DataTable dt = new();
        dt.Columns.Add("table_schema", typeof(string));
        dt.Columns.Add("table_name", typeof(string));
        dt.Rows.Add("public", "orders");
        dt.Rows.Add(DBNull.Value, DBNull.Value);

        IReadOnlyList<(string Schema, string Table)> tables = _sut.ParseTables(dt);

        Assert.Equal(("public", "orders"), tables[0]);
        Assert.Equal((string.Empty, string.Empty), tables[1]);
    }

    [Fact]
    public void ParseColumns_MapsFlagsLengthAndForeignKeyTable()
    {
        DataTable dt = CreateColumnsTable();
        dt.Rows.Add("id", "integer", "NO", 32, 1, "customers");
        dt.Rows.Add(DBNull.Value, DBNull.Value, "YES", DBNull.Value, 0, DBNull.Value);

        IReadOnlyList<ColumnSchema> columns = _sut.ParseColumns(dt);

        Assert.Equal(2, columns.Count);

        Assert.Equal("id", columns[0].Name);
        Assert.Equal("integer", columns[0].DataType);
        Assert.False(columns[0].IsNullable);
        Assert.Equal(32, columns[0].MaxLength);
        Assert.True(columns[0].IsPrimaryKey);
        Assert.True(columns[0].IsForeignKey);
        Assert.Equal("customers", columns[0].ForeignKeyTable);

        Assert.Equal(string.Empty, columns[1].Name);
        Assert.Equal(string.Empty, columns[1].DataType);
        Assert.True(columns[1].IsNullable);
        Assert.Null(columns[1].MaxLength);
        Assert.False(columns[1].IsPrimaryKey);
        Assert.False(columns[1].IsForeignKey);
        Assert.Null(columns[1].ForeignKeyTable);
    }

    private static DataTable CreateColumnsTable()
    {
        DataTable table = new();
        table.Columns.Add("column_name", typeof(string));
        table.Columns.Add("data_type", typeof(string));
        table.Columns.Add("is_nullable", typeof(string));
        table.Columns.Add("character_maximum_length", typeof(int));
        table.Columns.Add("is_pk", typeof(int));
        table.Columns.Add("fk_table", typeof(string));
        return table;
    }
}
