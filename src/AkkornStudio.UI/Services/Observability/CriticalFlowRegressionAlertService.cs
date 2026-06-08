using System.Globalization;

namespace AkkornStudio.UI.Services.Observability;

public sealed class CriticalFlowRegressionAlertService : ICriticalFlowRegressionAlertService
{
    private const double CriticalFlowSuccessDropThresholdPercentPoints = 5d;
    private const int FailedEventsGrowthThresholdPercent = 30;

    public CriticalFlowRegressionAlert Evaluate(CriticalFlowBaselineReport current, CriticalFlowBaselineReport previous)
    {
        var issues = new List<string>();

        double successDrop = previous.CriticalFlowSuccessPercent - current.CriticalFlowSuccessPercent;
        if (successDrop >= CriticalFlowSuccessDropThresholdPercentPoints)
        {
            issues.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Critical flow success dropped by {0:0.##} pp ({1:0.##}% -> {2:0.##}%).",
                successDrop,
                previous.CriticalFlowSuccessPercent,
                current.CriticalFlowSuccessPercent));
        }

        if (previous.FailedEvents > 0)
        {
            double failedGrowthPercent = ((current.FailedEvents - previous.FailedEvents) * 100d) / previous.FailedEvents;
            if (failedGrowthPercent >= FailedEventsGrowthThresholdPercent)
            {
                issues.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Failed events grew by {0:0.##}% ({1} -> {2}).",
                    failedGrowthPercent,
                    previous.FailedEvents,
                    current.FailedEvents));
            }
        }
        else if (current.FailedEvents > 0)
        {
            issues.Add($"Failed events moved from 0 to {current.FailedEvents}.");
        }

        foreach (CriticalFlowSuccessMetric metric in current.SuccessByFlow)
        {
            CriticalFlowSuccessMetric? previousMetric = previous.SuccessByFlow
                .FirstOrDefault(item => string.Equals(item.FlowId, metric.FlowId, StringComparison.OrdinalIgnoreCase));

            if (previousMetric is null || previousMetric.TotalEvents == 0)
                continue;

            double flowDrop = previousMetric.SuccessRatePercent - metric.SuccessRatePercent;
            if (flowDrop >= CriticalFlowSuccessDropThresholdPercentPoints)
            {
                issues.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Flow {0} dropped by {1:0.##} pp ({2:0.##}% -> {3:0.##}%).",
                    metric.FlowId,
                    flowDrop,
                    previousMetric.SuccessRatePercent,
                    metric.SuccessRatePercent));
            }
        }

        if (issues.Count == 0)
            return new CriticalFlowRegressionAlert(false, "No regression signal detected.", []);

        return new CriticalFlowRegressionAlert(
            true,
            $"Detected {issues.Count} regression signal(s).",
            issues);
    }
}
