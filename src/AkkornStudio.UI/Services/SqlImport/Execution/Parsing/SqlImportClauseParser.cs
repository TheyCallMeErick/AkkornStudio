using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using AkkornStudio.SqlImport.Diagnostics;
using AkkornStudio.UI.Services.SqlImport;
using AkkornStudio.UI.Services.SqlImport.Rewriting;
using AkkornStudio.UI.ViewModels.Canvas;

namespace AkkornStudio.UI.Services.SqlImport.Execution.Parsing;

public sealed class SqlImportClauseParser(SqlImportCteRewriteService cteRewriteService)
{
    private readonly SqlImportCteRewriteService _cteRewriteService = cteRewriteService;

    public SqlImportParseResult Parse(
        string sql,
        ObservableCollection<ImportReportItem> report,
        CancellationToken cancellationToken
    )
    {
        int imported = 0;
        int partial = 0;
        int skipped = 0;

        sql = Regex.Replace(sql, @"--[^\n]*", " ");
        sql = Regex.Replace(sql, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        sql = Regex.Replace(sql, @"\s+", " ").Trim();

        if (Regex.IsMatch(sql, @"\bWITH\b", RegexOptions.IgnoreCase)
            && _cteRewriteService.TryRewriteSimpleCteQuery(sql, out string rewrittenSql, out int rewrittenCteCount))
        {
            sql = rewrittenSql;
            report.Add(
                new ImportReportItem(
                    "CTE",
                    ImportItemStatus.Imported,
                    rewrittenCteCount == 1
                        ? "Single CTE rewritten to supported import shape."
                        : $"{rewrittenCteCount} chained CTEs rewritten to supported import shape."
                )
            );
            imported++;
        }

        if (_cteRewriteService.TryRewriteSimpleFromSubquery(sql, out string rewrittenFromSubquerySql))
        {
            sql = rewrittenFromSubquerySql;
            report.Add(
                new ImportReportItem(
                    "FROM sub-query",
                    ImportItemStatus.Imported,
                    "Simple FROM sub-query rewritten to supported import shape."
                )
            );
            imported++;
        }

        if (_cteRewriteService.TryRewriteSimpleJoinSubqueries(sql, out string rewrittenJoinSubquerySql, out int rewrittenJoinSubqueryCount))
        {
            sql = rewrittenJoinSubquerySql;
            report.Add(
                new ImportReportItem(
                    "JOIN sub-query",
                    ImportItemStatus.Imported,
                    rewrittenJoinSubqueryCount == 1
                        ? "Simple JOIN sub-query rewritten to supported import shape."
                        : $"{rewrittenJoinSubqueryCount} simple JOIN sub-queries rewritten to supported import shape."
                )
            );
            imported++;
        }

        IReadOnlyList<SqlImportSetOperation> setOperations = [];
        if (TryExtractUnionSetOperation(sql, out string leftOperandSql, out SqlImportSetOperation setOperation))
        {
            sql = leftOperandSql;
            setOperations = [setOperation];
            report.Add(
                new ImportReportItem(
                    setOperation.IsAll ? "UNION ALL" : "UNION",
                    ImportItemStatus.Imported,
                    "Set operation preserved as a graph Set Operation node."
                )
            );
            imported++;
        }

        string qualifiedIdentifierPattern = SqlImportIdentifierNormalizer.QualifiedIdentifierPattern;

        bool hasSupportedWhereSubquery = Regex.IsMatch(
            sql,
            $@"\bWHERE\s+\(*\s*(?:(?:NOT\s+)?EXISTS\s*\(\s*SELECT|{qualifiedIdentifierPattern}\s+(?:NOT\s+)?IN\s*\(\s*SELECT|{qualifiedIdentifierPattern}\s*(?:<>|!=|>=|<=|=|>|<)\s*\(+\s*SELECT)",
            RegexOptions.IgnoreCase
        );

        bool hasUnsupportedCteOrSubquery =
            Regex.IsMatch(sql, @"\bWITH\b", RegexOptions.IgnoreCase)
            || (Regex.IsMatch(sql, @"\(SELECT\b", RegexOptions.IgnoreCase) && !hasSupportedWhereSubquery);

        bool hasCorrelatedSubquery =
            Regex.IsMatch(sql, @"\b(EXISTS|IN)\s*\(\s*SELECT\b", RegexOptions.IgnoreCase)
            && Regex.IsMatch(sql, @"\b\w+\.\w+\s*=\s*\w+\.\w+\b", RegexOptions.IgnoreCase);

        Match fromMatchForAlias = Regex.Match(
            sql,
            @"FROM\s+(.+?)(?=\s+(?:WHERE|ORDER\s+BY|LIMIT|GROUP\s+BY)|$)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        HashSet<string> outerAliases = _cteRewriteService.ExtractSourceAliases(
            fromMatchForAlias.Success ? fromMatchForAlias.Groups[1].Value.Trim() : string.Empty
        );

        if (hasUnsupportedCteOrSubquery)
        {
            if (Regex.IsMatch(sql, @"\bWITH\b", RegexOptions.IgnoreCase))
            {
                foreach (string cteIssue in _cteRewriteService.AnalyzeCteNameIssues(sql))
                {
                    report.Add(SqlImportReportFactory.Partial(
                        SqlImportDiagnosticCodes.AstUnsupported,
                        "CTE name diagnostics",
                        cteIssue
                    ));
                    partial++;
                }

                foreach (string cteIssue in _cteRewriteService.AnalyzeBlockedCteSafetyIssues(sql))
                {
                    report.Add(SqlImportReportFactory.Partial(
                        SqlImportDiagnosticCodes.AstUnsupported,
                        "CTE safety diagnostics",
                        cteIssue
                    ));
                    partial++;
                }
            }

            if (hasCorrelatedSubquery)
            {
                string correlatedFields = SqlImportCteRewriteService.DescribeCorrelatedOuterReferences(sql, outerAliases);
                report.Add(
                    SqlImportReportFactory.Partial(
                        SqlImportDiagnosticCodes.AstUnsupported,
                        "Correlated sub-query",
                        SqlImportDiagnosticMessages.CorrelatedSubqueryFallbackWithExternalRefsReportNote(correlatedFields)
                    )
                );
                partial++;
            }

            report.Add(SqlImportReportFactory.Skipped(
                SqlImportDiagnosticCodes.AstUnsupported,
                "CTE / sub-query",
                SqlImportDiagnosticMessages.CteSubqueryNotSupportedReportNote
            ));
            skipped++;

            report.Add(
                SqlImportReportFactory.Skipped(
                    SqlImportDiagnosticCodes.AstUnsupported,
                    "Raw fallback",
                    SqlImportDiagnosticMessages.RawFallbackDisabledForCteSubqueryReportNote
                )
            );
            skipped++;

            return new SqlImportParseResult(null, imported, partial, skipped, true);
        }

        if (Regex.IsMatch(sql, @"\bUNION\b", RegexOptions.IgnoreCase))
        {
            report.Add(SqlImportReportFactory.Skipped(
                SqlImportDiagnosticCodes.AstUnsupported,
                "UNION",
                SqlImportDiagnosticMessages.UnionNotSupportedReportNote
            ));
            skipped++;
        }

        int? topLimit = null;
        Match distinctTopPrefix = Regex.Match(
            sql,
            @"^\s*SELECT\s+DISTINCT\s+TOP\s+(?<top>\d+)\s+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (distinctTopPrefix.Success)
        {
            topLimit = int.Parse(distinctTopPrefix.Groups["top"].Value);
            sql = Regex.Replace(
                sql,
                @"^\s*SELECT\s+DISTINCT\s+TOP\s+\d+\s+",
                "SELECT DISTINCT ",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        else
        {
            Match topPrefix = Regex.Match(
                sql,
                @"^\s*SELECT\s+TOP\s+(?<top>\d+)\s+",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (topPrefix.Success)
            {
                topLimit = int.Parse(topPrefix.Groups["top"].Value);
                sql = Regex.Replace(
                    sql,
                    @"^\s*SELECT\s+TOP\s+\d+\s+",
                    "SELECT ",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
        }

        Match selMatch = Regex.Match(
            sql,
            @"SELECT\s+(DISTINCT\s+)?(.+?)\s+FROM\b",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );
        if (!selMatch.Success)
            throw new InvalidOperationException("Could not find SELECT ... FROM in the query.");

        bool isDistinct = selMatch.Groups[1].Success;
        string colPart = selMatch.Groups[2].Value.Trim();
        Match qualifiedStarMatch = Regex.Match(
            colPart,
            $@"^\s*(?<source>{SqlImportIdentifierNormalizer.QualifiedIdentifierPattern})\s*\.\s*\*\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        bool isStar = colPart == "*" || qualifiedStarMatch.Success;
        string? starQualifier = qualifiedStarMatch.Success
            ? SqlImportIdentifierNormalizer.NormalizeQualifiedIdentifier(qualifiedStarMatch.Groups["source"].Value)
            : null;

        var selectedCols = new List<SqlImportSelectedColumn>();
        if (!isStar)
        {
            foreach (string raw in SplitCommas(colPart))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string col = raw.Trim();
                Match asMatch = Regex.Match(
                    col,
                    $@"^(.+?)\s+AS\s+({SqlImportIdentifierNormalizer.IdentifierPattern})$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (asMatch.Success)
                {
                    selectedCols.Add(new SqlImportSelectedColumn(
                        asMatch.Groups[1].Value.Trim(),
                        SqlImportIdentifierNormalizer.NormalizeIdentifierToken(asMatch.Groups[2].Value)));
                }
                else
                    selectedCols.Add(new SqlImportSelectedColumn(col, null));
            }
        }

        Match fromBlock = Regex.Match(
            sql,
            @"FROM\s+(.+?)(?=\s+(?:WHERE|ORDER\s+BY|LIMIT|OFFSET|GROUP\s+BY|HAVING)|$)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );
        if (!fromBlock.Success)
            throw new InvalidOperationException("Could not parse FROM clause.");

        string fromSql = fromBlock.Groups[1].Value.Trim();

        string[] joinKeywords = ["INNER JOIN", "LEFT JOIN", "RIGHT JOIN", "FULL JOIN", "JOIN"];
        var fromParts = new List<SqlImportSourcePart>();

        int firstJoinIdx = -1;
        string upperFrom = fromSql.ToUpperInvariant();
        foreach (string joinKeyword in joinKeywords)
        {
            int idx = upperFrom.IndexOf(joinKeyword, StringComparison.Ordinal);
            if (idx >= 0 && (firstJoinIdx < 0 || idx < firstJoinIdx))
                firstJoinIdx = idx;
        }

        string primaryPart = firstJoinIdx >= 0 ? fromSql[..firstJoinIdx].Trim() : fromSql.Trim();
        (string primaryTable, string? primaryAlias) = ExtractTableAndAlias(primaryPart);
        fromParts.Add(new SqlImportSourcePart(primaryTable, primaryAlias, null, null));

        MatchCollection joinMatches = Regex.Matches(
            fromSql,
            $@"(?:INNER\s+JOIN|LEFT\s+(?:OUTER\s+)?JOIN|RIGHT\s+(?:OUTER\s+)?JOIN|FULL\s+(?:OUTER\s+)?JOIN|JOIN)\s+({qualifiedIdentifierPattern})(?:\s+(?:AS\s+)?({SqlImportIdentifierNormalizer.IdentifierPattern}))?\s+ON\s+(.+?)(?=\s+(?:INNER\s+JOIN|LEFT\s+(?:OUTER\s+)?JOIN|RIGHT\s+(?:OUTER\s+)?JOIN|FULL\s+(?:OUTER\s+)?JOIN|JOIN)|$)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant
        );
        foreach (Match jm in joinMatches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string jTable = SqlImportIdentifierNormalizer.NormalizeQualifiedIdentifier(jm.Groups[1].Value);
            string? jAlias = jm.Groups[2].Success
                ? SqlImportIdentifierNormalizer.NormalizeIdentifierToken(jm.Groups[2].Value)
                : null;
            string onClause = jm.Groups[3].Value.Trim();
            string jType = Regex.Match(
                    jm.Value,
                    @"(?:INNER\s+JOIN|LEFT\s+(?:OUTER\s+)?JOIN|RIGHT\s+(?:OUTER\s+)?JOIN|FULL\s+(?:OUTER\s+)?JOIN|JOIN)",
                    RegexOptions.IgnoreCase
                )
                .Value.Trim()
                .ToUpperInvariant();
            fromParts.Add(new SqlImportSourcePart(jTable, jAlias, jType, onClause));
        }

        Match whereMatch = Regex.Match(
            sql,
            @"WHERE\s+(.+?)(?=\s+(?:GROUP\s+BY|HAVING|ORDER\s+BY|LIMIT|OFFSET|TOP)|$)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );
        string? whereClause = whereMatch.Success ? whereMatch.Groups[1].Value.Trim() : null;

        Match orderMatch = Regex.Match(
            sql,
            @"ORDER\s+BY\s+(.+?)(?=\s+(?:LIMIT|OFFSET)|$)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );
        string? orderBy = orderMatch.Success ? orderMatch.Groups[1].Value.Trim() : null;

        Match groupMatch = Regex.Match(
            sql,
            @"GROUP\s+BY\s+(.+?)(?=\s+(?:HAVING|ORDER\s+BY|LIMIT|TOP)|$)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );
        string? groupBy = groupMatch.Success ? groupMatch.Groups[1].Value.Trim() : null;

        Match havingMatch = Regex.Match(
            sql,
            @"HAVING\s+(.+?)(?=\s+(?:ORDER\s+BY|LIMIT|TOP)|$)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );
        string? havingClause = havingMatch.Success ? havingMatch.Groups[1].Value.Trim() : null;

        int? limitVal = null;
        Match limitMatch = Regex.Match(sql, @"\bLIMIT\s+(\d+)", RegexOptions.IgnoreCase);
        if (limitMatch.Success)
            limitVal = int.Parse(limitMatch.Groups[1].Value);
        else
        {
            Match topMatch = Regex.Match(sql, @"\bTOP\s+(\d+)", RegexOptions.IgnoreCase);
            if (topMatch.Success)
                limitVal = int.Parse(topMatch.Groups[1].Value);
            else if (topLimit.HasValue)
                limitVal = topLimit.Value;
        }

        var parsed = new SqlImportParsedQuery(
            isDistinct,
            isStar,
            starQualifier,
            selectedCols,
            fromParts,
            whereClause,
            orderBy,
            groupBy,
            havingClause,
            limitVal,
            outerAliases,
            setOperations
        );

        return new SqlImportParseResult(parsed, imported, partial, skipped, false);
    }

    private static (string Table, string? Alias) ExtractTableAndAlias(string part)
    {
        string trimmed = part.Trim();
        Match match = Regex.Match(
            trimmed,
            $@"^({SqlImportIdentifierNormalizer.QualifiedIdentifierPattern})(?:\s+(?:AS\s+)?({SqlImportIdentifierNormalizer.IdentifierPattern}))?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (match.Success)
        {
            string table = SqlImportIdentifierNormalizer.NormalizeQualifiedIdentifier(match.Groups[1].Value);
            string? alias = match.Groups[2].Success
                ? SqlImportIdentifierNormalizer.NormalizeIdentifierToken(match.Groups[2].Value)
                : null;
            return (table, alias);
        }

        string firstToken = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? trimmed;
        return (SqlImportIdentifierNormalizer.NormalizeQualifiedIdentifier(firstToken), null);
    }

    private static List<string> SplitCommas(string value)
    {
        var parts = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '(')
                depth++;
            else if (value[i] == ')')
                depth--;
            else if (value[i] == ',' && depth == 0)
            {
                parts.Add(value[start..i]);
                start = i + 1;
            }
        }

        parts.Add(value[start..]);
        return parts;
    }

    private static bool TryExtractUnionSetOperation(
        string sql,
        out string leftOperandSql,
        out SqlImportSetOperation setOperation)
    {
        leftOperandSql = sql;
        setOperation = default;

        int depth = 0;
        bool inString = false;
        for (int index = 0; index < sql.Length; index++)
        {
            char current = sql[index];
            if (current == '\'')
            {
                bool escapedQuote = index + 1 < sql.Length && sql[index + 1] == '\'';
                if (escapedQuote)
                {
                    index++;
                    continue;
                }

                inString = !inString;
                continue;
            }

            if (inString)
                continue;

            if (current == '(')
            {
                depth++;
                continue;
            }

            if (current == ')')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (depth != 0)
                continue;

            Match match = Regex.Match(
                sql[index..],
                @"^\s*UNION\b(?:\s+(ALL))?",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
                continue;

            string left = sql[..index].Trim();
            string right = sql[(index + match.Length)..].Trim();
            if (string.IsNullOrWhiteSpace(left)
                || string.IsNullOrWhiteSpace(right)
                || !Regex.IsMatch(right.TrimStart('(', ' '), @"^SELECT\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return false;
            }

            leftOperandSql = left;
            setOperation = new SqlImportSetOperation("UNION", match.Groups[1].Success, right);
            return true;
        }

        return false;
    }
}
