using System.Data;

namespace AkkornStudio.UI.ViewModels;

public sealed class SqlEditorResultSet
{
    public required string StatementSql { get; init; }
    public bool Success { get; init; }
    public DataTable? Data { get; init; }
    public string? ErrorMessage { get; init; }
    public SqlExecutionErrorCategory ErrorCategory { get; init; } = SqlExecutionErrorCategory.None;
    public long? RowsAffected { get; init; }
    public TimeSpan ExecutionTime { get; init; }
    public DateTimeOffset ExecutedAt { get; init; }
}
