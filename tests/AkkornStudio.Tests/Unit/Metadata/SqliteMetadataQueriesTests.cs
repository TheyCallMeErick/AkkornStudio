using System.Data;
using AkkornStudio.Metadata;

namespace AkkornStudio.Tests.Unit.Metadata;

public sealed class SqliteMetadataQueriesTests
{
    private readonly SqliteMetadataQueries _sut = new();

    [Fact]
    public void QueryTexts_AreNonEmptyAndContainExpectedSources()
    {
        string tables = _sut.GetTablesQuery();
        string columns = _sut.GetColumnsQuery();
        string pks = _sut.GetPrimaryKeysQuery();
        string fks = _sut.GetForeignKeysQuery();

        Assert.Contains("sqlite_master", tables, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pragma_table_info", columns, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pragma_table_info", pks, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pragma_foreign_key_list", fks, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("'id' as id", fks, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("from as id", fks, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseTables_UsesMainSchemaAndFallbackEmptyTableName()
    {
        DataTable table = new();
        table.Columns.Add("TABLE_SCHEMA", typeof(string));
        table.Columns.Add("TABLE_NAME", typeof(string));
        table.Rows.Add("ignored", "orders");
        table.Rows.Add("ignored", DBNull.Value);

        IReadOnlyList<(string Schema, string Table)> result = _sut.ParseTables(table);

        Assert.Equal(2, result.Count);
        Assert.Equal(("main", "orders"), result[0]);
        Assert.Equal(("main", string.Empty), result[1]);
    }

    [Fact]
    public void ParseColumns_NormalizesAllTypeAffinityBranches_AndMapsCommonFlags()
    {
        DataTable table = CreateColumnsTable();
        table.Rows.Add("i", "bigint", "NO", 10L, 1, DBNull.Value);
        table.Rows.Add("t", "varchar(20)", "YES", DBNull.Value, 0, DBNull.Value);
        table.Rows.Add("b", "blob", "NO", 15L, 0, DBNull.Value);
        table.Rows.Add("r", "double precision", "YES", 20L, 0, DBNull.Value);
        table.Rows.Add("u", "uuid", "YES", 25L, 0, DBNull.Value);
        table.Rows.Add("e", string.Empty, "YES", 28L, 0, DBNull.Value);
        table.Rows.Add("n", DBNull.Value, "YES", 30L, 0, DBNull.Value);
        table.Rows.Add(DBNull.Value, "int", "YES", 31L, 0, DBNull.Value);

        IReadOnlyList<ColumnSchema> columns = _sut.ParseColumns(table);

        Assert.Equal(8, columns.Count);

        Assert.Equal("INTEGER", columns[0].DataType);
        Assert.False(columns[0].IsNullable);
        Assert.Equal(10, columns[0].MaxLength);
        Assert.True(columns[0].IsPrimaryKey);
        Assert.False(columns[0].IsForeignKey);
        Assert.Null(columns[0].ForeignKeyTable);

        Assert.Equal("TEXT", columns[1].DataType);
        Assert.True(columns[1].IsNullable);
        Assert.Null(columns[1].MaxLength);

        Assert.Equal("BLOB", columns[2].DataType);
        Assert.Equal("REAL", columns[3].DataType);
        Assert.Equal("UUID", columns[4].DataType);
        Assert.Equal("TEXT", columns[5].DataType);
        Assert.Equal("TEXT", columns[6].DataType);
        Assert.Equal(string.Empty, columns[7].Name);
    }

    [Fact]
    public void ParseColumns_ReturnsEmpty_WhenNoRowsExist()
    {
        DataTable table = CreateColumnsTable();

        IReadOnlyList<ColumnSchema> columns = _sut.ParseColumns(table);

        Assert.Empty(columns);
    }

    [Fact]
    public void ParseColumns_ThrowsWhenMaxLengthIsOutOfIntRange()
    {
        DataTable table = CreateColumnsTable();
        table.Rows.Add("too_big", "int", "YES", (long)int.MaxValue + 1L, 0, DBNull.Value);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => _sut.ParseColumns(table));

        Assert.Equal(
            $"SQLite max_length '{(long)int.MaxValue + 1L}' is outside supported Int32 range.",
            ex.Message);
    }

    [Fact]
    public void ParseColumns_ThrowsWhenMaxLengthIsBelowIntRange()
    {
        DataTable table = CreateColumnsTable();
        table.Rows.Add("too_small", "int", "YES", (long)int.MinValue - 1L, 0, DBNull.Value);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => _sut.ParseColumns(table));

        Assert.Equal(
            $"SQLite max_length '{(long)int.MinValue - 1L}' is outside supported Int32 range.",
            ex.Message);
    }

    private static DataTable CreateColumnsTable()
    {
        DataTable table = new();
        table.Columns.Add("name", typeof(string));
        table.Columns.Add("type", typeof(string));
        table.Columns.Add("is_nullable", typeof(string));
        table.Columns.Add("max_length", typeof(long));
        table.Columns.Add("is_pk", typeof(int));
        table.Columns.Add("fk_table", typeof(string));
        return table;
    }
}
