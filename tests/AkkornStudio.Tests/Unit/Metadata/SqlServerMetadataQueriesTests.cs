using System.Data;
using AkkornStudio.Metadata;

namespace AkkornStudio.Tests.Unit.Metadata;

public sealed class SqlServerMetadataQueriesTests
{
    private readonly SqlServerMetadataQueries _sut = new();

    [Fact]
    public void GetTablesQuery_FiltersSystemSchemas()
    {
        string sql = _sut.GetTablesQuery();

        Assert.Contains("INFORMATION_SCHEMA.TABLES", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TABLE_TYPE = 'BASE TABLE'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TABLE_SCHEMA NOT IN ('sys', 'INFORMATION_SCHEMA')", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TABLE_CATALOG NOT IN ('master', 'model', 'msdb', 'tempdb', 'Resource')", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetColumnsPrimaryAndForeignQueries_AreNonEmptyAndContainExpectedContracts()
    {
        string columns = _sut.GetColumnsQuery();
        string pks = _sut.GetPrimaryKeysQuery();
        string fks = _sut.GetForeignKeysQuery();

        Assert.Contains("CONSTRAINT_TYPE = 'PRIMARY KEY'", columns, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CONSTRAINT_TYPE = 'PRIMARY KEY'", pks, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("REFERENTIAL_CONSTRAINTS", fks, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LIKE 'PK", columns, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LIKE 'PK", pks, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseTables_UsesStringFallbackForNullValues()
    {
        DataTable table = new();
        table.Columns.Add("TABLE_SCHEMA", typeof(string));
        table.Columns.Add("TABLE_NAME", typeof(string));
        table.Rows.Add("dbo", "orders");
        table.Rows.Add(DBNull.Value, DBNull.Value);

        IReadOnlyList<(string Schema, string Table)> rows = _sut.ParseTables(table);

        Assert.Equal(("dbo", "orders"), rows[0]);
        Assert.Equal((string.Empty, string.Empty), rows[1]);
    }

    [Fact]
    public void ParseColumns_MapsFlagsAndFallbacks()
    {
        DataTable table = CreateColumnsTable();
        table.Rows.Add("id", "int", "NO", 4, 1, "customers");
        table.Rows.Add(DBNull.Value, DBNull.Value, "YES", DBNull.Value, 0, DBNull.Value);

        IReadOnlyList<ColumnSchema> cols = _sut.ParseColumns(table);

        Assert.Equal(2, cols.Count);

        Assert.Equal("id", cols[0].Name);
        Assert.Equal("int", cols[0].DataType);
        Assert.False(cols[0].IsNullable);
        Assert.Equal(4, cols[0].MaxLength);
        Assert.True(cols[0].IsPrimaryKey);
        Assert.True(cols[0].IsForeignKey);
        Assert.Equal("customers", cols[0].ForeignKeyTable);

        Assert.Equal(string.Empty, cols[1].Name);
        Assert.Equal(string.Empty, cols[1].DataType);
        Assert.True(cols[1].IsNullable);
        Assert.Null(cols[1].MaxLength);
        Assert.False(cols[1].IsPrimaryKey);
        Assert.False(cols[1].IsForeignKey);
        Assert.Null(cols[1].ForeignKeyTable);
    }

    private static DataTable CreateColumnsTable()
    {
        DataTable table = new();
        table.Columns.Add("column_name", typeof(string));
        table.Columns.Add("data_type", typeof(string));
        table.Columns.Add("is_nullable", typeof(string));
        table.Columns.Add("max_length", typeof(int));
        table.Columns.Add("is_pk", typeof(int));
        table.Columns.Add("fk_table", typeof(string));
        return table;
    }
}
