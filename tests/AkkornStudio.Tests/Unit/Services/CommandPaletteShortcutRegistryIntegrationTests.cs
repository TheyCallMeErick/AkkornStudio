using System.Runtime.Serialization;
using Avalonia.Controls;
using AkkornStudio.UI.Services;
using AkkornStudio.UI.Services.CommandPalette;
using AkkornStudio.UI.Services.Input.ShortcutRegistry;
using AkkornStudio.UI.Services.Workspace.Models;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.Services;

public sealed class CommandPaletteShortcutRegistryIntegrationTests
{
    [Fact]
    public void TogglePreviewCommand_UsesEffectiveGestureFromRegistry()
    {
        var fixture = CreateFixture();
        ShortcutUpdateResult update = fixture.Registry.TryOverride(ShortcutActionIds.TogglePreview, "Ctrl+Alt+P");
        Assert.True(update.Success);

        fixture.Service.Refresh();
        fixture.Service.ViewModel.Open();

        PaletteCommandItem command = Assert.Single(
            fixture.Service.ViewModel.Results,
            item => item.ActionId == ShortcutActionIds.TogglePreview);
        Assert.Equal("Ctrl+Alt+P", command.Shortcut);
    }

    [Fact]
    public void RunPreviewCommand_IsNotExposedInPalette()
    {
        var fixture = CreateFixture();
        fixture.Service.Refresh();
        fixture.Service.ViewModel.Open();
        Assert.DoesNotContain(
            fixture.Service.ViewModel.Results,
            item => item.ActionId == ShortcutActionIds.RunPreview);
    }

    [Fact]
    public void FuzzyFilter_UsesEffectiveShortcutText()
    {
        var fixture = CreateFixture();
        ShortcutUpdateResult update = fixture.Registry.TryOverride(ShortcutActionIds.TogglePreview, "Ctrl+Alt+P");
        Assert.True(update.Success);

        fixture.Service.Refresh();
        fixture.Service.ViewModel.Open();
        fixture.Service.ViewModel.Query = "ctrl alt p";

        PaletteCommandItem command = Assert.Single(
            fixture.Service.ViewModel.Results,
            item => item.ActionId == ShortcutActionIds.TogglePreview);
        Assert.Equal("Ctrl+Alt+P", command.Shortcut);
    }

    [Fact]
    public void OpenConnectionManagerCommand_UsesActiveDdlManager_WhenDdlModeIsActive()
    {
        var fixture = CreateFixture();
        fixture.Shell.EnterCanvas();
        fixture.Shell.ActivateDocument(WorkspaceDocumentType.DdlCanvas);

        CanvasViewModel queryCanvas = fixture.Shell.EnsureCanvas();
        CanvasViewModel ddlCanvas = fixture.Shell.EnsureDdlCanvas();

        fixture.Service.Refresh();
        fixture.Service.ViewModel.Open();
        PaletteCommandItem command = Assert.Single(
            fixture.Service.ViewModel.Results,
            item => item.ActionId == ShortcutActionIds.OpenConnectionManager);

        command.Execute();

        Assert.False(queryCanvas.ConnectionManager.IsVisible);
        Assert.True(ddlCanvas.ConnectionManager.IsVisible);
        Assert.True(fixture.Shell.IsDdlConnectionManagerOverlayVisible);
        Assert.False(fixture.Shell.IsQueryConnectionManagerOverlayVisible);
    }

    private static Fixture CreateFixture()
    {
#pragma warning disable SYSLIB0050
        var window = (Window)FormatterServices.GetUninitializedObject(typeof(Window));
#pragma warning restore SYSLIB0050
        var vm = new CanvasViewModel();
        var shell = new ShellViewModel(vm, connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        var fileOps = new FileOperationsService(window, vm);
        var export = new ExportService(window, vm);
        var preview = new PreviewService(window, vm);
        var registry = new global::AkkornStudio.UI.Services.Input.ShortcutRegistry.ShortcutRegistry(
            customizationStore: new NoOpShortcutCustomizationStore());

        var factory = new CommandPaletteFactory(
            window,
            () => shell.ActiveCanvas ?? vm,
            () => shell,
            fileOps,
            export,
            preview,
            shortcutRegistry: registry);

        return new Fixture(registry, new CommandPaletteService(factory), shell);
    }

    private sealed record Fixture(
        global::AkkornStudio.UI.Services.Input.ShortcutRegistry.ShortcutRegistry Registry,
        CommandPaletteService Service,
        ShellViewModel Shell);
}

