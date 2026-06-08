namespace AkkornStudio.UI.Services.Observability;

public sealed record CriticalFlowRegressionAlert(
    bool HasRegression,
    string Summary,
    IReadOnlyList<string> Issues)
{
    public string ToMarkdown(DateOnly currentStart, DateOnly currentEnd, DateOnly previousStart, DateOnly previousEnd)
    {
        var lines = new List<string>
        {
            "# Observability Regression Alert",
            string.Empty,
            $"Current window: {currentStart:yyyy-MM-dd} to {currentEnd:yyyy-MM-dd}",
            $"Previous window: {previousStart:yyyy-MM-dd} to {previousEnd:yyyy-MM-dd}",
            string.Empty,
            $"Status: {(HasRegression ? "REGRESSION_DETECTED" : "NO_REGRESSION")}",
            $"Summary: {Summary}",
            string.Empty,
        };

        if (Issues.Count > 0)
        {
            lines.Add("## Issues");
            lines.Add(string.Empty);
            foreach (string issue in Issues)
                lines.Add($"- {issue}");
            lines.Add(string.Empty);
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}
