
namespace AkkornStudio.UI.Services.QueryPreview;

internal sealed class QueryCompilationValidationService(CanvasViewModel canvas, DatabaseProvider provider)
{
    private readonly QueryCompilationWindowFunctionValidator _windowValidator = new(canvas);
    private readonly QueryCompilationConnectionTypeValidator _connectionTypeValidator = new(canvas);
    private readonly QueryCompilationAliasAmbiguityValidator _aliasAmbiguityValidator = new(canvas);
    private readonly QueryCompilationPredicateStructureValidator _predicateValidator = new(canvas);
    private readonly QueryCompilationComparisonValidator _comparisonValidator = new(canvas);
    private readonly QueryCompilationNotAndJsonValidator _notAndJsonValidator = new(canvas);
    private readonly QueryCompilationOutputSourceReachabilityValidator _outputSourceReachabilityValidator = new(canvas);
    private readonly QueryCompilationSourceConflictValidator _sourceConflictValidator = new(canvas);
    private readonly QueryCompilationImplicitCrossJoinValidator _implicitCrossJoinValidator = new();
    private readonly QueryCompilationSelectStarProjectionValidator _selectStarProjectionValidator = new(canvas);
    private readonly QueryCompilationPaginationValidator _paginationValidator = new(canvas, provider);
    private readonly QueryCompilationQueryHintsValidator _queryHintsValidator = new(provider);
    private readonly QueryCompilationPivotValidator _pivotValidator = new(provider);

    public void ValidateWindowFunctionNodes(List<string> errors) =>
        _windowValidator.Validate(errors);

    public void ValidateConnectionTypeCompatibility(List<string> errors) =>
        _connectionTypeValidator.Validate(errors);

    public void ValidatePredicateNodes(NodeViewModel resultOutputNode, List<string> errors) =>
        _predicateValidator.Validate(resultOutputNode, errors);

    public void ValidateComparisonNodes(NodeViewModel resultOutputNode, List<string> errors) =>
        _comparisonValidator.Validate(resultOutputNode, errors);

    public void ValidateNotAndJsonNodes(NodeViewModel resultOutputNode, List<string> errors) =>
        _notAndJsonValidator.Validate(resultOutputNode, errors);

    public void ValidateOutputSourceReachability(NodeViewModel resultOutputNode, List<string> errors) =>
        _outputSourceReachabilityValidator.Validate(resultOutputNode, errors);

    public void ValidateSourceConflicts(
        NodeViewModel resultOutputNode,
        IReadOnlyList<JoinDefinition> joins,
        List<string> errors) =>
        _sourceConflictValidator.Validate(resultOutputNode, joins, errors);

    public void ValidateImplicitCrossJoins(IReadOnlyList<JoinDefinition> joins, List<string> errors) =>
        _implicitCrossJoinValidator.Validate(joins, errors);

    public void ValidateSelectStarProjection(NodeViewModel resultOutputNode, List<string> errors) =>
        _selectStarProjectionValidator.Validate(resultOutputNode, errors);

    public void ValidateQueryHints(NodeViewModel resultOutputNode, List<string> errors) =>
        _queryHintsValidator.Validate(resultOutputNode, errors);

    public void ValidatePaginationSettings(NodeViewModel resultOutputNode, List<string> errors) =>
        _paginationValidator.Validate(resultOutputNode, errors);

    public void ValidateAliasAmbiguity(
        NodeViewModel resultOutputNode,
        IReadOnlyList<NodeViewModel> cteDefinitions,
        IReadOnlyDictionary<string, string> cteDefinitionNamesById,
        List<string> errors) =>
        _aliasAmbiguityValidator.Validate(resultOutputNode, cteDefinitions, cteDefinitionNamesById, errors);

    public void ValidatePivotSettings(NodeViewModel resultOutputNode, List<string> errors) =>
        _pivotValidator.Validate(resultOutputNode, errors);
}
