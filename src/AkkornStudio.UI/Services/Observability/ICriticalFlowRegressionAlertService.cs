namespace AkkornStudio.UI.Services.Observability;

public interface ICriticalFlowRegressionAlertService
{
    CriticalFlowRegressionAlert Evaluate(CriticalFlowBaselineReport current, CriticalFlowBaselineReport previous);
}
