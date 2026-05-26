using AkkornStudio.UI.Services.QueryPreview;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.Nodes;

namespace AkkornStudio.Tests.Unit.ViewModels.QueryPreview;

public class QueryCompilationValidationStageTests
{
    [Fact]
    public void Execute_InvokesAllValidationDelegates()
    {
        int aliasCalls = 0;
        int windowCalls = 0;
        int compatCalls = 0;
        int predicateCalls = 0;
        int comparisonCalls = 0;
        int notJsonCalls = 0;
        int outputReachabilityCalls = 0;
        int sourceConflictCalls = 0;
        int implicitCrossJoinCalls = 0;
        int selectStarProjectionCalls = 0;
        int paginationCalls = 0;
        int hintsCalls = 0;
        int pivotCalls = 0;

        var stage = new QueryCompilationValidationStage(
            (resultNode, ctes, cteMap, errors) => aliasCalls++,
            errors => windowCalls++,
            errors => compatCalls++,
            (resultNode, errors) => predicateCalls++,
            (resultNode, errors) => comparisonCalls++,
            (resultNode, errors) => notJsonCalls++,
            (resultNode, errors) => outputReachabilityCalls++,
            (resultNode, joins, errors) => sourceConflictCalls++,
            (joins, errors) => implicitCrossJoinCalls++,
            (resultNode, errors) => selectStarProjectionCalls++,
            (resultNode, errors) => paginationCalls++,
            (resultNode, errors) => hintsCalls++,
            (resultNode, errors) => pivotCalls++);

        NodeViewModel resultNode = QueryPreviewTestNodeFactory.Node(NodeType.ResultOutput);
        var errors = new List<string>();

        stage.Execute(new QueryCompilationValidationStageInput(
            [],
            resultNode,
            [],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            errors));

        Assert.Equal(1, aliasCalls);
        Assert.Equal(1, windowCalls);
        Assert.Equal(1, compatCalls);
        Assert.Equal(1, predicateCalls);
        Assert.Equal(1, comparisonCalls);
        Assert.Equal(1, notJsonCalls);
        Assert.Equal(1, outputReachabilityCalls);
        Assert.Equal(1, sourceConflictCalls);
        Assert.Equal(1, implicitCrossJoinCalls);
        Assert.Equal(1, selectStarProjectionCalls);
        Assert.Equal(1, paginationCalls);
        Assert.Equal(1, hintsCalls);
        Assert.Equal(1, pivotCalls);
    }

    [Fact]
    public void Execute_AccumulatesErrorsFromValidationDelegates()
    {
        var stage = new QueryCompilationValidationStage(
            (resultNode, ctes, cteMap, errors) => errors.Add("alias-error"),
            errors => errors.Add("window-error"),
            errors => errors.Add("compat-error"),
            (resultNode, errors) => errors.Add("predicate-error"),
            (resultNode, errors) => errors.Add("comparison-error"),
            (resultNode, errors) => errors.Add("not-json-error"),
            (resultNode, errors) => errors.Add("output-reachability-error"),
            (resultNode, joins, errors) => errors.Add("source-conflict-error"),
            (joins, errors) => errors.Add("implicit-cross-join-error"),
            (resultNode, errors) => errors.Add("select-star-error"),
            (resultNode, errors) => errors.Add("pagination-error"),
            (resultNode, errors) => errors.Add("hints-error"),
            (resultNode, errors) => errors.Add("pivot-error"));

        NodeViewModel resultNode = QueryPreviewTestNodeFactory.Node(NodeType.ResultOutput);
        var errors = new List<string> { "seed-error" };

        stage.Execute(new QueryCompilationValidationStageInput(
            [],
            resultNode,
            [],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            errors));

        Assert.Contains("seed-error", errors);
        Assert.Contains("alias-error", errors);
        Assert.Contains("window-error", errors);
        Assert.Contains("compat-error", errors);
        Assert.Contains("predicate-error", errors);
        Assert.Contains("comparison-error", errors);
        Assert.Contains("not-json-error", errors);
        Assert.Contains("output-reachability-error", errors);
        Assert.Contains("source-conflict-error", errors);
        Assert.Contains("implicit-cross-join-error", errors);
        Assert.Contains("select-star-error", errors);
        Assert.Contains("pagination-error", errors);
        Assert.Contains("hints-error", errors);
        Assert.Contains("pivot-error", errors);
    }

}
