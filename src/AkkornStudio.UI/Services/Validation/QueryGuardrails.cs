namespace AkkornStudio.UI.Services.Validation;

// ── Pre-execution safety rules ────────────────────────────────────────────────

/// <summary>
/// Evaluates a set of configurable safety rules against raw SQL before preview
/// execution. Rules are intentionally lenient (warn-first) to avoid blocking
/// legitimate queries; only genuinely dangerous patterns are classified as Block.
/// </summary>
public static partial class QueryGuardrails
{
    /// <summary>Runs all rules against <paramref name="sql"/> and returns issues.</summary>
    public static IReadOnlyList<GuardIssue> Check(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return [];

        string commentStripped = StripSqlCommentsPreservingStrings(sql);
        // Normalise for matching: collapse whitespace, upper-case
        string normalised = MyRegex().Replace(commentStripped.Trim(), " ").ToUpperInvariant();

        // Only run SELECT-specific rules on SELECT statements
        bool isSelect =
            normalised.StartsWith("SELECT", StringComparison.Ordinal)
            || normalised.StartsWith("WITH ", StringComparison.Ordinal); // CTE

        var issues = new List<GuardIssue>();

        if (isSelect)
        {
            CheckRequireLimit(normalised, issues);
            CheckSelectStar(normalised, issues);
            CheckNoWhereFilter(normalised, issues);
        }

        return issues;
    }

    // ── Rule: LIMIT / TOP / FETCH NEXT required ───────────────────────────────

    private static void CheckRequireLimit(string sql, List<GuardIssue> issues)
    {
        bool hasLimit =
            sql.Contains(" LIMIT ")
            || sql.Contains("\nLIMIT ")
            || sql.Contains(" LIMIT\n")
            || sql.Contains(" TOP ")
            || sql.Contains("FETCH NEXT")
            || sql.Contains("FETCH FIRST")
            || sql.Contains("ROWNUM ");

        if (!hasLimit)
            issues.Add(
                new GuardIssue(
                    GuardSeverity.Warning,
                    "NO_LIMIT",
                    "Query has no row limit — may return very large result sets",
                    "Add LIMIT <n> (PostgreSQL/MySQL) or TOP <n> (SQL Server) to cap result size"
                )
            );
    }

    // ── Rule: SELECT * ────────────────────────────────────────────────────────

    private static void CheckSelectStar(string sql, List<GuardIssue> issues)
    {
        bool hasStar = SelectStarRegex().IsMatch(sql);

        if (hasStar)
            issues.Add(
                new GuardIssue(
                    GuardSeverity.Warning,
                    "SELECT_STAR",
                    "SELECT * fetches all columns — may be slow on wide tables and obscures schema intent",
                    "Specify only the columns you need (e.g. SELECT id, name, created_at)"
                )
            );
    }

    // ── Rule: no WHERE / HAVING filter ───────────────────────────────────────

    private static void CheckNoWhereFilter(string sql, List<GuardIssue> issues)
    {
        bool hasWhere = sql.Contains(" WHERE ") || sql.Contains("\nWHERE ");
        bool hasHaving = sql.Contains(" HAVING ") || sql.Contains("\nHAVING ");
        bool hasGroupBy = sql.Contains(" GROUP BY ") || sql.Contains("\nGROUP BY ");
        bool hasLimit =
            sql.Contains(" LIMIT ")
            || sql.Contains("\nLIMIT ")
            || sql.Contains(" LIMIT\n")
            || sql.Contains(" TOP ")
            || sql.Contains("FETCH NEXT")
            || sql.Contains("FETCH FIRST")
            || sql.Contains("ROWNUM ");

        // Suppress when there is already a row-count cap (LIMIT / TOP) — the user has
        // deliberately bounded the result set, so a full-scan warning is not actionable.
        if (!hasWhere && !hasHaving && !hasGroupBy && !hasLimit && sql.Contains(" FROM "))
            issues.Add(
                new GuardIssue(
                    GuardSeverity.Warning,
                    "NO_FILTER",
                    "Query has no WHERE / HAVING clause or row limit — may perform a full table scan",
                    "Add a WHERE condition to filter rows, or connect a TOP / LIMIT node to cap the result size"
                )
            );
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\s+")]
    private static partial System.Text.RegularExpressions.Regex MyRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"\bSELECT\s*(?:DISTINCT\s*)?\*", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex SelectStarRegex();

    private static string StripSqlCommentsPreservingStrings(string sql)
    {
        if (string.IsNullOrEmpty(sql))
            return sql;

        var sb = new System.Text.StringBuilder(sql.Length);
        bool inSingleQuote = false;
        bool inLineComment = false;
        bool inBlockComment = false;

        for (int i = 0; i < sql.Length; i++)
        {
            char current = sql[i];
            char next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (inLineComment)
            {
                if (current is '\r' or '\n')
                {
                    inLineComment = false;
                    sb.Append(current);
                }

                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (current == '\'' && !inSingleQuote)
            {
                inSingleQuote = true;
                sb.Append(current);
                continue;
            }

            if (current == '\'' && inSingleQuote)
            {
                // SQL-standard escaped quote: ''.
                if (next == '\'')
                {
                    sb.Append(current);
                    sb.Append(next);
                    i++;
                    continue;
                }

                inSingleQuote = false;
                sb.Append(current);
                continue;
            }

            if (!inSingleQuote && current == '-' && next == '-')
            {
                inLineComment = true;
                i++;
                continue;
            }

            if (!inSingleQuote && current == '/' && next == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            sb.Append(current);
        }

        return sb.ToString();
    }
}
