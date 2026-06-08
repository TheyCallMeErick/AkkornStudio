using System.Text.Json;

namespace AkkornStudio.UI.Services.Observability;

public sealed class LocalCriticalFlowBaselineReportService : ICriticalFlowBaselineReportService
{
    private static readonly string[] OfficialCriticalFlows =
    [
        "CF-01-open-app-load-project",
        "CF-02-navigate-shell",
    ];

    private const string ProductFolderName = "AkkornStudio";
    private const string TelemetryFolderName = "telemetry";
    private readonly string _logDirectory;

    public LocalCriticalFlowBaselineReportService()
        : this(logDirectory: null)
    {
    }

    public LocalCriticalFlowBaselineReportService(string? logDirectory)
    {
        _logDirectory = string.IsNullOrWhiteSpace(logDirectory)
            ? ResolveDefaultDirectory()
            : logDirectory;
    }

    public CriticalFlowBaselineReport Build(DateOnly startDate, DateOnly endDate)
    {
        if (endDate < startDate)
            throw new ArgumentOutOfRangeException(nameof(endDate), "End date must be greater than or equal to start date.");

        var events = new List<CriticalFlowEvent>();
        foreach (DateOnly day in EnumerateDateRange(startDate, endDate))
        {
            string path = Path.Combine(_logDirectory, $"critical-flows-{day:yyyy-MM-dd}.jsonl");
            if (!File.Exists(path))
                continue;

            foreach (string line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                CriticalFlowEvent? parsed = TryParse(line);
                if (parsed is not null)
                    events.Add(parsed);
            }
        }

        var byFlow = events
            .GroupBy(evt => evt.FlowId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var byStep = events
            .GroupBy(evt => evt.Step, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var byOutcome = events
            .GroupBy(evt => evt.Outcome, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        int successful = events.Count(evt => IsOutcome(evt.Outcome, "ok"));
        int cancelled = events.Count(evt => IsOutcome(evt.Outcome, "cancelled"));
        int failed = events.Count(evt =>
            IsOutcome(evt.Outcome, "error")
            || IsOutcome(evt.Outcome, "failed")
            || IsOutcome(evt.Outcome, "fail"));

        int sessions = events
            .Select(evt => evt.SessionId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        List<CriticalFlowSuccessMetric> successByFlow = BuildFlowSuccess(events);
        int totalCriticalFlowEvents = successByFlow.Sum(metric => metric.TotalEvents);
        int totalCriticalFlowOk = successByFlow.Sum(metric => metric.SuccessfulEvents);
        double criticalFlowSuccessPercent = totalCriticalFlowEvents == 0
            ? 0d
            : Math.Round((totalCriticalFlowOk * 100d) / totalCriticalFlowEvents, 2, MidpointRounding.AwayFromZero);

        return new CriticalFlowBaselineReport(
            startDate,
            endDate,
            TotalEvents: events.Count,
            TotalSessions: sessions,
            SuccessfulEvents: successful,
            CancelledEvents: cancelled,
            FailedEvents: failed,
            EventsByFlow: byFlow,
            EventsByStep: byStep,
            EventsByOutcome: byOutcome,
            SuccessByFlow: successByFlow,
            CriticalFlowSuccessPercent: criticalFlowSuccessPercent);
    }

    private static List<CriticalFlowSuccessMetric> BuildFlowSuccess(List<CriticalFlowEvent> events)
    {
        var metrics = new List<CriticalFlowSuccessMetric>();
        foreach (string flowId in OfficialCriticalFlows)
        {
            int total = events.Count(evt => string.Equals(evt.FlowId, flowId, StringComparison.OrdinalIgnoreCase));
            int ok = events.Count(evt =>
                string.Equals(evt.FlowId, flowId, StringComparison.OrdinalIgnoreCase)
                && IsOutcome(evt.Outcome, "ok"));

            double percent = total == 0 ? 0d : Math.Round((ok * 100d) / total, 2, MidpointRounding.AwayFromZero);
            metrics.Add(new CriticalFlowSuccessMetric(flowId, total, ok, percent));
        }

        return metrics;
    }

    private static bool IsOutcome(string candidate, string expected)
        => string.Equals(candidate, expected, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<DateOnly> EnumerateDateRange(DateOnly start, DateOnly end)
    {
        for (DateOnly cursor = start; cursor <= end; cursor = cursor.AddDays(1))
            yield return cursor;
    }

    private static CriticalFlowEvent? TryParse(string line)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;

            string? sessionId = ReadString(root, "SessionId", "sessionId");
            string? flowId = ReadString(root, "FlowId", "flowId");
            string? step = ReadString(root, "Step", "step");
            string? outcome = ReadString(root, "Outcome", "outcome");

            if (string.IsNullOrWhiteSpace(sessionId)
                || string.IsNullOrWhiteSpace(flowId)
                || string.IsNullOrWhiteSpace(step)
                || string.IsNullOrWhiteSpace(outcome))
            {
                return null;
            }

            DateTimeOffset timestampUtc = DateTimeOffset.UtcNow;
            string? rawTimestamp = ReadString(root, "TimestampUtc", "timestampUtc");
            if (!string.IsNullOrWhiteSpace(rawTimestamp)
                && DateTimeOffset.TryParse(rawTimestamp, out DateTimeOffset parsedTimestamp))
            {
                timestampUtc = parsedTimestamp;
            }

            return new CriticalFlowEvent(
                sessionId,
                timestampUtc,
                flowId,
                step,
                outcome,
                new Dictionary<string, object?>());
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement root, string pascalName, string camelName)
    {
        if (root.TryGetProperty(pascalName, out JsonElement pascalValue) && pascalValue.ValueKind == JsonValueKind.String)
            return pascalValue.GetString();

        if (root.TryGetProperty(camelName, out JsonElement camelValue) && camelValue.ValueKind == JsonValueKind.String)
            return camelValue.GetString();

        return null;
    }

    private static string ResolveDefaultDirectory()
    {
        string baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDirectory))
            baseDirectory = AppContext.BaseDirectory;

        return Path.Combine(baseDirectory, ProductFolderName, TelemetryFolderName);
    }
}
