namespace AkkornStudio.UI.Services.Observability;

public interface ICriticalFlowBaselineReportService
{
    CriticalFlowBaselineReport Build(DateOnly startDate, DateOnly endDate);
}
