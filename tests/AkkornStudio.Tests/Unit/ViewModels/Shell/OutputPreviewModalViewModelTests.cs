using Avalonia;
using AkkornStudio.Nodes;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.Canvas.Strategies;

namespace AkkornStudio.Tests.Unit.ViewModels.Shell;

public class OutputPreviewModalViewModelTests
{
    [Fact]
    public void OpenForDdl_WithEmptyCanvas_ShowsModalWithoutInlineErrors()
    {
        var canvas = new CanvasViewModel(
            nodeManager: null,
            pinManager: null,
            selectionManager: null,
            localizationService: null,
            domainStrategy: new DdlDomainStrategy());
        LiveDdlBarViewModel liveDdl = Assert.IsType<LiveDdlBarViewModel>(canvas.LiveDdl);
        liveDdl.Recompile();

        var vm = new OutputPreviewModalViewModel();
        vm.OpenForDdl(canvas, liveDdl, "PostgreSQL");

        Assert.True(vm.IsVisible);
        Assert.True(vm.IsDdlMode);
        Assert.False(vm.HasDdlSql);
        Assert.True(vm.HasCanvasDiagnostics);
        Assert.True(vm.HasStructureDiagnostics);
        Assert.Same(liveDdl, vm.DdlTool);
    }

    [Fact]
    public void OpenForDdl_WithInvalidGraph_DoesNotExposeInlineCompilerErrors()
    {
        var canvas = new CanvasViewModel(
            nodeManager: null,
            pinManager: null,
            selectionManager: null,
            localizationService: null,
            domainStrategy: new DdlDomainStrategy());

        canvas.SpawnNode(NodeDefinitionRegistry.Get(NodeType.TableDefinition), new Point(40, 40));
        LiveDdlBarViewModel liveDdl = Assert.IsType<LiveDdlBarViewModel>(canvas.LiveDdl);
        liveDdl.Recompile();

        var vm = new OutputPreviewModalViewModel();
        vm.OpenForDdl(canvas, liveDdl, "PostgreSQL");

        Assert.True(vm.IsVisible);
        Assert.True(vm.IsDdlMode);
        Assert.False(vm.HasDdlSql);
        Assert.True(vm.HasCanvasDiagnostics);
        Assert.True(vm.HasStructureDiagnostics);
        Assert.Same(liveDdl, vm.DdlTool);
    }

    [Fact]
    public void OpenUnavailable_UsesExplicitUnavailableSurfaceWithoutDiagnostics()
    {
        var vm = new OutputPreviewModalViewModel();

        vm.OpenUnavailable("Preview", "Preview", "Preview indisponivel para este documento.");

        Assert.True(vm.IsVisible);
        Assert.True(vm.IsUnavailableMode);
        Assert.True(vm.ShowUnavailablePrimaryContent);
        Assert.False(vm.HasCanvasDiagnostics);
        Assert.False(vm.HasStructureDiagnostics);
        Assert.Null(vm.DdlTool);
        Assert.Equal("Preview indisponivel para este documento.", vm.UnavailableMessage);
    }

    [Fact]
    public void OpenForQuery_ClearsDdlToolAfterDdlMode()
    {
        var ddlCanvas = new CanvasViewModel(
            nodeManager: null,
            pinManager: null,
            selectionManager: null,
            localizationService: null,
            domainStrategy: new DdlDomainStrategy());
        LiveDdlBarViewModel liveDdl = Assert.IsType<LiveDdlBarViewModel>(ddlCanvas.LiveDdl);
        liveDdl.Recompile();

        var queryCanvas = new CanvasViewModel();
        LiveSqlBarViewModel liveSql = queryCanvas.LiveSql;

        var vm = new OutputPreviewModalViewModel();
        vm.OpenForDdl(ddlCanvas, liveDdl, "PostgreSQL");
        Assert.Same(liveDdl, vm.DdlTool);

        vm.OpenForQuery(queryCanvas, liveSql, "PostgreSQL");

        Assert.Null(vm.DdlTool);
        Assert.True(vm.IsQueryMode);
    }

    [Fact]
    public void OpenForSqlBenchmark_ClearsDdlToolAfterDdlMode()
    {
        var ddlCanvas = new CanvasViewModel(
            nodeManager: null,
            pinManager: null,
            selectionManager: null,
            localizationService: null,
            domainStrategy: new DdlDomainStrategy());
        LiveDdlBarViewModel liveDdl = Assert.IsType<LiveDdlBarViewModel>(ddlCanvas.LiveDdl);
        liveDdl.Recompile();

        var queryCanvas = new CanvasViewModel();
        var vm = new OutputPreviewModalViewModel();

        vm.OpenForDdl(ddlCanvas, liveDdl, "PostgreSQL");
        Assert.Same(liveDdl, vm.DdlTool);

        vm.OpenForSqlBenchmark(queryCanvas, "SELECT 1", null);

        Assert.Null(vm.DdlTool);
        Assert.True(vm.IsSqlBenchmarkMode);
    }

    [Fact]
    public void Close_ClearsDdlToolAfterDdlMode()
    {
        var ddlCanvas = new CanvasViewModel(
            nodeManager: null,
            pinManager: null,
            selectionManager: null,
            localizationService: null,
            domainStrategy: new DdlDomainStrategy());
        LiveDdlBarViewModel liveDdl = Assert.IsType<LiveDdlBarViewModel>(ddlCanvas.LiveDdl);
        liveDdl.Recompile();

        var vm = new OutputPreviewModalViewModel();
        vm.OpenForDdl(ddlCanvas, liveDdl, "PostgreSQL");
        Assert.Same(liveDdl, vm.DdlTool);

        vm.Close();

        Assert.Null(vm.DdlTool);
        Assert.False(vm.IsVisible);
    }
}
