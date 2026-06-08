using AkkornStudio.Core;

namespace AkkornStudio.UI.Services.SqlEditor.Results;

public sealed record SqlResultChangeSet(
    Guid ResultSessionId,
    string ConnectionId,
    DatabaseProvider Provider,
    IReadOnlyList<PendingCellEdit> Edits
);
