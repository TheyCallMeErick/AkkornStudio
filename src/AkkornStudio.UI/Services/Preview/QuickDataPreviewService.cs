using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.UI.Services.SqlEditor;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Services.Preview;

public sealed record QuickDataPreviewRequest(
    string Sql,
    ConnectionConfig? Connection,
    DatabaseProvider Provider,
    DbMetadata? Metadata,
    string? FocusTableFullName,
    int MaxRows = 120);

public sealed record QuickDataPreviewRelationship(
    string SourceTable,
    string SourceColumn,
    string TargetTable,
    string TargetColumn,
    string Cardinality,
    string DirectionLabel);

public sealed record QuickDataPreviewResult(
    string Sql,
    SqlEditorResultSet Execution,
    IReadOnlyList<QuickDataPreviewRelationship> Relationships);

public sealed class QuickDataPreviewService
{
    private readonly SqlEditorExecutionService _executionService;

    public QuickDataPreviewService(SqlEditorExecutionService? executionService = null)
    {
        _executionService = executionService ?? new SqlEditorExecutionService();
    }

    public async Task<QuickDataPreviewResult> ExecuteAsync(
        QuickDataPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        SqlEditorResultSet execution = await _executionService.ExecuteAsync(
            request.Sql,
            request.Connection,
            maxRows: Math.Max(1, request.MaxRows),
            ct: cancellationToken);

        IReadOnlyList<QuickDataPreviewRelationship> relationships = ResolveRelationships(
            request.Metadata,
            request.FocusTableFullName);

        return new QuickDataPreviewResult(
            request.Sql,
            execution,
            relationships);
    }

    public string BuildTablePreviewSql(
        DatabaseProvider provider,
        string tableFullName,
        int maxRows = 50,
        string? whereColumn = null,
        object? whereValue = null,
        int offset = 0)
    {
        string tableExpression = QuoteCompositeIdentifier(provider, tableFullName);
        int safeRows = Math.Max(1, maxRows);
        int safeOffset = Math.Max(0, offset);

        var parts = new System.Text.StringBuilder();

        if (provider == DatabaseProvider.SqlServer && safeOffset == 0)
            parts.AppendLine($"SELECT TOP {safeRows} *");
        else
            parts.AppendLine("SELECT *");

        parts.Append($"FROM {tableExpression}");

        if (!string.IsNullOrWhiteSpace(whereColumn))
        {
            string where = whereValue is null || whereValue == DBNull.Value
                ? $"{QuoteIdentifier(provider, whereColumn)} IS NULL"
                : $"{QuoteIdentifier(provider, whereColumn)} = {ToSqlLiteral(whereValue)}";
            parts.AppendLine();
            parts.Append($"WHERE {where}");
        }

        if (provider == DatabaseProvider.SqlServer)
        {
            if (safeOffset > 0)
            {
                parts.AppendLine();
                parts.AppendLine("ORDER BY (SELECT NULL)");
                parts.Append($"OFFSET {safeOffset} ROWS FETCH NEXT {safeRows} ROWS ONLY");
            }
        }
        else
        {
            parts.AppendLine();
            parts.Append(safeOffset > 0
                ? $"LIMIT {safeRows} OFFSET {safeOffset}"
                : $"LIMIT {safeRows}");
        }

        parts.Append(';');
        return parts.ToString();
    }

