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

    private static SqlResultSession BuildSession(string sql)
    {
        var table = new DataTable();
        table.Columns.Add("id", typeof(int));
        table.Columns.Add("name", typeof(string));
        table.Rows.Add(1, "alice");

        return new SqlResultSession
        {
            Id = Guid.NewGuid(),
            SqlText = sql,
            ConnectionId = "conn-1",
            DatabaseName = "app_db",
            SchemaName = "public",
            ExecutedAt = new DateTimeOffset(2026, 05, 12, 12, 0, 0, TimeSpan.Zero),
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
                ExecutedAt = new DateTimeOffset(2026, 05, 12, 12, 0, 0, TimeSpan.Zero),
            },
            ViewState = new SqlResultViewState(),
        };
    }
}
