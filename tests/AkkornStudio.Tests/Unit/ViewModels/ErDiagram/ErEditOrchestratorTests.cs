using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.UI.Services.SqlEditor;
using AkkornStudio.UI.ViewModels.ErDiagram;
using AkkornStudio.UI.ViewModels.ErDiagram.Commands;
using AkkornStudio.UI.ViewModels.UndoRedo;

namespace AkkornStudio.Tests.Unit.ViewModels.ErDiagram;

public sealed class ErEditOrchestratorTests
{
    [Fact]
    public void Constructor_WithNullCanvas_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ErEditOrchestrator(
            null!,
            DatabaseProvider.Postgres,
            new MutationGuardService()));
    }

    [Fact]
    public void Constructor_WithNullMutationGuardService_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ErEditOrchestrator(
            new ErCanvasViewModel(),
            DatabaseProvider.Postgres,
            null!));
    }

    [Fact]
    public void Constructor_WithNullAnalyzeMutation_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ErEditOrchestrator(
            new ErCanvasViewModel(),
            DatabaseProvider.Postgres,
            new MutationGuardService(),
            null!));
    }

    [Fact]
    public void Constructor_InternalWithNullMutationGuardService_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ErEditOrchestrator(
            new ErCanvasViewModel(),
            DatabaseProvider.Postgres,
            null!,
            _ => MutationGuardResult.Safe()));
    }

    [Fact]
    public void ApplyCommand_WithNullCommand_Throws()
    {
        using var canvas = new CanvasViewModel();
        var orchestrator = CreateOrchestrator();

        Assert.Throws<ArgumentNullException>(() => orchestrator.ApplyCommand(null!, canvas));
    }

    [Fact]
    public void ApplyCommand_WithNullCanvas_Throws()
    {
        var orchestrator = CreateOrchestrator();

        Assert.Throws<ArgumentNullException>(() => orchestrator.ApplyCommand(
            new FlagCommand(),
            null!));
    }

    [Fact]
    public void ApplyCommand_DestructiveWithoutForce_WhenRequiresConfirmation_DoesNotExecuteOrPersistDdl()
    {
        using var canvas = new CanvasViewModel();
        var erCanvas = new ErCanvasViewModel();
        ErEntityNodeViewModel entity = CreateEntity("public", "orders");
        erCanvas.Entities.Add(entity);
        var orchestrator = CreateOrchestrator(erCanvas);
        var command = new ErDropEntityCommand(erCanvas, entity);

        bool applied = orchestrator.ApplyCommand(command, canvas, force: false);

        Assert.False(applied);
        Assert.Contains(entity, erCanvas.Entities);
        Assert.Equal(string.Empty, orchestrator.LastGeneratedDdl);
        MutationGuardResult guard = Assert.IsType<MutationGuardResult>(orchestrator.LastMutationGuardResult);
        Assert.True(guard.RequiresConfirmation);
    }

    [Fact]
    public void ApplyCommand_DestructiveWithForce_ExecutesAndPersistsDdl()
    {
        using var canvas = new CanvasViewModel();
        var erCanvas = new ErCanvasViewModel();
        ErEntityNodeViewModel entity = CreateEntity("public", "orders");
        erCanvas.Entities.Add(entity);
        var orchestrator = CreateOrchestrator(erCanvas);
        var command = new ErDropEntityCommand(erCanvas, entity);

        bool applied = orchestrator.ApplyCommand(command, canvas, force: true);

        Assert.True(applied);
        Assert.DoesNotContain(entity, erCanvas.Entities);
        Assert.Contains("DROP TABLE", orchestrator.LastGeneratedDdl, StringComparison.OrdinalIgnoreCase);
        Assert.Null(orchestrator.LastMutationGuardResult);
    }

    [Fact]
    public void ApplyCommand_DestructiveWithoutForce_WhenGuardAllows_ExecutesAndPersistsDdl()
    {
        using var canvas = new CanvasViewModel();
        var erCanvas = new ErCanvasViewModel();
        ErEntityNodeViewModel entity = CreateEntity("public", "orders");
        erCanvas.Entities.Add(entity);
        var orchestrator = new ErEditOrchestrator(
            erCanvas,
            DatabaseProvider.Postgres,
            new MutationGuardService(),
            _ => MutationGuardResult.Safe());
        var command = new ErDropEntityCommand(erCanvas, entity);

        bool applied = orchestrator.ApplyCommand(command, canvas, force: false);

        Assert.True(applied);
        Assert.DoesNotContain(entity, erCanvas.Entities);
        Assert.Contains("DROP TABLE", orchestrator.LastGeneratedDdl, StringComparison.OrdinalIgnoreCase);
        Assert.Null(orchestrator.LastMutationGuardResult);
    }

    [Fact]
    public void ApplyCommand_WhenExecutionThrows_KeepsPreviousLastGeneratedDdl()
    {
        using var canvas = new CanvasViewModel();
        var erCanvas = new ErCanvasViewModel();
        var orchestrator = CreateOrchestrator(erCanvas);

        ErEntityNodeViewModel reference = CreateEntity("public", "baseline");
        string initialDdl = GetDropTableSql(orchestrator, erCanvas, reference, canvas);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            orchestrator.ApplyCommand(new ThrowingCommand(), canvas, force: true));

        Assert.Equal("boom", ex.Message);
        Assert.Equal(initialDdl, orchestrator.LastGeneratedDdl);
        Assert.Null(orchestrator.LastMutationGuardResult);
    }

    [Fact]
    public void ApplyCommand_WithUndoRedoStack_ExecutesThroughStack()
    {
        using var canvas = new CanvasViewModel();
        var erCanvas = new ErCanvasViewModel();
        var orchestrator = CreateOrchestrator(erCanvas);
        var undoRedo = new UndoRedoStack(canvas);
        var command = new FlagCommand();

        bool applied = orchestrator.ApplyCommand(command, canvas, undoRedo: undoRedo, force: true);

        Assert.True(applied);
        Assert.True(command.Executed);
        Assert.True(undoRedo.CanUndo);
    }

    [Fact]
    public void ApplyCommand_NonDestructiveWithoutForce_Executes()
    {
        using var canvas = new CanvasViewModel();
        var erCanvas = new ErCanvasViewModel();
        var orchestrator = CreateOrchestrator(erCanvas);
        var command = new FlagCommand();

        bool applied = orchestrator.ApplyCommand(command, canvas, force: false);

        Assert.True(applied);
        Assert.True(command.Executed);
        Assert.Null(orchestrator.LastMutationGuardResult);
    }

    [Fact]
    public void ApplyCommand_RemoveColumnWithoutForce_WhenRequiresConfirmation_DoesNotExecute()
    {
        using var canvas = new CanvasViewModel();
        var erCanvas = new ErCanvasViewModel();
        ErEntityNodeViewModel entity = CreateEntity("public", "orders");
        erCanvas.Entities.Add(entity);
        var orchestrator = CreateOrchestrator(erCanvas);
        var command = new ErRemoveColumnCommand(erCanvas, entity, "id");

        bool applied = orchestrator.ApplyCommand(command, canvas, force: false);

        Assert.False(applied);
        Assert.Single(entity.Columns);
        Assert.Equal("id", entity.Columns[0].ColumnName);
    }

    [Fact]
    public void ApplyCommand_RemoveForeignKeyWithoutForce_WhenRequiresConfirmation_DoesNotExecute()
    {
        using var canvas = new CanvasViewModel();
        var erCanvas = new ErCanvasViewModel();
        ErEntityNodeViewModel child = CreateEntity("public", "orders");
        ErEntityNodeViewModel parent = CreateEntity("public", "customers");
        erCanvas.Entities.Add(child);
        erCanvas.Entities.Add(parent);
        var edge = new ErRelationEdgeViewModel(
            constraintName: "fk_orders_customers",
            childEntityId: child.Id,
            parentEntityId: parent.Id,
            childColumn: "id",
            parentColumn: "id",
            onDelete: ReferentialAction.NoAction,
            onUpdate: ReferentialAction.NoAction);
        erCanvas.Edges.Add(edge);
        var orchestrator = CreateOrchestrator(erCanvas);
        var command = new ErRemoveForeignKeyCommand(erCanvas, edge);

        bool applied = orchestrator.ApplyCommand(command, canvas, force: false);

        Assert.False(applied);
        Assert.Contains(edge, erCanvas.Edges);
    }

    private static ErEditOrchestrator CreateOrchestrator(ErCanvasViewModel? erCanvas = null) =>
        new(erCanvas ?? new ErCanvasViewModel(), DatabaseProvider.Postgres, new MutationGuardService());

    private static ErEntityNodeViewModel CreateEntity(string schema, string name)
    {
        return new ErEntityNodeViewModel(
            schema: schema,
            name: name,
            isView: false,
            estimatedRowCount: null,
            columns:
            [
                new ErColumnRowViewModel("id", "int", false, true, false, true, null),
            ],
            dependencies: null,
            createStatementSql: null);
    }

    private static string GetDropTableSql(
        ErEditOrchestrator orchestrator,
        ErCanvasViewModel erCanvas,
        ErEntityNodeViewModel entity,
        CanvasViewModel canvas)
    {
        var command = new ErDropEntityCommand(erCanvas, entity);
        bool applied = orchestrator.ApplyCommand(command, canvas, force: true);
        Assert.True(applied);
        return orchestrator.LastGeneratedDdl;
    }

    private sealed class FlagCommand : ICanvasCommand
    {
        public bool Executed { get; private set; }
        public string Description => "flag";
        public void Execute(CanvasViewModel canvas) => Executed = true;
        public void Undo(CanvasViewModel canvas) => Executed = false;
    }

    private sealed class ThrowingCommand : ICanvasCommand
    {
        public string Description => "throw";
        public void Execute(CanvasViewModel canvas) => throw new InvalidOperationException("boom");
        public void Undo(CanvasViewModel canvas) { }
    }
}
