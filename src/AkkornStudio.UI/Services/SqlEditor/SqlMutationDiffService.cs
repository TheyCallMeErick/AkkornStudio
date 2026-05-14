using System.Text.RegularExpressions;
using System.Data;
using AkkornStudio.Core;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Services.SqlEditor;

public sealed class SqlMutationDiffService
{
    private readonly Func<string, ConnectionConfig?, int, CancellationToken, Task<SqlEditorResultSet>> _executeAsync;
    private readonly ILocalizationService _localization;

    public SqlMutationDiffService(
        SqlEditorExecutionService executionService,
        ILocalizationService? localization = null)
    {
        ArgumentNullException.ThrowIfNull(executionService);
        _executeAsync = executionService.ExecuteAsync;
        _localization = localization ?? LocalizationService.Instance;
    }

    internal SqlMutationDiffService(
        Func<string, ConnectionConfig?, int, CancellationToken, Task<SqlEditorResultSet>> executeAsync,
        ILocalizationService? localization = null)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        _localization = localization ?? LocalizationService.Instance;
    }

    public async Task<SqlMutationDiffPreview> BuildPreviewAsync(
        string statementSql,
        MutationGuardResult guard,
        ConnectionConfig? config,
        long? estimatedAffectedRows,
        CancellationToken ct = default)
    {
        if (!guard.SupportsDiff)
            return SqlMutationDiffPreview.Unavailable(L("sqlEditor.diff.unavailable.noPreview", "No transactional diff preview available for this statement."));

        string ctePrefix = ExtractLeadingWithPrefix(statementSql);
        string effectiveStatementSql = StripLeadingWithForMutationDispatch(statementSql);
        string upper = effectiveStatementSql.TrimStart().ToUpperInvariant();

        if (upper.StartsWith("INSERT ", StringComparison.Ordinal))
            return await BuildInsertPreviewAsync(effectiveStatementSql, ctePrefix, config, ct);

        if (upper.StartsWith("MERGE ", StringComparison.Ordinal))
            return await BuildMergePreviewAsync(effectiveStatementSql, ctePrefix, guard, config, ct);

        if (string.IsNullOrWhiteSpace(guard.CountQuery))
            return SqlMutationDiffPreview.Unavailable(L("sqlEditor.diff.unavailable.noPreview", "No transactional diff preview available for this statement."));

        ParsedCountQuery? parsed = ParseCountQuery(guard.CountQuery);
        if (parsed is null)
            return SqlMutationDiffPreview.Unavailable(L("sqlEditor.diff.unavailable.parseError", "Could not parse mutation target for transactional diff preview."));

        long? affectedRows = estimatedAffectedRows ?? await ExecuteScalarCountAsync(guard.CountQuery, config, ct);
        long? totalBefore = await ExecuteScalarCountAsync($"SELECT COUNT(*) FROM {parsed.TableName}", config, ct);
        if (!affectedRows.HasValue || !totalBefore.HasValue)
            return SqlMutationDiffPreview.Unavailable(L("sqlEditor.diff.unavailable.connection", "Transactional diff preview unavailable due to connection or query limitations."));

        if (upper.StartsWith("DELETE ", StringComparison.Ordinal))
        {
            long totalAfter = Math.Max(0, totalBefore.Value - affectedRows.Value);
            string rowPreview = await TryBuildRowLevelPreviewAsync(
                "delete",
                effectiveStatementSql,
                guard.CountQuery,
                ctePrefix,
                config,
                ct);
            return new SqlMutationDiffPreview
            {
                Available = true,
                Message = string.Format(
                    L(
                        "sqlEditor.diff.deleteSummary",
                        "Transactional diff preview (ROLLBACK guaranteed): table {0}, total rows before {1}, affected {2}, total rows after {3}."),
                    parsed.TableName,
                    totalBefore.Value,
                    affectedRows.Value,
                    totalAfter)
                    + rowPreview,
            };
        }

        if (upper.StartsWith("TRUNCATE ", StringComparison.Ordinal))
        {
            return new SqlMutationDiffPreview
            {
                Available = true,
                Message = string.Format(
                    L(
                        "sqlEditor.diff.truncateSummary",
                        "Transactional diff preview (ROLLBACK guaranteed): table {0}, total rows before {1}, rows removed {2}, total rows after {3}."),
                    parsed.TableName,
                    totalBefore.Value,
                    totalBefore.Value,
                    0),
            };
        }

        if (upper.StartsWith("UPDATE ", StringComparison.Ordinal))
        {
            string rowPreview = await TryBuildRowLevelPreviewAsync(
                "update",
                effectiveStatementSql,
                guard.CountQuery,
                ctePrefix,
                config,
                ct);
            return new SqlMutationDiffPreview
            {
                Available = true,
                Message = string.Format(
                    L(
                        "sqlEditor.diff.updateSummary",
                        "Transactional diff preview (ROLLBACK guaranteed): table {0}, total rows before {1}, candidate rows affected {2}, total rows after {3}."),
                    parsed.TableName,
                    totalBefore.Value,
                    affectedRows.Value,
                    totalBefore.Value)
                    + rowPreview,
            };
        }

        return SqlMutationDiffPreview.Unavailable(L("sqlEditor.diff.unavailable.unsupportedStatement", "Transactional diff preview currently supports UPDATE and DELETE only."));
    }

    private async Task<SqlMutationDiffPreview> BuildInsertPreviewAsync(
        string statementSql,
        string ctePrefix,
        ConnectionConfig? config,
        CancellationToken ct)
    {
        string? targetTable = ParseInsertTargetTable(statementSql);
        if (string.IsNullOrWhiteSpace(targetTable))
            return SqlMutationDiffPreview.Unavailable(L("sqlEditor.diff.unavailable.parseError", "Could not parse mutation target for transactional diff preview."));

        long? totalBefore = await ExecuteScalarCountAsync($"SELECT COUNT(*) FROM {targetTable}", config, ct);
        if (!totalBefore.HasValue)
            return SqlMutationDiffPreview.Unavailable(L("sqlEditor.diff.unavailable.connection", "Transactional diff preview unavailable due to connection or query limitations."));

        long? insertedRows = TryEstimateInsertedRows(statementSql);
        if (!insertedRows.HasValue)
            insertedRows = await TryEstimateInsertSelectRowsAsync(statementSql, ctePrefix, config, ct);

        if (insertedRows.HasValue)
        {
            return new SqlMutationDiffPreview
            {
                Available = true,
                Message = string.Format(
                    L(
                        "sqlEditor.diff.insertSummary",
                        "Transactional diff preview (ROLLBACK guaranteed): table {0}, total rows before {1}, estimated inserted rows {2}, total rows after {3}."),
                    targetTable,
                    totalBefore.Value,
                    insertedRows.Value,
                    totalBefore.Value + insertedRows.Value),
            };
        }

        return new SqlMutationDiffPreview
        {
            Available = true,
            Message = string.Format(
                L(
                    "sqlEditor.diff.insertSummaryUnknown",
                    "Transactional diff preview (ROLLBACK guaranteed): table {0}, total rows before {1}. Inserted row count could not be estimated from this INSERT shape."),
                targetTable,
                totalBefore.Value),
        };
    }

    private async Task<SqlMutationDiffPreview> BuildMergePreviewAsync(
        string statementSql,
        string ctePrefix,
        MutationGuardResult guard,
        ConnectionConfig? config,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(guard.CountQuery))
            return SqlMutationDiffPreview.Unavailable(L("sqlEditor.diff.unavailable.noPreview", "No transactional diff preview available for this statement."));

        ParsedCountQuery? parsed = ParseCountQuery(guard.CountQuery);
        if (parsed is null)
            return SqlMutationDiffPreview.Unavailable(L("sqlEditor.diff.unavailable.parseError", "Could not parse mutation target for transactional diff preview."));

        long? totalBefore = await ExecuteScalarCountAsync(guard.CountQuery, config, ct);
        if (!totalBefore.HasValue)
            return SqlMutationDiffPreview.Unavailable(L("sqlEditor.diff.unavailable.connection", "Transactional diff preview unavailable due to connection or query limitations."));

        string mergeBranches = DescribeMergeBranches(statementSql);
        long? sourceRows = await TryEstimateMergeSourceRowsAsync(statementSql, ctePrefix, config, ct);
        MergeOperationEstimate? operationEstimate = await TryEstimateMergeOperationsAsync(
            statementSql,
            ctePrefix,
            parsed.TableName,
            totalBefore.Value,
            sourceRows,
            config,
            ct);
        string operationSummary = operationEstimate is null
            ? string.Empty
            : string.Format(
                L(
                    "sqlEditor.diff.mergeOperationSummary",
                    " Estimated branch candidates: matched {0}, not matched by target {1}, not matched by source {2}."),
                operationEstimate.MatchedRows,
                operationEstimate.NotMatchedByTargetRows,
                operationEstimate.NotMatchedBySourceRows);

        if (sourceRows.HasValue)
        {
            return new SqlMutationDiffPreview
            {
                Available = true,
                Message = string.Format(
                    L(
                        "sqlEditor.diff.mergeSummary",
                        "Transactional diff preview (ROLLBACK guaranteed): MERGE target {0}, total rows before {1}, source candidate rows {2}. Detected branches: {3}."),
                    parsed.TableName,
                    totalBefore.Value,
                    sourceRows.Value,
                    mergeBranches)
                    + operationSummary,
            };
        }

        return new SqlMutationDiffPreview
        {
            Available = true,
            Message = string.Format(
                L(
                    "sqlEditor.diff.mergeSummaryUnknown",
                    "Transactional diff preview (ROLLBACK guaranteed): MERGE target {0}, total rows before {1}. Source candidate row count could not be estimated from this MERGE shape. Detected branches: {2}."),
                parsed.TableName,
                totalBefore.Value,
                mergeBranches)
                + operationSummary,
        };
    }

    private async Task<long?> ExecuteScalarCountAsync(string sql, ConnectionConfig? config, CancellationToken ct)
    {
        SqlEditorResultSet result = await _executeAsync(sql, config, 1, ct);
        if (!result.Success || result.Data is null || result.Data.Rows.Count == 0 || result.Data.Columns.Count == 0)
            return null;

        object? value = result.Data.Rows[0][0];
        if (value is null || value is DBNull)
            return null;

        return long.TryParse(value.ToString(), out long parsed) ? parsed : null;
    }

    private async Task<string> TryBuildRowLevelPreviewAsync(
        string mutationKind,
        string statementSql,
        string countQuery,
        string ctePrefix,
        ConnectionConfig? config,
        CancellationToken ct)
    {
        string? sampleSql = BuildSampleRowsSql(countQuery, config?.Provider ?? DatabaseProvider.Postgres);
        if (string.IsNullOrWhiteSpace(sampleSql))
            return string.Empty;

        try
        {
            SqlEditorResultSet result = await _executeAsync(sampleSql, config, 5, ct);
            if (!result.Success || result.Data is null || result.Data.Rows.Count == 0)
                return string.Empty;

            string sample = FormatSampleRows(result.Data, maxRows: 5, maxColumns: 6);
            if (string.IsNullOrWhiteSpace(sample))
                return string.Empty;

            if (mutationKind.Equals("update", StringComparison.OrdinalIgnoreCase)
                && TryExtractUpdateAssignments(statementSql, out IReadOnlyList<string> assignments)
                && assignments.Count > 0)
            {
                return string.Format(
                    L(
                        "sqlEditor.diff.rowPreview.update",
                        " Row-level before/after sample (max 5): before [{0}]. After SET preview: {1}."),
                    sample,
                    string.Join(", ", assignments));
            }

            return string.Format(
                L(
                    "sqlEditor.diff.rowPreview.delete",
                    " Row-level before sample (max 5 deleted candidates): [{0}]."),
                sample);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string? BuildSampleRowsSql(string countQuery, DatabaseProvider provider)
    {
        MatchCollection matches = Regex.Matches(
            countQuery,
            @"\bSELECT\s+COUNT\(\*\)\s+FROM\s+(?<tail>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (matches.Count == 0)
            return null;

        Match match = matches[^1];
        string prefix = countQuery[..match.Index].Trim();
        string tail = match.Groups["tail"].Value.Trim().TrimEnd(';');
        if (string.IsNullOrWhiteSpace(tail))
            return null;

        string select = provider == DatabaseProvider.SqlServer
            ? $"SELECT TOP 5 * FROM {tail}"
            : $"SELECT * FROM {tail} LIMIT 5";

        return string.IsNullOrWhiteSpace(prefix) ? select : $"{prefix} {select}";
    }

    private static string FormatSampleRows(DataTable data, int maxRows, int maxColumns)
    {
        if (data.Rows.Count == 0 || data.Columns.Count == 0)
            return string.Empty;

        int rowCount = Math.Min(maxRows, data.Rows.Count);
        int columnCount = Math.Min(maxColumns, data.Columns.Count);
        var rows = new List<string>(rowCount);

        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            DataRow row = data.Rows[rowIndex];
            var values = new List<string>(columnCount);
            for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                DataColumn column = data.Columns[columnIndex];
                object? value = row[columnIndex];
                string display = value is null or DBNull
                    ? "NULL"
                    : value.ToString() ?? string.Empty;
                values.Add($"{column.ColumnName}={display}");
            }

            rows.Add("{" + string.Join(", ", values) + "}");
        }

        return string.Join("; ", rows);
    }

    private static bool TryExtractUpdateAssignments(
        string statementSql,
        out IReadOnlyList<string> assignments)
    {
        assignments = [];
        Match match = Regex.Match(
            statementSql,
            @"\bSET\s+(?<set>.+?)(?:\s+FROM\b|\s+WHERE\b|$)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (!match.Success)
            return false;

        var parsed = new List<string>();
        foreach (string assignment in SplitTopLevelComma(match.Groups["set"].Value))
        {
            Match assignmentMatch = Regex.Match(
                assignment.Trim(),
                @"^(?<column>[A-Za-z_][A-Za-z0-9_\.]*)\s*=\s*(?<value>.+)$",
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
            if (!assignmentMatch.Success)
                continue;

            string value = assignmentMatch.Groups["value"].Value.Trim();
            if (!IsSimplePreviewValue(value))
                continue;

            parsed.Add($"{assignmentMatch.Groups["column"].Value.Trim()} => {value.TrimEnd(';')}");
        }

        assignments = parsed;
        return parsed.Count > 0;
    }

    private static IReadOnlyList<string> SplitTopLevelComma(string value)
    {
        var parts = new List<string>();
        int depth = 0;
        bool inSingleQuote = false;
        int start = 0;

        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (current == '\'' && (index == 0 || value[index - 1] != '\\'))
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (inSingleQuote)
                continue;

            if (current == '(')
                depth++;
            else if (current == ')')
                depth = Math.Max(0, depth - 1);
            else if (current == ',' && depth == 0)
            {
                parts.Add(value[start..index]);
                start = index + 1;
            }
        }

        parts.Add(value[start..]);
        return parts;
    }

    private static bool IsSimplePreviewValue(string value)
    {
        string trimmed = value.Trim().TrimEnd(';');
        return Regex.IsMatch(
            trimmed,
            @"^(NULL|TRUE|FALSE|-?\d+(?:\.\d+)?|'([^']|'')*')$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static ParsedCountQuery? ParseCountQuery(string countQuery)
    {
        MatchCollection matches = Regex.Matches(
            countQuery,
            @"\bSELECT\s+COUNT\(\*\)\s+FROM\s+([^\s;]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (matches.Count == 0)
            return null;

        Match match = matches[^1];
        return new ParsedCountQuery(match.Groups[1].Value.Trim());
    }

    private sealed record ParsedCountQuery(string TableName);

    private sealed record MergeOperationEstimate(long MatchedRows, long NotMatchedByTargetRows, long NotMatchedBySourceRows);

    private static string? ParseInsertTargetTable(string statementSql)
    {
        Match match = Regex.Match(
            statementSql,
            @"^\s*INSERT\s+INTO\s+([^\s(]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static long? TryEstimateInsertedRows(string statementSql)
    {
        if (Regex.IsMatch(
                statementSql,
                @"^\s*INSERT\s+INTO\s+[^\s(]+(?:\s*\([^)]*\))?\s+DEFAULT\s+VALUES\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return 1;
        }

        Match valuesMatch = Regex.Match(
            statementSql,
            @"\bVALUES\b(?<values>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (!valuesMatch.Success)
            return null;

        string valuesSegment = valuesMatch.Groups["values"].Value;
        int tupleCount = Regex.Matches(valuesSegment, @"\(", RegexOptions.CultureInvariant).Count;
        return tupleCount > 0 ? tupleCount : null;
    }

    private async Task<long?> TryEstimateInsertSelectRowsAsync(
        string statementSql,
        string ctePrefix,
        ConnectionConfig? config,
        CancellationToken ct)
    {
        string? selectSource = TryExtractInsertSelectSource(statementSql);
        if (string.IsNullOrWhiteSpace(selectSource))
            return null;

        string countSql = $"SELECT COUNT(*) FROM ({selectSource}) AS akkorn_insert_src";
        return await ExecuteScalarCountAsync(
            PrefixWithClause(countSql, ctePrefix),
            config,
            ct);
    }

    private static string? TryExtractInsertSelectSource(string statementSql)
    {
        Match match = Regex.Match(
            statementSql,
            @"^\s*INSERT\s+INTO\s+[^\s(]+(?:\s*\([^)]*\))?\s+(?<source>SELECT\b.+?)(?:\s+RETURNING\b.+)?\s*;?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["source"].Value.Trim() : null;
    }

    private async Task<long?> TryEstimateMergeSourceRowsAsync(
        string statementSql,
        string ctePrefix,
        ConnectionConfig? config,
        CancellationToken ct)
    {
        string? source = TryExtractMergeSource(statementSql);
        if (string.IsNullOrWhiteSpace(source))
            return null;

        if (TryExtractParenthesizedMergeSourceSelect(source, out string? innerSelect))
        {
            if (!innerSelect.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                return null;

            string countSql = $"SELECT COUNT(*) FROM ({innerSelect}) AS akkorn_merge_src";
            return await ExecuteScalarCountAsync(
                PrefixWithClause(countSql, ctePrefix),
                config,
                ct);
        }

        string sourceTable = ExtractLeadingTableToken(source);
        if (string.IsNullOrWhiteSpace(sourceTable))
            return null;

        return await ExecuteScalarCountAsync(PrefixWithClause($"SELECT COUNT(*) FROM {sourceTable}", ctePrefix), config, ct);
    }

    private static string? TryExtractMergeSource(string statementSql)
    {
        Match match = Regex.Match(
            statementSql,
            @"\bUSING\s+(?<source>\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\)(?:\s+(?:AS\s+)?[A-Za-z_][A-Za-z0-9_]*)?|[^\s;]+(?:\s+[A-Za-z_][A-Za-z0-9_]*)?)\s+ON\b",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["source"].Value.Trim() : null;
    }

    private async Task<MergeOperationEstimate?> TryEstimateMergeOperationsAsync(
        string statementSql,
        string ctePrefix,
        string targetTable,
        long totalBefore,
        long? sourceRows,
        ConnectionConfig? config,
        CancellationToken ct)
    {
        if (!sourceRows.HasValue)
            return null;

        string? source = TryExtractMergeSource(statementSql);
        string? onClause = TryExtractMergeOnClause(statementSql);
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(onClause))
            return null;

        string targetClause = TryExtractMergeTargetClause(statementSql) ?? targetTable;
        string sourceClause = NormalizeMergeSourceClause(source);
        if (string.IsNullOrWhiteSpace(sourceClause))
            return null;

        string matchedSql = $"SELECT COUNT(*) FROM {targetClause} INNER JOIN {sourceClause} ON {onClause}";
        long? matchedRows = await ExecuteScalarCountAsync(
            PrefixWithClause(matchedSql, ctePrefix),
            config,
            ct);

        if (!matchedRows.HasValue)
            return null;

        return new MergeOperationEstimate(
            matchedRows.Value,
            Math.Max(0, sourceRows.Value - matchedRows.Value),
            Math.Max(0, totalBefore - matchedRows.Value));
    }

    private static string? TryExtractMergeOnClause(string statementSql)
    {
        Match match = Regex.Match(
            statementSql,
            @"\bON\s+(?<on>.+?)\s+\bWHEN\b",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["on"].Value.Trim() : null;
    }

    private static string? TryExtractMergeTargetClause(string statementSql)
    {
        Match match = Regex.Match(
            statementSql,
            @"^\s*MERGE\s+INTO\s+(?<target>[^\s;]+)(?:\s+(?:AS\s+)?(?<alias>[A-Za-z_][A-Za-z0-9_]*))?\s+USING\b",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (!match.Success)
            return null;

        string target = match.Groups["target"].Value.Trim();
        return match.Groups["alias"].Success
            ? $"{target} {match.Groups["alias"].Value.Trim()}"
            : target;
    }

    private static string NormalizeMergeSourceClause(string source)
    {
        return source.Trim();
    }

    private static bool TryExtractParenthesizedMergeSourceSelect(string source, out string innerSelect)
    {
        innerSelect = string.Empty;
        string trimmed = source.Trim();
        if (!trimmed.StartsWith("(", StringComparison.Ordinal))
            return false;

        int depth = 0;
        bool inSingleQuote = false;
        int closingIndex = -1;
        for (int i = 0; i < trimmed.Length; i++)
        {
            char current = trimmed[i];
            if (current == '\'' && (i == 0 || trimmed[i - 1] != '\\'))
                inSingleQuote = !inSingleQuote;

            if (inSingleQuote)
                continue;

            if (current == '(')
                depth++;
            else if (current == ')')
            {
                depth--;
                if (depth == 0)
                {
                    closingIndex = i;
                    break;
                }
            }
        }

        if (closingIndex <= 0)
            return false;

        string extracted = trimmed[1..closingIndex].Trim();
        string tail = trimmed[(closingIndex + 1)..].Trim();
        if (!string.IsNullOrWhiteSpace(tail)
            && !Regex.IsMatch(tail, @"^(?:AS\s+)?[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return false;
        }

        innerSelect = extracted;
        return true;
    }

    private static string ExtractLeadingTableToken(string source)
    {
        Match match = Regex.Match(
            source,
            @"^[^\s;]+",
            RegexOptions.CultureInvariant);
        return match.Success ? match.Value.Trim() : string.Empty;
    }

    private static string DescribeMergeBranches(string statementSql)
    {
        List<string> branches = [];
        string normalized = statementSql.ToUpperInvariant();

        if (Regex.IsMatch(normalized, @"\bWHEN\s+MATCHED\b", RegexOptions.CultureInvariant))
            branches.Add("MATCHED");
        if (Regex.IsMatch(normalized, @"\bWHEN\s+NOT\s+MATCHED\s+BY\s+SOURCE\b", RegexOptions.CultureInvariant))
            branches.Add("NOT MATCHED BY SOURCE");
        if (Regex.IsMatch(normalized, @"\bWHEN\s+NOT\s+MATCHED\b", RegexOptions.CultureInvariant)
            && !branches.Contains("NOT MATCHED BY SOURCE", StringComparer.Ordinal))
        {
            branches.Add("NOT MATCHED");
        }

        return branches.Count == 0
            ? "unknown"
            : string.Join(", ", branches);
    }

    private string L(string key, string fallback)
    {
        string value = _localization[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    private static string StripLeadingWithForMutationDispatch(string statementSql)
    {
        int index = 0;
        while (index < statementSql.Length && char.IsWhiteSpace(statementSql[index]))
            index++;

        if (!statementSql.AsSpan(index).StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
            return statementSql;

        index += 4;
        bool consumedDefinition = false;
        while (index < statementSql.Length)
        {
            while (index < statementSql.Length && (char.IsWhiteSpace(statementSql[index]) || statementSql[index] == ','))
                index++;

            if (index >= statementSql.Length || !IsIdentifierStart(statementSql[index]))
                return statementSql;

            index++;
            while (index < statementSql.Length && IsIdentifierPart(statementSql[index]))
                index++;

            while (index < statementSql.Length && char.IsWhiteSpace(statementSql[index]))
                index++;

            if (index < statementSql.Length && statementSql[index] == '(')
            {
                if (!TrySkipBalancedParentheses(statementSql, ref index))
                    return statementSql;

                while (index < statementSql.Length && char.IsWhiteSpace(statementSql[index]))
                    index++;
            }

            if (!statementSql.AsSpan(index).StartsWith("AS", StringComparison.OrdinalIgnoreCase))
                return statementSql;

            index += 2;
            while (index < statementSql.Length && char.IsWhiteSpace(statementSql[index]))
                index++;

            if (index >= statementSql.Length || statementSql[index] != '(')
                return statementSql;

            if (!TrySkipBalancedParentheses(statementSql, ref index))
                return statementSql;

            consumedDefinition = true;
            while (index < statementSql.Length && char.IsWhiteSpace(statementSql[index]))
                index++;

            if (index < statementSql.Length && statementSql[index] == ',')
            {
                index++;
                continue;
            }

            break;
        }

        return consumedDefinition && index < statementSql.Length
            ? statementSql[index..].TrimStart()
            : statementSql;
    }

    private static string ExtractLeadingWithPrefix(string statementSql)
    {
        int index = 0;
        while (index < statementSql.Length && char.IsWhiteSpace(statementSql[index]))
            index++;

        if (!statementSql.AsSpan(index).StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        int start = index;
        index += 4;
        bool consumedDefinition = false;
        while (index < statementSql.Length)
        {
            while (index < statementSql.Length && (char.IsWhiteSpace(statementSql[index]) || statementSql[index] == ','))
                index++;

            if (index >= statementSql.Length || !IsIdentifierStart(statementSql[index]))
                return string.Empty;

            index++;
            while (index < statementSql.Length && IsIdentifierPart(statementSql[index]))
                index++;

            while (index < statementSql.Length && char.IsWhiteSpace(statementSql[index]))
                index++;

            if (index < statementSql.Length && statementSql[index] == '(')
            {
                if (!TrySkipBalancedParentheses(statementSql, ref index))
                    return string.Empty;

                while (index < statementSql.Length && char.IsWhiteSpace(statementSql[index]))
                    index++;
            }

            if (!statementSql.AsSpan(index).StartsWith("AS", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            index += 2;
            while (index < statementSql.Length && char.IsWhiteSpace(statementSql[index]))
                index++;

            if (index >= statementSql.Length || statementSql[index] != '(')
                return string.Empty;

            if (!TrySkipBalancedParentheses(statementSql, ref index))
                return string.Empty;

            consumedDefinition = true;
            while (index < statementSql.Length && char.IsWhiteSpace(statementSql[index]))
                index++;

            if (index < statementSql.Length && statementSql[index] == ',')
            {
                index++;
                continue;
            }

            break;
        }

        return consumedDefinition && index < statementSql.Length
            ? statementSql[start..index].Trim()
            : string.Empty;
    }

    private static string PrefixWithClause(string sql, string ctePrefix) =>
        string.IsNullOrWhiteSpace(ctePrefix) ? sql : $"{ctePrefix} {sql}";

    private static bool TrySkipBalancedParentheses(string value, ref int index)
    {
        if (index >= value.Length || value[index] != '(')
            return false;

        int depth = 0;
        bool inSingleQuote = false;
        while (index < value.Length)
        {
            char current = value[index];
            if (current == '\'' && (index == 0 || value[index - 1] != '\\'))
                inSingleQuote = !inSingleQuote;

            if (!inSingleQuote)
            {
                if (current == '(')
                    depth++;
                else if (current == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        index++;
                        return true;
                    }
                }
            }

            index++;
        }

        return false;
    }

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';
}
