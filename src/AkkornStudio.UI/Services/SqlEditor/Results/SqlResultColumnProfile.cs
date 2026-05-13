namespace AkkornStudio.UI.Services.SqlEditor.Results;

public sealed record SqlResultColumnProfile(
    string ColumnName,
    SqlResultColumnProfileKind Kind,
    int RowCount,
    int NullCount,
    int EmptyCount,
    int DistinctCount,
    string TopValuesSummary,
    double? NumericMin,
    double? NumericMax,
    double? NumericAverage,
    DateTimeOffset? TemporalMin,
    DateTimeOffset? TemporalMax,
    int SuspectFutureValueCount
);
