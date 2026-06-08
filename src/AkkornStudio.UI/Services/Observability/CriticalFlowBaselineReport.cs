using System.Globalization;

namespace AkkornStudio.UI.Services.Observability;

public sealed record CriticalFlowBaselineReport(
    DateOnly StartDate,
    DateOnly EndDate,
    int TotalEvents,
    int TotalSessions,
    int SuccessfulEvents,
    int CancelledEvents,
    int FailedEvents,
    IReadOnlyDictionary<string, int> EventsByFlow,
    IReadOnlyDictionary<string, int> EventsByStep,
    IReadOnlyDictionary<string, int> EventsByOutcome,
    IReadOnlyList<CriticalFlowSuccessMetric> SuccessByFlow,
    double CriticalFlowSuccessPercent)
{
    public string ToMarkdown()
    {
        var lines = new List<string>
        {
            "# Observability Baseline Report",
            string.Empty,
            $"Period: {StartDate:yyyy-MM-dd} to {EndDate:yyyy-MM-dd}",
            string.Empty,
            "## Summary",
            string.Empty,
            "| Metric | Value |",
            "|---|---:|",
            $"| Total events | {TotalEvents} |",
            $"| Total sessions | {TotalSessions} |",
            $"| Outcome ok | {SuccessfulEvents} |",
            $"| Outcome cancelled | {CancelledEvents} |",
            $"| Outcome error/failed | {FailedEvents} |",
            $"| Critical flow success (%) | {CriticalFlowSuccessPercent.ToString("0.##", CultureInfo.InvariantCulture)} |",
            string.Empty,
            "## Critical flow success",
            string.Empty,
            "| Flow | Success | Total | Success (%) |",
            "|---|---:|---:|---:|",
        };

        foreach (CriticalFlowSuccessMetric metric in SuccessByFlow)
            lines.Add($"| {metric.FlowId} | {metric.SuccessfulEvents} | {metric.TotalEvents} | {metric.SuccessRatePercent.ToString("0.##", CultureInfo.InvariantCulture)} |");

        lines.AddRange(
        [
            string.Empty,
            "## Events by flow",
            string.Empty,
            "| Flow | Events |",
            "|---|---:|",
        ]);

        foreach ((string flow, int count) in EventsByFlow.OrderByDescending(pair => pair.Value))
            lines.Add($"| {flow} | {count} |");

        lines.Add(string.Empty);
        lines.Add("## Events by outcome");
        lines.Add(string.Empty);
        lines.Add("| Outcome | Events |");
        lines.Add("|---|---:|");
        foreach ((string outcome, int count) in EventsByOutcome.OrderByDescending(pair => pair.Value))
            lines.Add($"| {outcome} | {count} |");

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    public string ToCsv()
    {
        var lines = new List<string>
        {
            "scope,key,count",
            $"summary,total_events,{TotalEvents}",
            $"summary,total_sessions,{TotalSessions}",
            $"summary,outcome_ok,{SuccessfulEvents}",
            $"summary,outcome_cancelled,{CancelledEvents}",
            $"summary,outcome_failed,{FailedEvents}",
            $"summary,critical_flow_success_percent,{CriticalFlowSuccessPercent.ToString("0.##", CultureInfo.InvariantCulture)}",
        };

        lines.AddRange(SuccessByFlow.Select(metric =>
            $"flow_success,{EscapeCsv(metric.FlowId)},{metric.SuccessRatePercent.ToString("0.##", CultureInfo.InvariantCulture)}"));

        lines.AddRange(EventsByFlow
            .OrderByDescending(pair => pair.Value)
            .Select(pair => $"flow,{EscapeCsv(pair.Key)},{pair.Value}"));

        lines.AddRange(EventsByStep
            .OrderByDescending(pair => pair.Value)
            .Select(pair => $"step,{EscapeCsv(pair.Key)},{pair.Value}"));

        lines.AddRange(EventsByOutcome
            .OrderByDescending(pair => pair.Value)
            .Select(pair => $"outcome,{EscapeCsv(pair.Key)},{pair.Value}"));

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string EscapeCsv(string raw)
    {
        if (!raw.Contains(',') && !raw.Contains('"') && !raw.Contains('\n'))
            return raw;

        return $"\"{raw.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
