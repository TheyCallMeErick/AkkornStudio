namespace AkkornStudio.UI.ViewModels;

public sealed record SqlEditorReportSchemaDetail(
    string Name,
    string Kind,
    long NullCount,
    long DistinctCount,
    string? Example,
    string? MinValue,
    string? MaxValue
);
