
namespace AkkornStudio.UI.Services.QueryPreview;

/// <summary>
/// Builds NodeGraph structures from canvas state and generates SQL.
/// Handles SELECT bindings, WHERE conditions, JOINs, and LIMIT clauses.
/// </summary>
public sealed class QueryGraphBuilder(CanvasViewModel canvas, DatabaseProvider provider)
{
    private readonly CanvasViewModel _canvas = canvas;
    private readonly DatabaseProvider _provider = provider;
    private readonly QueryPreviewDiagnosticContextEnricher _diagnosticEnricher = new();
    private readonly QueryCompilationGenerationErrorMapper _generationErrorMapper = new();
    private readonly QueryPreviewSqlPreviewFormatter _previewFormatter = new(provider);
    private readonly QueryCompilationCteResolver _cteResolver = new(canvas, provider);
    private readonly QueryCompilationValidationService _validationService = new(canvas, provider);
    private QueryCompilationNodeGraphAssembler? _graphAssembler;
    private QueryCompilationPipelineRunner? _pipelineRunner;

    private QueryCompilationNodeGraphAssembler GraphAssembler =>
        _graphAssembler ??= new QueryCompilationNodeGraphAssembler(_canvas, _provider, _cteResolver);

    private QueryCompilationPipelineRunner PipelineRunner =>
        _pipelineRunner ??= new QueryCompilationPipelineRunner(
            _canvas,
            _provider,
            _cteResolver,
            _validationService,
            GraphAssembler,
            _previewFormatter,
            _generationErrorMapper);

    public bool TryBuildGraphSnapshot(
        out NodeGraph graph,
        out string? fromTable,
        out string? previewSql,
        out List<string> errors)
    {
        graph = new NodeGraph();
        fromTable = null;
        previewSql = null;
        errors = [];

        NodeViewModel? resultOutputNode = _canvas.Nodes.FirstOrDefault(n =>
            n.Type == NodeType.ResultOutput
        );
        if (resultOutputNode is null)
        {
            errors.Add("Add a Result Output node to generate SQL");
            return false;
        }

        List<NodeViewModel> allCteDefinitions = _canvas.Nodes.Where(n => n.Type == NodeType.CteDefinition).ToList();
        List<NodeViewModel> cteDefinitions = [.. QueryCompilationNodeGraphAssembler.CollectRelevantCteDefinitions(_canvas, resultOutputNode, allCteDefinitions)];
        Dictionary<string, string> cteNames = _cteResolver.BuildCteDefinitionNameMap(cteDefinitions);

        graph = GraphAssembler.BuildNodeGraph(resultOutputNode, cteDefinitions, cteNames, includeCtes: true);

        List<NodeViewModel> tableNodes = _canvas.Nodes.Where(n => n.Type == NodeType.TableSource).ToList();
        List<NodeViewModel> cteSourceNodes = _canvas.Nodes.Where(n => n.Type == NodeType.CteSource).ToList();
        List<NodeViewModel> subqueryNodes = _canvas.Nodes
            .Where(n => n.Type is NodeType.Subquery or NodeType.SubqueryReference)
            .ToList();
        (string resolvedFromTable, string? warning) = ResolveFromTable(tableNodes, cteSourceNodes, subqueryNodes, cteNames);
        fromTable = resolvedFromTable;
        if (!string.IsNullOrWhiteSpace(warning))
            errors.Add(warning);

        if (string.IsNullOrWhiteSpace(fromTable))
            return false;

        try
        {
            var svc = QueryGeneratorService.Create(_provider);
            GeneratedQuery result = svc.Generate(graph);
            previewSql = _previewFormatter.InlineBindingsForPreview(result.Sql, result.Bindings);
        }
        catch (Exception ex)
        {
            errors.AddRange(_generationErrorMapper.Map(ex));
            return false;
        }

        return true;
    }

    /// <summary>
    /// Builds SQL from the current canvas state. Returns (sql, errors).
    /// </summary>
    public (string Sql, List<string> Errors) BuildSql()
    {
        QuerySqlBuildResult result = BuildSqlWithDiagnostics();
        return (result.PreviewSql, result.Diagnostics.Select(d => d.Message).ToList());
    }

    /// <summary>
    /// Builds SQL and returns structured diagnostics with stable categories/codes.
    /// </summary>
    public QuerySqlBuildResult BuildSqlWithDiagnostics()
    {
        (string sql, string? executionSqlTemplate, IReadOnlyDictionary<string, object?> bindings, IReadOnlyDictionary<string, QueryExecutionParameterContext> parameterContexts, List<string> legacyMessages) = BuildSqlCore();
        List<PreviewDiagnostic> diagnostics = PreviewDiagnosticMapper.FromLegacyMessages(legacyMessages);
        _diagnosticEnricher.Enrich(_canvas, diagnostics);
        return new QuerySqlBuildResult(sql, executionSqlTemplate, bindings, parameterContexts, diagnostics);
    }

    private (string Sql, string? ExecutionSqlTemplate, IReadOnlyDictionary<string, object?> Bindings, IReadOnlyDictionary<string, QueryExecutionParameterContext> ParameterContexts, List<string> Errors) BuildSqlCore()
    {
        return PipelineRunner.Execute();
    }

    private (string FromTable, string? Warning) ResolveFromTable(
        IReadOnlyList<NodeViewModel> tableNodes,
        IReadOnlyList<NodeViewModel> cteSourceNodes,
        IReadOnlyList<NodeViewModel> subqueryNodes,
        IReadOnlyDictionary<string, string> cteDefinitionNamesById
    ) =>
        new QueryCompilationSourceResolver(_canvas, _cteResolver.ResolveCteSourceReference)
            .ResolveFromTable(tableNodes, cteSourceNodes, subqueryNodes, cteDefinitionNamesById);

}

public sealed record QuerySqlBuildResult(
    string PreviewSql,
    string? ExecutionSqlTemplate,
    IReadOnlyDictionary<string, object?> Bindings,
    IReadOnlyDictionary<string, QueryExecutionParameterContext> ParameterContexts,
    List<PreviewDiagnostic> Diagnostics);
