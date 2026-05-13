using System.Data;
using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.UI.Services.SqlEditor;
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
    public void SetSession_UpdatesResultBreadcrumbText()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession("select * from customers;");

        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());

        Assert.Equal("conn-1 > app_db > public > Resultado", sut.ResultBreadcrumbText);
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
    public void SessionAnnotationCommands_SaveAndClearAnnotation()
    {
        var service = new SqlResultSessionService();
        SqlResultSession session = service.Add(new SqlResultSessionCreateRequest(
            SqlText: "select * from customers;",
            ConnectionId: "conn-1",
            DatabaseName: "app_db",
            SchemaName: "public",
            ResultSet: BuildSession("select * from customers;").ResultSet));

        var sut = new SqlResultPageViewModel();
        sut.ConfigureSessionService(service);
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        sut.SessionAnnotationText = "  Investigar outliers de nome  ";

        sut.SaveSessionAnnotationCommand.Execute(null);

        SqlResultSession? saved = service.Get(session.Id);
        Assert.NotNull(saved);
        Assert.Equal("Investigar outliers de nome", saved!.Annotation);
        Assert.Equal("Investigar outliers de nome", sut.SessionAnnotationText);
        Assert.True(sut.HasSessionAnnotation);

        sut.ClearSessionAnnotationCommand.Execute(null);

        SqlResultSession? cleared = service.Get(session.Id);
        Assert.NotNull(cleared);
        Assert.Null(cleared!.Annotation);
        Assert.Equal(string.Empty, sut.SessionAnnotationText);
        Assert.False(sut.HasSessionAnnotation);
    }

    [Fact]
    public void SaveCurrentSqlAsSnippetCommand_PersistsSnippetAndSelectsIt()
    {
        var snippetStore = new InMemorySqlResultSnippetStore();
        var sut = new SqlResultPageViewModel(
            new MutationGuardService(),
            new SqlMutationDiffService(new SqlEditorExecutionService()),
            null,
            null,
            null,
            snippetStore);
        SqlResultSession session = BuildSession("select * from customers;");

        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        sut.SnippetNameInput = "clientes-base";
        sut.SnippetDescriptionInput = "consulta principal";
        sut.SnippetTagsInput = "cliente base";

        sut.SaveCurrentSqlAsSnippetCommand.Execute(null);

        Assert.Single(sut.SavedSnippets);
        SqlSavedQuerySnippet snippet = sut.SavedSnippets[0];
        Assert.Equal("clientes-base", snippet.Name);
        Assert.Equal("consulta principal", snippet.Description);
        Assert.Equal("cliente base", snippet.Tags);
        Assert.Equal("select * from customers;", snippet.SqlText);
        Assert.Equal(snippet.Id, sut.SelectedSnippet?.Id);
    }

    [Fact]
    public void ToggleCurrentSqlFavoriteCommand_TogglesFavoriteState()
    {
        var snippetStore = new InMemorySqlResultSnippetStore();
        var sut = new SqlResultPageViewModel(
            new MutationGuardService(),
            new SqlMutationDiffService(new SqlEditorExecutionService()),
            null,
            null,
            null,
            snippetStore);
        SqlResultSession session = BuildSession("select * from customers;");
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());

        sut.ToggleCurrentSqlFavoriteCommand.Execute(null);
        Assert.True(sut.IsCurrentSqlFavorite);
        Assert.Single(sut.SavedSnippets);
        Assert.True(sut.SavedSnippets[0].IsFavorite);

        sut.ToggleCurrentSqlFavoriteCommand.Execute(null);
        Assert.False(sut.IsCurrentSqlFavorite);
        Assert.Single(sut.SavedSnippets);
        Assert.False(sut.SavedSnippets[0].IsFavorite);
    }

    [Fact]
    public void OpenSelectedSnippetInEditorCommand_AppendsSnippetSqlToEditor()
    {
        var snippetStore = new InMemorySqlResultSnippetStore();
        DateTimeOffset now = new(2026, 05, 13, 12, 0, 0, TimeSpan.Zero);
        snippetStore.Upsert(new SqlSavedQuerySnippet(
            Id: Guid.NewGuid().ToString("N"),
            Name: "saved-query",
            Description: "desc",
            Tags: "tag",
            SqlText: "select 42;",
            ConnectionId: "conn-1",
            DatabaseName: "app_db",
            CreatedAtUtc: now,
            UpdatedAtUtc: now,
            IsFavorite: false));

        var sut = new SqlResultPageViewModel(
            new MutationGuardService(),
            new SqlMutationDiffService(new SqlEditorExecutionService()),
            null,
            null,
            null,
            snippetStore);
        SqlResultSession session = BuildSession("select * from customers;");
        string? appendedSql = null;
        sut.ConfigureSqlAppendToEditor((_, sql) => appendedSql = sql);
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        sut.SelectedSnippet = sut.SavedSnippets.First();

        sut.OpenSelectedSnippetInEditorCommand.Execute(null);

        Assert.Equal("select 42;", appendedSql);
    }

    [Fact]
    public void SendSelectedSqlTemplateToEditorCommand_ForPostgres_UsesLimitSyntax()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select * from customers;",
            inlineEditEligibility: new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "public.customers",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["name"]),
            provider: DatabaseProvider.Postgres);

        string? appendedSql = null;
        sut.ConfigureSqlAppendToEditor((_, sql) => appendedSql = sql);
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        sut.SelectedSqlTemplate = sut.AvailableSqlTemplates.First(template => template.Key == "select_top_100");

        sut.SendSelectedSqlTemplateToEditorCommand.Execute(null);

        Assert.NotNull(appendedSql);
        Assert.Contains("LIMIT 100", appendedSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TOP 100", appendedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM public.customers", appendedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void SendSelectedSqlTemplateToEditorCommand_ForSqlServer_UsesTopSyntax()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select * from customers;",
            inlineEditEligibility: new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "dbo.customers",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["name"]),
            provider: DatabaseProvider.SqlServer);

        string? appendedSql = null;
        sut.ConfigureSqlAppendToEditor((_, sql) => appendedSql = sql);
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        sut.SelectedSqlTemplate = sut.AvailableSqlTemplates.First(template => template.Key == "select_top_100");

        sut.SendSelectedSqlTemplateToEditorCommand.Execute(null);

        Assert.NotNull(appendedSql);
        Assert.Contains("TOP 100", appendedSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LIMIT 100", appendedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM dbo.customers", appendedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void SendTableQuickActionToEditorCommand_ForPostgresStructure_GeneratesInformationSchemaQuery()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select * from customers;",
            inlineEditEligibility: new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "public.customers",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["name"]),
            provider: DatabaseProvider.Postgres);

        string? appendedSql = null;
        sut.ConfigureSqlAppendToEditor((_, sql) => appendedSql = sql);
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        SqlResultPageViewModel.SqlTableQuickActionOption action = sut.AvailableTableQuickActions
            .First(item => item.Key == "table_structure");

        sut.SendTableQuickActionToEditorCommand.Execute(action);

        Assert.NotNull(appendedSql);
        Assert.Contains("information_schema.columns", appendedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("table_schema = 'public'", appendedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("table_name = 'customers'", appendedSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SendTableQuickActionToEditorCommand_ForSqlServerSelect_GeneratesTopQuery()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select * from customers;",
            inlineEditEligibility: new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "dbo.customers",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["name"]),
            provider: DatabaseProvider.SqlServer);

        string? appendedSql = null;
        sut.ConfigureSqlAppendToEditor((_, sql) => appendedSql = sql);
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        SqlResultPageViewModel.SqlTableQuickActionOption action = sut.AvailableTableQuickActions
            .First(item => item.Key == "table_select_basic");

        sut.SendTableQuickActionToEditorCommand.Execute(action);

        Assert.NotNull(appendedSql);
        Assert.Contains("SELECT TOP 100", appendedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM [dbo].[customers]", appendedSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LIMIT 100", appendedSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NavigateSelectedForeignKeyCommand_WhenUniqueForeignKey_AppendsParentLookupSql()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select id, name from customers;",
            inlineEditEligibility: new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "public.customers",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["name"]),
            provider: DatabaseProvider.Postgres);

        string? appendedSql = null;
        sut.ConfigureSqlAppendToEditor((_, sql) => appendedSql = sql);
        sut.ConfigureMetadataResolver(() => BuildMetadata(
            DatabaseProvider.Postgres,
            new ForeignKeyRelation(
                ConstraintName: "fk_customers_account",
                ChildSchema: "public",
                ChildTable: "customers",
                ChildColumn: "id",
                ParentSchema: "public",
                ParentTable: "accounts",
                ParentColumn: "id",
                OnDelete: ReferentialAction.NoAction,
                OnUpdate: ReferentialAction.NoAction)));
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);
        sut.SelectCell(rowsView[0], "id");

        Assert.True(sut.CanNavigateSelectedForeignKey);
        sut.NavigateSelectedForeignKeyCommand.Execute(null);

        Assert.NotNull(appendedSql);
        Assert.Contains("FROM \"public\".\"accounts\"", appendedSql, StringComparison.Ordinal);
        Assert.Contains("WHERE \"id\" = 1", appendedSql, StringComparison.Ordinal);
        Assert.Contains("LIMIT 100", appendedSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NavigateSelectedForeignKeyCommand_WhenFkIsAmbiguous_DoesNotAppendSql()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select id, name from customers;",
            inlineEditEligibility: new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "public.customers",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["name"]),
            provider: DatabaseProvider.Postgres);

        string? appendedSql = null;
        sut.ConfigureSqlAppendToEditor((_, sql) => appendedSql = sql);
        sut.ConfigureMetadataResolver(() => BuildMetadata(
            DatabaseProvider.Postgres,
            new ForeignKeyRelation(
                ConstraintName: "fk_customers_account",
                ChildSchema: "public",
                ChildTable: "customers",
                ChildColumn: "id",
                ParentSchema: "public",
                ParentTable: "accounts",
                ParentColumn: "id",
                OnDelete: ReferentialAction.NoAction,
                OnUpdate: ReferentialAction.NoAction),
            new ForeignKeyRelation(
                ConstraintName: "fk_customers_archive",
                ChildSchema: "public",
                ChildTable: "customers",
                ChildColumn: "id",
                ParentSchema: "public",
                ParentTable: "customers_archive",
                ParentColumn: "legacy_id",
                OnDelete: ReferentialAction.NoAction,
                OnUpdate: ReferentialAction.NoAction)));
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);
        sut.SelectCell(rowsView[0], "id");

        Assert.False(sut.CanNavigateSelectedForeignKey);
        sut.NavigateSelectedForeignKeyCommand.Execute(null);

        Assert.Null(appendedSql);
    }

    [Fact]
    public void NavigateSelectedForeignKeyCommand_WithoutMetadataResolver_DoesNotAppendSql()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select id, name from customers;",
            inlineEditEligibility: new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "public.customers",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["name"]),
            provider: DatabaseProvider.Postgres);

        string? appendedSql = null;
        sut.ConfigureSqlAppendToEditor((_, sql) => appendedSql = sql);
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);
        sut.SelectCell(rowsView[0], "id");

        Assert.False(sut.CanNavigateSelectedForeignKey);
        sut.NavigateSelectedForeignKeyCommand.Execute(null);

        Assert.Null(appendedSql);
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
    public void CloseSessionTabCommand_WhenClosingInactiveSession_KeepsActiveSession()
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

        sut.CloseSessionTabCommand.Execute(first);

        Assert.Null(service.Get(first.Id));
        Assert.NotNull(service.Get(second.Id));
        Assert.Equal(second.Id, sut.SelectedSession?.Id);
        Assert.True(sut.HasSession);
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
    public void ShowSelectedRowDetailsCommand_DisplaysDetailsOnDemandAndAllowsClose()
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

        Assert.False(sut.IsResultDetailVisible);
        Assert.True(sut.CanShowSelectedRowDetails);

        sut.ShowSelectedRowDetailsCommand.Execute(null);
        Assert.True(sut.IsResultDetailVisible);
        Assert.False(sut.CanShowSelectedRowDetails);

        sut.HideSelectedRowDetailsCommand.Execute(null);
        Assert.False(sut.IsResultDetailVisible);
        Assert.True(sut.CanShowSelectedRowDetails);
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
        sut.ShowSelectedRowDetailsCommand.Execute(null);
        Assert.True(sut.IsResultDetailVisible);

        sut.ClearSelectedRowCommand.Execute(null);

        Assert.False(sut.HasSelectedRow);
        Assert.False(sut.IsResultDetailVisible);
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

    [Fact]
    public void SelectCell_UpdatesState_AndCopySelectedCellPublishesClipboardPayload()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select * from customers;",
            rows:
            [
                (1, "alice"),
                (2, "bob"),
            ]);

        string? clipboardPayload = null;
        sut.ClipboardCopyRequested += payload => clipboardPayload = payload;

        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);
        DataRowView firstRow = rowsView[0];

        sut.SelectCell(firstRow, "name");
        sut.CopySelectedCellCommand.Execute(null);

        Assert.True(sut.HasSelectedCell);
        Assert.Equal("R1 · name", sut.SelectedCellSummary);
        Assert.Equal("alice", clipboardPayload);
    }

    [Fact]
    public void GenerateWhereClauseCommand_WhenEligible_BuildsWhereUsingPrimaryKey()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select id, name from customers;",
            rows:
            [
                (1, "alice"),
                (2, "bob"),
            ],
            inlineEditEligibility: new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "public.customers",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["name"]));

        string? clipboardPayload = null;
        sut.ClipboardCopyRequested += payload => clipboardPayload = payload;
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);
        sut.SelectedRowItem = rowsView[1];
        sut.SelectCell(rowsView[1], "name");

        sut.GenerateWhereClauseCommand.Execute(null);

        Assert.Equal("WHERE id = 2", sut.GeneratedWhereClause);
        Assert.Equal("WHERE id = 2", clipboardPayload);
    }

    [Fact]
    public void CopySelectedRowAsMarkdownCommand_FormatsAsMarkdownTable()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select * from customers;",
            rows:
            [
                (1, "alice"),
                (2, "bob"),
            ]);

        string? clipboardPayload = null;
        sut.ClipboardCopyRequested += payload => clipboardPayload = payload;
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);
        sut.SelectedRowItem = rowsView[0];

        sut.CopySelectedRowAsMarkdownCommand.Execute(null);

        Assert.NotNull(clipboardPayload);
        Assert.Contains("| id | name |", clipboardPayload, StringComparison.Ordinal);
        Assert.Contains("| 1 | alice |", clipboardPayload, StringComparison.Ordinal);
    }

    [Fact]
    public void CopyVisibleRowsAsCsvCommand_FormatsVisiblePageAsCsv()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select * from customers;",
            rows:
            [
                (1, "alice"),
                (2, "bob"),
            ]);

        string? clipboardPayload = null;
        sut.ClipboardCopyRequested += payload => clipboardPayload = payload;
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());

        sut.CopyVisibleRowsAsCsvCommand.Execute(null);

        Assert.NotNull(clipboardPayload);
        Assert.Equal("id,name\r\n1,alice\r\n2,bob", clipboardPayload);
    }

    [Fact]
    public void CopyVisibleRowsAsJsonCommand_FormatsVisiblePageAsJsonArray()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select * from customers;",
            rows:
            [
                (1, "alice"),
                (2, "bob"),
            ]);

        string? clipboardPayload = null;
        sut.ClipboardCopyRequested += payload => clipboardPayload = payload;
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());

        sut.CopyVisibleRowsAsJsonCommand.Execute(null);

        Assert.NotNull(clipboardPayload);
        Assert.Contains("\"id\": 1", clipboardPayload, StringComparison.Ordinal);
        Assert.Contains("\"name\": \"alice\"", clipboardPayload, StringComparison.Ordinal);
        Assert.Contains("\"id\": 2", clipboardPayload, StringComparison.Ordinal);
        Assert.Contains("\"name\": \"bob\"", clipboardPayload, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuildReportExportContext_BuildsExpectedContext()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select * from customers;",
            rows:
            [
                (1, "alice"),
                (2, "bob"),
            ]);
        sut.ConfigureConnectionResolver(_ => BuildConnectionConfig());
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());

        bool built = sut.TryBuildReportExportContext(out SqlEditorReportExportContext? context);

        Assert.True(built);
        Assert.NotNull(context);
        Assert.Equal("select * from customers;", context!.Sql);
        Assert.Equal(["id", "name"], context.SchemaColumns);
        Assert.Equal(2, context.ResultRows.Count);
        Assert.Equal("success", context.ExecutionResult.Status);
        Assert.Equal(1, context.ExecutionResult.RowCount);
        Assert.NotNull(context.Connection);
    }

    [Fact]
    public void ExportVisibleRowsAsCsvCommand_RaisesExportRequestWithCsvPayload()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select * from customers;",
            rows:
            [
                (1, "alice"),
                (2, "bob"),
            ],
            inlineEditEligibility: new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "public.customers",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["name"]));

        SqlResultPageViewModel.SqlResultExportRequest? exportRequest = null;
        sut.ExportRequested += request => exportRequest = request;
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());

        sut.ExportVisibleRowsAsCsvCommand.Execute(null);

        Assert.NotNull(exportRequest);
        Assert.Equal("csv", exportRequest!.DefaultExtension);
        Assert.Equal("public-customers.csv", exportRequest.SuggestedFileName);
        Assert.Equal("id,name\r\n1,alice\r\n2,bob", exportRequest.Content);
    }

    [Fact]
    public void ExportVisibleRowsAsJsonCommand_RaisesExportRequestWithJsonPayload()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select * from customers;",
            rows:
            [
                (1, "alice"),
                (2, "bob"),
            ]);

        SqlResultPageViewModel.SqlResultExportRequest? exportRequest = null;
        sut.ExportRequested += request => exportRequest = request;
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());

        sut.ExportVisibleRowsAsJsonCommand.Execute(null);

        Assert.NotNull(exportRequest);
        Assert.Equal("json", exportRequest!.DefaultExtension);
        Assert.Equal("JSON", exportRequest.FileTypeTitle);
        Assert.Contains("\"id\": 1", exportRequest.Content, StringComparison.Ordinal);
        Assert.Contains("\"name\": \"alice\"", exportRequest.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildColumnProfilesAsync_WhenSessionHasData_PopulatesProfileCollection()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select id, name from customers;",
            rows:
            [
                (1, "alice"),
                (2, "bob"),
                (3, "bob"),
            ]);

        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());

        bool profiled = await sut.BuildColumnProfilesAsync();

        Assert.True(profiled);
        Assert.True(sut.HasColumnProfiles);
        Assert.NotEmpty(sut.ColumnProfiles);
        Assert.Contains("generated", sut.ColumnProfileStatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FilterBySelectedCellValueCommand_AddsEqualsFilterAndRestrictsRows()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select * from customers;",
            rows:
            [
                (1, "alice"),
                (2, "bob"),
                (3, "bob"),
            ]);

        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);
        sut.SelectCell(rowsView[0], "name");

        sut.FilterBySelectedCellValueCommand.Execute(null);

        Assert.Single(session.ViewState.Filters);
        Assert.Equal("name", session.ViewState.Filters[0].ColumnName);
        Assert.Equal("equals", session.ViewState.Filters[0].Operation);
        Assert.Equal("alice", session.ViewState.Filters[0].Value);
        Assert.Equal("1", sut.RowCountText);
    }

    [Fact]
    public void TryApplyInlineCellEdit_WhenEligible_CreatesPendingEditAndUpdatesValue()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select id, name from customers;",
            rows:
            [
                (1, "alice"),
                (2, "bob"),
            ],
            inlineEditEligibility: new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "public.customers",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["name"]));

        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);
        DataRowView firstRow = rowsView[0];

        bool edited = sut.TryApplyInlineCellEdit(firstRow, "name", "alice-updated", out string? errorMessage);

        Assert.True(edited);
        Assert.Null(errorMessage);
        Assert.True(sut.HasPendingEdits);
        Assert.Equal(1, sut.PendingEditsCount);
        PendingCellEdit pending = Assert.Single(session.ViewState.PendingEdits);
        Assert.Equal("name", pending.ColumnName);
        Assert.Equal("alice", pending.OriginalValue);
        Assert.Equal("alice-updated", pending.NewValue);
        Assert.Equal(1, Convert.ToInt32(pending.KeyValues["id"]));
        Assert.Equal("alice-updated", Convert.ToString(firstRow["name"]));
    }

    [Fact]
    public void TryApplyInlineCellEdit_WhenRevertedToOriginal_RemovesPendingEdit()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select id, name from customers;",
            rows:
            [
                (1, "alice"),
            ],
            inlineEditEligibility: new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "public.customers",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["name"]));

        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);
        DataRowView row = rowsView[0];

        bool firstEdit = sut.TryApplyInlineCellEdit(row, "name", "alice-updated", out _);
        bool secondEdit = sut.TryApplyInlineCellEdit(row, "name", "alice", out _);

        Assert.True(firstEdit);
        Assert.True(secondEdit);
        Assert.False(sut.HasPendingEdits);
        Assert.Equal(0, sut.PendingEditsCount);
        Assert.Empty(session.ViewState.PendingEdits);
        Assert.Equal("alice", Convert.ToString(row["name"]));
    }

    [Fact]
    public void CancelPendingEditsCommand_RestoresOriginalValuesAndClearsPendingState()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select id, name from customers;",
            rows:
            [
                (1, "alice"),
                (2, "bob"),
            ],
            inlineEditEligibility: new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "public.customers",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["name"]));

        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);

        bool firstEdit = sut.TryApplyInlineCellEdit(rowsView[0], "name", "alice-updated", out _);
        bool secondEdit = sut.TryApplyInlineCellEdit(rowsView[1], "name", "bob-updated", out _);
        Assert.True(firstEdit);
        Assert.True(secondEdit);
        Assert.Equal(2, sut.PendingEditsCount);
        Assert.True(sut.CanCancelPendingEdits);

        sut.CancelPendingEditsCommand.Execute(null);

        Assert.False(sut.HasPendingEdits);
        Assert.Equal(0, sut.PendingEditsCount);
        Assert.False(sut.CanCancelPendingEdits);
        Assert.Empty(session.ViewState.PendingEdits);
        DataView restoredRowsView = Assert.IsType<DataView>(sut.RowsView);
        Assert.Equal("alice", Convert.ToString(restoredRowsView[0]["name"]));
        Assert.Equal("bob", Convert.ToString(restoredRowsView[1]["name"]));
    }

    [Fact]
    public void TryApplyInlineCellEdit_WhenTypeConversionFails_ReturnsErrorAndKeepsOriginalValue()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildNumericEditableSession();
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);
        DataRowView row = rowsView[0];

        bool edited = sut.TryApplyInlineCellEdit(row, "qty", "not-a-number", out string? errorMessage);

        Assert.False(edited);
        Assert.Equal("Invalid value for column 'qty'.", errorMessage);
        Assert.False(sut.HasPendingEdits);
        Assert.Empty(session.ViewState.PendingEdits);
        Assert.Equal(10, Convert.ToInt32(row["qty"]));
    }

    [Fact]
    public void IsCellPending_WhenPendingEditExists_ReturnsTrueForEditedCellOnly()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select id, name from customers;",
            rows:
            [
                (1, "alice"),
                (2, "bob"),
            ],
            inlineEditEligibility: new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "public.customers",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["name"]));

        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);

        bool edited = sut.TryApplyInlineCellEdit(rowsView[0], "name", "alice-updated", out _);

        Assert.True(edited);
        Assert.True(sut.IsCellPending(rowsView[0], "name"));
        Assert.False(sut.IsCellPending(rowsView[1], "name"));
        Assert.False(sut.IsCellPending(rowsView[0], "id"));
    }

    [Fact]
    public void IsCellPending_AfterCancelPendingEdits_ReturnsFalse()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select id, name from customers;",
            rows:
            [
                (1, "alice"),
            ],
            inlineEditEligibility: new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "public.customers",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["name"]));

        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);
        bool edited = sut.TryApplyInlineCellEdit(rowsView[0], "name", "alice-updated", out _);
        Assert.True(edited);
        Assert.True(sut.IsCellPending(rowsView[0], "name"));

        sut.CancelPendingEditsCommand.Execute(null);

        DataView restoredRowsView = Assert.IsType<DataView>(sut.RowsView);
        Assert.False(sut.IsCellPending(restoredRowsView[0], "name"));
    }

    [Fact]
    public void GeneratePendingSqlCommand_WhenHasPendingEdits_BuildsProviderAwareSql()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select id, name from customers;",
            rows:
            [
                (1, "alice"),
            ],
            inlineEditEligibility: new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "dbo.customers",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["name"]),
            provider: DatabaseProvider.SqlServer);
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);
        Assert.True(sut.TryApplyInlineCellEdit(rowsView[0], "name", "alice-updated", out _));

        sut.GeneratePendingSqlCommand.Execute(null);

        Assert.True(sut.HasGeneratedPendingSqlText);
        Assert.Contains("UPDATE [dbo].[customers]", sut.GeneratedPendingSqlText, StringComparison.Ordinal);
        Assert.Contains("[name] = 'alice-updated'", sut.GeneratedPendingSqlText, StringComparison.Ordinal);
        Assert.Contains("WHERE [id] = 1", sut.GeneratedPendingSqlText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreparePendingChangesPreviewAsync_WhenHasPendingEdits_PopulatesPreviewState()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select id, name from customers;",
            rows:
            [
                (1, "alice"),
                (2, "bob"),
            ],
            inlineEditEligibility: new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "public.customers",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["name"]));
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);
        Assert.True(sut.TryApplyInlineCellEdit(rowsView[0], "name", "alice-updated", out _));

        bool prepared = await sut.PreparePendingChangesPreviewAsync();

        Assert.True(prepared);
        Assert.True(sut.HasPendingChangeSetPreview);
        Assert.Contains("Pending edits: 1 cell(s) in 1 row(s).", sut.PendingChangeSetSummaryText, StringComparison.Ordinal);
        Assert.Contains("name: alice -> alice-updated", sut.PendingChangeSetSummaryText, StringComparison.Ordinal);
        Assert.True(sut.HasGeneratedPendingSqlText);
        Assert.False(string.IsNullOrWhiteSpace(sut.PendingDiffPreviewText));
    }

    [Fact]
    public void CopyGeneratedPendingSqlCommand_RaisesClipboardPayload()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select id, name from customers;",
            rows:
            [
                (1, "alice"),
            ],
            inlineEditEligibility: new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "public.customers",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["name"]));
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);
        Assert.True(sut.TryApplyInlineCellEdit(rowsView[0], "name", "alice-updated", out _));
        sut.GeneratePendingSqlCommand.Execute(null);

        string? copiedText = null;
        sut.ClipboardCopyRequested += text => copiedText = text;
        sut.CopyGeneratedPendingSqlCommand.Execute(null);

        Assert.False(string.IsNullOrWhiteSpace(copiedText));
        Assert.Equal(sut.GeneratedPendingSqlText, copiedText);
    }

    [Fact]
    public void SendGeneratedPendingSqlToEditorCommand_UsesConfiguredCallbackAndSourceDocumentId()
    {
        var sut = new SqlResultPageViewModel();
        Guid sourceDocumentId = Guid.NewGuid();
        Guid? receivedSourceId = null;
        string? receivedSql = null;
        sut.ConfigureSqlAppendToEditor((docId, sql) =>
        {
            receivedSourceId = docId;
            receivedSql = sql;
        });

        SqlResultSession session = BuildSession(
            "select id, name from customers;",
            rows:
            [
                (1, "alice"),
            ],
            inlineEditEligibility: new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "public.customers",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["name"]));
        sut.SetSession(session, sourceDocumentId);
        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);
        Assert.True(sut.TryApplyInlineCellEdit(rowsView[0], "name", "alice-updated", out _));
        sut.GeneratePendingSqlCommand.Execute(null);

        sut.SendGeneratedPendingSqlToEditorCommand.Execute(null);

        Assert.Equal(sourceDocumentId, receivedSourceId);
        Assert.Equal(sut.GeneratedPendingSqlText, receivedSql);
    }

    [Fact]
    public void GeneratePendingSqlCommand_WithMultipleRowEdits_GeneratesMultipleUpdateStatements()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select id, name from customers;",
            rows:
            [
                (1, "alice"),
                (2, "bob"),
            ],
            inlineEditEligibility: new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "public.customers",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["name"]));
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);
        Assert.True(sut.TryApplyInlineCellEdit(rowsView[0], "name", "alice-updated", out _));
        Assert.True(sut.TryApplyInlineCellEdit(rowsView[1], "name", "bob-updated", out _));

        sut.GeneratePendingSqlCommand.Execute(null);

        string generated = sut.GeneratedPendingSqlText;
        Assert.Contains("\"id\" = 1", generated, StringComparison.Ordinal);
        Assert.Contains("\"id\" = 2", generated, StringComparison.Ordinal);
        Assert.Equal(2, generated.Split("UPDATE", StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void RequestExecutePendingChangesCommand_WhenPendingEditsExist_SetsConfirmationWithoutExecuting()
    {
        int executeCalls = 0;
        SqlResultPageViewModel sut = BuildSqlResultPageViewModelForExecutionTests(
            analyzeGuard: _ => MutationGuardResult.Safe(),
            executeStatement: (sql, _, _, _) =>
            {
                executeCalls++;
                return Task.FromResult(BuildSuccessResult(sql ?? string.Empty));
            });

        SqlResultSession session = BuildSession(
            "select id, name from customers;",
            rows:
            [
                (1, "alice"),
            ],
            inlineEditEligibility: new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "public.customers",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["name"]));
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        sut.ConfigureConnectionResolver(_ => BuildConnectionConfig());

        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);
        Assert.True(sut.TryApplyInlineCellEdit(rowsView[0], "name", "alice-updated", out _));

        sut.RequestExecutePendingChangesCommand.Execute(null);

        Assert.True(sut.IsPendingExecutionConfirmationVisible);
        Assert.Contains("requires explicit confirmation", sut.PendingExecutionStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, executeCalls);
    }

    [Fact]
    public async Task ConfirmExecutePendingChangesAsync_WhenExecutionSucceeds_ClearsPendingEdits()
    {
        int executeCalls = 0;
        SqlResultPageViewModel sut = BuildSqlResultPageViewModelForExecutionTests(
            analyzeGuard: _ => MutationGuardResult.Safe(),
            executeStatement: (sql, _, _, _) =>
            {
                executeCalls++;
                if ((sql ?? string.Empty).StartsWith("select", StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(BuildSelectResult(sql ?? string.Empty, (1, "alice-updated")));

                return Task.FromResult(BuildSuccessResult(sql ?? string.Empty));
            });

        SqlResultSession session = BuildSession(
            "select id, name from customers;",
            rows:
            [
                (1, "alice"),
            ],
            inlineEditEligibility: new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "public.customers",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["name"]));
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        sut.ConfigureConnectionResolver(_ => BuildConnectionConfig());

        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);
        Assert.True(sut.TryApplyInlineCellEdit(rowsView[0], "name", "alice-updated", out _));
        sut.RequestExecutePendingChangesCommand.Execute(null);

        bool confirmed = await sut.ConfirmExecutePendingChangesAsync();

        Assert.True(confirmed);
        Assert.False(sut.HasPendingEdits);
        Assert.False(sut.IsPendingExecutionConfirmationVisible);
        Assert.Contains("and refreshed result set", sut.PendingExecutionStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, executeCalls);
        DataView refreshedRows = Assert.IsType<DataView>(sut.RowsView);
        Assert.Equal("alice-updated", Convert.ToString(refreshedRows[0]["name"]));
    }

    [Fact]
    public async Task ConfirmExecutePendingChangesAsync_WhenGuardBlocks_KeepsPendingEditsAndReturnsFalse()
    {
        MutationGuardResult blockingGuard = new()
        {
            IsSafe = false,
            RequiresConfirmation = true,
            Issues = [new MutationGuardIssue(MutationGuardSeverity.Critical, "NO_WHERE", "blocked-by-guard", "fix-where")],
            CountQuery = null,
            SupportsDiff = false,
        };

        SqlResultPageViewModel sut = BuildSqlResultPageViewModelForExecutionTests(
            analyzeGuard: _ => blockingGuard,
            executeStatement: (sql, _, _, _) => Task.FromResult(BuildSuccessResult(sql ?? string.Empty)));

        SqlResultSession session = BuildSession(
            "select id, name from customers;",
            rows:
            [
                (1, "alice"),
            ],
            inlineEditEligibility: new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "public.customers",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["name"]));
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        sut.ConfigureConnectionResolver(_ => BuildConnectionConfig());

        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);
        Assert.True(sut.TryApplyInlineCellEdit(rowsView[0], "name", "alice-updated", out _));
        sut.RequestExecutePendingChangesCommand.Execute(null);

        bool confirmed = await sut.ConfirmExecutePendingChangesAsync();

        Assert.False(confirmed);
        Assert.True(sut.HasPendingEdits);
        Assert.True(sut.IsPendingExecutionConfirmationVisible);
        Assert.True(sut.HasPendingExecutionError);
        Assert.Contains("blocked-by-guard", sut.PendingExecutionStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfirmExecutePendingChangesAsync_WhenTransactionModeCommitSelected_UsesTransactionalExecutor()
    {
        int refreshExecuteCalls = 0;
        int transactionExecuteCalls = 0;
        bool? commitSelection = null;

        SqlResultPageViewModel sut = BuildSqlResultPageViewModelForExecutionTests(
            analyzeGuard: _ => MutationGuardResult.Safe(),
            executeStatement: (sql, _, _, _) =>
            {
                refreshExecuteCalls++;
                return Task.FromResult(BuildSelectResult(sql ?? string.Empty, (1, "alice-updated")));
            },
            executeTransactionalStatement: (_, statements, commitChanges, _) =>
            {
                transactionExecuteCalls++;
                commitSelection = commitChanges;
                return Task.FromResult(new SqlResultTransactionExecutionResult(
                    Success: true,
                    ExecutedStatements: statements.Count,
                    WasCommitted: commitChanges,
                    WasRolledBack: !commitChanges));
            });

        SqlResultSession session = BuildSession(
            "select id, name from customers;",
            rows:
            [
                (1, "alice"),
            ],
            inlineEditEligibility: new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "public.customers",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["name"]));
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        sut.ConfigureConnectionResolver(_ => BuildConnectionConfig());

        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);
        Assert.True(sut.TryApplyInlineCellEdit(rowsView[0], "name", "alice-updated", out _));
        sut.UseTransactionalExecution = true;
        sut.RequestExecutePendingChangesCommand.Execute(null);

        bool confirmed = await sut.ConfirmExecutePendingChangesAsync();

        Assert.True(confirmed);
        Assert.Equal(1, transactionExecuteCalls);
        Assert.Equal(true, commitSelection);
        Assert.Equal(1, refreshExecuteCalls);
        Assert.False(sut.HasPendingEdits);
        Assert.Contains("Committed", sut.PendingExecutionStatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfirmExecutePendingChangesWithRollbackAsync_WhenTransactionModeSelected_KeepsPendingEdits()
    {
        int refreshExecuteCalls = 0;
        int transactionExecuteCalls = 0;
        bool? commitSelection = null;

        SqlResultPageViewModel sut = BuildSqlResultPageViewModelForExecutionTests(
            analyzeGuard: _ => MutationGuardResult.Safe(),
            executeStatement: (sql, _, _, _) =>
            {
                refreshExecuteCalls++;
                return Task.FromResult(BuildSelectResult(sql ?? string.Empty, (1, "alice-updated")));
            },
            executeTransactionalStatement: (_, statements, commitChanges, _) =>
            {
                transactionExecuteCalls++;
                commitSelection = commitChanges;
                return Task.FromResult(new SqlResultTransactionExecutionResult(
                    Success: true,
                    ExecutedStatements: statements.Count,
                    WasCommitted: commitChanges,
                    WasRolledBack: !commitChanges));
            });

        SqlResultSession session = BuildSession(
            "select id, name from customers;",
            rows:
            [
                (1, "alice"),
            ],
            inlineEditEligibility: new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "public.customers",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["name"]));
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        sut.ConfigureConnectionResolver(_ => BuildConnectionConfig());

        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);
        Assert.True(sut.TryApplyInlineCellEdit(rowsView[0], "name", "alice-updated", out _));
        sut.UseTransactionalExecution = true;
        sut.RequestExecutePendingChangesCommand.Execute(null);

        bool confirmed = await sut.ConfirmExecutePendingChangesWithRollbackAsync();

        Assert.True(confirmed);
        Assert.Equal(1, transactionExecuteCalls);
        Assert.Equal(false, commitSelection);
        Assert.Equal(0, refreshExecuteCalls);
        Assert.True(sut.HasPendingEdits);
        Assert.Contains("rolled back", sut.PendingExecutionStatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfirmExecutePendingChangesAsync_WhenTransactionExecutionFails_ReturnsFalseAndKeepsPendingEdits()
    {
        int transactionExecuteCalls = 0;
        SqlResultPageViewModel sut = BuildSqlResultPageViewModelForExecutionTests(
            analyzeGuard: _ => MutationGuardResult.Safe(),
            executeStatement: (sql, _, _, _) => Task.FromResult(BuildSuccessResult(sql ?? string.Empty)),
            executeTransactionalStatement: (_, _, _, _) =>
            {
                transactionExecuteCalls++;
                return Task.FromResult(new SqlResultTransactionExecutionResult(
                    Success: false,
                    ExecutedStatements: 0,
                    WasCommitted: false,
                    WasRolledBack: true,
                    ErrorMessage: "tx-failure"));
            });

        SqlResultSession session = BuildSession(
            "select id, name from customers;",
            rows:
            [
                (1, "alice"),
            ],
            inlineEditEligibility: new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "public.customers",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["name"]));
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        sut.ConfigureConnectionResolver(_ => BuildConnectionConfig());

        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);
        Assert.True(sut.TryApplyInlineCellEdit(rowsView[0], "name", "alice-updated", out _));
        sut.UseTransactionalExecution = true;
        sut.RequestExecutePendingChangesCommand.Execute(null);

        bool confirmed = await sut.ConfirmExecutePendingChangesAsync();

        Assert.False(confirmed);
        Assert.Equal(1, transactionExecuteCalls);
        Assert.True(sut.HasPendingEdits);
        Assert.True(sut.IsPendingExecutionConfirmationVisible);
        Assert.True(sut.HasPendingExecutionError);
        Assert.Contains("tx-failure", sut.PendingExecutionStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void SetSession_WhenProviderDoesNotSupportTransaction_ExposesUnavailableState()
    {
        var sut = new SqlResultPageViewModel();
        SqlResultSession session = BuildSession(
            "select id, name from customers;",
            rows:
            [
                (1, "alice"),
            ],
            inlineEditEligibility: new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "public.customers",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["name"]),
            provider: (DatabaseProvider)999);

        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        sut.UseTransactionalExecution = true;

        Assert.False(sut.IsTransactionModeAvailable);
        Assert.False(sut.UseTransactionalExecution);
        Assert.True(sut.HasTransactionModeStatusText);
        Assert.Contains("unavailable", sut.TransactionModeStatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequestExecutePendingChangesCommand_WhenProductionLikeContext_BlocksExecution()
    {
        SqlResultPageViewModel sut = BuildSqlResultPageViewModelForExecutionTests(
            analyzeGuard: _ => MutationGuardResult.Safe(),
            executeStatement: (sql, _, _, _) => Task.FromResult(BuildSuccessResult(sql ?? string.Empty)));

        SqlResultSession session = BuildSession(
            "select id, name from customers;",
            rows:
            [
                (1, "alice"),
            ],
            inlineEditEligibility: new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "public.customers",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["name"]));
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        sut.ConfigureConnectionResolver(_ => BuildConnectionConfig(database: "production_main"));

        DataView rowsView = Assert.IsType<DataView>(sut.RowsView);
        Assert.True(sut.TryApplyInlineCellEdit(rowsView[0], "name", "alice-updated", out _));

        sut.RequestExecutePendingChangesCommand.Execute(null);

        Assert.False(sut.IsPendingExecutionConfirmationVisible);
        Assert.True(sut.HasPendingExecutionError);
        Assert.Contains("blocked for production-like", sut.PendingExecutionStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.False(sut.CanRequestExecutePendingChanges);
    }

    [Fact]
    public async Task RefreshCurrentSessionAsync_WhenSuccessful_ReexecutesOriginalSqlAndUpdatesRows()
    {
        int executeCalls = 0;
        string? executedSql = null;
        SqlResultPageViewModel sut = BuildSqlResultPageViewModelForExecutionTests(
            analyzeGuard: _ => MutationGuardResult.Safe(),
            executeStatement: (sql, _, _, _) =>
            {
                executeCalls++;
                executedSql = sql;
                return Task.FromResult(BuildSelectResult(sql ?? string.Empty, (1, "after-refresh")));
            });

        SqlResultSession session = BuildSession(
            "select id, name from customers where id = 1;",
            rows:
            [
                (1, "before-refresh"),
            ],
            inlineEditEligibility: new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "public.customers",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["name"]));
        sut.SetSession(session, sourceSqlEditorDocumentId: Guid.NewGuid());
        sut.ConfigureConnectionResolver(_ => BuildConnectionConfig());

        bool refreshed = await sut.RefreshCurrentSessionAsync();

        Assert.True(refreshed);
        Assert.Equal(1, executeCalls);
        Assert.Equal(session.SqlText, executedSql);
        Assert.Contains("refreshed successfully", sut.PendingExecutionStatusText, StringComparison.OrdinalIgnoreCase);
        DataView rows = Assert.IsType<DataView>(sut.RowsView);
        Assert.Equal("after-refresh", Convert.ToString(rows[0]["name"]));
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

    private static DbMetadata BuildMetadata(DatabaseProvider provider, params ForeignKeyRelation[] foreignKeys)
    {
        return new DbMetadata(
            DatabaseName: "app_db",
            Provider: provider,
            ServerVersion: "test",
            CapturedAt: new DateTimeOffset(2026, 05, 12, 12, 0, 0, TimeSpan.Zero),
            Schemas: [],
            AllForeignKeys: foreignKeys);
    }

    private static SqlResultSession BuildSession(
        string sql,
        DateTimeOffset? executedAtOverride = null,
        IReadOnlyList<(int Id, string Name)>? rows = null,
        SqlInlineEditEligibility? inlineEditEligibility = null,
        DatabaseProvider provider = DatabaseProvider.Postgres)
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
            Provider = provider,
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
            InlineEditEligibility = inlineEditEligibility ?? SqlInlineEditEligibility.NotEligible,
            ViewState = new SqlResultViewState(),
        };
    }

    private static SqlResultSession BuildNumericEditableSession()
    {
        DateTimeOffset executedAt = new(2026, 05, 12, 12, 0, 0, TimeSpan.Zero);
        var table = new DataTable();
        table.Columns.Add("id", typeof(int));
        table.Columns.Add("qty", typeof(int));
        table.Rows.Add(1, 10);

        return new SqlResultSession
        {
            Id = Guid.NewGuid(),
            SqlText = "select id, qty from inventory;",
            ConnectionId = "conn-1",
            DatabaseName = "app_db",
            SchemaName = "public",
            ExecutedAt = executedAt,
            ExecutionTime = TimeSpan.FromMilliseconds(20),
            Status = SqlResultSessionStatus.Success,
            ResultSet = new SqlEditorResultSet
            {
                StatementSql = "select id, qty from inventory;",
                Success = true,
                Data = table,
                ErrorMessage = null,
                RowsAffected = 1,
                ExecutionTime = TimeSpan.FromMilliseconds(20),
                ExecutedAt = executedAt,
            },
            InlineEditEligibility = new SqlInlineEditEligibility(
                IsEligible: true,
                TableFullName: "public.inventory",
                PrimaryKeyColumns: ["id"],
                EditableColumns: ["qty"]),
            ViewState = new SqlResultViewState(),
        };
    }

    private static SqlResultPageViewModel BuildSqlResultPageViewModelForExecutionTests(
        Func<string?, MutationGuardResult> analyzeGuard,
        Func<string?, ConnectionConfig?, int, CancellationToken, Task<SqlEditorResultSet>> executeStatement,
        Func<ConnectionConfig, IReadOnlyList<string>, bool, CancellationToken, Task<SqlResultTransactionExecutionResult>>? executeTransactionalStatement = null)
    {
        var guardService = new MutationGuardService();
        var diffService = new SqlMutationDiffService((sql, _, _, _) =>
        {
            var table = new DataTable();
            table.Columns.Add("count", typeof(long));
            table.Rows.Add(1L);
            return Task.FromResult(new SqlEditorResultSet
            {
                StatementSql = sql,
                Success = true,
                Data = table,
                ExecutionTime = TimeSpan.FromMilliseconds(1),
                ExecutedAt = DateTimeOffset.UtcNow,
            });
        });
        var orchestrator = new SqlEditorMutationExecutionOrchestrator(
            executeStatement,
            analyzeGuard,
            (_, _, _, _, _) => Task.FromResult(SqlMutationDiffPreview.Unavailable("none")));
        executeTransactionalStatement ??= (_, statements, commitChanges, _) =>
            Task.FromResult(new SqlResultTransactionExecutionResult(
                Success: true,
                ExecutedStatements: statements.Count,
                WasCommitted: commitChanges,
                WasRolledBack: !commitChanges));
        return new SqlResultPageViewModel(
            guardService,
            diffService,
            orchestrator,
            executeStatement,
            executeTransactionalStatement);
    }

    private sealed class InMemorySqlResultSnippetStore : ISqlResultSnippetStore
    {
        private readonly List<SqlSavedQuerySnippet> _items = [];

        public IReadOnlyList<SqlSavedQuerySnippet> Load() =>
            _items
                .OrderByDescending(item => item.UpdatedAtUtc)
                .ToList();

        public void Upsert(SqlSavedQuerySnippet snippet)
        {
            int index = _items.FindIndex(item => string.Equals(item.Id, snippet.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                _items[index] = snippet;
            else
                _items.Add(snippet);
        }

        public bool Delete(string snippetId)
        {
            int removed = _items.RemoveAll(item => string.Equals(item.Id, snippetId, StringComparison.OrdinalIgnoreCase));
            return removed > 0;
        }
    }

    private static ConnectionConfig BuildConnectionConfig(string database = "app_db")
    {
        return new ConnectionConfig(
            Provider: DatabaseProvider.Postgres,
            Host: "localhost",
            Port: 5432,
            Database: database,
            Username: "postgres",
            Password: "postgres");
    }

    private static SqlEditorResultSet BuildSuccessResult(string sql)
    {
        return new SqlEditorResultSet
        {
            StatementSql = sql,
            Success = true,
            RowsAffected = 1,
            ExecutionTime = TimeSpan.FromMilliseconds(1),
            ExecutedAt = DateTimeOffset.UtcNow,
        };
    }

    private static SqlEditorResultSet BuildSelectResult(string sql, params (int Id, string Name)[] rows)
    {
        var table = new DataTable();
        table.Columns.Add("id", typeof(int));
        table.Columns.Add("name", typeof(string));
        foreach ((int id, string name) in rows)
            table.Rows.Add(id, name);

        return new SqlEditorResultSet
        {
            StatementSql = sql,
            Success = true,
            Data = table,
            RowsAffected = null,
            ExecutionTime = TimeSpan.FromMilliseconds(3),
            ExecutedAt = DateTimeOffset.UtcNow,
        };
    }
}
