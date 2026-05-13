using System.Globalization;
using System.Text;
using AkkornStudio.Core;

namespace AkkornStudio.UI.Services.SqlEditor;

public sealed class SqlQueryParameterProcessor
{
    public IReadOnlyList<string> DetectNames(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return [];

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (SqlQueryParameterOccurrence occurrence in EnumerateOccurrences(sql!))
        {
            if (seen.Add(occurrence.Name))
                names.Add(occurrence.Name);
        }

        return names;
    }

    public bool TryApply(
        string? sql,
        IReadOnlyDictionary<string, string> parameterValues,
        DatabaseProvider provider,
        out string resolvedSql,
        out IReadOnlyList<string> missingParameters,
        out string? errorMessage)
    {
        resolvedSql = sql ?? string.Empty;
        errorMessage = null;
        missingParameters = [];
        if (string.IsNullOrWhiteSpace(sql))
            return true;

        var misses = new List<string>();
        var builder = new StringBuilder(sql!.Length + 64);
        int cursor = 0;
        foreach (SqlQueryParameterOccurrence occurrence in EnumerateOccurrences(sql))
        {
            if (occurrence.Start > cursor)
                builder.Append(sql, cursor, occurrence.Start - cursor);

            if (!parameterValues.TryGetValue(occurrence.Name, out string? rawValue) || string.IsNullOrWhiteSpace(rawValue))
            {
                if (!misses.Contains(occurrence.Name, StringComparer.OrdinalIgnoreCase))
                    misses.Add(occurrence.Name);
                builder.Append(sql, occurrence.Start, occurrence.Length);
            }
            else
            {
                if (!TryConvertToSqlLiteral(rawValue!, provider, out string literal, out string? conversionError))
                {
                    errorMessage = conversionError;
                    missingParameters = [];
                    resolvedSql = sql;
                    return false;
                }

                builder.Append(literal);
            }

            cursor = occurrence.Start + occurrence.Length;
        }

        if (cursor < sql.Length)
            builder.Append(sql, cursor, sql.Length - cursor);

        if (misses.Count > 0)
        {
            missingParameters = misses;
            resolvedSql = sql;
            return false;
        }

        resolvedSql = builder.ToString();
        return true;
    }

    private static IEnumerable<SqlQueryParameterOccurrence> EnumerateOccurrences(string sql)
    {
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        bool inLineComment = false;
        bool inBlockComment = false;
        for (int i = 0; i < sql.Length; i++)
        {
            char ch = sql[i];
            char next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (inLineComment)
            {
                if (ch == '\n')
                    inLineComment = false;
                continue;
            }

            if (inBlockComment)
            {
                if (ch == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }
                continue;
            }

            if (inSingleQuote)
            {
                if (ch == '\'' && next == '\'')
                {
                    i++;
                    continue;
                }

                if (ch == '\'')
                    inSingleQuote = false;
                continue;
            }

            if (inDoubleQuote)
            {
                if (ch == '"')
                    inDoubleQuote = false;
                continue;
            }

            if (ch == '-' && next == '-')
            {
                inLineComment = true;
                i++;
                continue;
            }

            if (ch == '/' && next == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            if (ch == '\'')
            {
                inSingleQuote = true;
                continue;
            }

            if (ch == '"')
            {
                inDoubleQuote = true;
                continue;
            }

            if (ch is not (':' or '@'))
                continue;

            if (ch == ':' && next == ':')
            {
                i++;
                continue;
            }

            if (i + 1 >= sql.Length || !IsIdentifierStart(sql[i + 1]))
                continue;

            int start = i;
            int index = i + 2;
            while (index < sql.Length && IsIdentifierPart(sql[index]))
                index++;

            string name = sql.Substring(i + 1, index - (i + 1));
            yield return new SqlQueryParameterOccurrence(name, start, index - start);
            i = index - 1;
        }
    }

    private static bool IsIdentifierStart(char ch) => char.IsLetter(ch) || ch == '_';

    private static bool IsIdentifierPart(char ch) => char.IsLetterOrDigit(ch) || ch == '_';

    private static bool TryConvertToSqlLiteral(
        string rawValue,
        DatabaseProvider provider,
        out string sqlLiteral,
        out string? errorMessage)
    {
        errorMessage = null;
        string value = rawValue.Trim();
        if (value.Length == 0)
        {
            sqlLiteral = "NULL";
            return true;
        }

        if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
        {
            sqlLiteral = "NULL";
            return true;
        }

        if (bool.TryParse(value, out bool boolValue))
        {
            sqlLiteral = provider == DatabaseProvider.SqlServer
                ? (boolValue ? "1" : "0")
                : (boolValue ? "TRUE" : "FALSE");
            return true;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long intValue))
        {
            sqlLiteral = intValue.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal decimalValue))
        {
            sqlLiteral = decimalValue.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (Guid.TryParse(value, out Guid guidValue))
        {
            sqlLiteral = $"'{guidValue:D}'";
            return true;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out DateTimeOffset dto))
        {
            sqlLiteral = $"'{dto:yyyy-MM-dd HH:mm:ss.fffffff zzz}'";
            return true;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out DateTime dt))
        {
            sqlLiteral = $"'{dt:yyyy-MM-dd HH:mm:ss.fffffff}'";
            return true;
        }

        if (value.IndexOf('\0') >= 0)
        {
            sqlLiteral = string.Empty;
            errorMessage = "Query parameter contains invalid null character.";
            return false;
        }

        sqlLiteral = $"'{EscapeSqlLiteral(value)}'";
        return true;
    }

    private static string EscapeSqlLiteral(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);

    private sealed record SqlQueryParameterOccurrence(string Name, int Start, int Length);
}
