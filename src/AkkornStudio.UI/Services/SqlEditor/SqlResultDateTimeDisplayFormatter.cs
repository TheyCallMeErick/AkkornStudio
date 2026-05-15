using System.Globalization;
using AkkornStudio.UI.Services.Settings;

namespace AkkornStudio.UI.Services.SqlEditor;

internal static class SqlResultDateTimeDisplayFormatter
{
    private static readonly string[] RawDateTimePatterns =
    [
        "O",
        "o",
        "yyyy-MM-ddTHH:mm:ss.FFFFFFFK",
        "yyyy-MM-ddTHH:mm:ssK",
        "yyyy-MM-dd HH:mm:ss",
    ];

    public static string FormatDisplayValue(object? value, SqlEditorResultDateTimeDisplaySettings settings, string nullText)
    {
        if (value is null || value == DBNull.Value)
            return nullText;

        if (settings.PreferRawValues)
            return FormatRaw(value);

        string dateTimePattern = BuildDateTimePattern(settings);
        return value switch
        {
            DateTimeOffset dto => dto.ToString($"{dateTimePattern} zzz", CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString(dateTimePattern, CultureInfo.InvariantCulture),
            _ => FormatRaw(value),
        };
    }

    public static string BuildDateTimePattern(SqlEditorResultDateTimeDisplaySettings settings)
    {
        string separator = settings.DateSeparator == "/" ? "/" : "-";
        string datePattern = settings.DateOrder switch
        {
            "DMY" => $"dd{separator}MM{separator}yyyy",
            "MDY" => $"MM{separator}dd{separator}yyyy",
            _ => $"yyyy{separator}MM{separator}dd",
        };

        return $"{datePattern} HH:mm:ss";
    }

    public static string BuildDatePattern(SqlEditorResultDateTimeDisplaySettings settings)
    {
        string dateTimePattern = BuildDateTimePattern(settings);
        int timeSplit = dateTimePattern.IndexOf(' ');
        return timeSplit <= 0 ? dateTimePattern : dateTimePattern[..timeSplit];
    }

    public static string BuildEditorText(object? value, SqlEditorResultDateTimeDisplaySettings settings)
    {
        return FormatDisplayValue(value, settings, string.Empty);
    }

    public static bool TryConvertEditedDateTimeValue(
        Type targetType,
        string? editedText,
        SqlEditorResultDateTimeDisplaySettings settings,
        out object? convertedValue)
    {
        string input = (editedText ?? string.Empty).Trim();
        if (input.Length == 0)
        {
            convertedValue = null;
            return true;
        }

        Type normalizedType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        string datePattern = BuildDatePattern(settings);
        string dateTimePattern = BuildDateTimePattern(settings);

        if (normalizedType == typeof(DateTime))
        {
            if (TryParseDateTime(input, datePattern, dateTimePattern, out DateTime parsedDateTime))
            {
                convertedValue = parsedDateTime;
                return true;
            }

            convertedValue = null;
            return false;
        }

        if (normalizedType == typeof(DateTimeOffset))
        {
            if (TryParseDateTimeOffset(input, datePattern, dateTimePattern, out DateTimeOffset parsedOffset))
            {
                convertedValue = parsedOffset;
                return true;
            }

            convertedValue = null;
            return false;
        }

        convertedValue = null;
        return false;
    }

    public static string ApplyCalendarDateToText(
        string? currentText,
        DateTime selectedDate,
        Type targetType,
        SqlEditorResultDateTimeDisplaySettings settings)
    {
        Type normalizedType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        string current = (currentText ?? string.Empty).Trim();

        if (normalizedType == typeof(DateTimeOffset))
        {
            if (TryParseDateTimeOffset(current, BuildDatePattern(settings), BuildDateTimePattern(settings), out DateTimeOffset parsed))
            {
                var merged = new DateTimeOffset(
                    selectedDate.Year,
                    selectedDate.Month,
                    selectedDate.Day,
                    parsed.Hour,
                    parsed.Minute,
                    parsed.Second,
                    parsed.Offset);
                return FormatDisplayValue(merged, settings, string.Empty);
            }

            var dateOnly = new DateTimeOffset(selectedDate.Year, selectedDate.Month, selectedDate.Day, 0, 0, 0, TimeSpan.Zero);
            return FormatDisplayValue(dateOnly, settings, string.Empty);
        }

        if (TryParseDateTime(current, BuildDatePattern(settings), BuildDateTimePattern(settings), out DateTime parsedDateTime))
        {
            var merged = new DateTime(
                selectedDate.Year,
                selectedDate.Month,
                selectedDate.Day,
                parsedDateTime.Hour,
                parsedDateTime.Minute,
                parsedDateTime.Second,
                parsedDateTime.Kind);
            return FormatDisplayValue(merged, settings, string.Empty);
        }

        var newValue = new DateTime(selectedDate.Year, selectedDate.Month, selectedDate.Day, 0, 0, 0, DateTimeKind.Unspecified);
        return FormatDisplayValue(newValue, settings, string.Empty);
    }

    private static bool TryParseDateTime(string input, string datePattern, string dateTimePattern, out DateTime parsed)
    {
        if (DateTime.TryParseExact(input, RawDateTimePatterns, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed))
            return true;

        if (DateTime.TryParseExact(input, dateTimePattern, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed))
            return true;

        if (DateTime.TryParseExact(input, datePattern, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed))
            return true;

        if (DateTime.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed))
            return true;

        return DateTime.TryParse(input, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out parsed);
    }

    private static bool TryParseDateTimeOffset(string input, string datePattern, string dateTimePattern, out DateTimeOffset parsed)
    {
        if (DateTimeOffset.TryParseExact(input, RawDateTimePatterns, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed))
            return true;

        if (DateTimeOffset.TryParseExact(input, $"{dateTimePattern} zzz", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed))
            return true;

        if (DateTimeOffset.TryParseExact(input, dateTimePattern, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed))
            return true;

        if (DateTimeOffset.TryParseExact(input, datePattern, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed))
            return true;

        if (DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed))
            return true;

        return DateTimeOffset.TryParse(input, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out parsed);
    }

    private static string FormatRaw(object value)
    {
        return value switch
        {
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            TimeSpan ts => ts.ToString("c", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }
}
