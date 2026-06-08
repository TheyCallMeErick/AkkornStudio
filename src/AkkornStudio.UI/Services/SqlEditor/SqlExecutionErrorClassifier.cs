using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Services.SqlEditor;

public static class SqlExecutionErrorClassifier
{
    public static SqlExecutionErrorCategory Classify(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            return SqlExecutionErrorCategory.None;

        string lowered = errorMessage.ToLowerInvariant();
        if (lowered.Contains("cancel", StringComparison.Ordinal))
            return SqlExecutionErrorCategory.Cancelled;

        if (lowered.Contains("timeout", StringComparison.Ordinal)
            || lowered.Contains("timed out", StringComparison.Ordinal))
        {
            return SqlExecutionErrorCategory.Timeout;
        }

        if (lowered.Contains("read-only", StringComparison.Ordinal)
            || lowered.Contains("single sql statement", StringComparison.Ordinal)
            || lowered.Contains("parameter", StringComparison.Ordinal)
            || lowered.Contains("no sql statement selected", StringComparison.Ordinal))
        {
            return SqlExecutionErrorCategory.Validation;
        }

        if (lowered.Contains("permission", StringComparison.Ordinal)
            || lowered.Contains("denied", StringComparison.Ordinal)
            || lowered.Contains("unauthorized", StringComparison.Ordinal)
            || lowered.Contains("forbidden", StringComparison.Ordinal))
        {
            return SqlExecutionErrorCategory.Security;
        }

        if (lowered.Contains("connection", StringComparison.Ordinal)
            || lowered.Contains("network", StringComparison.Ordinal)
            || lowered.Contains("could not", StringComparison.Ordinal)
            || lowered.Contains("cannot", StringComparison.Ordinal)
            || lowered.Contains("failed", StringComparison.Ordinal)
            || lowered.Contains("error", StringComparison.Ordinal))
        {
            return SqlExecutionErrorCategory.Operational;
        }

        return SqlExecutionErrorCategory.Unexpected;
    }
}
