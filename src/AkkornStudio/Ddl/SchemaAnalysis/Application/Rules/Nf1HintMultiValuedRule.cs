using System.Security.Cryptography;
using System.Text;
using AkkornStudio.Ddl.SchemaAnalysis.Application.Processing;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Contracts;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Enums;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Normalization;
using AkkornStudio.Metadata;

namespace AkkornStudio.Ddl.SchemaAnalysis.Application.Rules;

public sealed class Nf1HintMultiValuedRule : ISchemaAnalysisRule
{
    private readonly SchemaNameTokenizer _tokenizer = new();

    private static readonly HashSet<string> StrongTokens = new(StringComparer.Ordinal)
    {
        "list",
        "lista",
        "item",
        "itens",
        "items",
        "csv",
        "tag",
        "tags",
        "value",
        "values",
        "valores",
    };

    public SchemaRuleCode RuleCode => SchemaRuleCode.NF1_HINT_MULTI_VALUED;

    public Task<SchemaRuleExecutionResult> ExecuteAsync(
        SchemaAnalysisExecutionContext context,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        List<SchemaIssue> issues = [];

        foreach (TableMetadata table in context.Metadata.AllTables.OrderBy(static table => table.FullName, StringComparer.OrdinalIgnoreCase))
        {
            foreach (ColumnMetadata column in table.Columns.OrderBy(static column => column.OrdinalPosition))
            {
                cancellationToken.ThrowIfCancellationRequested();

                NormalizedNameTokens tokens = _tokenizer.Tokenize(column.Name, context.Profile);
                bool hasStrongToken = tokens.AllTokens.Any(token => StrongTokens.Contains(token));
                bool defaultContainsComma = !string.IsNullOrWhiteSpace(column.DefaultValue) && column.DefaultValue.Contains(',', StringComparison.Ordinal);
                bool hasJsonXmlRelationalContext =
                    IsJsonOrXml(column.NativeType) && table.OutboundForeignKeys.Count >= 1;

                int objectiveSignals = CountTrue(hasStrongToken, defaultContainsComma, hasJsonXmlRelationalContext);
                if (objectiveSignals < 2)
                {
                    continue;
                }

                bool isAllowlisted = IsAllowlisted(context.Profile.SemiStructuredPayloadAllowlist, table, column);
                double score = 0.40
                    + (hasStrongToken ? 0.25 : 0)
                    + (defaultContainsComma ? 0.20 : 0)
                    + (hasJsonXmlRelationalContext ? 0.15 : 0)
                    - (isAllowlisted ? 0.20 : 0);
                score = Math.Round(Math.Clamp(score, 0d, 1d), 4, MidpointRounding.ToEven);

                double threshold = Math.Max(
                    context.Profile.MinConfidenceGlobal,
                    context.Profile.RuleSettings[SchemaRuleCode.NF1_HINT_MULTI_VALUED].MinConfidence
                );

                if (score < threshold)
                {
                    continue;
                }

                issues.Add(CreateIssue(table, column, score, hasStrongToken, defaultContainsComma, hasJsonXmlRelationalContext, isAllowlisted));
            }
        }

        return Task.FromResult(new SchemaRuleExecutionResult(issues, []));
    }

    private static bool IsJsonOrXml(string rawType)
    {
        string normalized = rawType.Trim().ToLowerInvariant();
        return normalized.Contains("json", StringComparison.Ordinal) || normalized.Contains("xml", StringComparison.Ordinal);
    }

    private static int CountTrue(params bool[] values) => values.Count(static value => value);

    private static bool IsAllowlisted(
        IReadOnlyList<string> allowlist,
        TableMetadata table,
        ColumnMetadata column
    )
    {
        string schemaTableColumn = $"{table.Schema}.{table.Name}.{column.Name}";
        string tableColumn = $"{table.Name}.{column.Name}";
        string columnOnly = column.Name;

        if (allowlist.Any(item => string.Equals(item, schemaTableColumn, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (allowlist.Any(item => string.Equals(item, tableColumn, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return allowlist.Any(item => string.Equals(item, columnOnly, StringComparison.OrdinalIgnoreCase));
    }

    private static SchemaIssue CreateIssue(
        TableMetadata table,
        ColumnMetadata column,
        double score,
        bool hasStrongToken,
        bool defaultContainsComma,
        bool hasJsonXmlRelationalContext,
        bool isAllowlisted
    )
    {
        string title = "1NF hint multi-valued";
        string message = $"A coluna '{table.FullName}.{column.Name}' apresenta indícios estruturais de não atomicidade.";

        List<SchemaEvidence> evidence =
        [
            SchemaEvidenceFactory.MetadataFact("score", score.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture), 1.0),
        ];

        if (hasStrongToken)
        {
            evidence.Add(SchemaEvidenceFactory.MetadataFact("strongToken", column.Name, 0.9));
        }

        if (defaultContainsComma)
        {
            evidence.Add(SchemaEvidenceFactory.MetadataFact("defaultContainsComma", column.DefaultValue ?? string.Empty, 0.85));
        }

        if (hasJsonXmlRelationalContext)
        {
            evidence.Add(SchemaEvidenceFactory.ConstraintTopology("jsonXmlRelationalContext", "true", 0.8));
        }

        if (isAllowlisted)
        {
            evidence.Add(SchemaEvidenceFactory.PolicyRequirement("semiStructuredPayloadAllowlist", "matched", 0.75));
        }

        IReadOnlyList<ColumnMetadata> primaryKeyColumns = table.PrimaryKeyColumns;
        if (primaryKeyColumns.Count == 1)
        {
            ColumnMetadata primaryKeyColumn = primaryKeyColumns[0];
            evidence.Add(SchemaEvidenceFactory.MetadataFact("primaryKeyColumn", primaryKeyColumn.Name, 0.85));
            evidence.Add(SchemaEvidenceFactory.MetadataFact("primaryKeyNativeType", primaryKeyColumn.NativeType, 0.85));
            evidence.Add(SchemaEvidenceFactory.MetadataFact("columnNativeType", column.NativeType, 0.85));
        }

        return new SchemaIssue(
            IssueId: ComputeIssueId(table, column, title, message, score),
            RuleCode: SchemaRuleCode.NF1_HINT_MULTI_VALUED,
            Severity: score >= 0.8000 ? SchemaIssueSeverity.Warning : SchemaIssueSeverity.Info,
            Confidence: score,
            TargetType: SchemaTargetType.Column,
            SchemaName: table.Schema,
            TableName: table.Name,
            ColumnName: column.Name,
            ConstraintName: null,
            Title: title,
            Message: message,
            Evidence: evidence,
            Suggestions: [],
            IsAmbiguous: false
        );
    }

    private static string ComputeIssueId(
        TableMetadata table,
        ColumnMetadata column,
        string title,
        string message,
        double score
    )
    {
        string payload = string.Join(
            "|",
            SchemaRuleCode.NF1_HINT_MULTI_VALUED,
            SchemaTargetType.Column,
            NormalizeText(table.Schema),
            NormalizeText(table.Name),
            NormalizeText(column.Name),
            "∅",
            NormalizeText(title),
            NormalizeText(message),
            score.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture),
            "0"
        );

        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeText(string? value)
    {
        return SchemaIssueTextNormalizer.NormalizeForHash(value);
    }
}
