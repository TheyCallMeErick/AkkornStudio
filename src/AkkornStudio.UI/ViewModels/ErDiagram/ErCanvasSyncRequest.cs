namespace AkkornStudio.UI.ViewModels.ErDiagram;

public sealed record ErCanvasSyncRequest(
    IReadOnlyList<ErEntityNodeViewModel> Entities,
    IReadOnlyList<ErRelationEdgeViewModel> Edges);
