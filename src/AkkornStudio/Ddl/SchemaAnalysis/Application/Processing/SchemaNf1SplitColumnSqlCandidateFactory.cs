using AkkornStudio.Core;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Contracts;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Enums;

namespace AkkornStudio.Ddl.SchemaAnalysis.Application.Processing;

public sealed class SchemaNf1SplitColumnSqlCandidateFactory
{
    private readonly SchemaSqlDialectEscaping _escaping = new();

    public SqlFixCandidate? CreateCandidate(
        SchemaIssue issue,
        DatabaseProvider provider,
        string suggestionId
    )
    {
        ArgumentNullException.ThrowIfNull(issue);
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestionId);

        if (issue.RuleCode != SchemaRuleCode.NF1_HINT_MULTI_VALUED
            || issue.TargetType != SchemaTargetType.Column
            || string.IsNullOrWhiteSpace(issue.TableName)
            || string.IsNullOrWhiteSpace(issue.ColumnName))
        {
            return null;
        }

        string? primaryKeyColumn = GetEvidenceValue(issue, "primaryKeyColumn");
        string? primaryKeyNativeType = GetEvidenceValue(issue, "primaryKeyNativeType");
        string? valueNativeType = GetEvidenceValue(issue, "columnNativeType");
        if (string.IsNullOrWhiteSpace(primaryKeyColumn)
            || string.IsNullOrWhiteSpace(primaryKeyNativeType)
            || string.IsNullOrWhiteSpace(valueNativeType))
        {
            return null;
        }

        string childTableName = BuildChildTableName(issue.TableName!, issue.ColumnName!);
        string valueColumnName = BuildValueColumnName(issue.ColumnName!);
        string sql = BuildSql(
            provider,
            issue.SchemaName,
            issue.TableName!,
            issue.ColumnName!,
            primaryKeyColumn!,
            primaryKeyNativeType!,
            valueNativeType!,
            childTableName,
            valueColumnName);
        IReadOnlyList<string>? preconditions = BuildPreconditions(provider, issue.SchemaName, childTableName);
        if (preconditions is null || preconditions.Count == 0)
        {
            return null;
        }

        const string title = "Create normalized child table";
        const SqlCandidateSafety safety = SqlCandidateSafety.NonDestructive;

        return new SqlFixCandidate(
            CandidateId: SchemaDeterministicIdFactory.CreateCandidateId(suggestionId, provider, title, sql, safety),
            Provider: provider,
            Title: title,
            Sql: sql,
            PreconditionsSql: preconditions,
            Safety: safety,
            Visibility: CandidateVisibility.VisibleActionable,
            IsAutoApplicable: false,
            Notes:
            [
                "Cria uma estrutura normalizada para substituir a coluna multi-valorada.",
                "Nao remove nem altera a coluna original; o backfill deve ser revisado conforme o formato real dos dados.",
                "Execute somente apos validar chaves, tipos e estrategia de migracao.",
            ]);
    }

    private string BuildSql(
        DatabaseProvider provider,
        string? schemaName,
        string parentTable,
        string sourceColumn,
        string primaryKeyColumn,
        string primaryKeyNativeType,
        string valueNativeType,
        string childTableName,
        string valueColumnName)
    {
        string childQualified = _escaping.QuoteQualifiedName(provider, schemaName, childTableName);
        string parentQualified = _escaping.QuoteQualifiedName(provider, schemaName, parentTable);
        string quotedPrimaryKeyColumn = _escaping.QuoteIdentifier(provider, primaryKeyColumn);
        string quotedValueColumn = _escaping.QuoteIdentifier(provider, valueColumnName);
        string quotedPrimaryKeyConstraint = _escaping.QuoteIdentifier(provider, $"pk_{childTableName}");
        string quotedForeignKeyConstraint = _escaping.QuoteIdentifier(provider, $"fk_{childTableName}_{parentTable}");

        return $"""
CREATE TABLE {childQualified} (
    {quotedPrimaryKeyColumn} {primaryKeyNativeType} NOT NULL,
    {quotedValueColumn} {valueNativeType} NOT NULL,
    CONSTRAINT {quotedPrimaryKeyConstraint} PRIMARY KEY ({quotedPrimaryKeyColumn}, {quotedValueColumn}),
    CONSTRAINT {quotedForeignKeyConstraint} FOREIGN KEY ({quotedPrimaryKeyColumn}) REFERENCES {parentQualified} ({quotedPrimaryKeyColumn})
);
-- Backfill {childQualified}.{quotedValueColumn} from {parentQualified}.{_escaping.QuoteIdentifier(provider, sourceColumn)} after validating delimiter/JSON semantics.
""";
    }

    private static IReadOnlyList<string>? BuildPreconditions(
        DatabaseProvider provider,
        string? schemaName,
        string childTableName)
    {
        string schema = string.IsNullOrWhiteSpace(schemaName) ? "public" : schemaName!;
        string escapedSchema = EscapeSqlLiteral(schema);
        string escapedTable = EscapeSqlLiteral(childTableName);

        return provider switch
        {
            DatabaseProvider.Postgres =>
            [
                $"SELECT 1 WHERE NOT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = '{escapedSchema}' AND table_name = '{escapedTable}')",
            ],
            DatabaseProvider.SqlServer =>
            [
                $"SELECT 1 WHERE OBJECT_ID(N'{EscapeSqlServerIdentifier(schema)}.{EscapeSqlServerIdentifier(childTableName)}', N'U') IS NULL",
            ],
            DatabaseProvider.MySql =>
            [
                $"SELECT 1 WHERE NOT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = '{escapedTable}')",
            ],
            DatabaseProvider.SQLite =>
            [
                $"SELECT 1 WHERE NOT EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = '{escapedTable}')",
            ],
            _ => null,
        };
    }

    private static string BuildChildTableName(string tableName, string columnName) =>
        $"{NormalizeIdentifierPart(tableName)}_{NormalizeIdentifierPart(columnName)}";

    private static string BuildValueColumnName(string columnName)
    {
        string normalized = NormalizeIdentifierPart(columnName);
        return normalized.EndsWith("_value", StringComparison.Ordinal)
            ? normalized
            : $"{normalized}_value";
    }

    private static string NormalizeIdentifierPart(string value)
    {
        string normalized = new(value
            .Select(static character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_')
            .ToArray());

        while (normalized.Contains("__", StringComparison.Ordinal))
            normalized = normalized.Replace("__", "_", StringComparison.Ordinal);

        return normalized.Trim('_');
    }

    private static string? GetEvidenceValue(SchemaIssue issue, string key) =>
        issue.Evidence.FirstOrDefault(evidence => string.Equals(evidence.Key, key, StringComparison.Ordinal))?.Value;

    private static string EscapeSqlLiteral(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);

    private static string EscapeSqlServerIdentifier(string value) =>
        value.Replace("]", "]]", StringComparison.Ordinal);
}
