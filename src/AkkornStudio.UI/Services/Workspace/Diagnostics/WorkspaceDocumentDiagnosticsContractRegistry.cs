using AkkornStudio.UI.Services.Workspace.Models;

namespace AkkornStudio.UI.Services.Workspace.Diagnostics;

public sealed class WorkspaceDocumentDiagnosticsContractRegistry : IWorkspaceDocumentDiagnosticsContractRegistry
{
    private static readonly WorkspaceDocumentDiagnosticsContract HasDiagnosticsContract = new(HasLocalDiagnostics: true);
    private static readonly WorkspaceDocumentDiagnosticsContract NoDiagnosticsContract = new(HasLocalDiagnostics: false);

    public WorkspaceDocumentDiagnosticsContract Resolve(WorkspaceDocumentType documentType)
    {
        return documentType switch
        {
            WorkspaceDocumentType.QueryCanvas => HasDiagnosticsContract,
            WorkspaceDocumentType.DdlCanvas => HasDiagnosticsContract,
            WorkspaceDocumentType.SqlEditor => NoDiagnosticsContract,
            WorkspaceDocumentType.SqlResult => NoDiagnosticsContract,
            WorkspaceDocumentType.DdlSchemaCompare => NoDiagnosticsContract,
            WorkspaceDocumentType.DdlSchemaAnalysis => NoDiagnosticsContract,
            WorkspaceDocumentType.ErDiagram => NoDiagnosticsContract,
            _ => NoDiagnosticsContract,
        };
    }
}
