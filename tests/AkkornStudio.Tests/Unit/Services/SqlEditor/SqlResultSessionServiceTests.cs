using System.Data;
using AkkornStudio.Core;
using AkkornStudio.UI.Services.SqlEditor;
using AkkornStudio.UI.Services.SqlEditor.Results;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.Services.SqlEditor;

public sealed class SqlResultSessionServiceTests
{
    [Fact]
    public void Add_CreatesSession_WithDefaultViewStateFromColumns()
    {
        var sut = new SqlResultSessionService();
        SqlEditorResultSet resultSet = BuildResultSet(
            sql: "select id, name from customers;",
            success: true,
            executedAt: new DateTimeOffset(2026, 05, 12, 9, 0, 0, TimeSpan.Zero),
            columnNames: ["id", "name"]);

        SqlResultSession session = sut.Add(new SqlResultSessionCreateRequest(
            SqlText: resultSet.StatementSql,
            ConnectionId: "conn-1",
            DatabaseName: "app_db",
            SchemaName: "public",
            ResultSet: resultSet));

        Assert.Equal(SqlResultSessionStatus.Success, session.Status);
        Assert.Equal(["id", "name"], session.ViewState.ColumnOrder);
        Assert.Contains("id", session.ViewState.VisibleColumns);
        Assert.Contains("name", session.ViewState.VisibleColumns);
    }

    [Fact]
    public void Add_RespectsMaxHistory_KeepingMostRecentSessions()
    {
        var sut = new SqlResultSessionService(maxHistoryEntries: 2);
        sut.Add(BuildRequest("select 1;", new DateTimeOffset(2026, 05, 12, 9, 0, 0, TimeSpan.Zero)));
        sut.Add(BuildRequest("select 2;", new DateTimeOffset(2026, 05, 12, 9, 1, 0, TimeSpan.Zero)));
        sut.Add(BuildRequest("select 3;", new DateTimeOffset(2026, 05, 12, 9, 2, 0, TimeSpan.Zero)));

        IReadOnlyList<SqlResultSession> sessions = sut.Sessions;
        Assert.Equal(2, sessions.Count);
        Assert.Equal("select 3;", sessions[0].SqlText);
        Assert.Equal("select 2;", sessions[1].SqlText);
    }

    [Fact]
    public void SetPinned_AndSetAnnotation_UpdateSessionState()
    {
        var sut = new SqlResultSessionService();
        SqlResultSession session = sut.Add(BuildRequest("select * from t;", DateTimeOffset.UtcNow));

        bool pinnedUpdated = sut.SetPinned(session.Id, true);
        bool annotationUpdated = sut.SetAnnotation(session.Id, " revisar linhas duplicadas ");

        Assert.True(pinnedUpdated);
        Assert.True(annotationUpdated);
        Assert.True(session.IsPinned);
        Assert.Equal("revisar linhas duplicadas", session.Annotation);
    }

    [Fact]
    public void Add_PreservesInlineEditEligibilityMetadata()
    {
        var sut = new SqlResultSessionService();
        SqlEditorResultSet resultSet = BuildResultSet(
            sql: "select id, name from customers;",
            success: true,
            executedAt: new DateTimeOffset(2026, 05, 12, 9, 0, 0, TimeSpan.Zero),
            columnNames: ["id", "name"]);

        var eligibility = new SqlInlineEditEligibility(
            IsEligible: true,
            TableFullName: "public.customers",
            PrimaryKeyColumns: ["id"],
            EditableColumns: ["name"]);

        SqlResultSession session = sut.Add(new SqlResultSessionCreateRequest(
            SqlText: resultSet.StatementSql,
            ConnectionId: "conn-1",
            DatabaseName: "app_db",
            SchemaName: "public",
            ResultSet: resultSet,
            InlineEditEligibility: eligibility));

        Assert.True(session.InlineEditEligibility.IsEligible);
        Assert.Equal("public.customers", session.InlineEditEligibility.TableFullName);
        Assert.Equal(["id"], session.InlineEditEligibility.PrimaryKeyColumns);
    }

    [Fact]
    public void Add_PreservesProviderMetadata()
    {
        var sut = new SqlResultSessionService();
        SqlEditorResultSet resultSet = BuildResultSet(
            sql: "select id from customers;",
            success: true,
            executedAt: new DateTimeOffset(2026, 05, 12, 9, 0, 0, TimeSpan.Zero),
            columnNames: ["id"]);

        SqlResultSession session = sut.Add(new SqlResultSessionCreateRequest(
            SqlText: resultSet.StatementSql,
            ConnectionId: "conn-1",
            DatabaseName: "app_db",
            SchemaName: "public",
            ResultSet: resultSet,
            Provider: DatabaseProvider.SqlServer));

        Assert.Equal(DatabaseProvider.SqlServer, session.Provider);
    }

    private static SqlResultSessionCreateRequest BuildRequest(string sql, DateTimeOffset executedAt)
    {
        SqlEditorResultSet resultSet = BuildResultSet(sql, true, executedAt, ["value"]);
        return new SqlResultSessionCreateRequest(
            SqlText: sql,
            ConnectionId: "conn-1",
            DatabaseName: "app_db",
            SchemaName: "public",
            ResultSet: resultSet);
    }

    private static SqlEditorResultSet BuildResultSet(
        string sql,
        bool success,
        DateTimeOffset executedAt,
        IReadOnlyList<string> columnNames)
    {
        var table = new DataTable();
        foreach (string columnName in columnNames)
            table.Columns.Add(columnName, typeof(string));

        return new SqlEditorResultSet
        {
            StatementSql = sql,
            Success = success,
            Data = table,
            ErrorMessage = null,
            RowsAffected = 0,
            ExecutionTime = TimeSpan.FromMilliseconds(42),
            ExecutedAt = executedAt,
        };
    }
}
