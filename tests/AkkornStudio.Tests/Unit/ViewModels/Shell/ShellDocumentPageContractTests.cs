using AkkornStudio.UI.Services.Workspace.Models;
using AkkornStudio.UI.Services.Workspace.Preview;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.ViewModels.Shell;

public class ShellDocumentPageContractTests
{
    [Fact]
    public void ActivePageContract_QueryDocument_ExposesDiagramPageWithoutQueryTabs()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        shell.ActivateDocument(WorkspaceDocumentType.QueryCanvas);

        Assert.True(shell.IsQueryDocumentPageActive);
        Assert.True(shell.IsDiagramDocumentPageActive);
        Assert.True(shell.ActivePageContract.ShowsQueryCanvasPage);
        Assert.True(shell.ActivePageContract.ShowsDiagramSidebar);
        Assert.False(shell.ActivePageContract.ShowsQueryTabs);
        Assert.True(shell.ActivePageContract.CanCollapseSidebars);
        Assert.Equal(WorkspaceDocumentPreviewKind.Query, shell.ActivePreviewContract.Kind);
        Assert.True(shell.ActiveDiagnosticsContract.HasLocalDiagnostics);
    }

    [Fact]
    public void ActivePageContract_DdlDocument_ExposesDdlPageAndDiagramSidebar()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        shell.ActivateDocument(WorkspaceDocumentType.DdlCanvas);

        Assert.True(shell.IsDdlDocumentPageActive);
        Assert.True(shell.IsDiagramDocumentPageActive);
        Assert.True(shell.ActivePageContract.ShowsDdlCanvasPage);
        Assert.True(shell.ActivePageContract.ShowsDiagramSidebar);
        Assert.False(shell.ActivePageContract.ShowsQueryTabs);
        Assert.True(shell.ActivePageContract.CanCollapseSidebars);
        Assert.Equal(WorkspaceDocumentPreviewKind.Ddl, shell.ActivePreviewContract.Kind);
        Assert.True(shell.ActiveDiagnosticsContract.HasLocalDiagnostics);
    }

    [Fact]
    public void ActivePageContract_SqlEditorDocument_ExposesSqlEditorPageAndSqlSidebars()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        shell.ActivateDocument(WorkspaceDocumentType.SqlEditor);

        Assert.True(shell.IsSqlEditorDocumentPageActive);
        Assert.False(shell.IsDiagramDocumentPageActive);
        Assert.True(shell.ActivePageContract.ShowsSqlEditorPage);
        Assert.True(shell.ActivePageContract.ShowsSqlEditorSidebar);
        Assert.False(shell.ActivePageContract.ShowsQueryTabs);
        Assert.False(shell.ActivePageContract.CanCollapseSidebars);
        Assert.Equal(WorkspaceDocumentPreviewKind.Unavailable, shell.ActivePreviewContract.Kind);
        Assert.False(shell.ActiveDiagnosticsContract.HasLocalDiagnostics);
    }

    [Fact]
    public void ActivePageContract_SchemaCompareDocument_ExposesDedicatedComparePage()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        shell.OpenNewDocument(WorkspaceDocumentType.DdlSchemaCompare);
        shell.ActivateDocument(WorkspaceDocumentType.DdlSchemaCompare);

        Assert.True(shell.IsDdlSchemaCompareDocumentPageActive);
        Assert.False(shell.IsDiagramDocumentPageActive);
        Assert.True(shell.ActivePageContract.ShowsSchemaComparePage);
        Assert.False(shell.ActivePageContract.ShowsDiagramSidebar);
        Assert.False(shell.ActivePageContract.ShowsSqlEditorSidebar);
        Assert.False(shell.ActivePageContract.CanCollapseSidebars);
        Assert.Equal(WorkspaceDocumentPreviewKind.Unavailable, shell.ActivePreviewContract.Kind);
        Assert.False(shell.ActiveDiagnosticsContract.HasLocalDiagnostics);
    }
}
