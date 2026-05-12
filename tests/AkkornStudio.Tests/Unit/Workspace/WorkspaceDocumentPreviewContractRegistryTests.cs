using AkkornStudio.UI.Services.Workspace.Models;
using AkkornStudio.UI.Services.Workspace.Preview;

namespace AkkornStudio.Tests.Unit.Workspace;

public class WorkspaceDocumentPreviewContractRegistryTests
{
    [Theory]
    [InlineData(WorkspaceDocumentType.QueryCanvas, WorkspaceDocumentPreviewKind.Query)]
    [InlineData(WorkspaceDocumentType.DdlCanvas, WorkspaceDocumentPreviewKind.Ddl)]
    [InlineData(WorkspaceDocumentType.SqlEditor, WorkspaceDocumentPreviewKind.Unavailable)]
    [InlineData(WorkspaceDocumentType.SqlResult, WorkspaceDocumentPreviewKind.Unavailable)]
    [InlineData(WorkspaceDocumentType.DdlSchemaCompare, WorkspaceDocumentPreviewKind.Unavailable)]
    [InlineData(WorkspaceDocumentType.ErDiagram, WorkspaceDocumentPreviewKind.Unavailable)]
    public void Resolve_ReturnsExpectedPreviewKindForDocumentType(
        WorkspaceDocumentType documentType,
        WorkspaceDocumentPreviewKind expectedKind)
    {
        var registry = new WorkspaceDocumentPreviewContractRegistry();

        WorkspaceDocumentPreviewContract contract = registry.Resolve(documentType);

        Assert.Equal(expectedKind, contract.Kind);
        Assert.False(string.IsNullOrWhiteSpace(contract.Title));
        Assert.False(string.IsNullOrWhiteSpace(contract.PrimaryTabLabel));
        Assert.False(string.IsNullOrWhiteSpace(contract.UnavailableMessage));
    }
}
