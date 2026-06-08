using AkkornStudio.UI.Services.Observability;

namespace AkkornStudio.Tests.Unit.Services.Observability;

public sealed class ObservabilityBaselineDateRangeTests
{
    [Theory]
    [InlineData(7, "2026-05-05", "2026-04-29")]
    [InlineData(14, "2026-05-05", "2026-04-22")]
    [InlineData(30, "2026-05-05", "2026-04-06")]
    public void ResolveUtcWindow_ReturnsExpectedRange(int lookbackDays, string end, string start)
    {
        DateOnly utcToday = DateOnly.Parse(end);

        (DateOnly startDate, DateOnly endDate) = ObservabilityBaselineDateRange.ResolveUtcWindow(utcToday, lookbackDays);

        Assert.Equal(DateOnly.Parse(start), startDate);
        Assert.Equal(DateOnly.Parse(end), endDate);
    }

    [Fact]
    public void ResolveUtcWindow_WhenLookbackDaysIsInvalid_Throws()
    {
        DateOnly utcToday = new(2026, 5, 5);

        Assert.Throws<ArgumentOutOfRangeException>(() => ObservabilityBaselineDateRange.ResolveUtcWindow(utcToday, 0));
    }
}
