using System;

namespace AkkornStudio.UI.ViewModels;

public sealed class SqlEditorExecutionTelemetry
{
    public int StatementCount { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public long TotalDurationMs { get; init; }
    public IReadOnlyList<string> ErrorMessages { get; init; } = [];
    public IReadOnlyDictionary<string, int> FailureByCategory { get; init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public static SqlEditorExecutionTelemetry Empty() => new()
    {
        StatementCount = 0,
        SuccessCount = 0,
        FailureCount = 0,
        TotalDurationMs = 0,
        ErrorMessages = [],
        FailureByCategory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
    };
}
