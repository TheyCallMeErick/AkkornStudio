using AkkornStudio.Core;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Contracts;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Enums;

namespace AkkornStudio.Ddl.SchemaAnalysis.Application.Processing;

public sealed class SchemaMissingRequiredCommentSqlCandidateFactory
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

        if (issue.RuleCode != SchemaRuleCode.MISSING_REQUIRED_COMMENT || provider == DatabaseProvider.SQLite)
        {
            return null;
        }

        if (issue.TargetType == SchemaTargetType.Constraint
            || string.IsNullOrWhiteSpace(issue.TableName)
            || string.IsNullOrWhiteSpace(issue.SchemaName) && provider is DatabaseProvider.Postgres or DatabaseProvider.SqlServer)
        {
            return null;
        }

        if (provider == DatabaseProvider.MySql && issue.TargetType == SchemaTargetType.Column)
        {
            return null;
        }

        string generatedComment = BuildGeneratedComment(issue);
        IReadOnlyList<string> preconditions = BuildPreconditions(issue, provider);
        if (preconditions.Count == 0)
        {
            return null;
        }

        string sql = BuildSql(issue, provider, generatedComment);
        if (string.IsNullOrWhiteSpace(sql))
        {
            return null;
        }

        const string title = "Add missing technical comment";
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
                "Este candidate depende de suporte específico do provider.",
                "As preconditions devem ser avaliadas antes da execução manual.",
                "Este candidate não é autoaplicável no MVP.",
            ]
        );
    }

    private string BuildSql(SchemaIssue issue, DatabaseProvider provider, string comment)
    {
        return provider switch
        {
            DatabaseProvider.Postgres => BuildPostgresSql(issue, comment),
            DatabaseProvider.SqlServer => BuildSqlServerSql(issue, comment),
            DatabaseProvider.MySql => BuildMySqlSql(issue, comment),
            _ => string.Empty,
        };
    }

    private string BuildPostgresSql(SchemaIssue issue, string comment)
    {
        string qualifiedTable = _escaping.QuoteQualifiedName(DatabaseProvider.Postgres, issue.SchemaName, issue.TableName!);
        string quotedComment = _escaping.QuoteStringLiteral(DatabaseProvider.Postgres, comment);

        if (issue.TargetType == SchemaTargetType.Table)
        {
            return $"COMMENT ON TABLE {qualifiedTable} IS {quotedComment};";
        }

        string quotedColumn = _escaping.QuoteIdentifier(DatabaseProvider.Postgres, issue.ColumnName!);
        return $"COMMENT ON COLUMN {qualifiedTable}.{quotedColumn} IS {quotedComment};";
    }

    private string BuildSqlServerSql(SchemaIssue issue, string comment)
    {
        string schemaLiteral = _escaping.QuoteUnicodeStringLiteral(DatabaseProvider.SqlServer, issue.SchemaName!);
        string tableLiteral = _escaping.QuoteUnicodeStringLiteral(DatabaseProvider.SqlServer, issue.TableName!);
        string commentLiteral = _escaping.QuoteUnicodeStringLiteral(DatabaseProvider.SqlServer, comment);

        if (issue.TargetType == SchemaTargetType.Table)
        {
            return $"""
IF EXISTS (SELECT 1 FROM fn_listextendedproperty('MS_Description', 'SCHEMA', {schemaLiteral}, 'TABLE', {tableLiteral}, NULL, NULL))
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value={commentLiteral}, @level0type=N'Schema', @level0name={schemaLiteral}, @level1type=N'Table', @level1name={tableLiteral};
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value={commentLiteral}, @level0type=N'Schema', @level0name={schemaLiteral}, @level1type=N'Table', @level1name={tableLiteral};
""";
        }

        string columnLiteral = _escaping.QuoteUnicodeStringLiteral(DatabaseProvider.SqlServer, issue.ColumnName!);
        return $"""
IF EXISTS (SELECT 1 FROM fn_listextendedproperty('MS_Description', 'SCHEMA', {schemaLiteral}, 'TABLE', {tableLiteral}, 'COLUMN', {columnLiteral}))
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value={commentLiteral}, @level0type=N'Schema', @level0name={schemaLiteral}, @level1type=N'Table', @level1name={tableLiteral}, @level2type=N'Column', @level2name={columnLiteral};
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value={commentLiteral}, @level0type=N'Schema', @level0name={schemaLiteral}, @level1type=N'Table', @level1name={tableLiteral}, @level2type=N'Column', @level2name={columnLiteral};
""";
    }

    private string BuildMySqlSql(SchemaIssue issue, string comment)
    {
        if (issue.TargetType != SchemaTargetType.Table)
        {
            return string.Empty;
        }

        string qualifiedTable = _escaping.QuoteQualifiedName(DatabaseProvider.MySql, issue.SchemaName, issue.TableName!);
        string quotedComment = _escaping.QuoteStringLiteral(DatabaseProvider.MySql, comment);
        return $"ALTER TABLE {qualifiedTable} COMMENT = {quotedComment};";
    }

    private IReadOnlyList<string> BuildPreconditions(SchemaIssue issue, DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.Postgres => BuildPostgresPreconditions(issue),
            DatabaseProvider.SqlServer => BuildSqlServerPreconditions(issue),
            DatabaseProvider.MySql => BuildMySqlPreconditions(issue),
            _ => [],
        };
    }

    private static IReadOnlyList<string> BuildPostgresPreconditions(SchemaIssue issue)
    {
        string schema = Escape(issue.SchemaName!);
        string table = Escape(issue.TableName!);

        if (issue.TargetType == SchemaTargetType.Table)
        {
            return
            [
                $"SELECT 1 FROM information_schema.tables WHERE table_schema = '{schema}' AND table_name = '{table}'",
            ];
        }

        string column = Escape(issue.ColumnName!);
        return
        [
            $"SELECT 1 FROM information_schema.columns WHERE table_schema = '{schema}' AND table_name = '{table}' AND column_name = '{column}'",
        ];
    }

    private IReadOnlyList<string> BuildSqlServerPreconditions(SchemaIssue issue)
    {
        string schema = _escaping.QuoteUnicodeStringLiteral(DatabaseProvider.SqlServer, issue.SchemaName!);
        string table = _escaping.QuoteUnicodeStringLiteral(DatabaseProvider.SqlServer, issue.TableName!);

        if (issue.TargetType == SchemaTargetType.Table)
        {
            return
            [
                $"SELECT 1 FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE s.name={schema} AND t.name={table}",
                $"SELECT 1 FROM fn_listextendedproperty('MS_Description', 'SCHEMA', {schema}, 'TABLE', {table}, NULL, NULL)",
            ];
        }

        string column = _escaping.QuoteUnicodeStringLiteral(DatabaseProvider.SqlServer, issue.ColumnName!);
        return
        [
            $"SELECT 1 FROM sys.columns c JOIN sys.tables t ON c.object_id=t.object_id JOIN sys.schemas s ON t.schema_id=s.schema_id WHERE s.name={schema} AND t.name={table} AND c.name={column}",
            $"SELECT 1 FROM fn_listextendedproperty('MS_Description', 'SCHEMA', {schema}, 'TABLE', {table}, 'COLUMN', {column})",
        ];
    }

    private static IReadOnlyList<string> BuildMySqlPreconditions(SchemaIssue issue)
    {
        if (issue.TargetType != SchemaTargetType.Table)
        {
            return [];
        }

        string table = Escape(issue.TableName!);
        return
        [
            $"SELECT 1 FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = '{table}'",
        ];
    }

    private static string BuildGeneratedComment(SchemaIssue issue)
    {
        return issue.TargetType == SchemaTargetType.Table
            ? $"Technical description for table {issue.TableName}."
            : $"Technical description for column {issue.TableName}.{issue.ColumnName}.";
    }
    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
