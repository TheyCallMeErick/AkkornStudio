using AkkornStudio.Core;
using AkkornStudio;
using AkkornStudio.UI.Services.Localization;

namespace AkkornStudio.UI.Services.SqlEditor;

public sealed class SqlEditorExecutionService
{
    private readonly IDbOrchestratorFactory _orchestratorFactory;
    private readonly ILocalizationService _localization;

    public SqlEditorExecutionService(
        IDbOrchestratorFactory? orchestratorFactory = null,
        ILocalizationService? localization = null)
    {
        _orchestratorFactory = orchestratorFactory ?? DbOrchestratorFactory.CreateDefault();
        _localization = localization ?? LocalizationService.Instance;
    }

    public async Task<SqlEditorResultSet> ExecuteAsync(
        string? sql,
        ConnectionConfig? config,
        int maxRows = 1000,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return new SqlEditorResultSet
            {
                StatementSql = string.Empty,
                Success = false,
                ErrorMessage = L("sqlEditor.error.noStatementSelected", "No SQL statement selected for execution."),
                ErrorCategory = SqlExecutionErrorCategory.Validation,
                ExecutedAt = DateTimeOffset.UtcNow,
            };
        }

        if (config is null)
        {
            return new SqlEditorResultSet
            {
                StatementSql = sql.Trim(),
                Success = false,
                ErrorMessage = L("sqlEditor.error.noConnection", "No active database connection for SQL execution."),
                ErrorCategory = SqlExecutionErrorCategory.Operational,
                ExecutedAt = DateTimeOffset.UtcNow,
            };
        }

        string statementSql = sql.Trim();
        try
        {
            await using IDbOrchestrator orchestrator = _orchestratorFactory.Create(config);
            PreviewResult preview = await orchestrator.ExecutePreviewAsync(statementSql, maxRows, ct);

            return new SqlEditorResultSet
            {
                StatementSql = statementSql,
                Success = preview.Success,
                Data = preview.Data,
                ErrorMessage = preview.ErrorMessage,
                ErrorCategory = preview.Success
                    ? SqlExecutionErrorCategory.None
                    : SqlExecutionErrorClassifier.Classify(preview.ErrorMessage),
                RowsAffected = preview.RowsAffected,
                ExecutionTime = preview.ExecutionTime ?? TimeSpan.Zero,
                ExecutedAt = DateTimeOffset.UtcNow,
            };
        }
        catch (OperationCanceledException)
        {
            return new SqlEditorResultSet
            {
                StatementSql = statementSql,
                Success = false,
                ErrorMessage = L("sqlEditor.error.executionCanceled", "SQL execution was canceled."),
                ErrorCategory = SqlExecutionErrorCategory.Cancelled,
                ExecutedAt = DateTimeOffset.UtcNow,
            };
        }
    }

    private string L(string key, string fallback)
    {
        string value = _localization[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }
}
