namespace AkkornStudio.UI.Services.QueryPreview;

internal sealed class QueryCompilationSelectStarProjectionValidator(CanvasViewModel canvas)
{
    private readonly CanvasViewModel _canvas = canvas;

    public void Validate(NodeViewModel resultOutputNode, List<string> errors)
    {
        IReadOnlyList<PinViewModel> projectedPins = QueryCompilationNodeGraphAssembler
            .CollectProjectedPins(_canvas, resultOutputNode);

        if (projectedPins.Count == 0)
            return;

        bool allWildcard = projectedPins.All(QueryCompilationNodeGraphAssembler.IsWildcardProjectionPin);
        if (!allWildcard)
            return;

        errors.Add(
            "SELECT * projection detected. Prefer explicit columns to reduce accidental data exposure and large payloads."
        );
    }
}
