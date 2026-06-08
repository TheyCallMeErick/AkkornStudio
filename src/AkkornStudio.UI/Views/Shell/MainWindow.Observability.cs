using AkkornStudio.UI.Services.Observability;

namespace AkkornStudio.UI;

public partial class MainWindow
{
    private void ExportObservabilityBaselineSafe(int lookbackDays)
    {
        try
        {
            ExportObservabilityBaseline(lookbackDays);
        }
        catch (Exception ex)
        {
            CurrentShell.Toasts.ShowError("Falha ao exportar baseline de observabilidade.", ex.Message);
        }
    }

    private void ExportObservabilityBaseline(int lookbackDays)
    {
        if (_criticalFlowBaselineReportService is null)
        {
            CurrentShell.Toasts.ShowWarning("Servico de baseline de observabilidade indisponivel.");
            return;
        }

        if (lookbackDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(lookbackDays), "lookbackDays must be greater than zero.");

        (DateOnly startDate, DateOnly endDate) = ObservabilityBaselineDateRange.ResolveUtcWindow(
            DateOnly.FromDateTime(DateTime.UtcNow),
            lookbackDays);
        CriticalFlowBaselineReport report = _criticalFlowBaselineReportService.Build(startDate, endDate);
        DateOnly previousEndDate = startDate.AddDays(-1);
        DateOnly previousStartDate = previousEndDate.AddDays(-(lookbackDays - 1));
        CriticalFlowBaselineReport previousReport = _criticalFlowBaselineReportService.Build(previousStartDate, previousEndDate);

        string directory = ResolveObservabilityReportsDirectory();
        Directory.CreateDirectory(directory);

        string stamp = DateTime.UtcNow.ToString("yyyy-MM-dd");
        string markdownPath = Path.Combine(directory, $"baseline-{stamp}.md");
        string csvPath = Path.Combine(directory, $"baseline-{stamp}.csv");
        string? alertPath = null;

        File.WriteAllText(markdownPath, report.ToMarkdown());
        File.WriteAllText(csvPath, report.ToCsv());

        CriticalFlowRegressionAlert? regressionAlert = _criticalFlowRegressionAlertService?.Evaluate(report, previousReport);
        if (regressionAlert is not null && regressionAlert.HasRegression)
        {
            alertPath = Path.Combine(directory, $"alerts-{stamp}.md");
            File.WriteAllText(alertPath, regressionAlert.ToMarkdown(startDate, endDate, previousStartDate, previousEndDate));

            CurrentShell.Toasts.ShowWarning(
                "Regressao de performance detectada.",
                $"Gerado {Path.GetFileName(alertPath)} para investigacao.");
        }
        else
        {
            CurrentShell.Toasts.ShowSuccess(
                "Baseline de observabilidade exportado.",
                $"Arquivos gerados: {Path.GetFileName(markdownPath)} e {Path.GetFileName(csvPath)}");
        }

        TrackCriticalFlow(
            flowId: "CF-02-navigate-shell",
            step: "export_observability_baseline",
            outcome: "ok",
            properties: new Dictionary<string, object?>
            {
                ["lookbackDays"] = lookbackDays,
                ["startDate"] = startDate.ToString("yyyy-MM-dd"),
                ["endDate"] = endDate.ToString("yyyy-MM-dd"),
                ["previousStartDate"] = previousStartDate.ToString("yyyy-MM-dd"),
                ["previousEndDate"] = previousEndDate.ToString("yyyy-MM-dd"),
                ["markdownPath"] = markdownPath,
                ["csvPath"] = csvPath,
                ["regressionDetected"] = regressionAlert?.HasRegression == true,
                ["alertPath"] = alertPath,
            });
    }

    private static string ResolveObservabilityReportsDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
            localAppData = AppContext.BaseDirectory;

        return Path.Combine(localAppData, "AkkornStudio", "telemetry", "reports");
    }
}