    public static string StripWhereClause(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return sql;

        // Find WHERE keyword at word boundary
        int whereIdx = -1;
        int searchFrom = 0;
        while (searchFrom < sql.Length)
        {
            int idx = sql.IndexOf("WHERE", searchFrom, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                break;
            bool leftOk = idx == 0 || !char.IsLetterOrDigit(sql[idx - 1]);
            bool rightOk = idx + 5 >= sql.Length || !char.IsLetterOrDigit(sql[idx + 5]);
            if (leftOk && rightOk)
            {
                whereIdx = idx;
                break;
            }
            searchFrom = idx + 1;
        }

        if (whereIdx < 0)
            return sql;

        string[] terminators = ["LIMIT ", "LIMIT\n", "ORDER BY", "GROUP BY", "HAVING ", "OFFSET "];
        int endIdx = -1;
        string afterWhere = sql[whereIdx..];
        foreach (string term in terminators)
        {
            int idx = afterWhere.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (idx > 0 && (endIdx < 0 || idx < endIdx))
                endIdx = idx;
        }

        string result;
        if (endIdx < 0)
        {
            result = sql[..whereIdx].TrimEnd();
            if (!result.EndsWith(';'))
                result += ";";
        }
        else
        {
            result = sql[..whereIdx] + afterWhere[endIdx..];
        }

        return result;
    }

    public IReadOnlyList<QuickDataPreviewRelationship> ResolveRelationships(
        DbMetadata? metadata,
        string? focusTableFullName)
    {
        if (metadata is null || string.IsNullOrWhiteSpace(focusTableFullName))
            return [];

        string normalizedFocus = NormalizeTableName(focusTableFullName);
        if (string.IsNullOrWhiteSpace(normalizedFocus))
            return [];

        var relationships = new List<QuickDataPreviewRelationship>();

        foreach (ForeignKeyRelation relation in metadata.AllForeignKeys)
        {
            string child = NormalizeTableName(relation.ChildFullTable);
            string parent = NormalizeTableName(relation.ParentFullTable);

            if (string.Equals(child, normalizedFocus, StringComparison.OrdinalIgnoreCase))
            {
                relationships.Add(new QuickDataPreviewRelationship(
                    relation.ChildFullTable,
                    relation.ChildColumn,
                    relation.ParentFullTable,
                    relation.ParentColumn,
                    "N:1",
                    "FK -> Referencia"));
                continue;
            }

            if (string.Equals(parent, normalizedFocus, StringComparison.OrdinalIgnoreCase))
            {
                relationships.Add(new QuickDataPreviewRelationship(
                    relation.ParentFullTable,
                    relation.ParentColumn,
                    relation.ChildFullTable,
                    relation.ChildColumn,
                    "1:N",
                    "Referenciada por"));
            }
        }

        return relationships
            .OrderBy(item => item.TargetTable, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.TargetColumn, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string QuoteCompositeIdentifier(DatabaseProvider provider, string fullName)
    {
        string[] parts = fullName
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => QuoteIdentifier(provider, part))
            .ToArray();

        return parts.Length == 0 ? QuoteIdentifier(provider, fullName) : string.Join('.', parts);
    }

    private static string QuoteIdentifier(DatabaseProvider provider, string identifier)
    {
        string clean = (identifier ?? string.Empty).Trim().Trim('"', '[', ']', '`');
        return provider switch
        {
            DatabaseProvider.MySql => $"`{clean}`",
            DatabaseProvider.SqlServer => $"[{clean}]",
            _ => $"\"{clean}\"",
        };
    }

    private static string ToSqlLiteral(object value)
    {
        if (value is null || value == DBNull.Value)
            return "NULL";

        return value switch
        {
            bool boolean => boolean ? "1" : "0",
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
                => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "0",
            DateTime dateTime => $"'{dateTime:yyyy-MM-dd HH:mm:ss.fff}'",
            DateTimeOffset dateTimeOffset => $"'{dateTimeOffset:yyyy-MM-dd HH:mm:ss.fff zzz}'",
            _ => $"'{EscapeSqlLiteral(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)}'",
        };
    }

    private static string EscapeSqlLiteral(string value) =>
        (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);

    private static string NormalizeTableName(string? tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            return string.Empty;

        string[] parts = tableName
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Trim().Trim('"', '[', ']', '`'))
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        if (parts.Length == 0)
            return string.Empty;

        return parts.Length > 2
            ? $"{parts[^2]}.{parts[^1]}"
            : string.Join('.', parts);
    }
}
