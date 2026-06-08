using AkkornStudio.UI.Services.ConnectionManager;
using AkkornStudio.UI.Services.Workspace.Models;
using AkkornStudio.UI.ViewModels;
using Xunit;

namespace AkkornStudio.Tests.Unit.ViewModels.Shell;

public sealed class ShellViewModelTabsTests
{
    private static ShellViewModel CreateShell() =>
        new(connectionManagerViewModelFactory: ConnectionManagerViewModelFactory.CreateDefault());

    [Fact]
    public void Shell_StartsWithSingleActiveQueryTab()
    {
        using var vm = CreateShell();

        Assert.Single(vm.Tabs);
        Assert.NotNull(vm.ActiveTab);
        Assert.True(vm.ActiveTab!.IsActive);
        Assert.Equal(WorkspaceDocumentType.QueryCanvas, vm.ActiveTab.CurrentMode);
        Assert.False(vm.CanCloseActiveTab);
    }

    [Fact]
    public void CreateTab_AddsAndActivatesTabInChosenMode()
    {
        using var vm = CreateShell();

        WorkspaceTabViewModel tab = vm.CreateTab(WorkspaceDocumentType.DdlCanvas);

        Assert.Equal(2, vm.Tabs.Count);
        Assert.Same(tab, vm.ActiveTab);
        Assert.True(tab.IsActive);
        Assert.Equal(WorkspaceDocumentType.DdlCanvas, tab.CurrentMode);
        Assert.True(vm.IsDdlModeActive);
        Assert.True(vm.CanCloseActiveTab);
    }

    [Fact]
    public void SwitchingModeWithinTab_PreservesTheTabsDocument()
    {
        using var vm = CreateShell();

        vm.ActivateDocument(WorkspaceDocumentType.DdlCanvas);
        object? firstDdl = vm.DdlCanvas;
        Assert.NotNull(firstDdl);

        vm.ActivateDocument(WorkspaceDocumentType.QueryCanvas);
        vm.ActivateDocument(WorkspaceDocumentType.DdlCanvas);

        Assert.Same(firstDdl, vm.DdlCanvas);
        Assert.Single(vm.Tabs);
    }

    [Fact]
    public void SeparateTabs_HaveIndependentDocumentsPerMode()
    {
        using var vm = CreateShell();

        vm.ActivateDocument(WorkspaceDocumentType.DdlCanvas);
        object? tab1Ddl = vm.DdlCanvas;
        WorkspaceTabViewModel tab1 = vm.ActiveTab!;

        vm.CreateTab(WorkspaceDocumentType.DdlCanvas);
        object? tab2Ddl = vm.DdlCanvas;

        Assert.NotNull(tab1Ddl);
        Assert.NotNull(tab2Ddl);
        Assert.NotSame(tab1Ddl, tab2Ddl);

        vm.ActivateTab(tab1);
        Assert.Same(tab1Ddl, vm.DdlCanvas);
    }

    [Fact]
    public void CloseTab_RemovesItAndActivatesNeighbor()
    {
        using var vm = CreateShell();

        WorkspaceTabViewModel first = vm.ActiveTab!;
        WorkspaceTabViewModel second = vm.CreateTab(WorkspaceDocumentType.SqlEditor);

        vm.CloseTab(second);

        Assert.Single(vm.Tabs);
        Assert.Same(first, vm.ActiveTab);
    }

    [Fact]
    public void CloseTab_IsNoOpWhenOnlyOneTab()
    {
        using var vm = CreateShell();

        vm.CloseTab(vm.ActiveTab);

        Assert.Single(vm.Tabs);
    }

    [Fact]
    public void NewTabModal_ChoosingMode_CreatesTab()
    {
        using var vm = CreateShell();

        vm.NewTabCommand.Execute(null);
        Assert.True(vm.IsNewTabModalVisible);

        vm.NewTabModal.ChooseModeCommand.Execute(WorkspaceDocumentType.SqlEditor);

        Assert.False(vm.IsNewTabModalVisible);
        Assert.Equal(2, vm.Tabs.Count);
        Assert.Equal(WorkspaceDocumentType.SqlEditor, vm.ActiveTab!.CurrentMode);
    }
}
