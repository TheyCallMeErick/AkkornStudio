using Avalonia.Controls;
using Avalonia;
using AkkornStudio.Nodes;
using System.Runtime.Serialization;
using Material.Icons;
using AkkornStudio.UI.Services;
using AkkornStudio.UI.Services.Input.ShortcutRegistry;
using AkkornStudio.UI.Services.Workspace.Models;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.Canvas.Strategies;
using Xunit;

namespace AkkornStudio.Tests.Unit.Services;

public class CommandPaletteFactoryNewCanvasTests
{
    [Fact]
    public void NewCanvasCommand_UsesInjectedCreateCanvasAction()
    {
#pragma warning disable SYSLIB0050
        var window = (Window)FormatterServices.GetUninitializedObject(typeof(Window));
#pragma warning restore SYSLIB0050
        var vm = new CanvasViewModel();
        var shell = new ShellViewModel(vm, connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        var fileOps = new FileOperationsService(window, vm);
        var export = new ExportService(window, vm);
        var preview = new PreviewService(window, vm);

        bool invoked = false;
        var factory = new CommandPaletteFactory(
            window,
            () => shell.ActiveCanvas ?? vm,
            () => shell,
            fileOps,
            export,
            preview,
            () => invoked = true);
        var service = new CommandPaletteService(factory);

        service.Refresh();
        shell.SetCommandPalette(service.ViewModel);

        service.ViewModel.Open();
        var cmd = Assert.Single(service.ViewModel.Results, r => r.Shortcut == "Ctrl+N");
        cmd.Execute();

        Assert.True(invoked);
    }

    [Fact]
    public void NewCanvasCommand_UsesShortcutFromRegistryOverride()
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
        ShortcutUpdateResult overrideResult = registry.TryOverride(ShortcutActionIds.NewCanvas, "Ctrl+Shift+N");
        Assert.True(overrideResult.Success);

        var factory = new CommandPaletteFactory(
            window,
            () => shell.ActiveCanvas ?? vm,
            () => shell,
            fileOps,
            export,
            preview,
            onCreateNewCanvas: null,
            shortcutRegistry: registry);
        var service = new CommandPaletteService(factory);
        service.Refresh();
        service.ViewModel.Open();

        PaletteCommandItem cmd = Assert.Single(service.ViewModel.Results, r => r.Name.Contains("New Canvas", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Ctrl+Shift+N", cmd.Shortcut);
    }

    [Fact]
    public void TogglePreviewCommand_InQueryMode_OpensQueryPreviewState()
    {
#pragma warning disable SYSLIB0050
        var window = (Window)FormatterServices.GetUninitializedObject(typeof(Window));
#pragma warning restore SYSLIB0050
        var vm = new CanvasViewModel();
        SeedValidQueryCanvas(vm);
        var shell = new ShellViewModel(vm, connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        var fileOps = new FileOperationsService(window, vm);
        var export = new ExportService(window, vm);
        var preview = new PreviewService(window, vm);

        var factory = new CommandPaletteFactory(
            window,
            () => shell.ActiveCanvas ?? vm,
            () => shell,
            fileOps,
            export,
            preview
        );
        var service = new CommandPaletteService(factory);
        service.Refresh();
        shell.SetCommandPalette(service.ViewModel);

        var cmd = Assert.Single(service.ViewModel.Results, r => r.Shortcut == "F3");
        cmd.Execute();

        Assert.True(shell.OutputPreview.IsVisible);
        Assert.Equal(OutputPreviewModalViewModel.EOutputPreviewMode.Query, shell.OutputPreview.Mode);
        Assert.True(shell.OutputPreview.ShowQueryPrimaryContent);
    }

    [Fact]
    public void ExplainCommand_RoutesToActiveDdlCanvasWhenDdlModeIsActive()
    {
#pragma warning disable SYSLIB0050
        var window = (Window)FormatterServices.GetUninitializedObject(typeof(Window));
#pragma warning restore SYSLIB0050
        var queryVm = new CanvasViewModel();
        var shell = new ShellViewModel(queryVm, connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        var ddlVm = shell.EnsureDdlCanvas();
        shell.ActivateDocument(WorkspaceDocumentType.DdlCanvas);
        var fileOps = new FileOperationsService(window, queryVm, ddlVm);
        var export = new ExportService(window, queryVm);
        var preview = new PreviewService(window, queryVm);

        var factory = new CommandPaletteFactory(
            window,
            () => shell.ActiveCanvas ?? queryVm,
            () => shell,
            fileOps,
            export,
            preview
        );
        var service = new CommandPaletteService(factory);
        service.Refresh();
        shell.SetCommandPalette(service.ViewModel);

        var cmd = Assert.Single(service.ViewModel.Results, r => r.Shortcut == "F4");
        cmd.Execute();

        Assert.False(queryVm.ExplainPlan.IsVisible);
        Assert.True(ddlVm.ExplainPlan.IsVisible);
    }

    [Fact]
    public void TogglePreviewCommand_InDdlMode_UsesShellDdlCanvas()
    {
#pragma warning disable SYSLIB0050
        var window = (Window)FormatterServices.GetUninitializedObject(typeof(Window));
#pragma warning restore SYSLIB0050
        var queryVm = new CanvasViewModel();
        var shell = new ShellViewModel(queryVm, connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        var ensuredDdlCanvas = shell.EnsureDdlCanvas();
        var activeDdlCanvas = CreateValidDdlCanvas();
        shell.ActivateDocument(WorkspaceDocumentType.DdlCanvas);

        ensuredDdlCanvas.Nodes.Clear();
        ensuredDdlCanvas.Connections.Clear();
        foreach (var node in activeDdlCanvas.Nodes)
            ensuredDdlCanvas.Nodes.Add(node);
        foreach (var conn in activeDdlCanvas.Connections)
            ensuredDdlCanvas.Connections.Add(conn);

        var fileOps = new FileOperationsService(window, queryVm, ensuredDdlCanvas);
        var export = new ExportService(window, queryVm);
        var preview = new PreviewService(window, queryVm);

        var factory = new CommandPaletteFactory(
            window,
            () => activeDdlCanvas,
            () => shell,
            fileOps,
            export,
            preview
        );
        var service = new CommandPaletteService(factory);
        service.Refresh();
        shell.SetCommandPalette(service.ViewModel);

        var cmd = Assert.Single(service.ViewModel.Results, r => r.Shortcut == "F3");
        cmd.Execute();

        Assert.Equal(OutputPreviewModalViewModel.EOutputPreviewMode.Ddl, shell.OutputPreview.Mode);
        Assert.Contains("CREATE TABLE [dbo].[orders]", shell.OutputPreview.DdlSqlText, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(shell.OutputPreview.DdlSqlText));
    }

    [Fact]
    public void TogglePreviewCommand_InDdlMode_FallsBackToShellDdlCanvas_WhenActiveAccessorHasNoDdlOutput()
    {
#pragma warning disable SYSLIB0050
        var window = (Window)FormatterServices.GetUninitializedObject(typeof(Window));
#pragma warning restore SYSLIB0050
        var queryVm = new CanvasViewModel();
        var shell = new ShellViewModel(queryVm, connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        var ddlVm = CreateValidDdlCanvas();
        shell.ActivateDocument(WorkspaceDocumentType.DdlCanvas);

        // Ensure shell DDL canvas is the one with valid output graph.
        // We can't directly set DdlCanvas, so materialize then copy content.
        var ensuredDdl = shell.EnsureDdlCanvas();
        ensuredDdl.Nodes.Clear();
        ensuredDdl.Connections.Clear();
        foreach (var node in ddlVm.Nodes)
            ensuredDdl.Nodes.Add(node);
        foreach (var conn in ddlVm.Connections)
            ensuredDdl.Connections.Add(conn);

        var fileOps = new FileOperationsService(window, queryVm, ensuredDdl);
        var export = new ExportService(window, queryVm);
        var preview = new PreviewService(window, queryVm);

        var factory = new CommandPaletteFactory(
            window,
            () => queryVm,
            () => shell,
            fileOps,
            export,
            preview
        );
        var service = new CommandPaletteService(factory);
        service.Refresh();
        shell.SetCommandPalette(service.ViewModel);

        var cmd = Assert.Single(service.ViewModel.Results, r => r.Shortcut == "F3");
        cmd.Execute();

        Assert.Equal(OutputPreviewModalViewModel.EOutputPreviewMode.Ddl, shell.OutputPreview.Mode);
        Assert.Contains("CREATE TABLE [dbo].[orders]", shell.OutputPreview.DdlSqlText, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(shell.OutputPreview.DdlSqlText));
    }

    [Fact]
    public void ImportSqlToGraphCommand_InQueryMode_OpensSqlImporterOverlay()
    {
#pragma warning disable SYSLIB0050
        var window = (Window)FormatterServices.GetUninitializedObject(typeof(Window));
#pragma warning restore SYSLIB0050
        var queryVm = new CanvasViewModel();
        var shell = new ShellViewModel(queryVm, connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.ActivateDocument(WorkspaceDocumentType.QueryCanvas);

        var fileOps = new FileOperationsService(window, queryVm);
        var export = new ExportService(window, queryVm);
        var preview = new PreviewService(window, queryVm);

        var factory = new CommandPaletteFactory(
            window,
            () => shell.ActiveCanvas ?? queryVm,
            () => shell,
            fileOps,
            export,
            preview
        );
        var service = new CommandPaletteService(factory);
        service.Refresh();
        service.ViewModel.Open();

        PaletteCommandItem cmd = Assert.Single(
            service.ViewModel.Results,
            c => c.ActionId == "canvas.importSqlToGraph");

        cmd.Execute();

        Assert.True(queryVm.SqlImporter.IsVisible);
    }

    [Fact]
    public void ImportSqlToGraphCommand_InDdlMode_DoesNotOpenSqlImporterOverlay()
    {
#pragma warning disable SYSLIB0050
        var window = (Window)FormatterServices.GetUninitializedObject(typeof(Window));
#pragma warning restore SYSLIB0050
        var queryVm = new CanvasViewModel();
        var shell = new ShellViewModel(queryVm, connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        CanvasViewModel ddlVm = shell.EnsureDdlCanvas();
        shell.ActivateDocument(WorkspaceDocumentType.DdlCanvas);

        var fileOps = new FileOperationsService(window, queryVm, ddlVm);
        var export = new ExportService(window, queryVm);
        var preview = new PreviewService(window, queryVm);

        var factory = new CommandPaletteFactory(
            window,
            () => shell.ActiveCanvas ?? queryVm,
            () => shell,
            fileOps,
            export,
            preview
        );
        var service = new CommandPaletteService(factory);
        service.Refresh();
        service.ViewModel.Open();

        PaletteCommandItem cmd = Assert.Single(
            service.ViewModel.Results,
            c => c.ActionId == "canvas.importSqlToGraph");

        cmd.Execute();

        Assert.False(ddlVm.SqlImporter.IsVisible);
        Assert.True(shell.Toasts.IsVisible);
    }

    private static CanvasViewModel CreateValidDdlCanvas()
    {
        var ddlCanvas = new CanvasViewModel(null, null, null, null, new DdlDomainStrategy());

        var table = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.TableDefinition), new Point(0, 0));
        table.Parameters["SchemaName"] = "dbo";
        table.Parameters["TableName"] = "orders";

        var column = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.ColumnDefinition), new Point(0, 0));
        column.Parameters["ColumnName"] = "id";
        column.Parameters["DataType"] = "INT";
        column.Parameters["IsNullable"] = "false";

        var output = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.CreateTableOutput), new Point(0, 0));

        ddlCanvas.Nodes.Add(table);
        ddlCanvas.Nodes.Add(column);
        ddlCanvas.Nodes.Add(output);

        ddlCanvas.Connections.Add(new ConnectionViewModel(
            column.OutputPins.First(p => p.Name == "column"),
            new Point(0, 0),
            new Point(10, 10)
        ) { ToPin = table.InputPins.First(p => p.Name == "column") });

        ddlCanvas.Connections.Add(new ConnectionViewModel(
            table.OutputPins.First(p => p.Name == "table"),
            new Point(0, 0),
            new Point(10, 10)
        ) { ToPin = output.InputPins.First(p => p.Name == "table") });

        ddlCanvas.LiveDdl!.Recompile();
        return ddlCanvas;
    }

    private static void SeedValidQueryCanvas(CanvasViewModel canvas)
    {
        var table = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.TableSource), new Point(0, 0));
        table.Parameters["table_full_name"] = "public.orders";

        var output = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.ResultOutput), new Point(220, 0));

        canvas.Nodes.Add(table);
        canvas.Nodes.Add(output);
        canvas.Connections.Add(new ConnectionViewModel(
            table.OutputPins.First(p => p.Name == "*"),
            new Point(0, 0),
            new Point(10, 10))
        { ToPin = output.InputPins.First(p => p.Name == "column") });

    }
}
