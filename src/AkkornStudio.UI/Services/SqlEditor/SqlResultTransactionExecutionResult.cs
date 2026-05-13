namespace AkkornStudio.UI.Services.SqlEditor;

public sealed record SqlResultTransactionExecutionResult(
    bool Success,
    int ExecutedStatements,
    bool WasCommitted,
    bool WasRolledBack,
    string? ErrorMessage = null,
    TimeSpan? ExecutionTime = null
);
