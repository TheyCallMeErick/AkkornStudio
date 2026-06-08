namespace AkkornStudio.UI.Services.Observability;

public sealed record CriticalFlowSuccessMetric(
    string FlowId,
    int TotalEvents,
    int SuccessfulEvents,
    double SuccessRatePercent);
