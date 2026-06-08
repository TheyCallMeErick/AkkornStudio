using AkkornStudio.Core;
using AkkornStudio.Ddl.Compare;

namespace AkkornStudio.Tests.Unit.Ddl.Compare;

public sealed class DataComparerTests
{
    private static DataRowSet RowSet(IReadOnlyList<string> columns, params object?[][] rows) =>
        new(columns, rows.Select(static r => (IReadOnlyList<object?>)r).ToList());

    [Fact]
    public void Compare_WithPrimaryKey_ClassifiesInsertUpdateDeleteAndUnchanged()
    {
        string[] columns = ["id", "name"];
        DataRowSet source = RowSet(columns,
            [1, "alice"],   // unchanged
            [2, "BOB"],     // update (name differs)
            [3, "carol"]);  // insert (missing in target)
        DataRowSet target = RowSet(columns,
            [1, "alice"],
            [2, "bob"],
            [4, "dave"]);   // delete (missing in source)

        DataComparison result = new DataComparer().Compare(source, target, ["id"]);

        Assert.Equal(1, result.InsertCount);
        Assert.Equal(1, result.UpdateCount);
        Assert.Equal(1, result.DeleteCount);
        Assert.Equal(1, result.UnchangedCount);

        RowDifference update = result.Differences.Single(d => d.Kind == RowDifferenceKind.UpdateInTarget);
        Assert.Equal(["name"], update.ChangedColumns);
    }

    [Fact]
    public void Compare_WithoutKey_FallsBackToWholeRow_SoChangeBecomesInsertPlusDelete()
    {
        string[] columns = ["a", "b"];
        DataRowSet source = RowSet(columns, ["x", "1"], ["y", "2"]);
        DataRowSet target = RowSet(columns, ["x", "1"], ["y", "9"]);

        DataComparison result = new DataComparer().Compare(source, target, []);

        Assert.Equal(0, result.UpdateCount);
        Assert.Equal(1, result.InsertCount);
        Assert.Equal(1, result.DeleteCount);
        Assert.Equal(1, result.UnchangedCount);
        Assert.Contains(result.Warnings, w => w.Contains("linha inteira", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compare_CrossTypeNumericAndNull_AreTreatedAsEqual()
    {
        string[] columns = ["id", "qty", "note"];
        DataRowSet source = RowSet(columns, [1, 10L, null]);
        DataRowSet target = RowSet(columns, [1, 10m, DBNull.Value]);

        DataComparison result = new DataComparer().Compare(source, target, ["id"]);

        Assert.Equal(1, result.UnchangedCount);
        Assert.Equal(0, result.UpdateCount);
    }

    [Fact]
    public void Compare_ColumnsOnlyOnOneSide_AreIgnoredWithWarning()
    {
        DataRowSet source = RowSet(["id", "name", "extra"], [1, "a", "ignored"]);
        DataRowSet target = RowSet(["id", "name"], [1, "a"]);

        DataComparison result = new DataComparer().Compare(source, target, ["id"]);

        Assert.Equal(["id", "name"], result.Columns);
        Assert.Equal(1, result.UnchangedCount);
        Assert.Contains(result.Warnings, w => w.Contains("extra", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Generate_EmitsInsertUpdateAndCommentedDelete()
    {
        string[] columns = ["id", "name"];
        DataRowSet source = RowSet(columns, [2, "BOB"], [3, "carol"]);
        DataRowSet target = RowSet(columns, [2, "bob"], [4, "dave"]);

        DataComparison comparison = new DataComparer().Compare(source, target, ["id"]);
        string sql = new DataSyncScriptGenerator().Generate(
            comparison, DatabaseProvider.Postgres, "public", "users", DataSyncOptions.Default);

        Assert.Contains("INSERT INTO \"public\".\"users\" (\"id\", \"name\") VALUES (3, 'carol');", sql);
        Assert.Contains("UPDATE \"public\".\"users\" SET \"name\" = 'BOB' WHERE \"id\" = 2;", sql);
        Assert.Contains("-- (destrutivo, revise) DELETE FROM \"public\".\"users\" WHERE \"id\" = 4;", sql);
    }

    [Fact]
    public void Generate_WhenInSync_ReturnsNoOpComment()
    {
        string[] columns = ["id", "name"];
        DataRowSet rows = RowSet(columns, [1, "a"]);

        DataComparison comparison = new DataComparer().Compare(rows, rows, ["id"]);
        string sql = new DataSyncScriptGenerator().Generate(
            comparison, DatabaseProvider.SqlServer, "dbo", "users", DataSyncOptions.Default);

        Assert.Contains("ja sincronizados", sql);
    }

    [Fact]
    public void FormatLiteral_EscapesStringsAndFormatsBoolPerProvider()
    {
        Assert.Equal("'O''Brien'", DataSyncScriptGenerator.FormatLiteral(DatabaseProvider.Postgres, "O'Brien"));
        Assert.Equal("NULL", DataSyncScriptGenerator.FormatLiteral(DatabaseProvider.MySql, null));
        Assert.Equal("TRUE", DataSyncScriptGenerator.FormatLiteral(DatabaseProvider.Postgres, true));
        Assert.Equal("1", DataSyncScriptGenerator.FormatLiteral(DatabaseProvider.MySql, true));
    }
}
