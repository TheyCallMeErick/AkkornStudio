using AkkornStudio.UI.Services.ConnectionManager;
using AkkornStudio.UI.Services.Workspace.Models;
using AkkornStudio.UI.Services.Workspace.Pages;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.ViewModels.Shell;

public sealed class ShellPageContractFallbackTests
{
    [Fact]
    public void Constructor_WhenPageContractRegistryThrows_UsesFallbackContract()
    {
        var shell = new ShellViewModel(
            pageContractRegistry: new ThrowingPageContractRegistry(),
            connectionManagerViewModelFactory: ConnectionManagerViewModelFactory.CreateDefault());

        WorkspaceDocumentPageContract active = shell.ActivePageContract;

        Assert.True(active.ShowsQueryCanvasPage);
        Assert.True(active.ShowsDiagramSidebar);
        Assert.False(active.ShowsDdlCanvasPage);
    }

    private sealed class ThrowingPageContractRegistry : IWorkspaceDocumentPageContractRegistry
    {
        public WorkspaceDocumentPageContract Resolve(WorkspaceDocumentType documentType)
        {
            throw new InvalidOperationException("registry not ready");
        }
    }
}
