using System.Data;
using AkkornStudio.UI.Services.SqlEditor;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.Services.SqlEditor;

public sealed class SqlEditorResultStateServiceTests
{
    [Fact]
    public void AppendResult_AddsResultTabAndHistory()
    {
        var sut = new SqlEditorResultStateService();
        var tab = new SqlEditorTabState { Id = "tab-1", FallbackTitle = "Script 1" };
        SqlEditorResultSet result = BuildResult("SELECT 1", true, 2, 3);

        sut.AppendResult(tab, result);

        Assert.Same(result, tab.LastResult);
        Assert.Single(tab.ResultTabs);
        Assert.Contains(tab.ResultTabs[0].Title, new[] { "Resultado 1", "Result 1" });
        Assert.Single(tab.ExecutionHistory);
        Assert.Equal("SELECT 1", tab.ExecutionHistory[0].Sql);
    }

    [Fact]
    public void AppendResult_CapsHistoryAtFiveHundred()
    {
        var sut = new SqlEditorResultStateService();
        var tab = new SqlEditorTabState { Id = "tab-1", FallbackTitle = "Script 1" };

        for (int i = 0; i < 700; i++)
            sut.AppendResult(tab, BuildResult($"SELECT {i}", true, 1, 1));

        Assert.Equal(500, tab.ExecutionHistory.Count);
    }

    [Fact]
    public void BuildTelemetry_WhenEmpty_ReturnsZeroed()
    {
        var sut = new SqlEditorResultStateService();

        SqlEditorExecutionTelemetry telemetry = sut.BuildTelemetry([]);

        Assert.Equal(0, telemetry.StatementCount);
        Assert.Equal(0, telemetry.SuccessCount);
        Assert.Equal(0, telemetry.FailureCount);
        Assert.Empty(telemetry.FailureByCategory);
    }

    [Fact]
    public void BuildTelemetry_AggregatesErrorsDistinctAndDuration()
    {
        var sut = new SqlEditorResultStateService();
        IReadOnlyList<SqlEditorResultSet> results =
        [
            BuildResult("SELECT 1", true, 1, 2),
            BuildResult("SELECT x", false, null, 4, "bad"),
            BuildResult("SELECT y", false, null, 3, "bad", SqlExecutionErrorCategory.Operational),
            BuildResult("SELECT z", false, null, 1, "worse", SqlExecutionErrorCategory.Timeout),
        ];

        SqlEditorExecutionTelemetry telemetry = sut.BuildTelemetry(results);

        Assert.Equal(4, telemetry.StatementCount);
        Assert.Equal(1, telemetry.SuccessCount);
        Assert.Equal(3, telemetry.FailureCount);
        Assert.Equal(10, telemetry.TotalDurationMs);
        Assert.Equal(2, telemetry.ErrorMessages.Count);
        Assert.Equal(2, telemetry.FailureByCategory["Operational"]);
        Assert.Equal(1, telemetry.FailureByCategory["Timeout"]);
    }

    private static SqlEditorResultSet BuildResult(
        string sql,
        bool success,
        long? rows,
        long ms,
        string? error = null,
        SqlExecutionErrorCategory errorCategory = SqlExecutionErrorCategory.None)
    {
        DataTable? data = null;
        if (success)
        {
            data = new DataTable();
            data.Columns.Add("id", typeof(int));
            data.Rows.Add(1);
        }

        return new SqlEditorResultSet
        {
            StatementSql = sql,
            Success = success,
            RowsAffected = rows,
            ExecutionTime = TimeSpan.FromMilliseconds(ms),
            Data = data,
            ErrorMessage = error,
            ErrorCategory = errorCategory == SqlExecutionErrorCategory.None && !success
                ? SqlExecutionErrorCategory.Operational
                : errorCategory,
            ExecutedAt = DateTimeOffset.UtcNow,
        };
    }
}
