using System.Globalization;
using AkkornStudio.Core;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Contracts;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Enums;

namespace AkkornStudio.Ddl.SchemaAnalysis.Application.Processing;

public sealed class SchemaSuggestionFactory
{
    private readonly SchemaMissingFkSqlCandidateFactory _missingFkSqlCandidateFactory = new();
    private readonly SchemaMissingRequiredCommentSqlCandidateFactory _missingRequiredCommentSqlCandidateFactory = new();
    private readonly SchemaNf1SplitColumnSqlCandidateFactory _nf1SplitColumnSqlCandidateFactory = new();

    public IReadOnlyList<SchemaSuggestion> CreateSuggestions(
        SchemaIssue issue,
        DatabaseProvider provider,
        SchemaAnalysisProfile profile,
        IReadOnlySet<string>? existingConstraintNames = null
    )
    {
        ArgumentNullException.ThrowIfNull(issue);
        ArgumentNullException.ThrowIfNull(profile);

        List<SchemaSuggestion> suggestions = issue.RuleCode switch
        {
            SchemaRuleCode.MISSING_FK => BuildMissingFkSuggestions(issue, provider, existingConstraintNames),
            SchemaRuleCode.MISSING_REQUIRED_COMMENT => BuildMissingRequiredCommentSuggestions(issue, provider),
            SchemaRuleCode.NAMING_CONVENTION_VIOLATION => BuildNamingSuggestions(issue),
            SchemaRuleCode.LOW_SEMANTIC_NAME => BuildLowSemanticNameSuggestions(issue),
            SchemaRuleCode.NF1_HINT_MULTI_VALUED => BuildNf1NormalizationSuggestions(issue, provider),
            SchemaRuleCode.NF2_HINT_PARTIAL_DEPENDENCY => BuildNormalizationSuggestions(issue, "Avaliar mover o atributo para entidade dependente do componente parcial."),
            SchemaRuleCode.NF3_HINT_TRANSITIVE_DEPENDENCY => BuildNormalizationSuggestions(issue, "Avaliar mover o atributo descritivo para a tabela de referencia correspondente."),
            _ => [],
        };

        return suggestions
            .OrderByDescending(static suggestion => suggestion.Confidence)
            .ThenBy(static suggestion => suggestion.Title, StringComparer.Ordinal)
            .ThenBy(static suggestion => suggestion.SuggestionId, StringComparer.Ordinal)
            .Take(profile.MaxSuggestionsPerIssue)
            .ToList();
    }

    private List<SchemaSuggestion> BuildMissingFkSuggestions(
        SchemaIssue issue,
        DatabaseProvider provider,
        IReadOnlySet<string>? existingConstraintNames
    )
    {
        string qualifiedColumn = BuildQualifiedColumn(issue);
        SchemaSuggestion primarySuggestion = CreateSuggestion(
            issue,
            "Review inferred foreign key",
            $"Revisar a coluna '{qualifiedColumn}' e confirmar se ela deve ser promovida a FK explicita.",
            issue.Confidence,
            []
        );

        SqlFixCandidate? candidate = _missingFkSqlCandidateFactory.CreateCandidate(
            issue,
            provider,
            primarySuggestion.SuggestionId,
            existingConstraintNames
        );

        return
        [
            primarySuggestion with
            {
                SqlCandidates = candidate is null ? [] : [candidate],
            },
            CreateSuggestion(
                issue,
                "Validate relationship cardinality",
                $"Confirmar cardinalidade, nulabilidade e comportamento de delete/update antes de materializar a relacao em {provider}.",
                Math.Max(0d, issue.Confidence - 0.1000),
                []
            ),
        ];
    }

    private List<SchemaSuggestion> BuildMissingRequiredCommentSuggestions(SchemaIssue issue, DatabaseProvider provider)
    {
        string target = BuildIssueTarget(issue);
        SchemaSuggestion suggestion = CreateSuggestion(
            issue,
            "Add technical comment",
            $"Adicionar comentario tecnico objetivo para '{target}' compativel com o provider {provider}.",
            issue.Confidence,
            []
        );

        SqlFixCandidate? candidate = _missingRequiredCommentSqlCandidateFactory.CreateCandidate(
            issue,
            provider,
            suggestion.SuggestionId
        );

        return
        [
            suggestion with
            {
                SqlCandidates = candidate is null ? [] : [candidate],
            },
        ];
    }

    private static List<SchemaSuggestion> BuildNamingSuggestions(SchemaIssue issue)
    {
        string target = BuildIssueTarget(issue);
        return
        [
            CreateSuggestion(
                issue,
                "Rename to configured convention",
                $"Renomear '{target}' para aderir a convencao configurada sem alterar o significado tecnico.",
                issue.Confidence,
                []
            ),
        ];
    }

    private static List<SchemaSuggestion> BuildLowSemanticNameSuggestions(SchemaIssue issue)
    {
        string target = BuildIssueTarget(issue);
        return
        [
            CreateSuggestion(
                issue,
                "Use a more specific identifier",
                $"Substituir '{target}' por nome tecnico mais especifico e observavel no schema.",
                issue.Confidence,
                []
            ),
        ];
    }

    private List<SchemaSuggestion> BuildNf1NormalizationSuggestions(SchemaIssue issue, DatabaseProvider provider)
    {
        SchemaSuggestion suggestion = CreateSuggestion(
            issue,
            "Review normalization shape",
            "Avaliar decomposicao do atributo em uma tabela filha atomica.",
            issue.Confidence,
            []);

        SqlFixCandidate? candidate = _nf1SplitColumnSqlCandidateFactory.CreateCandidate(
            issue,
            provider,
            suggestion.SuggestionId);

        return
        [
            suggestion with
            {
                SqlCandidates = candidate is null ? [] : [candidate],
            },
        ];
    }

    private static List<SchemaSuggestion> BuildNormalizationSuggestions(SchemaIssue issue, string description)
    {
        return
        [
            CreateSuggestion(
                issue,
                "Review normalization shape",
                description,
                issue.Confidence,
                []
            ),
        ];
    }

    private static SchemaSuggestion CreateSuggestion(
        SchemaIssue issue,
        string title,
        string description,
        double confidence,
        IReadOnlyList<SqlFixCandidate> candidates
    )
    {
        double roundedConfidence = Math.Round(Math.Clamp(confidence, 0d, 1d), 4, MidpointRounding.ToEven);
        string suggestionId = SchemaDeterministicIdFactory.CreateSuggestionId(issue.IssueId, title, description, roundedConfidence);

        return new SchemaSuggestion(
            SuggestionId: suggestionId,
            Title: title,
            Description: description,
            Confidence: roundedConfidence,
            SqlCandidates: candidates
        );
    }

    private static string BuildQualifiedColumn(SchemaIssue issue)
    {
        if (!string.IsNullOrWhiteSpace(issue.SchemaName)
            && !string.IsNullOrWhiteSpace(issue.TableName)
            && !string.IsNullOrWhiteSpace(issue.ColumnName))
        {
            return $"{issue.SchemaName}.{issue.TableName}.{issue.ColumnName}";
        }

        return BuildIssueTarget(issue);
    }

    private static string BuildIssueTarget(SchemaIssue issue)
    {
        return issue.TargetType switch
        {
            SchemaTargetType.Table => $"{issue.SchemaName}.{issue.TableName}",
            SchemaTargetType.Column => $"{issue.SchemaName}.{issue.TableName}.{issue.ColumnName}",
            SchemaTargetType.Constraint => issue.ConstraintName ?? issue.Title,
            _ => issue.Title,
        };
    }
}
