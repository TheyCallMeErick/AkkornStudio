using System.Data;
using AkkornStudio.UI.Services.SqlEditor.Results;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.ViewModels.SqlEditor;

public sealed class SqlResultPageViewModelTests
{
    [Fact]
    public void SetSession_UpdatesDerivedProperties()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession("select * from customers;");

        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());

        Assert.True(sut.HasSession);
        Assert.Equal("select * from customers;", sut.SqlText);
        Assert.Equal("conn-1", sut.ConnectionId);
        Assert.Equal("1", sut.RowCountText);
        Assert.Equal("2", sut.ColumnCountText);
    }

    [Fact]
    public void BackToEditorCommand_InvokesConfiguredNavigation()
    {
        var sut = new SqlResultPageViewModel();
        Guid expectedSourceId = Guid.NewGuid();
        Guid? receivedId = null;

        sut.ConfigureBackNavigation(id => receivedId = id);
        sut.SetSession(BuildSession("select 1;"), expectedSourceId);
        sut.BackToEditorCommand.Execute(null);

        Assert.Equal(expectedSourceId, receivedId);
    }

    [Fact]
    public void TogglePinCommand_TogglesCurrentSessionPinState()
    {
        var service = new SqlResultSessionService();
        SqlResultSession session = service.Add(new SqlResultSessionCreateRequest(
            SqlText: "select * from customers;",
            ConnectionId: "conn-1",
            DatabaseName: "app_db",
            SchemaName: "public",
            ResultSet: BuildSession("select * from customers;", new DateTimeOffset(2026, 05, 12, 12, 0, 0, TimeSpan.Zero)).ResultSet));

        var sut = new SqlResultPageViewModel();
        sut.ConfigureSessionService(service);
        sut.SetSession(session, Guid.NewGuid());

        sut.TogglePinCommand.Execute(null);
        Assert.True(service.Get(session.Id)?.IsPinned);

        sut.TogglePinCommand.Execute(null);
        Assert.False(service.Get(session.Id)?.IsPinned);
    }

    [Fact]
    public void CloseSessionCommand_RemovesSessionAndSelectsNext()
    {
        var service = new SqlResultSessionService();
        SqlResultSession first = service.Add(new SqlResultSessionCreateRequest(
            SqlText: "select 1;",
            ConnectionId: "conn-1",
            DatabaseName: "app_db",
            SchemaName: "public",
            ResultSet: BuildSession("select 1;", new DateTimeOffset(2026, 05, 12, 12, 0, 0, TimeSpan.Zero)).ResultSet));
        SqlResultSession second = service.Add(new SqlResultSessionCreateRequest(
            SqlText: "select 2;",
            ConnectionId: "conn-1",
            DatabaseName: "app_db",
            SchemaName: "public",
            ResultSet: BuildSession("select 2;", new DateTimeOffset(2026, 05, 12, 12, 1, 0, TimeSpan.Zero)).ResultSet));

        var sut = new SqlResultPageViewModel();
        sut.ConfigureSessionService(service);
        sut.SetSession(second, Guid.NewGuid());

        sut.CloseSessionCommand.Execute(null);

        Assert.Null(service.Get(second.Id));
        Assert.True(sut.HasSession);
        Assert.Equal(first.Id, sut.SelectedSession?.Id);
    }

    [Fact]
    public void ColumnVisibilityToggle_HidesColumnInGridProjection()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select * from customers;",
            rows:
            [
                (1, "alice"),
                (2, "bob"),
            ]);

        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        SqlResultPageViewModel.SqlResultColumnVisibilityItemViewModel nameColumn = sut.ColumnVisibilityItems
            .First(item => item.ColumnName == "name");

        nameColumn.IsVisible = false;

        Assert.DoesNotContain("name", session.ViewState.VisibleColumns);
        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);
        DataTable projectedTable = Assert.IsType<DataTable>(rowsView.Table);
        Assert.False(projectedTable.Columns.Contains("name"));
        Assert.True(projectedTable.Columns.Contains("id"));
    }

    [Fact]
    public void ApplyColumnFilterCommand_FiltersProjectedRows()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select * from customers;",
            rows:
            [
                (1, "alice"),
                (2, "bob"),
                (3, "bobby"),
            ]);

        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        sut.SelectedFilterColumn = "name";
        sut.SelectedFilterOperation = "contains";
        sut.ColumnFilterValue = "bob";
        sut.ApplyColumnFilterCommand.Execute(null);

        Assert.Equal("2", sut.RowCountText);
        Assert.NotNull(sut.RowsView);
        Assert.Equal(2, sut.RowsView!.Count);
        Assert.Single(session.ViewState.Filters);

        sut.ClearColumnFilterCommand.Execute(null);
        Assert.Equal("3", sut.RowCountText);
        Assert.Empty(session.ViewState.Filters);
    }

    [Fact]
    public void ApplyColumnFilterCommand_SupportsNumericComparison()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select * from customers;",
            rows:
            [
                (1, "alice"),
                (2, "bob"),
                (3, "charlie"),
            ]);

        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        sut.SelectedFilterColumn = "id";
        sut.SelectedFilterOperation = "gt";
        sut.ColumnFilterValue = "1";
        sut.ApplyColumnFilterCommand.Execute(null);

        Assert.Equal("2", sut.RowCountText);
        int[] orderedIds = ReadColumnAsIntArray(sut.RowsView, "id");
        Assert.Equal([2, 3], orderedIds);
    }

    [Fact]
    public void ApplyColumnFilterCommand_SupportsMultipleCriteriaWithAnd()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select * from customers;",
            rows:
            [
                (1, "bob"),
                (2, "bobby"),
                (3, "bobby"),
                (4, "alice"),
            ]);

        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        sut.SelectedFilterColumn = "name";
        sut.SelectedFilterOperation = "contains";
        sut.ColumnFilterValue = "bob";
        sut.ApplyColumnFilterCommand.Execute(null);

        sut.SelectedFilterColumn = "id";
        sut.SelectedFilterOperation = "gt";
        sut.ColumnFilterValue = "2";
        sut.ApplyColumnFilterCommand.Execute(null);

        Assert.Equal("1", sut.RowCountText);
        int[] ids = ReadColumnAsIntArray(sut.RowsView, "id");
        Assert.Equal([3], ids);
        Assert.Equal(2, sut.ActiveFilterCriteria.Count);
    }

    [Fact]
    public void GlobalSearch_UsesOnlyVisibleColumns()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select * from customers;",
            rows:
            [
                (1, "alice"),
                (2, "needle"),
            ]);

        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        sut.SearchText = "needle";
        Assert.Equal("1", sut.RowCountText);

        SqlResultPageViewModel.SqlResultColumnVisibilityItemViewModel nameColumn = sut.ColumnVisibilityItems
            .First(item => item.ColumnName == "name");
        nameColumn.IsVisible = false;
        sut.SearchText = "needle";

        Assert.Equal("0", sut.RowCountText);
    }

    [Fact]
    public void AddSortCriterionCommand_AppliesMultiColumnOrdering()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select * from customers;",
            rows:
            [
                (1, "bravo"),
                (2, "alpha"),
                (3, "alpha"),
                (4, "bravo"),
            ]);

        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());

        sut.SelectedSortColumn = "name";
        sut.AddSortCriterionCommand.Execute(null); // name asc
        sut.SelectedSortColumn = "id";
        sut.ToggleSortDirectionCommand.Execute(null); // desc
        sut.AddSortCriterionCommand.Execute(null); // id desc

        Assert.Equal(2, session.ViewState.Sorts.Count);
        Assert.Equal("name", session.ViewState.Sorts[0].ColumnName);
        Assert.Equal("id", session.ViewState.Sorts[1].ColumnName);
        Assert.False(session.ViewState.Sorts[0].Descending);
        Assert.True(session.ViewState.Sorts[1].Descending);

        int[] orderedIds = ReadColumnAsIntArray(sut.RowsView, "id");
        Assert.Equal([3, 2, 4, 1], orderedIds);
    }

    [Fact]
    public void AddGroupColumnCommand_GroupsRowsBySelectedColumn()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select * from customers;",
            rows:
            [
                (1, "bravo"),
                (2, "alpha"),
                (3, "alpha"),
                (4, "bravo"),
            ]);

        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        sut.SelectedGroupColumn = "name";
        sut.AddGroupColumnCommand.Execute(null);

        Assert.Single(session.ViewState.GroupedColumns);
        Assert.Equal("name", session.ViewState.GroupedColumns[0]);
        Assert.Single(sut.ActiveGroupColumns);
        Assert.Equal("name", sut.ActiveGroupColumns[0].ColumnName);

        string[] orderedNames = ReadColumnAsStringArray(sut.RowsView, "name");
        Assert.Equal(["alpha", "alpha", "bravo", "bravo"], orderedNames);
    }

    [Fact]
    public void SessionSwitch_PreservesPerSessionGroupCriteria()
    {
        var service = new SqlResultSessionService();
        SqlResultSession first = service.Add(new SqlResultSessionCreateRequest(
            SqlText: "select * from first;",
            ConnectionId: "conn-1",
            DatabaseName: "app_db",
            SchemaName: "public",
            ResultSet: BuildSession("select * from first;", rows: [(1, "b"), (2, "a")]).ResultSet));
        SqlResultSession second = service.Add(new SqlResultSessionCreateRequest(
            SqlText: "select * from second;",
            ConnectionId: "conn-1",
            DatabaseName: "app_db",
            SchemaName: "public",
            ResultSet: BuildSession("select * from second;", rows: [(1, "x"), (2, "y")]).ResultSet));

        var sut = new SqlResultPageViewModel();
        sut.ConfigureSessionService(service);
        sut.SetSession(first, Guid.NewGuid());
        sut.SelectedGroupColumn = "name";
        sut.AddGroupColumnCommand.Execute(null);

        sut.SetSession(second, Guid.NewGuid());
        Assert.Empty(second.ViewState.GroupedColumns);
        Assert.Empty(sut.ActiveGroupColumns);

        sut.SelectSessionCommand.Execute(first);
        Assert.Single(sut.ActiveGroupColumns);
        Assert.Equal("name", sut.ActiveGroupColumns[0].ColumnName);
    }

    [Fact]
    public void GroupBuckets_CollapseAndExpand_HidesAndRestoresGroupedRows()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select * from customers;",
            rows:
            [
                (1, "alpha"),
                (2, "alpha"),
                (3, "bravo"),
            ]);

        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        sut.SelectedGroupColumn = "name";
        sut.AddGroupColumnCommand.Execute(null);

        Assert.Equal(2, sut.GroupBuckets.Count);
        SqlResultPageViewModel.SqlResultGroupBucketItemViewModel alphaBucket = sut.GroupBuckets
            .First(bucket => bucket.GroupKey.Contains("alpha", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, alphaBucket.RowCount);

        sut.ToggleGroupBucketCollapsedCommand.Execute(alphaBucket);
        Assert.Equal("1", sut.RowCountText);
        string[] collapsedNames = ReadColumnAsStringArray(sut.RowsView, "name");
        Assert.Equal(["bravo"], collapsedNames);

        SqlResultPageViewModel.SqlResultGroupBucketItemViewModel updatedAlphaBucket = sut.GroupBuckets
            .First(bucket => bucket.GroupKey.Contains("alpha", StringComparison.OrdinalIgnoreCase));
        sut.ToggleGroupBucketCollapsedCommand.Execute(updatedAlphaBucket);
        Assert.Equal("3", sut.RowCountText);
        string[] expandedNames = ReadColumnAsStringArray(sut.RowsView, "name");
        Assert.Equal(["alpha", "alpha", "bravo"], expandedNames);
    }

    [Fact]
    public void SessionSwitch_PreservesPerSessionCollapsedGroupState()
    {
        var service = new SqlResultSessionService();
        SqlResultSession first = service.Add(new SqlResultSessionCreateRequest(
            SqlText: "select * from first;",
            ConnectionId: "conn-1",
            DatabaseName: "app_db",
            SchemaName: "public",
            ResultSet: BuildSession("select * from first;", rows: [(1, "a"), (2, "a"), (3, "b")]).ResultSet));
        SqlResultSession second = service.Add(new SqlResultSessionCreateRequest(
            SqlText: "select * from second;",
            ConnectionId: "conn-1",
            DatabaseName: "app_db",
            SchemaName: "public",
            ResultSet: BuildSession("select * from second;", rows: [(1, "x"), (2, "y")]).ResultSet));

        var sut = new SqlResultPageViewModel();
        sut.ConfigureSessionService(service);
        sut.SetSession(first, Guid.NewGuid());
        sut.SelectedGroupColumn = "name";
        sut.AddGroupColumnCommand.Execute(null);
        SqlResultPageViewModel.SqlResultGroupBucketItemViewModel aBucket = sut.GroupBuckets
            .First(bucket => bucket.GroupKey.Contains("a", StringComparison.OrdinalIgnoreCase));
        sut.ToggleGroupBucketCollapsedCommand.Execute(aBucket);
        Assert.Equal("1", sut.RowCountText);

        sut.SetSession(second, Guid.NewGuid());
        Assert.Equal("2", sut.RowCountText);

        sut.SelectSessionCommand.Execute(first);
        Assert.Equal("1", sut.RowCountText);
    }

    [Fact]
    public void SelectedRowItem_UpdatesSelectedRowStateAndJson()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select * from customers;",
            rows:
            [
                (1, "alice"),
                (2, "bob"),
            ]);

        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);
        DataRowView selected = rowsView[1];
        sut.SelectedRowItem = selected;

        Assert.True(sut.HasSelectedRow);
        Assert.Equal(1, session.ViewState.SelectedRowIndex);
        Assert.Contains("\"id\": 2", sut.SelectedRowJson, StringComparison.Ordinal);
        Assert.Contains("\"name\": \"bob\"", sut.SelectedRowJson, StringComparison.Ordinal);
        Assert.Contains(sut.SelectedRowFields, field => field.ColumnName == "id" && field.Value == "2");
        Assert.Contains(sut.SelectedRowFields, field => field.ColumnName == "name" && field.Value == "bob");
    }

    [Fact]
    public void ClearSelectedRowCommand_ClearsSelectionAndSessionState()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select * from customers;",
            rows:
            [
                (1, "alice"),
                (2, "bob"),
            ]);

        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);
        sut.SelectedRowItem = rowsView[0];
        Assert.True(sut.HasSelectedRow);

        sut.ClearSelectedRowCommand.Execute(null);

        Assert.False(sut.HasSelectedRow);
        Assert.Null(session.ViewState.SelectedRowIndex);
        Assert.Equal("{}", sut.SelectedRowJson);
    }

    [Fact]
    public void SessionSwitch_PreservesPerSessionSelectedRow()
    {
        var service = new SqlResultSessionService();
        SqlResultSession first = service.Add(new SqlResultSessionCreateRequest(
            SqlText: "select * from first;",
            ConnectionId: "conn-1",
            DatabaseName: "app_db",
            SchemaName: "public",
            ResultSet: BuildSession("select * from first;", rows: [(1, "a"), (2, "b")]).ResultSet));
        SqlResultSession second = service.Add(new SqlResultSessionCreateRequest(
            SqlText: "select * from second;",
            ConnectionId: "conn-1",
            DatabaseName: "app_db",
            SchemaName: "public",
            ResultSet: BuildSession("select * from second;", rows: [(1, "x"), (2, "y")]).ResultSet));

        var sut = new SqlResultPageViewModel();
        sut.ConfigureSessionService(service);
        sut.SetSession(first, Guid.NewGuid());
        DataView firstRows = Assert.IsType<DataView>(sut.RowsView);
        sut.SelectedRowItem = firstRows[1];
        Assert.Equal(1, first.ViewState.SelectedRowIndex);

        sut.SetSession(second, Guid.NewGuid());
        Assert.False(sut.HasSelectedRow);

        sut.SelectSessionCommand.Execute(first);
        Assert.True(sut.HasSelectedRow);
        Assert.Equal("Row 2", sut.SelectedRowSummary);
    }

    [Fact]
    public void SessionSwitch_PreservesPerSessionSortCriteria()
    {
        var service = new SqlResultSessionService();
        SqlResultSession first = service.Add(new SqlResultSessionCreateRequest(
            SqlText: "select * from first;",
            ConnectionId: "conn-1",
            DatabaseName: "app_db",
            SchemaName: "public",
            ResultSet: BuildSession("select * from first;", rows: [(1, "c"), (2, "a"), (3, "b")]).ResultSet));
        SqlResultSession second = service.Add(new SqlResultSessionCreateRequest(
            SqlText: "select * from second;",
            ConnectionId: "conn-1",
            DatabaseName: "app_db",
            SchemaName: "public",
            ResultSet: BuildSession("select * from second;", rows: [(1, "x"), (2, "y"), (3, "z")]).ResultSet));

        var sut = new SqlResultPageViewModel();
        sut.ConfigureSessionService(service);
        sut.SetSession(first, Guid.NewGuid());
        sut.SelectedSortColumn = "name";
        sut.AddSortCriterionCommand.Execute(null);

        sut.SetSession(second, Guid.NewGuid());
        Assert.Empty(second.ViewState.Sorts);
        Assert.Empty(sut.ActiveSortCriteria);

        sut.SelectSessionCommand.Execute(first);
        Assert.Single(sut.ActiveSortCriteria);
        Assert.Equal("name", sut.ActiveSortCriteria[0].ColumnName);
    }

    [Fact]
    public void SessionSwitch_PreservesPerSessionFilterCriteria()
    {
        var service = new SqlResultSessionService();
        SqlResultSession first = service.Add(new SqlResultSessionCreateRequest(
            SqlText: "select * from first;",
            ConnectionId: "conn-1",
            DatabaseName: "app_db",
            SchemaName: "public",
            ResultSet: BuildSession("select * from first;", rows: [(1, "bob"), (2, "alice")]).ResultSet));
        SqlResultSession second = service.Add(new SqlResultSessionCreateRequest(
            SqlText: "select * from second;",
            ConnectionId: "conn-1",
            DatabaseName: "app_db",
            SchemaName: "public",
            ResultSet: BuildSession("select * from second;", rows: [(1, "x"), (2, "y")]).ResultSet));

        var sut = new SqlResultPageViewModel();
        sut.ConfigureSessionService(service);
        sut.SetSession(first, Guid.NewGuid());
        sut.SelectedFilterColumn = "name";
        sut.SelectedFilterOperation = "contains";
        sut.ColumnFilterValue = "bob";
        sut.ApplyColumnFilterCommand.Execute(null);

        sut.SetSession(second, Guid.NewGuid());
        Assert.Empty(second.ViewState.Filters);
        Assert.Empty(sut.ActiveFilterCriteria);

        sut.SelectSessionCommand.Execute(first);
        Assert.Single(sut.ActiveFilterCriteria);
        Assert.Equal("name", sut.ActiveFilterCriteria[0].ColumnName);
        Assert.Equal("contains", sut.ActiveFilterCriteria[0].Operation);
    }

    [Fact]
    public void MoveColumnUpCommand_ReordersProjectedColumnsAndPersistsState()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select * from customers;",
            rows:
            [
                (1, "alice"),
                (2, "bob"),
            ]);

        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        SqlResultPageViewModel.SqlResultColumnVisibilityItemViewModel nameColumn = sut.ColumnVisibilityItems
            .First(item => item.ColumnName == "name");

        sut.MoveColumnUpCommand.Execute(nameColumn);

        Assert.Equal(["name", "id"], session.ViewState.ColumnOrder);
        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);
        DataTable table = Assert.IsType<DataTable>(rowsView.Table);
        Assert.Equal("name", table.Columns[0].ColumnName);
        Assert.Equal("id", table.Columns[1].ColumnName);
    }

    [Fact]
    public void SessionSwitch_PreservesPerSessionColumnOrder()
    {
        var service = new SqlResultSessionService();
        SqlResultSession first = service.Add(new SqlResultSessionCreateRequest(
            SqlText: "select * from first;",
            ConnectionId: "conn-1",
            DatabaseName: "app_db",
            SchemaName: "public",
            ResultSet: BuildSession("select * from first;", rows: [(1, "alpha"), (2, "beta")]).ResultSet));
        SqlResultSession second = service.Add(new SqlResultSessionCreateRequest(
            SqlText: "select * from second;",
            ConnectionId: "conn-1",
            DatabaseName: "app_db",
            SchemaName: "public",
            ResultSet: BuildSession("select * from second;", rows: [(1, "x"), (2, "y")]).ResultSet));

        var sut = new SqlResultPageViewModel();
        sut.ConfigureSessionService(service);
        sut.SetSession(first, Guid.NewGuid());
        SqlResultPageViewModel.SqlResultColumnVisibilityItemViewModel firstNameColumn = sut.ColumnVisibilityItems
            .First(item => item.ColumnName == "name");
        sut.MoveColumnUpCommand.Execute(firstNameColumn);

        sut.SetSession(second, Guid.NewGuid());
        DataView secondRowsView = Assert.IsType<DataView>(sut.RowsView);
        DataTable secondTable = Assert.IsType<DataTable>(secondRowsView.Table);
        Assert.Equal("id", secondTable.Columns[0].ColumnName);

        sut.SelectSessionCommand.Execute(first);
        DataView firstRowsView = Assert.IsType<DataView>(sut.RowsView);
        DataTable firstTable = Assert.IsType<DataTable>(firstRowsView.Table);
        Assert.Equal("name", firstTable.Columns[0].ColumnName);
    }

    [Fact]
    public void ToggleColumnFrozenCommand_FreezesAndUnfreezesColumn()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select * from customers;",
            rows:
            [
                (1, "alice"),
                (2, "bob"),
            ]);

        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        SqlResultPageViewModel.SqlResultColumnVisibilityItemViewModel nameColumn = sut.ColumnVisibilityItems
            .First(item => item.ColumnName == "name");

        sut.ToggleColumnFrozenCommand.Execute(nameColumn);
        Assert.Contains("name", session.ViewState.FrozenColumns);
        Assert.Equal(1, sut.FrozenColumnCount);
        DataView frozenRowsView = Assert.IsType<DataView>(sut.RowsView);
        DataTable frozenTable = Assert.IsType<DataTable>(frozenRowsView.Table);
        Assert.Equal("name", frozenTable.Columns[0].ColumnName);

        sut.ToggleColumnFrozenCommand.Execute(nameColumn);
        Assert.DoesNotContain("name", session.ViewState.FrozenColumns);
        Assert.Equal(0, sut.FrozenColumnCount);
    }

    [Fact]
    public void SessionSwitch_PreservesPerSessionFrozenColumns()
    {
        var service = new SqlResultSessionService();
        SqlResultSession first = service.Add(new SqlResultSessionCreateRequest(
            SqlText: "select * from first;",
            ConnectionId: "conn-1",
            DatabaseName: "app_db",
            SchemaName: "public",
            ResultSet: BuildSession("select * from first;", rows: [(1, "alpha"), (2, "beta")]).ResultSet));
        SqlResultSession second = service.Add(new SqlResultSessionCreateRequest(
            SqlText: "select * from second;",
            ConnectionId: "conn-1",
            DatabaseName: "app_db",
            SchemaName: "public",
            ResultSet: BuildSession("select * from second;", rows: [(1, "x"), (2, "y")]).ResultSet));

        var sut = new SqlResultPageViewModel();
        sut.ConfigureSessionService(service);
        sut.SetSession(first, Guid.NewGuid());
        SqlResultPageViewModel.SqlResultColumnVisibilityItemViewModel firstNameColumn = sut.ColumnVisibilityItems
            .First(item => item.ColumnName == "name");
        sut.ToggleColumnFrozenCommand.Execute(firstNameColumn);

        sut.SetSession(second, Guid.NewGuid());
        Assert.Equal(0, sut.FrozenColumnCount);

        sut.SelectSessionCommand.Execute(first);
        Assert.Equal(1, sut.FrozenColumnCount);
        Assert.Contains("name", first.ViewState.FrozenColumns);
    }

    [Fact]
    public void SessionSwitch_PreservesPerSessionColumnVisibilityState()
    {
        var service = new SqlResultSessionService();
        SqlResultSession first = service.Add(new SqlResultSessionCreateRequest(
            SqlText: "select * from first;",
            ConnectionId: "conn-1",
            DatabaseName: "app_db",
            SchemaName: "public",
            ResultSet: BuildSession("select * from first;", rows: [(1, "alpha"), (2, "beta")]).ResultSet));
        SqlResultSession second = service.Add(new SqlResultSessionCreateRequest(
            SqlText: "select * from second;",
            ConnectionId: "conn-1",
            DatabaseName: "app_db",
            SchemaName: "public",
            ResultSet: BuildSession("select * from second;", rows: [(1, "x"), (2, "y")]).ResultSet));

        var sut = new SqlResultPageViewModel();
        sut.ConfigureSessionService(service);
        sut.SetSession(first, Guid.NewGuid());
        sut.ColumnVisibilityItems.First(item => item.ColumnName == "name").IsVisible = false;

        sut.SetSession(second, Guid.NewGuid());
        DataView secondRowsView = Assert.IsType<DataView>(sut.RowsView);
        DataTable secondTable = Assert.IsType<DataTable>(secondRowsView.Table);
        Assert.True(secondTable.Columns.Contains("name"));

        sut.SelectSessionCommand.Execute(first);
        DataView firstRowsView = Assert.IsType<DataView>(sut.RowsView);
        DataTable firstTable = Assert.IsType<DataTable>(firstRowsView.Table);
        Assert.False(firstTable.Columns.Contains("name"));
        Assert.DoesNotContain("name", first.ViewState.VisibleColumns);
    }

    private static int[] ReadColumnAsIntArray(DataView? rowsView, string columnName)
    {
        DataView view = Assert.IsType<DataView>(rowsView);
        DataTable table = Assert.IsType<DataTable>(view.Table);
        return table.Rows.Cast<DataRow>().Select(row => Convert.ToInt32(row[columnName])).ToArray();
    }

    private static string[] ReadColumnAsStringArray(DataView? rowsView, string columnName)
    {
        DataView view = Assert.IsType<DataView>(rowsView);
        DataTable table = Assert.IsType<DataTable>(view.Table);
        return table.Rows.Cast<DataRow>().Select(row => Convert.ToString(row[columnName]) ?? string.Empty).ToArray();
    }

    private static SqlResultSession BuildSession(
        string sql,
        DateTimeOffset? executedAtOverride = null,
        IReadOnlyList<(int Id, string Name)>? rows = null)
    {
        DateTimeOffset executedAt = executedAtOverride ?? new DateTimeOffset(2026, 05, 12, 12, 0, 0, TimeSpan.Zero);
        var table = new DataTable();
        table.Columns.Add("id", typeof(int));
        table.Columns.Add("name", typeof(string));

        IReadOnlyList<(int Id, string Name)> sourceRows = rows ?? [(1, "alice")];
        foreach ((int id, string name) in sourceRows)
            table.Rows.Add(id, name);

        return new SqlResultSession
        {
            Id = Guid.NewGuid(),
            SqlText = sql,
            ConnectionId = "conn-1",
            DatabaseName = "app_db",
            SchemaName = "public",
            ExecutedAt = executedAt,
            ExecutionTime = TimeSpan.FromMilliseconds(20),
            Status = SqlResultSessionStatus.Success,
            ResultSet = new SqlEditorResultSet
            {
                StatementSql = sql,
                Success = true,
                Data = table,
                ErrorMessage = null,
                RowsAffected = 1,
                ExecutionTime = TimeSpan.FromMilliseconds(20),
                ExecutedAt = executedAt,
            },
            ViewState = new SqlResultViewState(),
        };
    }
}
