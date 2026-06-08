namespace AkkornStudio.UI.Services.QueryPreview;

internal sealed class QueryCompilationValidationStage(
    Action<NodeViewModel, IReadOnlyList<NodeViewModel>, IReadOnlyDictionary<string, string>, List<string>> validateAliasAmbiguity,
    Action<List<string>> validateWindowFunctionNodes,
    Action<List<string>> validateConnectionTypeCompatibility,
    Action<NodeViewModel, List<string>> validatePredicateNodes,
    Action<NodeViewModel, List<string>> validateComparisonNodes,
    Action<NodeViewModel, List<string>> validateNotAndJsonNodes,
    Action<NodeViewModel, List<string>> validateOutputSourceReachability,
    Action<NodeViewModel, IReadOnlyList<JoinDefinition>, List<string>> validateSourceConflicts,
    Action<IReadOnlyList<JoinDefinition>, List<string>> validateImplicitCrossJoins,
    Action<NodeViewModel, List<string>> validateSelectStarProjection,
    Action<NodeViewModel, List<string>> validatePaginationSettings,
    Action<NodeViewModel, List<string>> validateQueryHints,
    Action<NodeViewModel, List<string>> validatePivotSettings)
{
    private readonly Action<NodeViewModel, IReadOnlyList<NodeViewModel>, IReadOnlyDictionary<string, string>, List<string>> _validateAliasAmbiguity = validateAliasAmbiguity;
    private readonly Action<List<string>> _validateWindowFunctionNodes = validateWindowFunctionNodes;
    private readonly Action<List<string>> _validateConnectionTypeCompatibility = validateConnectionTypeCompatibility;
    private readonly Action<NodeViewModel, List<string>> _validatePredicateNodes = validatePredicateNodes;
    private readonly Action<NodeViewModel, List<string>> _validateComparisonNodes = validateComparisonNodes;
    private readonly Action<NodeViewModel, List<string>> _validateNotAndJsonNodes = validateNotAndJsonNodes;
    private readonly Action<NodeViewModel, List<string>> _validateOutputSourceReachability = validateOutputSourceReachability;
    private readonly Action<NodeViewModel, IReadOnlyList<JoinDefinition>, List<string>> _validateSourceConflicts = validateSourceConflicts;
    private readonly Action<IReadOnlyList<JoinDefinition>, List<string>> _validateImplicitCrossJoins = validateImplicitCrossJoins;
    private readonly Action<NodeViewModel, List<string>> _validateSelectStarProjection = validateSelectStarProjection;
    private readonly Action<NodeViewModel, List<string>> _validatePaginationSettings = validatePaginationSettings;
    private readonly Action<NodeViewModel, List<string>> _validateQueryHints = validateQueryHints;
    private readonly Action<NodeViewModel, List<string>> _validatePivotSettings = validatePivotSettings;

    public void Execute(QueryCompilationValidationStageInput input)
    {
        _validateAliasAmbiguity(
            input.ResultOutputNode,
            input.CteDefinitions,
            input.CteDefinitionNamesById,
            input.Errors);

        _validateWindowFunctionNodes(input.Errors);
        _validateConnectionTypeCompatibility(input.Errors);
        _validatePredicateNodes(input.ResultOutputNode, input.Errors);
        _validateComparisonNodes(input.ResultOutputNode, input.Errors);
        _validateNotAndJsonNodes(input.ResultOutputNode, input.Errors);
        _validateOutputSourceReachability(input.ResultOutputNode, input.Errors);
        _validateSourceConflicts(input.ResultOutputNode, input.Joins, input.Errors);
        _validateImplicitCrossJoins(input.Joins, input.Errors);
        _validateSelectStarProjection(input.ResultOutputNode, input.Errors);
        _validatePaginationSettings(input.ResultOutputNode, input.Errors);
        _validateQueryHints(input.ResultOutputNode, input.Errors);
        _validatePivotSettings(input.ResultOutputNode, input.Errors);
    }
}
