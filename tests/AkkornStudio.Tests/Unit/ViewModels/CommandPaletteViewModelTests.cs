namespace AkkornStudio.Tests.Unit.ViewModels;

public class CommandPaletteViewModelTests
{
    [Fact]
    public void SetCommands_ReplacesExistingEntries()
    {
        var vm = new CommandPaletteViewModel();
        vm.RegisterCommands(
            [
                new PaletteCommandItem
                {
                    Name = "legacy",
                    Description = "legacy",
                    Tags = "legacy",
                    Execute = () => { },
                },
            ]
        );

        vm.SetCommands(
            [
                new PaletteCommandItem
                {
                    Name = "alpha",
                    Description = "new command",
                    Tags = "alpha",
                    Execute = () => { },
                },
            ]
        );

        vm.Open();
        Assert.Single(vm.Results);
        Assert.Equal("alpha", vm.Results[0].Name);
    }

    [Fact]
    public void Open_ResetsQueryAndShowsResults()
    {
        var vm = new CommandPaletteViewModel();
        vm.SetCommands(
            [
                new PaletteCommandItem
                {
                    Name = "Open settings",
                    Description = "settings",
                    Tags = "settings",
                    Execute = () => { },
                },
                new PaletteCommandItem
                {
                    Name = "Export",
                    Description = "export",
                    Tags = "export",
                    Execute = () => { },
                },
            ]
        );

        vm.Query = "sett";
        Assert.Single(vm.Results);

        vm.Open();

        Assert.True(vm.IsVisible);
        Assert.Equal(string.Empty, vm.Query);
        Assert.Equal(0, vm.SelectedIndex);
        Assert.Equal(2, vm.Results.Count);
    }

    [Fact]
    public void SelectNextAndPrev_WrapAround()
    {
        var vm = new CommandPaletteViewModel();
        vm.SetCommands(
            [
                new PaletteCommandItem { Name = "A", Description = "A", Tags = "A", Execute = () => { } },
                new PaletteCommandItem { Name = "B", Description = "B", Tags = "B", Execute = () => { } },
            ]
        );
        vm.Open();

        vm.SelectNext();
        Assert.Equal(1, vm.SelectedIndex);

        vm.SelectNext();
        Assert.Equal(0, vm.SelectedIndex);

        vm.SelectPrev();
        Assert.Equal(1, vm.SelectedIndex);
    }

    [Fact]
    public void ExecuteSelected_ExecutesAndCloses()
    {
        var vm = new CommandPaletteViewModel();
        bool executed = false;
        vm.SetCommands(
            [
                new PaletteCommandItem
                {
                    Name = "Run",
                    Description = "Run",
                    Tags = "run",
                    Execute = () => executed = true,
                },
            ]
        );

        vm.Open();
        vm.ExecuteSelected();

        Assert.True(executed);
        Assert.False(vm.IsVisible);
        Assert.Equal(string.Empty, vm.Query);
        Assert.Null(vm.LastExecutionError);
    }

    [Fact]
    public void ExecuteSelected_WhenCommandThrows_DoesNotCrashAndKeepsPaletteOpen()
    {
        var vm = new CommandPaletteViewModel();
        vm.SetCommands(
            [
                new PaletteCommandItem
                {
                    Name = "Boom",
                    Description = "Boom",
                    Tags = "boom",
                    Execute = () => throw new InvalidOperationException("explode"),
                },
            ]
        );
        vm.Open();
        vm.Query = "bo";

        vm.ExecuteSelected();

        Assert.True(vm.IsVisible);
        Assert.Equal("bo", vm.Query);
        InvalidOperationException ex = Assert.IsType<InvalidOperationException>(vm.LastExecutionError);
        Assert.Equal("explode", ex.Message);
    }

    [Fact]
    public void SelectedIndex_GreaterThanResultCount_IsClampedToLast()
    {
        var vm = new CommandPaletteViewModel();
        vm.SetCommands(
            [
                new PaletteCommandItem
                {
                    Name = "Run A",
                    Description = "Run A",
                    Tags = "a",
                    Execute = () => { },
                },
                new PaletteCommandItem
                {
                    Name = "Run B",
                    Description = "Run B",
                    Tags = "b",
                    Execute = () => { },
                },
            ]
        );
        vm.Open();
        vm.SelectedIndex = 7;

        Assert.Equal(1, vm.SelectedIndex);
    }

    [Fact]
    public void SelectedIndex_Negative_IsClampedToZero()
    {
        var vm = new CommandPaletteViewModel();
        vm.SetCommands(
            [
                new PaletteCommandItem
                {
                    Name = "Run",
                    Description = "Run",
                    Tags = "run",
                    Execute = () => { },
                },
            ]
        );
        vm.Open();
        vm.SelectedIndex = -1;

        Assert.Equal(0, vm.SelectedIndex);
    }

    [Fact]
    public void SelectedIndex_WithinBounds_IsKept()
    {
        var vm = new CommandPaletteViewModel();
        vm.SetCommands(
            [
                new PaletteCommandItem { Name = "A", Description = "A", Tags = "A", Execute = () => { } },
                new PaletteCommandItem { Name = "B", Description = "B", Tags = "B", Execute = () => { } },
            ]
        );
        vm.Open();

        vm.SelectedIndex = 1;

        Assert.Equal(1, vm.SelectedIndex);
    }

    [Fact]
    public void ExecuteSelected_WhenNoResults_IsNoOp()
    {
        var vm = new CommandPaletteViewModel();
        vm.Open();
        vm.Query = "missing";

        vm.ExecuteSelected();

        Assert.True(vm.IsVisible);
        Assert.Null(vm.LastExecutionError);
    }

    [Fact]
    public void SelectNextAndPrev_WithNoResults_DoesNotChangeIndex()
    {
        var vm = new CommandPaletteViewModel();
        vm.Open();
        vm.Query = "missing";
        vm.SelectedIndex = 3;

        vm.SelectNext();
        vm.SelectPrev();

        Assert.Equal(0, vm.SelectedIndex);
        Assert.Empty(vm.Results);
    }
}
