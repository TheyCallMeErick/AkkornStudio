using System.Text.RegularExpressions;
using AkkornStudio.Core;

namespace AkkornStudio.QueryEngine;

public static partial class QueryHintSyntax
{
    public static bool TryNormalize(
        DatabaseProvider provider,
        string? raw,
        out string normalized,
        out string? validationError)
    {
        normalized = string.Empty;
        validationError = null;

        string hints = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(hints))
            return true;

        if (hints.Contains(';', StringComparison.Ordinal))
        {
            validationError = "Query hints contain ';'. Use only hint expressions.";
            return false;
        }

        if (!SafeCharsRegex().IsMatch(hints))
        {
            validationError = "Query hints contain unsupported characters.";
            return false;
        }

        if (provider is DatabaseProvider.SQLite or DatabaseProvider.Postgres)
        {
            validationError = provider == DatabaseProvider.Postgres
                ? "Query hints are not supported for PostgreSQL."
                : "Query hints are not supported for this provider.";
            return false;
        }

        List<string> items = SplitTopLevelByComma(hints);
        if (items.Count == 0)
            return true;

        bool valid = provider switch
        {
            DatabaseProvider.SqlServer => items.All(IsValidSqlServerOptionHint),
            DatabaseProvider.MySql => items.All(IsValidSelectCommentHint),
            _ => false,
        };

        if (!valid)
        {
            validationError = provider switch
            {
                DatabaseProvider.SqlServer => "Query hints include unsupported SQL Server OPTION() entries.",
                DatabaseProvider.MySql => "Query hints include unsupported MySQL SELECT comment hint entries.",
                DatabaseProvider.Postgres => "Query hints are not supported for PostgreSQL.",
                _ => "Query hints are not supported for this provider.",
            };
            return false;
        }

        normalized = string.Join(", ", items.Select(i => i.Trim()).Where(i => !string.IsNullOrWhiteSpace(i)));
        return true;
    }

    private static bool IsValidSqlServerOptionHint(string hint)
    {
        string h = hint.Trim();
        if (string.IsNullOrWhiteSpace(h))
            return false;

        return h.Equals("RECOMPILE", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(h, @"^MAXDOP\s+\d+$", RegexOptions.IgnoreCase)
            || Regex.IsMatch(h, @"^FAST\s+\d+$", RegexOptions.IgnoreCase)
            || Regex.IsMatch(h, @"^OPTIMIZE\s+FOR\s+UNKNOWN$", RegexOptions.IgnoreCase)
            || Regex.IsMatch(h, @"^USE\s+HINT\s*\(\s*'[^']+'\s*\)$", RegexOptions.IgnoreCase)
            || Regex.IsMatch(h, @"^TABLE\s+HINT\s*\(\s*[A-Za-z0-9_\.\[\]]+\s*,\s*.+\)$", RegexOptions.IgnoreCase);
    }

    private static bool IsValidSelectCommentHint(string hint)
    {
        string h = hint.Trim();
        if (string.IsNullOrWhiteSpace(h))
            return false;

        // Accept hint tokens like SeqScan(table), MAX_EXECUTION_TIME(1000), BKA(t1)
        return Regex.IsMatch(h, @"^[A-Za-z_][A-Za-z0-9_]*(\s*\(.*\))?$", RegexOptions.IgnoreCase);
    }

    private static List<string> SplitTopLevelByComma(string text)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        int depth = 0;

        foreach (char ch in text)
        {
            if (ch == '(')
                depth++;
            else if (ch == ')' && depth > 0)
                depth--;

            if (ch == ',' && depth == 0)
            {
                string piece = current.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(piece))
                    parts.Add(piece);
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        string tail = current.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(tail))
            parts.Add(tail);

        return parts;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_\s,\(\)\.\[\]'\""\-\+=/]+$", RegexOptions.Compiled)]
    private static partial Regex SafeCharsRegex();
}
