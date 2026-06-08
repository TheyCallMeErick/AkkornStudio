using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Services.SqlEditor;

public sealed class SqlEditorResultStateService
{
    private readonly ILocalizationService _localization;

    public SqlEditorResultStateService(ILocalizationService? localization = null)
    {
        _localization = localization ?? LocalizationService.Instance;
    }

    public void AppendResult(SqlEditorTabState tab, SqlEditorResultSet result)
    {
        tab.LastResult = result;
        List<SqlEditorResultTab> resultTabs = tab.ResultTabs.ToList();
        int resultIndex = tab.ResultTabCounter + 1;
        resultTabs.Add(new SqlEditorResultTab
        {
            Id = $"result-{resultIndex}",
            Title = string.Format(
                L("sqlEditor.result.tabTitle", "Result {0}"),
                resultIndex),
            Result = result,
        });
        tab.ResultTabCounter = resultIndex;
        tab.ResultTabs = resultTabs;
        tab.SelectedResultTabIndex = resultTabs.Count - 1;

        var historyEntry = new SqlEditorHistoryEntry(
            Sql: result.StatementSql,
            Success: result.Success,
            RowsAffected: result.RowsAffected,
            ExecutionTime: result.ExecutionTime,
            ExecutedAt: result.ExecutedAt);

        List<SqlEditorHistoryEntry> history = tab.ExecutionHistory.ToList();
        history.Insert(0, historyEntry);
        if (history.Count > 500)
            history = history.Take(500).ToList();

        tab.ExecutionHistory = history;
    }

    public SqlEditorExecutionTelemetry BuildTelemetry(IReadOnlyList<SqlEditorResultSet> results)
    {
        if (results.Count == 0)
            return new SqlEditorExecutionTelemetry();

        int successCount = results.Count(result => result.Success);
        int failureCount = results.Count - successCount;
        long totalMs = (long)Math.Round(results.Sum(result => result.ExecutionTime.TotalMilliseconds));
        IReadOnlyList<string> errors = results
            .Where(result => !result.Success && !string.IsNullOrWhiteSpace(result.ErrorMessage))
            .Select(result => result.ErrorMessage!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
        IReadOnlyDictionary<string, int> failureByCategory = results
            .Where(result => !result.Success && result.ErrorCategory != SqlExecutionErrorCategory.None)
            .GroupBy(result => result.ErrorCategory.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return new SqlEditorExecutionTelemetry
        {
            StatementCount = results.Count,
            SuccessCount = successCount,
            FailureCount = failureCount,
            TotalDurationMs = totalMs,
            ErrorMessages = errors,
            FailureByCategory = failureByCategory,
        };
    }

    private string L(string key, string fallback)
    {
        string value = _localization[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }
}

