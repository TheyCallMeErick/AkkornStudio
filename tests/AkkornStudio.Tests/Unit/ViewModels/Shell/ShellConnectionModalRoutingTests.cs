using AkkornStudio.UI.Services.ConnectionManager;
using AkkornStudio.UI.Services.Workspace.Models;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.ViewModels.Shell;

public sealed class ShellConnectionModalRoutingTests
{
    [Fact]
    public void TryCloseWorkspaceDocument_WhenClosingModalOwner_ClearsModalRouteAndHidesOverlay()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        shell.ActivateDocument(WorkspaceDocumentType.DdlCanvas);
        Guid ddlDocumentId = Assert.Single(
            shell.OpenWorkspaceDocuments.Where(document => document.Descriptor.DocumentType == WorkspaceDocumentType.DdlCanvas))
            .Descriptor.DocumentId;

        CanvasViewModel ddlCanvas = Assert.IsType<CanvasViewModel>(shell.ActiveDdlCanvasDocument);
        ddlCanvas.ConnectionManager.IsVisible = true;
        shell.AttachConnectionModalToActiveDocument();
        Assert.True(shell.IsConnectionManagerOverlayVisible);

        bool closed = shell.TryCloseWorkspaceDocument(ddlDocumentId);

        Assert.True(closed);
        Assert.Equal(WorkspaceDocumentType.QueryCanvas, shell.ActiveWorkspaceDocumentType);
        Assert.False(shell.IsConnectionManagerOverlayVisible);
    }
}
