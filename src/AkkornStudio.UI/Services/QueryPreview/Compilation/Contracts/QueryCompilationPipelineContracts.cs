
namespace AkkornStudio.UI.Services.QueryPreview;

internal readonly record struct QueryCompilationPipelineContext(
    CanvasViewModel Canvas,
    DatabaseProvider Provider);

internal sealed record QueryCompilationInputSnapshot(
    List<NodeViewModel> TableNodes,
    List<NodeViewModel> CteSourceNodes,
    List<NodeViewModel> SubqueryNodes,
    List<NodeViewModel> CteDefinitions,
    Dictionary<string, string> CteDefinitionNamesById,
    NodeViewModel ResultOutputNode,
    string FromTable,
    List<string> Errors);

internal sealed record QueryCompilationInputStageResult(
    QueryCompilationInputSnapshot? Snapshot,
    bool ShouldShortCircuit,
    string? ShortCircuitSql,
    List<string> Errors);

internal sealed record QueryCompilationValidationStageInput(
    IReadOnlyList<JoinDefinition> Joins,
    NodeViewModel ResultOutputNode,
    IReadOnlyList<NodeViewModel> CteDefinitions,
    IReadOnlyDictionary<string, string> CteDefinitionNamesById,
    List<string> Errors);

internal sealed record QueryCompilationExecutionPlanStageInput(
    IReadOnlyList<NodeViewModel> TableNodes,
    IReadOnlyList<NodeViewModel> CteDefinitions,
    IReadOnlyDictionary<string, string> CteDefinitionNamesById,
    NodeViewModel ResultOutputNode,
    List<string> Errors);

internal sealed record QueryCompilationExecutionPlanStageResult(
    NodeGraph Graph,
    List<JoinDefinition> Joins,
    SetOperationDefinition? SetOperation,
    List<string> Errors);

internal sealed record QueryCompilationGenerationStageInput(
    string FromTable,
    NodeGraph Graph,
    IReadOnlyList<JoinDefinition> Joins,
    SetOperationDefinition? SetOperation,
    List<string> Errors);

internal sealed record QueryCompilationGenerationStageResult(
    string Sql,
    string? ExecutionSqlTemplate,
    IReadOnlyDictionary<string, object?> Bindings,
    IReadOnlyDictionary<string, QueryExecutionParameterContext> ParameterContexts,
    List<string> Errors);
