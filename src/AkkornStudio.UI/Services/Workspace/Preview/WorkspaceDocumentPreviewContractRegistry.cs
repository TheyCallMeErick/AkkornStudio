using AkkornStudio.UI.Services.Workspace.Models;

namespace AkkornStudio.UI.Services.Workspace.Preview;

public sealed class WorkspaceDocumentPreviewContractRegistry : IWorkspaceDocumentPreviewContractRegistry
{
    private static readonly WorkspaceDocumentPreviewContract QueryContract = new(
        Kind: WorkspaceDocumentPreviewKind.Query,
        Title: "Preview",
        PrimaryTabLabel: "Preview",
        UnavailableMessage: "Preview is unavailable for this document.");

    private static readonly WorkspaceDocumentPreviewContract DdlContract = new(
        Kind: WorkspaceDocumentPreviewKind.Ddl,
        Title: "SQL DDL Preview",
        PrimaryTabLabel: "SQL DDL",
        UnavailableMessage: "DDL preview is unavailable for this document.");

    private static readonly WorkspaceDocumentPreviewContract UnavailableContract = new(
        Kind: WorkspaceDocumentPreviewKind.Unavailable,
        Title: "Preview",
        PrimaryTabLabel: "Preview",
        UnavailableMessage: "Preview is unavailable for this document.");

    public WorkspaceDocumentPreviewContract Resolve(WorkspaceDocumentType documentType)
    {
        return documentType switch
        {
            WorkspaceDocumentType.QueryCanvas => QueryContract,
            WorkspaceDocumentType.DdlCanvas => DdlContract,
            WorkspaceDocumentType.SqlEditor => UnavailableContract,
            WorkspaceDocumentType.DdlSchemaCompare => UnavailableContract,
            WorkspaceDocumentType.ErDiagram => UnavailableContract,
            _ => UnavailableContract,
        };
    }
}
