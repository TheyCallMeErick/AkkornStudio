using AkkornStudio.Core;
using System.Windows.Input;

namespace AkkornStudio.UI.ViewModels;

public sealed class SqlEditorTabState
{
    public const int CompletionTelemetryWindowSize = 200;
    public const long CompletionTelemetryBudgetMs = 100;

    public required string Id { get; init; }
    public required string FallbackTitle { get; set; }
    public string SqlText { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public bool IsDirty { get; set; }
    public DatabaseProvider Provider { get; set; }
    public string? ConnectionProfileId { get; set; }
    public SqlEditorResultSet? LastResult { get; set; }
    public IReadOnlyList<SqlEditorResultTab> ResultTabs { get; set; } = [];
    public int SelectedResultTabIndex { get; set; } = -1;
    public int ResultTabCounter { get; set; }
    public SqlEditorOutputPane SelectedOutputPane { get; set; } = SqlEditorOutputPane.Results;
    public IReadOnlyList<SqlEditorMessageEntry> OutputMessages { get; set; } = [];
    public HashSet<string> HiddenResultColumns { get; set; } = [];
    public List<string> HiddenResultColumnsHistory { get; set; } = [];
    public HashSet<string> PinnedResultColumns { get; set; } = [];
    public List<string> ResultColumnOrder { get; set; } = [];
    public string ResultGridFilterText { get; set; } = string.Empty;
    public bool IsResultGridFilterHydratedFromSettings { get; set; }
    public string? ResultGridSortColumn { get; set; }
    public bool ResultGridSortAscending { get; set; } = true;
    public IReadOnlyList<SqlEditorHistoryEntry> ExecutionHistory { get; set; } = [];
    public bool IsExecutionHistoryHydratedFromSettings { get; set; }
    public SqlEditorHistoryEntry? SelectedExecutionHistoryEntry { get; set; }
    public SqlEditorExecutionTelemetry ExecutionTelemetry { get; set; } = SqlEditorExecutionTelemetry.Empty();
    public SqlEditorCompletionTelemetryTracker CompletionTelemetryTracker { get; } = new(CompletionTelemetryWindowSize, CompletionTelemetryBudgetMs);
    public SqlEditorCompletionTelemetry CompletionTelemetry { get; set; } = SqlEditorCompletionTelemetry.Empty(CompletionTelemetryBudgetMs);
    public Dictionary<string, string> QueryParameterLastValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public ICommand? CloseCommand { get; set; }
}
