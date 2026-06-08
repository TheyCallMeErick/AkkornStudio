using AkkornStudio.UI.Services.Observability;

namespace AkkornStudio.Tests.Unit.Services.Observability;

public sealed class CriticalFlowRegressionAlertServiceTests
{
    [Fact]
    public void Evaluate_WhenNoRegression_ReturnsNoAlert()
    {
        var sut = new CriticalFlowRegressionAlertService();
        CriticalFlowBaselineReport previous = CreateReport(successPercent: 95, failedEvents: 2, cf01: 95, cf02: 95);
        CriticalFlowBaselineReport current = CreateReport(successPercent: 94, failedEvents: 2, cf01: 94, cf02: 94);

        CriticalFlowRegressionAlert alert = sut.Evaluate(current, previous);

        Assert.False(alert.HasRegression);
        Assert.Empty(alert.Issues);
    }

    [Fact]
    public void Evaluate_WhenSuccessDropsAndFailuresGrow_RaisesAlert()
    {
        var sut = new CriticalFlowRegressionAlertService();
        CriticalFlowBaselineReport previous = CreateReport(successPercent: 98, failedEvents: 2, cf01: 99, cf02: 97);
        CriticalFlowBaselineReport current = CreateReport(successPercent: 88, failedEvents: 4, cf01: 85, cf02: 91);

        CriticalFlowRegressionAlert alert = sut.Evaluate(current, previous);

        Assert.True(alert.HasRegression);
        Assert.True(alert.Issues.Count >= 2);
        Assert.Contains(alert.Issues, issue => issue.Contains("Critical flow success dropped", StringComparison.Ordinal));
        Assert.Contains(alert.Issues, issue => issue.Contains("Failed events grew", StringComparison.Ordinal));
        Assert.Contains(alert.Issues, issue => issue.Contains("Flow CF-01-open-app-load-project dropped", StringComparison.Ordinal));
    }

    private static CriticalFlowBaselineReport CreateReport(double successPercent, int failedEvents, double cf01, double cf02)
    {
        return new CriticalFlowBaselineReport(
            StartDate: new DateOnly(2026, 5, 1),
            EndDate: new DateOnly(2026, 5, 7),
            TotalEvents: 100,
            TotalSessions: 10,
            SuccessfulEvents: (int)Math.Round(successPercent, MidpointRounding.AwayFromZero),
            CancelledEvents: 0,
            FailedEvents: failedEvents,
            EventsByFlow: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["CF-01-open-app-load-project"] = 50,
                ["CF-02-navigate-shell"] = 50,
            },
            EventsByStep: new Dictionary<string, int>(),
            EventsByOutcome: new Dictionary<string, int>(),
            SuccessByFlow:
            [
                new CriticalFlowSuccessMetric("CF-01-open-app-load-project", 50, (int)Math.Round(cf01 / 2d, MidpointRounding.AwayFromZero), cf01),
                new CriticalFlowSuccessMetric("CF-02-navigate-shell", 50, (int)Math.Round(cf02 / 2d, MidpointRounding.AwayFromZero), cf02),
            ],
            CriticalFlowSuccessPercent: successPercent);
    }
}
