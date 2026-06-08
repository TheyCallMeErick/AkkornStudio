namespace AkkornStudio.UI.Services.Observability;

public sealed record CriticalFlowEvent(
    string SessionId,
    DateTimeOffset TimestampUtc,
    string FlowId,
    string Step,
    string Outcome,
    IReadOnlyDictionary<string, object?> Properties);
