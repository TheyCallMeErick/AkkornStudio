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
        int maxRows = 120,
        string? whereColumn = null,
        object? whereValue = null)
    {
        string tableExpression = QuoteCompositeIdentifier(provider, tableFullName);
        string head = provider == DatabaseProvider.SqlServer
            ? $"SELECT TOP {Math.Max(1, maxRows)} *\nFROM {tableExpression}"
            : $"SELECT *\nFROM {tableExpression}";

        if (!string.IsNullOrWhiteSpace(whereColumn))
        {
            string where = whereValue is null || whereValue == DBNull.Value
                ? $"{QuoteIdentifier(provider, whereColumn)} IS NULL"
                : $"{QuoteIdentifier(provider, whereColumn)} = {ToSqlLiteral(whereValue)}";
            head = $"{head}\nWHERE {where}";
        }

        if (provider != DatabaseProvider.SqlServer)
            head = $"{head}\nLIMIT {Math.Max(1, maxRows)}";

        return head + ";";
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
