using System.Data;
using System.Text.RegularExpressions;
using AkkornStudio.Core;
using AkkornStudio.Metadata;

namespace AkkornStudio.UI.Services.SqlEditor;

public sealed class SqlResultEligibilityDetector
{
    private static readonly Regex FromRegex = new(
        @"\bFROM\s+(?<table>[A-Za-z0-9_.""`\[\]]+)(?:\s+(?:AS\s+)?(?<alias>[A-Za-z_][A-Za-z0-9_]*))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SelectRegex = new(
        @"^\s*SELECT\s+(?<select>.+?)\s+FROM\s+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    public SqlInlineEditEligibility Evaluate(
        string? statementSql,
        DataTable resultTable,
        DbMetadata? metadata,
        ConnectionConfig? connectionConfig)
    {
        if (string.IsNullOrWhiteSpace(statementSql) || metadata is null || connectionConfig is null)
            return SqlInlineEditEligibility.Ineligible("Metadata or connection context is missing.");

        if (IsReadOnlyConnection(connectionConfig))
            return SqlInlineEditEligibility.Ineligible("Connection is read-only.");

        string sql = statementSql.Trim();
        if (!sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            return SqlInlineEditEligibility.Ineligible("Only SELECT result sets are eligible.");

        if (ContainsAny(sql, " JOIN ", " GROUP BY ", " HAVING ", " DISTINCT ", " UNION ", " INTERSECT ", " EXCEPT "))
            return SqlInlineEditEligibility.Ineligible("Query shape is complex (join/group/union) and is treated as read-only.");

        Match fromMatch = FromRegex.Match(sql);
        if (!fromMatch.Success)
            return SqlInlineEditEligibility.Ineligible("Could not resolve target table from query.");

        string tableRef = fromMatch.Groups["table"].Value.Trim();
        if (string.IsNullOrWhiteSpace(tableRef))
            return SqlInlineEditEligibility.Ineligible("Could not resolve target table from query.");

        TableMetadata? table = ResolveTable(metadata, tableRef);
        if (table is null || table.Kind != TableKind.Table)
            return SqlInlineEditEligibility.Ineligible("Target source is not a base table.");

        string? sourceAlias = fromMatch.Groups["alias"].Success
            ? fromMatch.Groups["alias"].Value.Trim()
            : null;

        if (!TryParseSelectColumns(sql, resultTable, sourceAlias, out IReadOnlyList<string> selectedColumns))
            return SqlInlineEditEligibility.Ineligible("SELECT list cannot be safely mapped to editable columns.");

        if (selectedColumns.Count == 0)
            return SqlInlineEditEligibility.Ineligible("No editable columns detected in SELECT list.");

        if (selectedColumns.Any(static column => IsAggregateName(column)))
            return SqlInlineEditEligibility.Ineligible("Aggregate projections are read-only.");

        var resultColumnNames = new HashSet<string>(
            resultTable.Columns.Cast<DataColumn>().Select(static c => c.ColumnName),
            StringComparer.OrdinalIgnoreCase);

        List<string> pkColumns = table.PrimaryKeyColumns
            .Select(static c => c.Name)
            .Where(resultColumnNames.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (pkColumns.Count == 0 || pkColumns.Count != table.PrimaryKeyColumns.Count)
            return SqlInlineEditEligibility.Ineligible("Primary key columns are missing in result set.");

        List<string> editableColumns = selectedColumns
            .Where(resultColumnNames.Contains)
            .Where(name => !pkColumns.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (editableColumns.Count == 0)
            return SqlInlineEditEligibility.Ineligible("No non-key editable columns were found.");

        return new SqlInlineEditEligibility(
            true,
            table.FullName,
            pkColumns,
            editableColumns);
    }

    private static bool TryParseSelectColumns(
        string sql,
        DataTable resultTable,
        string? sourceAlias,
        out IReadOnlyList<string> columns)
    {
        columns = [];
        Match match = SelectRegex.Match(sql);
        if (!match.Success)
            return false;

        string list = match.Groups["select"].Value;
        if (string.IsNullOrWhiteSpace(list))
            return false;

        string trimmedList = list.Trim();
        bool isStarProjection =
            string.Equals(trimmedList, "*", StringComparison.Ordinal)
            || (!string.IsNullOrWhiteSpace(sourceAlias)
                && string.Equals(trimmedList, $"{sourceAlias}.*", StringComparison.OrdinalIgnoreCase));
        if (isStarProjection)
        {
            columns = resultTable.Columns
                .Cast<DataColumn>()
                .Select(static column => column.ColumnName)
                .ToList();
            return columns.Count > 0;
        }

        string[] entries = list
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (entries.Length == 0)
            return false;

        var parsed = new List<string>(entries.Length);
        foreach (string entry in entries)
        {
            if (entry.Contains('(') || entry.Contains(')') || entry.Contains('*'))
                return false;

            string normalized = entry.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            string[] aliasSplit = Regex.Split(normalized, @"\s+AS\s+", RegexOptions.IgnoreCase);
            if (aliasSplit.Length > 2)
                return false;

            string expression = aliasSplit[0].Trim();
            string[] expressionParts = expression.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (expressionParts.Length is < 1 or > 2)
                return false;

            string columnName = expressionParts[^1];
            if (!Regex.IsMatch(columnName, @"^[A-Za-z_][A-Za-z0-9_]*$"))
                return false;

            if (aliasSplit.Length == 2 && !Regex.IsMatch(aliasSplit[1], @"^[A-Za-z_][A-Za-z0-9_]*$"))
                return false;

            parsed.Add(columnName);
        }

        columns = parsed;
        return true;
    }

    private static TableMetadata? ResolveTable(DbMetadata metadata, string tableRef)
    {
        string normalized = UnquoteIdentifier(tableRef.Trim());
        return metadata.AllTables.FirstOrDefault(t =>
                   t.FullName.Equals(normalized, StringComparison.OrdinalIgnoreCase))
               ?? metadata.AllTables.FirstOrDefault(t =>
                   t.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        foreach (string needle in needles)
        {
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsAggregateName(string name)
    {
        return name.Equals("COUNT", StringComparison.OrdinalIgnoreCase)
               || name.Equals("SUM", StringComparison.OrdinalIgnoreCase)
               || name.Equals("AVG", StringComparison.OrdinalIgnoreCase)
               || name.Equals("MIN", StringComparison.OrdinalIgnoreCase)
               || name.Equals("MAX", StringComparison.OrdinalIgnoreCase)
               || name.Equals("ARRAY_AGG", StringComparison.OrdinalIgnoreCase)
               || name.Equals("STRING_AGG", StringComparison.OrdinalIgnoreCase);
    }

    private static string UnquoteIdentifier(string identifier)
    {
        return identifier
            .Trim()
            .Trim('"')
            .Trim('`')
            .Trim('[', ']');
    }

    private static bool IsReadOnlyConnection(ConnectionConfig config)
    {
        if (config.ExtraParameters is null)
            return false;

        if (!config.ExtraParameters.TryGetValue("ReadOnly", out string? value))
            return false;

        return bool.TryParse(value, out bool parsed) && parsed;
    }
}
