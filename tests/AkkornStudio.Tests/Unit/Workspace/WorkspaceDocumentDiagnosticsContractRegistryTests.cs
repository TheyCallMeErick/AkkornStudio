using AkkornStudio.UI.Services.Workspace.Diagnostics;
using AkkornStudio.UI.Services.Workspace.Models;

namespace AkkornStudio.Tests.Unit.Workspace;

public class WorkspaceDocumentDiagnosticsContractRegistryTests
{
    [Theory]
    [InlineData(WorkspaceDocumentType.QueryCanvas, true)]
    [InlineData(WorkspaceDocumentType.DdlCanvas, true)]
    [InlineData(WorkspaceDocumentType.SqlEditor, false)]
    [InlineData(WorkspaceDocumentType.SqlResult, false)]
    [InlineData(WorkspaceDocumentType.DdlSchemaCompare, false)]
    [InlineData(WorkspaceDocumentType.ErDiagram, false)]
    public void Resolve_ReturnsExpectedDiagnosticsAvailabilityForDocumentType(
        WorkspaceDocumentType documentType,
        bool hasLocalDiagnostics)
    {
        var registry = new WorkspaceDocumentDiagnosticsContractRegistry();

        WorkspaceDocumentDiagnosticsContract contract = registry.Resolve(documentType);

        Assert.Equal(hasLocalDiagnostics, contract.HasLocalDiagnostics);
    }
}
