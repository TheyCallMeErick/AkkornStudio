
namespace AkkornStudio.UI.Services.QueryPreview;

internal sealed class QueryCompilationGenerationStageFactory(
    DatabaseProvider provider,
    Func<string, IReadOnlyDictionary<string, object?>, string> inlineBindingsForPreview,
    Func<Exception, IEnumerable<string>> mapGenerationErrors)
{
    private readonly DatabaseProvider _provider = provider;
    private readonly Func<string, IReadOnlyDictionary<string, object?>, string> _inlineBindingsForPreview = inlineBindingsForPreview;
    private readonly Func<Exception, IEnumerable<string>> _mapGenerationErrors = mapGenerationErrors;

    public QueryCompilationGenerationStage Create() =>
        new(
            new QueryCompilationSqlGenerator(_provider),
            _inlineBindingsForPreview,
            new QueryExecutionParameterContextExtractor(),
            _mapGenerationErrors,
            (fallbackFromTable, fallbackJoins) => JoinResolver.FallbackSql(fallbackFromTable, [.. fallbackJoins]));
}

