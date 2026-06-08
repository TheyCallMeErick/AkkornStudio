using AkkornStudio.UI.Services.Settings;
using AkkornStudio.UI.Services.SqlEditor;

namespace AkkornStudio.Tests.Unit.Services.SqlEditor;

public sealed class SqlResultDateTimeDisplayFormatterTests
{
    [Fact]
    public void FormatDisplayValue_WhenFormattedPreference_UsesConfiguredOrderAndSeparator()
    {
        var settings = new SqlEditorResultDateTimeDisplaySettings
        {
            DateOrder = "DMY",
            DateSeparator = "/",
            PreferRawValues = false,
        };

        DateTime input = new(2026, 5, 15, 14, 30, 45, DateTimeKind.Unspecified);
        string formatted = SqlResultDateTimeDisplayFormatter.FormatDisplayValue(input, settings, "NULL");

        Assert.Equal("15/05/2026 14:30:45", formatted);
    }

    [Fact]
    public void FormatDisplayValue_WhenRawPreference_UsesRoundtripFormat()
    {
        var settings = new SqlEditorResultDateTimeDisplaySettings
        {
            DateOrder = "YMD",
            DateSeparator = "-",
            PreferRawValues = true,
        };

        DateTime input = new(2026, 5, 15, 14, 30, 45, DateTimeKind.Utc);
        string formatted = SqlResultDateTimeDisplayFormatter.FormatDisplayValue(input, settings, "NULL");

        Assert.Contains("2026-05-15T14:30:45", formatted);
    }

    [Fact]
    public void TryConvertEditedDateTimeValue_AcceptsConfiguredDateFormat()
    {
        var settings = new SqlEditorResultDateTimeDisplaySettings
        {
            DateOrder = "MDY",
            DateSeparator = "/",
            PreferRawValues = false,
        };

        bool success = SqlResultDateTimeDisplayFormatter.TryConvertEditedDateTimeValue(
            typeof(DateTime),
            "05/15/2026 23:01:59",
            settings,
            out object? converted);

        Assert.True(success);
        Assert.IsType<DateTime>(converted);
        DateTime value = (DateTime)converted!;
        Assert.Equal(2026, value.Year);
        Assert.Equal(5, value.Month);
        Assert.Equal(15, value.Day);
        Assert.Equal(23, value.Hour);
        Assert.Equal(1, value.Minute);
        Assert.Equal(59, value.Second);
    }

    [Fact]
    public void ApplyCalendarDateToText_PreservesExistingTimePortion()
    {
        var settings = new SqlEditorResultDateTimeDisplaySettings
        {
            DateOrder = "YMD",
            DateSeparator = "-",
            PreferRawValues = false,
        };

        string merged = SqlResultDateTimeDisplayFormatter.ApplyCalendarDateToText(
            "2026-05-10 09:45:12",
            new DateTime(2026, 12, 31),
            typeof(DateTime),
            settings);

        Assert.Equal("2026-12-31 09:45:12", merged);
    }
}
