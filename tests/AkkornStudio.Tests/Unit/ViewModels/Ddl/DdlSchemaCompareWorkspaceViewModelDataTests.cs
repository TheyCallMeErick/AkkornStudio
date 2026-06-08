using AkkornStudio.Core;
using AkkornStudio.Ddl.Compare;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.ViewModels.Ddl;

public sealed class DdlSchemaCompareWorkspaceViewModelDataTests
{
    private static DataRowSet RowSet(IReadOnlyList<string> columns, params object?[][] rows) =>
        new(columns, rows.Select(static r => (IReadOnlyList<object?>)r).ToList());

    private static DdlSchemaCompareWorkspaceViewModel LoadData(bool commentDeletes = true)
    {
        var vm = new DdlSchemaCompareWorkspaceViewModel(new ConnectionManagerViewModel())
        {
            CommentDestructiveDeletes = commentDeletes,
        };

        string[] columns = ["id", "name"];
        DataRowSet source = RowSet(columns, [1, "alice"], [2, "BOB"], [3, "carol"]);
        DataRowSet target = RowSet(columns, [1, "alice"], [2, "bob"], [4, "dave"]);

        vm.LoadDataComparisonForTesting(source, target, ["id"], DatabaseProvider.Postgres, "public", "users");
        return vm;
    }

    [Fact]
    public void LoadData_PopulatesCountsAndExcludesUnchangedFromGrid()
    {
        using DdlSchemaCompareWorkspaceViewModel vm = LoadData();

        Assert.True(vm.HasDataResult);
        Assert.Equal(1, vm.DataInsertCount);
        Assert.Equal(1, vm.DataUpdateCount);
        Assert.Equal(1, vm.DataDeleteCount);
        Assert.Equal(1, vm.DataUnchangedCount);

        // The grid lists only actionable differences (no unchanged rows).
        Assert.Equal(3, vm.DataDifferences.Count);
        Assert.DoesNotContain(vm.DataDifferences, d => d.Kind == RowDifferenceKind.Unchanged);
    }

    [Fact]
    public void LoadData_GeneratesDmlForActionableRows()
    {
        using DdlSchemaCompareWorkspaceViewModel vm = LoadData();

        Assert.True(vm.HasGeneratedDataSql);
        Assert.Contains("INSERT INTO", vm.GeneratedDataSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UPDATE", vm.GeneratedDataSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-- (destrutivo, revise) DELETE", vm.GeneratedDataSql, StringComparison.Ordinal);
    }

    [Fact]
    public void CommentDestructiveDeletes_False_EmitsBareDelete()
    {
        using DdlSchemaCompareWorkspaceViewModel vm = LoadData(commentDeletes: false);

        Assert.Contains("DELETE FROM", vm.GeneratedDataSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("-- (destrutivo", vm.GeneratedDataSql, StringComparison.Ordinal);
    }

    [Fact]
    public void StatementCountAndInSyncFlags_ReflectDifferences()
    {
        using DdlSchemaCompareWorkspaceViewModel vm = LoadData();
        Assert.Equal(3, vm.DataStatementCount);
        Assert.True(vm.HasDataStatements);
        Assert.False(vm.DataIsInSync);

        string[] columns = ["id", "name"];
        DataRowSet identical = RowSet(columns, [1, "alice"]);
        vm.LoadDataComparisonForTesting(identical, identical, ["id"], DatabaseProvider.Postgres, "public", "users");

        Assert.Equal(0, vm.DataStatementCount);
        Assert.False(vm.HasDataStatements);
        Assert.True(vm.DataIsInSync);
    }

    [Fact]
    public void CustomKeyColumn_ChangesHowRowsAreMatched()
    {
        string[] columns = ["id", "name", "city"];
        DataRowSet source = RowSet(columns, [1, "alice", "NYC"]);
        DataRowSet target = RowSet(columns, [2, "alice", "LA"]);

        // Matching by name: same person, only the city differs -> a single UPDATE.
        using var byName = new DdlSchemaCompareWorkspaceViewModel(new ConnectionManagerViewModel());
        byName.LoadDataComparisonForTesting(source, target, ["name"], DatabaseProvider.Postgres, "public", "people");
        Assert.Equal(1, byName.DataUpdateCount);
        Assert.Equal(0, byName.DataInsertCount);
        Assert.Equal(0, byName.DataDeleteCount);

        // Matching by id: different ids never match -> one INSERT and one DELETE.
        using var byId = new DdlSchemaCompareWorkspaceViewModel(new ConnectionManagerViewModel());
        byId.LoadDataComparisonForTesting(source, target, ["id"], DatabaseProvider.Postgres, "public", "people");
        Assert.Equal(0, byId.DataUpdateCount);
        Assert.Equal(1, byId.DataInsertCount);
        Assert.Equal(1, byId.DataDeleteCount);
    }

    [Fact]
    public void IsDataComparison_TogglesWithComparisonKind()
    {
        using var vm = new DdlSchemaCompareWorkspaceViewModel(new ConnectionManagerViewModel());

        Assert.True(vm.IsStructureComparison);
        vm.ComparisonKindIndex = 1;
        Assert.True(vm.IsDataComparison);
        Assert.Equal(DdlSchemaCompareComparisonKind.Data, vm.ComparisonKind);
    }
}
