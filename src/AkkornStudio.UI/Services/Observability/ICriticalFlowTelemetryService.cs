namespace AkkornStudio.UI.Services.Observability;

public interface ICriticalFlowTelemetryService
{
    string SessionId { get; }

    void Track(
        string flowId,
        string step,
        string outcome,
        IReadOnlyDictionary<string, object?>? properties = null);
}
