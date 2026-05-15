namespace AkkornStudio.UI.Services.Workspace.Pages;

public sealed record WorkspaceDocumentPageContract(
    bool ShowsQueryCanvasPage,
    bool ShowsDdlCanvasPage,
    bool ShowsSqlEditorPage,
    bool ShowsSchemaComparePage,
    bool ShowsSchemaAnalysisPage,
    bool ShowsDiagramSidebar,
    bool ShowsSqlEditorSidebar,
    bool ShowsQueryTabs,
    bool CanCollapseSidebars);
