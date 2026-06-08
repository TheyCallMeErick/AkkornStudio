namespace AkkornStudio.UI.Services.SqlImport.Execution.Parsing;

public readonly record struct SqlImportSelectedColumn(string Expr, string? Alias);

public readonly record struct SqlImportSourcePart(string Table, string? Alias, string? JoinType, string? OnClause);

public readonly record struct SqlImportSetOperation(string Operator, bool IsAll, string RightSql);

public sealed record SqlImportParsedQuery(
    bool IsDistinct,
    bool IsStar,
    string? StarQualifier,
    IReadOnlyList<SqlImportSelectedColumn> SelectedColumns,
    IReadOnlyList<SqlImportSourcePart> FromParts,
    string? WhereClause,
    string? OrderBy,
    string? GroupBy,
    string? HavingClause,
    int? Limit,
    HashSet<string> OuterAliases,
    IReadOnlyList<SqlImportSetOperation> SetOperations
);
