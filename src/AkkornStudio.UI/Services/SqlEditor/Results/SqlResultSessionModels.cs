using System.Collections.ObjectModel;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Services.SqlEditor.Results;

public enum SqlResultSessionStatus
{
    Success,
    Error,
}

public sealed class SqlResultSession
{
    public required Guid Id { get; init; }
    public required string SqlText { get; init; }
    public required string ConnectionId { get; init; }
    public string? DatabaseName { get; init; }
    public string? SchemaName { get; init; }
    public required DateTimeOffset ExecutedAt { get; init; }
    public required TimeSpan ExecutionTime { get; init; }
    public required SqlResultSessionStatus Status { get; init; }
    public required SqlEditorResultSet ResultSet { get; init; }
    public required SqlResultViewState ViewState { get; init; }
    public bool IsPinned { get; set; }
    public string? Annotation { get; set; }
}

public sealed class SqlResultViewState
{
    public int PageSize { get; set; } = 100;
    public int CurrentPage { get; set; } = 1;
    public HashSet<string> VisibleColumns { get; set; } = [];
    public List<string> ColumnOrder { get; set; } = [];
    public HashSet<string> FrozenColumns { get; set; } = [];
    public List<SqlColumnFilter> Filters { get; set; } = [];
    public List<SqlColumnSort> Sorts { get; set; } = [];
    public List<string> GroupedColumns { get; set; } = [];
    public CellSelection? SelectedCell { get; set; }
    public int? SelectedRowIndex { get; set; }
    public ObservableCollection<PendingCellEdit> PendingEdits { get; set; } = [];
}

public sealed record SqlColumnFilter(string ColumnName, string Operation, string? Value);

public sealed record SqlColumnSort(string ColumnName, bool Descending);

public sealed record CellSelection(int RowIndex, string ColumnName);

public sealed record PendingCellEdit(
    string TableName,
    string? SchemaName,
    string ColumnName,
    object? OriginalValue,
    object? NewValue,
    IReadOnlyDictionary<string, object?> KeyValues
);

public sealed record SqlResultSessionCreateRequest(
    string SqlText,
    string ConnectionId,
    string? DatabaseName,
    string? SchemaName,
    SqlEditorResultSet ResultSet
);

public sealed class SqlResultPageRequestedEventArgs(SqlResultSessionCreateRequest request) : EventArgs
{
    public SqlResultSessionCreateRequest Request { get; } = request;
}
