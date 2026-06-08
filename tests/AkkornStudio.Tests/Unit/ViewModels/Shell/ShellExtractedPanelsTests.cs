using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.ViewModels.Shell;

public class ShellExtractedPanelsTests
{
    private const string AutoProjectionMarkerParameter = "__akkorn_auto_projection";

    [Fact]
    public void ExtractedPanels_QueryMode_BindsAndShowsQueryScaffold()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        CanvasViewModel query = shell.EnsureCanvas();

        shell.ActivateDocument(AkkornStudio.UI.Services.Workspace.Models.WorkspaceDocumentType.QueryCanvas);

        Assert.Same(query.Sidebar, shell.LeftSidebar.QuerySidebar);
        Assert.Same(query.PropertyPanel, shell.RightSidebar.PropertyPanel);
        Assert.True(shell.LeftSidebar.IsVisible);
        Assert.True(shell.RightSidebar.IsVisible);
    }

    [Fact]
    public void EnterCanvas_FirstActivation_ShowsQuerySidebars()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());

        shell.EnterCanvas();

        CanvasViewModel query = Assert.IsType<CanvasViewModel>(shell.ActiveQueryCanvasDocument);
        Assert.Same(query.Sidebar, shell.LeftSidebar.QuerySidebar);
        Assert.Same(query.PropertyPanel, shell.RightSidebar.PropertyPanel);
        Assert.True(shell.LeftSidebar.IsVisible);
        Assert.True(shell.RightSidebar.IsVisible);
    }

    [Fact]
    public void ExtractedPanels_DdlMode_ShowsFloatingSidebarsBoundToDdlCanvas()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnsureCanvas();

        shell.ActivateDocument(AkkornStudio.UI.Services.Workspace.Models.WorkspaceDocumentType.DdlCanvas);

        CanvasViewModel ddl = Assert.IsType<CanvasViewModel>(shell.DdlCanvas);
        Assert.Same(ddl.Sidebar, shell.LeftSidebar.QuerySidebar);
        Assert.Same(ddl.PropertyPanel, shell.RightSidebar.PropertyPanel);
        Assert.True(shell.LeftSidebar.IsVisible);
        Assert.True(shell.RightSidebar.IsVisible);
    }

    [Fact]
    public void ExtractedPanels_ViewSubcanvas_KeepsExpectedCanvasContext()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnsureCanvas();

        shell.ActivateDocument(AkkornStudio.UI.Services.Workspace.Models.WorkspaceDocumentType.DdlCanvas);
        shell.SetViewSubcanvasActive(true);

        Assert.Equal(CanvasContext.ViewSubcanvas, shell.ActiveCanvasContext);
    }

    [Fact]
    public void ExtractedPanels_ModeSwitch_ClearsViewSubcanvasResidualState()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnsureCanvas();

        shell.ActivateDocument(AkkornStudio.UI.Services.Workspace.Models.WorkspaceDocumentType.DdlCanvas);
        shell.SetViewSubcanvasActive(true);

        shell.ActivateDocument(AkkornStudio.UI.Services.Workspace.Models.WorkspaceDocumentType.QueryCanvas);
        Assert.Equal(CanvasContext.Query, shell.ActiveCanvasContext);
        Assert.True(shell.LeftSidebar.IsVisible);
        Assert.True(shell.RightSidebar.IsVisible);

        shell.ActivateDocument(AkkornStudio.UI.Services.Workspace.Models.WorkspaceDocumentType.DdlCanvas);
        Assert.Equal(CanvasContext.Ddl, shell.ActiveCanvasContext);
        Assert.True(shell.LeftSidebar.IsVisible);
        Assert.True(shell.RightSidebar.IsVisible);
    }

    [Fact]
    public void ExtractedPanels_EmptyState_HidesSidebars()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());

        Assert.False(shell.LeftSidebar.IsVisible);
        Assert.False(shell.RightSidebar.IsVisible);
    }

    [Fact]
    public void IsDiagramModeActive_IsTrueForQueryAndDdl_AndFalseForSqlEditor()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnsureCanvas();

        shell.ActivateDocument(AkkornStudio.UI.Services.Workspace.Models.WorkspaceDocumentType.QueryCanvas);
        Assert.True(shell.IsDiagramModeActive);

        shell.ActivateDocument(AkkornStudio.UI.Services.Workspace.Models.WorkspaceDocumentType.DdlCanvas);
        Assert.True(shell.IsDiagramModeActive);

        shell.ActivateDocument(AkkornStudio.UI.Services.Workspace.Models.WorkspaceDocumentType.SqlEditor);
        Assert.False(shell.IsDiagramModeActive);
    }

    [Fact]
    public void ExtractedPanels_QueryPropertyPanel_BindsOpenJoinInErAction()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        CanvasViewModel query = shell.EnsureCanvas();
        var joinNode = new NodeViewModel(AkkornStudio.Nodes.NodeDefinitionRegistry.Get(AkkornStudio.Nodes.NodeType.Join), new Avalonia.Point(0, 0));

        query.PropertyPanel.ShowNode(joinNode);

        Assert.True(query.PropertyPanel.CanOpenSelectedJoinInErDiagram);
        Assert.True(query.PropertyPanel.OpenSelectedJoinInErDiagramCommand.CanExecute(null));
    }

    [Fact]
    public void ExtractedPanels_QueryPropertyPanel_BindsRefineAutoProjectionAction()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        CanvasViewModel query = shell.EnsureCanvas();
        var resultOutput = new NodeViewModel(AkkornStudio.Nodes.NodeDefinitionRegistry.Get(AkkornStudio.Nodes.NodeType.ResultOutput), new Avalonia.Point(0, 0));
        resultOutput.Parameters[AutoProjectionMarkerParameter] = "true";

        query.PropertyPanel.ShowNode(resultOutput);

        Assert.True(query.PropertyPanel.CanRefineAutoProjection);
        Assert.True(query.PropertyPanel.RefineAutoProjectionCommand.CanExecute(null));
        Assert.True(query.PropertyPanel.CanResetAutoProjection);
        Assert.True(query.PropertyPanel.ResetAutoProjectionCommand.CanExecute(null));
        Assert.True(query.PropertyPanel.CanAddSuggestedFilter);
        Assert.True(query.PropertyPanel.AddSuggestedFilterCommand.CanExecute(null));
        Assert.True(query.PropertyPanel.CanApplySuggestedAggregation);
        Assert.True(query.PropertyPanel.ApplySuggestedAggregationCommand.CanExecute(null));
    }
}
