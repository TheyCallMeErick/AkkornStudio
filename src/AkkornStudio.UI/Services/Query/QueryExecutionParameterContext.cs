namespace AkkornStudio.UI.Services;

public sealed record QueryExecutionParameterContext(
    string? BindingLabel = null,
    string? SourceReference = null,
    string? TableRef = null,
    string? ColumnName = null,
    string? ContextLabel = null,
    string? ExpressionKind = null,
    int SourceCount = 0,
    IReadOnlyList<string>? SourceReferences = null);
