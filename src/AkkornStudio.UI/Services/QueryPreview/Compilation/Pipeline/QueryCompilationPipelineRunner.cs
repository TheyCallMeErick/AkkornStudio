
namespace AkkornStudio.UI.Services.QueryPreview;

internal sealed class QueryCompilationPipelineRunner(
    CanvasViewModel canvas,
    DatabaseProvider provider,
    QueryCompilationCteResolver cteResolver,
    QueryCompilationValidationService validationService,
    QueryCompilationNodeGraphAssembler graphAssembler,
    QueryPreviewSqlPreviewFormatter previewFormatter,
    QueryCompilationGenerationErrorMapper generationErrorMapper)
{
    private readonly CanvasViewModel _canvas = canvas;
    private readonly DatabaseProvider _provider = provider;
    private readonly QueryCompilationCteResolver _cteResolver = cteResolver;
    private readonly QueryCompilationValidationService _validationService = validationService;
    private readonly QueryCompilationNodeGraphAssembler _graphAssembler = graphAssembler;
    private readonly QueryPreviewSqlPreviewFormatter _previewFormatter = previewFormatter;
    private readonly QueryCompilationGenerationErrorMapper _generationErrorMapper = generationErrorMapper;

    public (string Sql, string? ExecutionSqlTemplate, IReadOnlyDictionary<string, object?> Bindings, IReadOnlyDictionary<string, QueryExecutionParameterContext> ParameterContexts, List<string> Errors) Execute()
    {
        QueryCompilationInputStage inputStageRunner = BuildInputStage();
        QueryCompilationPipelineContext context = new(_canvas, _provider);
        QueryCompilationInputStageResult inputStage = inputStageRunner.Execute(context);
        if (inputStage.ShouldShortCircuit)
            return (inputStage.ShortCircuitSql ?? string.Empty, null, new Dictionary<string, object?>(), new Dictionary<string, QueryExecutionParameterContext>(StringComparer.OrdinalIgnoreCase), inputStage.Errors);

        QueryCompilationInputSnapshot input = inputStage.Snapshot!;
        List<string> errors = input.Errors;
        List<NodeViewModel> tableNodes = input.TableNodes;
        List<NodeViewModel> cteDefinitions = input.CteDefinitions;
        Dictionary<string, string> cteDefinitionNamesById = input.CteDefinitionNamesById;
        NodeViewModel resultOutputNode = input.ResultOutputNode;
        string fromTable = input.FromTable;

        QueryCompilationExecutionPlanStage executionPlanStage = BuildExecutionPlanStage();

        QueryCompilationExecutionPlanStageResult executionPlan = executionPlanStage.Execute(
            new QueryCompilationExecutionPlanStageInput(
                tableNodes,
                cteDefinitions,
                cteDefinitionNamesById,
                resultOutputNode,
                errors));
        NodeGraph graph = executionPlan.Graph;
        List<JoinDefinition> joins = executionPlan.Joins;
        SetOperationDefinition? setOperation = executionPlan.SetOperation;

        QueryCompilationValidationStage validationStage = BuildValidationStage();
        validationStage.Execute(new QueryCompilationValidationStageInput(
            joins,
            resultOutputNode,
            cteDefinitions,
            cteDefinitionNamesById,
            errors));

        QueryCompilationGenerationStage generationStage = BuildGenerationStage();
        QueryCompilationGenerationStageResult generated = generationStage.Execute(
            new QueryCompilationGenerationStageInput(
                fromTable,
                graph,
                joins,
                setOperation,
                errors));

        return (generated.Sql, generated.ExecutionSqlTemplate, generated.Bindings, generated.ParameterContexts, generated.Errors);
    }

    private QueryCompilationInputStage BuildInputStage() =>
        new QueryCompilationInputStageFactory(
            _cteResolver.BuildCteDefinitionNameMap,
            ResolveFromTable,
            QueryCompilationNodeGraphAssembler.IsWildcardProjectionPin,
            QueryCompilationNodeGraphAssembler.IsProjectionInputPinName).Create();

    private QueryCompilationExecutionPlanStage BuildExecutionPlanStage() =>
        new QueryCompilationExecutionPlanStageFactory(_canvas, _provider, _graphAssembler.BuildNodeGraph).Create();

    private QueryCompilationValidationStage BuildValidationStage() =>
        new QueryCompilationValidationStageFactory(
            _validationService.ValidateAliasAmbiguity,
            _validationService.ValidateWindowFunctionNodes,
            _validationService.ValidateConnectionTypeCompatibility,
            _validationService.ValidatePredicateNodes,
            _validationService.ValidateComparisonNodes,
            _validationService.ValidateNotAndJsonNodes,
            _validationService.ValidateOutputSourceReachability,
            _validationService.ValidateSourceConflicts,
            _validationService.ValidateImplicitCrossJoins,
            _validationService.ValidateSelectStarProjection,
            _validationService.ValidatePaginationSettings,
            _validationService.ValidateQueryHints,
            _validationService.ValidatePivotSettings).Create();

    private QueryCompilationGenerationStage BuildGenerationStage() =>
        new QueryCompilationGenerationStageFactory(
            _provider,
            _previewFormatter.InlineBindingsForPreview,
            _generationErrorMapper.Map).Create();

    private (string FromTable, string? Warning) ResolveFromTable(
        IReadOnlyList<NodeViewModel> tableNodes,
        IReadOnlyList<NodeViewModel> cteSourceNodes,
        IReadOnlyList<NodeViewModel> subqueryNodes,
        IReadOnlyDictionary<string, string> cteDefinitionNamesById
    ) =>
        new QueryCompilationSourceResolver(_canvas, _cteResolver.ResolveCteSourceReference)
            .ResolveFromTable(tableNodes, cteSourceNodes, subqueryNodes, cteDefinitionNamesById);
}
