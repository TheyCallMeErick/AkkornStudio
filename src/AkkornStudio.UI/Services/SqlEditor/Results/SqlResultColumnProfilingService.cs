using System.Data;
using System.Globalization;

namespace AkkornStudio.UI.Services.SqlEditor.Results;

public sealed class SqlResultColumnProfilingService
{
    private const int TopValuesLimit = 5;

    public Task<IReadOnlyList<SqlResultColumnProfile>> BuildProfilesAsync(
        DataTable? table,
        CancellationToken ct = default)
    {
        if (table is null || table.Columns.Count == 0)
            return Task.FromResult<IReadOnlyList<SqlResultColumnProfile>>([]);

        return Task.Run(() => BuildProfiles(table, ct), ct);
    }

    private static IReadOnlyList<SqlResultColumnProfile> BuildProfiles(DataTable table, CancellationToken ct)
    {
        var profiles = new List<SqlResultColumnProfile>(table.Columns.Count);
        foreach (DataColumn column in table.Columns)
        {
            ct.ThrowIfCancellationRequested();
            profiles.Add(BuildColumnProfile(table, column, ct));
        }

        return profiles;
    }

    private static SqlResultColumnProfile BuildColumnProfile(DataTable table, DataColumn column, CancellationToken ct)
    {
        SqlResultColumnProfileKind kind = ResolveKind(column.DataType);
        int rowCount = table.Rows.Count;
        int nullCount = 0;
        int emptyCount = 0;
        int futureCount = 0;

        double? numericMin = null;
        double? numericMax = null;
        double numericSum = 0;
        int numericCount = 0;

        DateTimeOffset? temporalMin = null;
        DateTimeOffset? temporalMax = null;

        var distinctValues = new HashSet<string>(StringComparer.Ordinal);
        var topValueCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        DateTimeOffset futureThreshold = DateTimeOffset.UtcNow;

        foreach (DataRow row in table.Rows)
        {
            ct.ThrowIfCancellationRequested();
            object? value = row[column];
            if (value is null || value is DBNull)
            {
                nullCount += 1;
                continue;
            }

            switch (kind)
            {
                case SqlResultColumnProfileKind.Numeric:
                    if (TryConvertToDouble(value, out double numericValue))
                    {
                        numericMin = !numericMin.HasValue || numericValue < numericMin.Value ? numericValue : numericMin;
                        numericMax = !numericMax.HasValue || numericValue > numericMax.Value ? numericValue : numericMax;
                        numericSum += numericValue;
                        numericCount += 1;
                    }
                    break;
                case SqlResultColumnProfileKind.Temporal:
                    if (TryConvertToDateTimeOffset(value, out DateTimeOffset temporalValue))
                    {
                        temporalMin = !temporalMin.HasValue || temporalValue < temporalMin.Value
                            ? temporalValue
                            : temporalMin;
                        temporalMax = !temporalMax.HasValue || temporalValue > temporalMax.Value
                            ? temporalValue
                            : temporalMax;
                        if (temporalValue > futureThreshold)
                            futureCount += 1;
                    }
                    break;
            }

            string textValue = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            if (kind == SqlResultColumnProfileKind.Text && string.IsNullOrEmpty(textValue))
                emptyCount += 1;

            distinctValues.Add(textValue);
            if (topValueCounts.TryGetValue(textValue, out int currentCount))
                topValueCounts[textValue] = currentCount + 1;
            else
                topValueCounts[textValue] = 1;
        }

        double? numericAverage = numericCount > 0 ? numericSum / numericCount : null;
        string topValuesSummary = BuildTopValuesSummary(topValueCounts);

        return new SqlResultColumnProfile(
            ColumnName: column.ColumnName,
            Kind: kind,
            RowCount: rowCount,
            NullCount: nullCount,
            EmptyCount: emptyCount,
            DistinctCount: distinctValues.Count,
            TopValuesSummary: topValuesSummary,
            NumericMin: numericMin,
            NumericMax: numericMax,
            NumericAverage: numericAverage,
            TemporalMin: temporalMin,
            TemporalMax: temporalMax,
            SuspectFutureValueCount: futureCount);
    }

    private static SqlResultColumnProfileKind ResolveKind(Type type)
    {
        Type normalizedType = Nullable.GetUnderlyingType(type) ?? type;
        if (normalizedType == typeof(string) || normalizedType == typeof(char))
            return SqlResultColumnProfileKind.Text;

        if (normalizedType == typeof(DateTime) || normalizedType == typeof(DateTimeOffset))
            return SqlResultColumnProfileKind.Temporal;

        if (normalizedType == typeof(byte)
            || normalizedType == typeof(sbyte)
            || normalizedType == typeof(short)
            || normalizedType == typeof(ushort)
            || normalizedType == typeof(int)
            || normalizedType == typeof(uint)
            || normalizedType == typeof(long)
            || normalizedType == typeof(ulong)
            || normalizedType == typeof(float)
            || normalizedType == typeof(double)
            || normalizedType == typeof(decimal))
        {
            return SqlResultColumnProfileKind.Numeric;
        }

        return SqlResultColumnProfileKind.Other;
    }

    private static bool TryConvertToDouble(object value, out double result)
    {
        switch (value)
        {
            case double doubleValue:
                result = doubleValue;
                return true;
            case float floatValue:
                result = floatValue;
                return true;
            case decimal decimalValue:
                result = (double)decimalValue;
                return true;
            default:
                return double.TryParse(
                    Convert.ToString(value, CultureInfo.InvariantCulture),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out result);
        }
    }

    private static bool TryConvertToDateTimeOffset(object value, out DateTimeOffset result)
    {
        switch (value)
        {
            case DateTimeOffset offset:
                result = offset;
                return true;
            case DateTime dateTime:
                result = dateTime.Kind == DateTimeKind.Unspecified
                    ? new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc))
                    : new DateTimeOffset(dateTime);
                return true;
            default:
                return DateTimeOffset.TryParse(
                    Convert.ToString(value, CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out result);
        }
    }

    private static string BuildTopValuesSummary(IReadOnlyDictionary<string, int> topValueCounts)
    {
        if (topValueCounts.Count == 0)
            return "-";

        IEnumerable<string> tokens = topValueCounts
            .OrderByDescending(static entry => entry.Value)
            .ThenBy(static entry => entry.Key, StringComparer.Ordinal)
            .Take(TopValuesLimit)
            .Select(static entry => $"{FormatTopValue(entry.Key)} ({entry.Value})");
        return string.Join(", ", tokens);
    }

    private static string FormatTopValue(string value)
    {
        return string.IsNullOrEmpty(value) ? "<empty>" : value;
    }
}
