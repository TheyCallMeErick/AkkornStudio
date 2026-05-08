using AkkornStudio.UI.Services.Benchmark;

using Xunit;

namespace AkkornStudio.Tests.Unit.ViewModels;

public class StartMenuViewModelTests
{
    [Fact]
    public void CreateNewDiagramCommand_RaisesEvent()
    {
        var vm = new StartMenuViewModel();
        var called = false;

        vm.CreateNewDiagramRequested += () => called = true;
        vm.CreateNewDiagramCommand.Execute(null);

        Assert.True(called);
    }

    [Fact]
    public void OpenRecentProjectCommand_RaisesEventWithPayload()
    {
        var vm = new StartMenuViewModel();
        StartRecentProjectItem? received = null;
        var item = new StartRecentProjectItem("demo.vsaq", "Canvas", "agora", "C:/demo.vsaq");

        vm.OpenRecentProjectRequested += payload => received = payload;
        vm.OpenRecentProjectCommand.Execute(item);

        Assert.NotNull(received);
        Assert.Equal(item.DisplayName, received!.DisplayName);
    }

    [Fact]
    public void OpenTemplateCommand_RaisesEventWithPayload()
    {
        var vm = new StartMenuViewModel();
        StartTemplateItem? received = null;
        var item = new StartTemplateItem("Simple SELECT", "Basic", "desc");

        vm.OpenTemplateRequested += payload => received = payload;
        vm.OpenTemplateCommand.Execute(item);

        Assert.NotNull(received);
        Assert.Equal(item.Name, received!.Name);
    }

    [Fact]
    public void OpenSavedConnectionCommand_RaisesEventWithPayload()
    {
        var vm = new StartMenuViewModel();
        StartSavedConnectionItem? received = null;
        var item = new StartSavedConnectionItem("conn-1", "Local", "Postgres", "Salva", false);

        vm.OpenSavedConnectionRequested += payload => received = payload;
        vm.OpenSavedConnectionCommand.Execute(item);

        Assert.NotNull(received);
        Assert.Equal(item.Id, received!.Id);
    }

    [Fact]
    public void OpenSchemaCompareCommand_RaisesEvent()
    {
        var vm = new StartMenuViewModel();
        var called = false;

        vm.OpenSchemaCompareRequested += () => called = true;
        vm.OpenSchemaCompareCommand.Execute(null);

        Assert.True(called);
    }

    [Fact]
    public void RecentSearchQuery_FiltersRecentProjects()
    {
        var vm = new StartMenuViewModel();

        var allRecentField = typeof(StartMenuViewModel)
            .GetField("_allRecentProjects", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var allRecent = (List<StartRecentProjectItem>)allRecentField.GetValue(vm)!;

        allRecent.Clear();
        allRecent.Add(new StartRecentProjectItem("orders.vsaq", "Canvas", "agora", "C:/orders.vsaq"));
        allRecent.Add(new StartRecentProjectItem("customers.vsaq", "Canvas", "agora", "C:/customers.vsaq"));

        vm.RecentSearchQuery = "orders";

        Assert.Single(vm.RecentProjects);
        Assert.Equal("orders.vsaq", vm.RecentProjects[0].DisplayName);
    }

    [Fact]
    public void ToggleTemplateFavoriteCommand_TogglesFavoriteFlag()
    {
        var vm = new StartMenuViewModel();
        var item = new StartTemplateItem("Template A", "Basic", "desc");

        Assert.False(item.IsFavorite);

        vm.ToggleTemplateFavoriteCommand.Execute(item);
        Assert.True(item.IsFavorite);

        vm.ToggleTemplateFavoriteCommand.Execute(item);
        Assert.False(item.IsFavorite);
    }
}

