using AkkornStudio.UI.Services.Observability;

namespace AkkornStudio.Tests.Unit.Services.Observability;

public sealed class LocalCriticalFlowBaselineReportServiceTests
{
    [Fact]
    public void Build_AggregatesAcrossDateRange()
    {
        string root = Path.Combine(Path.GetTempPath(), "akkornstudio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        File.WriteAllLines(
            Path.Combine(root, "critical-flows-2026-05-04.jsonl"),
            [
                "{\"sessionId\":\"s-1\",\"timestampUtc\":\"2026-05-04T10:00:00Z\",\"flowId\":\"CF-01-open-app-load-project\",\"step\":\"app_bootstrap\",\"outcome\":\"ok\",\"properties\":{}}",
                "{\"sessionId\":\"s-1\",\"timestampUtc\":\"2026-05-04T10:01:00Z\",\"flowId\":\"CF-02-navigate-shell\",\"step\":\"canvas_initialized\",\"outcome\":\"ok\",\"properties\":{}}",
            ]);

        File.WriteAllLines(
            Path.Combine(root, "critical-flows-2026-05-05.jsonl"),
            [
                "{\"sessionId\":\"s-2\",\"timestampUtc\":\"2026-05-05T11:00:00Z\",\"flowId\":\"CF-01-open-app-load-project\",\"step\":\"open_from_disk_picker\",\"outcome\":\"cancelled\",\"properties\":{}}",
                "{\"sessionId\":\"s-2\",\"timestampUtc\":\"2026-05-05T11:01:00Z\",\"flowId\":\"CF-01-open-app-load-project\",\"step\":\"open_from_disk_completed\",\"outcome\":\"failed\",\"properties\":{}}",
            ]);

        var sut = new LocalCriticalFlowBaselineReportService(root);
        CriticalFlowBaselineReport report = sut.Build(new DateOnly(2026, 5, 4), new DateOnly(2026, 5, 5));

        Assert.Equal(4, report.TotalEvents);
        Assert.Equal(2, report.TotalSessions);
        Assert.Equal(2, report.SuccessfulEvents);
        Assert.Equal(1, report.CancelledEvents);
        Assert.Equal(1, report.FailedEvents);
        Assert.Equal(3, report.EventsByFlow["CF-01-open-app-load-project"]);
        Assert.Equal(2, report.SuccessByFlow.Count);
        Assert.Equal(33.33d, report.SuccessByFlow.First(m => m.FlowId == "CF-01-open-app-load-project").SuccessRatePercent);
        Assert.Equal(100d, report.SuccessByFlow.First(m => m.FlowId == "CF-02-navigate-shell").SuccessRatePercent);
        Assert.Equal(50d, report.CriticalFlowSuccessPercent);

        string markdown = report.ToMarkdown();
        Assert.Contains("Total events", markdown, StringComparison.Ordinal);
        Assert.Contains("CF-01-open-app-load-project", markdown, StringComparison.Ordinal);
        Assert.Contains("Critical flow success", markdown, StringComparison.Ordinal);
        Assert.Contains("Critical flow success (%)", markdown, StringComparison.Ordinal);

        string csv = report.ToCsv();
        Assert.Contains("summary,total_events,4", csv, StringComparison.Ordinal);
        Assert.Contains("summary,critical_flow_success_percent,50", csv, StringComparison.Ordinal);
        Assert.Contains("flow,CF-01-open-app-load-project,3", csv, StringComparison.Ordinal);
        Assert.Contains("flow_success,CF-01-open-app-load-project,33.33", csv, StringComparison.Ordinal);

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Build_IgnoresInvalidJsonLines()
    {
        string root = Path.Combine(Path.GetTempPath(), "akkornstudio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        File.WriteAllLines(
            Path.Combine(root, "critical-flows-2026-05-05.jsonl"),
            [
                "not a json line",
                "{\"sessionId\":\"s-1\",\"timestampUtc\":\"2026-05-05T10:00:00Z\",\"flowId\":\"CF-01-open-app-load-project\",\"step\":\"app_bootstrap\",\"outcome\":\"ok\",\"properties\":{}}",
            ]);

        var sut = new LocalCriticalFlowBaselineReportService(root);
        CriticalFlowBaselineReport report = sut.Build(new DateOnly(2026, 5, 5), new DateOnly(2026, 5, 5));

        Assert.Equal(1, report.TotalEvents);
        Assert.Equal(1, report.TotalSessions);
        Assert.Equal(1, report.SuccessfulEvents);
        Assert.Equal(100d, report.SuccessByFlow.First(m => m.FlowId == "CF-01-open-app-load-project").SuccessRatePercent);
        Assert.Equal(0d, report.SuccessByFlow.First(m => m.FlowId == "CF-02-navigate-shell").SuccessRatePercent);
        Assert.Equal(100d, report.CriticalFlowSuccessPercent);

        Directory.Delete(root, recursive: true);
    }
}
