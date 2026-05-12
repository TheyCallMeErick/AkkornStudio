using AkkornStudio.UI.Services.Workspace.Models;

namespace AkkornStudio.UI.Services.Workspace.Pages;

public sealed class WorkspaceDocumentPageContractRegistry : IWorkspaceDocumentPageContractRegistry
{
    private static readonly WorkspaceDocumentPageContract QueryContract = new(
        ShowsQueryCanvasPage: true,
        ShowsDdlCanvasPage: false,
        ShowsSqlEditorPage: false,
        ShowsSchemaComparePage: false,
        ShowsDiagramSidebar: true,
        ShowsSqlEditorSidebar: false,
        ShowsQueryTabs: false,
        CanCollapseSidebars: true);

    private static readonly WorkspaceDocumentPageContract DdlContract = new(
        ShowsQueryCanvasPage: false,
        ShowsDdlCanvasPage: true,
        ShowsSqlEditorPage: false,
        ShowsSchemaComparePage: false,
        ShowsDiagramSidebar: true,
        ShowsSqlEditorSidebar: false,
        ShowsQueryTabs: false,
        CanCollapseSidebars: true);

    private static readonly WorkspaceDocumentPageContract SqlEditorContract = new(
        ShowsQueryCanvasPage: false,
        ShowsDdlCanvasPage: false,
        ShowsSqlEditorPage: true,
        ShowsSchemaComparePage: false,
        ShowsDiagramSidebar: false,
        ShowsSqlEditorSidebar: true,
        ShowsQueryTabs: false,
        CanCollapseSidebars: false);

    private static readonly WorkspaceDocumentPageContract SqlResultContract = new(
        ShowsQueryCanvasPage: false,
        ShowsDdlCanvasPage: false,
        ShowsSqlEditorPage: false,
        ShowsSchemaComparePage: false,
        ShowsDiagramSidebar: false,
        ShowsSqlEditorSidebar: false,
        ShowsQueryTabs: false,
        CanCollapseSidebars: false);

    private static readonly WorkspaceDocumentPageContract SchemaCompareContract = new(
        ShowsQueryCanvasPage: false,
        ShowsDdlCanvasPage: false,
        ShowsSqlEditorPage: false,
        ShowsSchemaComparePage: true,
        ShowsDiagramSidebar: false,
        ShowsSqlEditorSidebar: false,
        ShowsQueryTabs: false,
        CanCollapseSidebars: false);

    private static readonly WorkspaceDocumentPageContract ErDiagramContract = new(
        ShowsQueryCanvasPage: false,
        ShowsDdlCanvasPage: false,
        ShowsSqlEditorPage: false,
        ShowsSchemaComparePage: false,
        ShowsDiagramSidebar: false,
        ShowsSqlEditorSidebar: false,
        ShowsQueryTabs: false,
        CanCollapseSidebars: false);

    public WorkspaceDocumentPageContract Resolve(WorkspaceDocumentType documentType)
    {
        return documentType switch
        {
            WorkspaceDocumentType.QueryCanvas => QueryContract,
            WorkspaceDocumentType.DdlCanvas => DdlContract,
            WorkspaceDocumentType.SqlEditor => SqlEditorContract,
            WorkspaceDocumentType.SqlResult => SqlResultContract,
            WorkspaceDocumentType.DdlSchemaCompare => SchemaCompareContract,
            WorkspaceDocumentType.ErDiagram => ErDiagramContract,
            _ => QueryContract,
        };
    }
}
