using System.Data;

namespace AkkornStudio.UI.Services.SqlEditor.Results;

public sealed class SqlResultSessionService
{
    private readonly List<SqlResultSession> _sessions = [];
    private readonly int _maxHistoryEntries;

    public SqlResultSessionService(int maxHistoryEntries = 50)
    {
        _maxHistoryEntries = Math.Max(1, maxHistoryEntries);
    }

    public IReadOnlyList<SqlResultSession> Sessions =>
        _sessions
            .OrderByDescending(session => session.ExecutedAt)
            .ToList();

    public SqlResultSession Add(SqlResultSessionCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var session = new SqlResultSession
        {
            Id = Guid.NewGuid(),
            SqlText = request.SqlText,
            ConnectionId = request.ConnectionId,
            DatabaseName = request.DatabaseName,
            SchemaName = request.SchemaName,
            ExecutedAt = request.ResultSet.ExecutedAt,
            ExecutionTime = request.ResultSet.ExecutionTime,
            Status = request.ResultSet.Success ? SqlResultSessionStatus.Success : SqlResultSessionStatus.Error,
            ResultSet = request.ResultSet,
            ViewState = BuildDefaultViewState(request.ResultSet.Data),
        };

        _sessions.Add(session);
        TrimHistory();
        return session;
    }

    public SqlResultSession? Get(Guid sessionId) =>
        _sessions.FirstOrDefault(session => session.Id == sessionId);

    public bool Remove(Guid sessionId)
    {
        SqlResultSession? target = Get(sessionId);
        if (target is null)
            return false;

        return _sessions.Remove(target);
    }

    public bool SetPinned(Guid sessionId, bool isPinned)
    {
        SqlResultSession? target = Get(sessionId);
        if (target is null)
            return false;

        target.IsPinned = isPinned;
        return true;
    }

    public bool SetAnnotation(Guid sessionId, string? annotation)
    {
        SqlResultSession? target = Get(sessionId);
        if (target is null)
            return false;

        target.Annotation = string.IsNullOrWhiteSpace(annotation) ? null : annotation.Trim();
        return true;
    }

    private static SqlResultViewState BuildDefaultViewState(DataTable? dataTable)
    {
        var state = new SqlResultViewState();
        if (dataTable is null)
            return state;

        foreach (DataColumn column in dataTable.Columns)
        {
            state.VisibleColumns.Add(column.ColumnName);
            state.ColumnOrder.Add(column.ColumnName);
        }

        return state;
    }

    private void TrimHistory()
    {
        if (_sessions.Count <= _maxHistoryEntries)
            return;

        foreach (SqlResultSession session in _sessions
                     .OrderByDescending(item => item.ExecutedAt)
                     .Skip(_maxHistoryEntries)
                     .ToList())
        {
            _sessions.Remove(session);
        }
    }
}
