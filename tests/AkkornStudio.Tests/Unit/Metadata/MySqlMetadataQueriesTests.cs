using System.Data;
using AkkornStudio.Metadata;

namespace AkkornStudio.Tests.Unit.Metadata;

public sealed class MySqlMetadataQueriesTests
{
    private readonly MySqlMetadataQueries _sut = new();

    [Fact]
    public void GetTablesQuery_FiltersSystemSchemas()
    {
        string sql = _sut.GetTablesQuery();

        Assert.Contains("INFORMATION_SCHEMA.TABLES", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'mysql'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'information_schema'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'performance_schema'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'sys'", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetColumnsQuery_AggregatesMultipleReferencedTablesPerColumn()
    {
        string sql = _sut.GetColumnsQuery();

        Assert.Contains("GROUP_CONCAT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GROUP  BY COLUMN_NAME", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("REFERENCED_TABLE_NAME AS fk_table", sql, StringComparison.OrdinalIgnoreCase);
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
    public void ParseTables_UsesStringFallbackForNullValues()
    {
        DataTable table = new();
        table.Columns.Add("TABLE_SCHEMA", typeof(string));
        table.Columns.Add("TABLE_NAME", typeof(string));
        table.Rows.Add("app", "orders");
        table.Rows.Add(DBNull.Value, DBNull.Value);

        IReadOnlyList<(string Schema, string Table)> rows = _sut.ParseTables(table);

        Assert.Equal(("app", "orders"), rows[0]);
        Assert.Equal((string.Empty, string.Empty), rows[1]);
    }

    [Fact]
    public void ParseColumns_MapsFlagsLengthAndAggregatedForeignTable()
    {
        DataTable table = CreateColumnsTable();
        table.Rows.Add("customer_id", "int", "NO", 11, 0, "accounts,users");
        table.Rows.Add(DBNull.Value, DBNull.Value, "YES", DBNull.Value, 1, DBNull.Value);

        IReadOnlyList<ColumnSchema> cols = _sut.ParseColumns(table);

        Assert.Equal(2, cols.Count);

        Assert.Equal("customer_id", cols[0].Name);
        Assert.Equal("int", cols[0].DataType);
        Assert.False(cols[0].IsNullable);
        Assert.Equal(11, cols[0].MaxLength);
        Assert.False(cols[0].IsPrimaryKey);
        Assert.True(cols[0].IsForeignKey);
        Assert.Equal("accounts,users", cols[0].ForeignKeyTable);

        Assert.Equal(string.Empty, cols[1].Name);
        Assert.Equal(string.Empty, cols[1].DataType);
        Assert.True(cols[1].IsNullable);
        Assert.Null(cols[1].MaxLength);
        Assert.True(cols[1].IsPrimaryKey);
        Assert.False(cols[1].IsForeignKey);
        Assert.Null(cols[1].ForeignKeyTable);
    }

    private static DataTable CreateColumnsTable()
    {
        DataTable table = new();
        table.Columns.Add("column_name", typeof(string));
        table.Columns.Add("column_type", typeof(string));
        table.Columns.Add("is_nullable", typeof(string));
        table.Columns.Add("character_maximum_length", typeof(int));
        table.Columns.Add("is_pk", typeof(int));
        table.Columns.Add("fk_table", typeof(string));
        return table;
    }
}
