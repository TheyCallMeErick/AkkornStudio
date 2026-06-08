namespace AkkornStudio.UI.Services.Observability;

public static class ObservabilityBaselineDateRange
{
    public static (DateOnly StartDate, DateOnly EndDate) ResolveUtcWindow(DateOnly utcToday, int lookbackDays)
    {
        if (lookbackDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(lookbackDays), "lookbackDays must be greater than zero.");

        DateOnly endDate = utcToday;
        DateOnly startDate = endDate.AddDays(-(lookbackDays - 1));
        return (startDate, endDate);
    }
}
