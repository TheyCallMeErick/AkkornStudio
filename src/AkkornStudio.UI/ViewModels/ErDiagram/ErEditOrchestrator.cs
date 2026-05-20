using AkkornStudio.Core;
using AkkornStudio.UI.Services.SqlEditor;
using AkkornStudio.UI.ViewModels.ErDiagram.Commands;
using AkkornStudio.UI.ViewModels.UndoRedo;

namespace AkkornStudio.UI.ViewModels.ErDiagram;

/// <summary>
/// Coordinates ER edit command execution, DDL generation, mutation guard and undo/redo integration.
/// </summary>
public sealed class ErEditOrchestrator
{
    private readonly ErCanvasViewModel _erCanvas;
    private readonly ErDdlEmitter _ddlEmitter;
    private readonly Func<string, MutationGuardResult> _analyzeMutation;

    public ErEditOrchestrator(
        ErCanvasViewModel erCanvas,
        DatabaseProvider provider,
        MutationGuardService mutationGuardService)
        : this(
            erCanvas,
            provider,
            mutationGuardService,
            mutationGuardService is null
                ? throw new ArgumentNullException(nameof(mutationGuardService))
                : mutationGuardService.Analyze)
    {
    }

    internal ErEditOrchestrator(
        ErCanvasViewModel erCanvas,
        DatabaseProvider provider,
        MutationGuardService mutationGuardService,
        Func<string, MutationGuardResult> analyzeMutation)
    {
        _erCanvas = erCanvas ?? throw new ArgumentNullException(nameof(erCanvas));
        _ = mutationGuardService ?? throw new ArgumentNullException(nameof(mutationGuardService));
        _ddlEmitter = new ErDdlEmitter(provider);
        _analyzeMutation = analyzeMutation ?? throw new ArgumentNullException(nameof(analyzeMutation));
    }

    public string LastGeneratedDdl { get; private set; } = string.Empty;
    public MutationGuardResult? LastMutationGuardResult { get; private set; }

    public bool ApplyCommand(
        ICanvasCommand command,
        CanvasViewModel canvas,
        UndoRedoStack? undoRedo = null,
        bool force = false)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(canvas);
        LastMutationGuardResult = null;

        string ddl = _ddlEmitter.Emit([command]);

        if (!force && IsDestructive(command))
        {
            MutationGuardResult guard = _analyzeMutation(ddl);
            if (guard.RequiresConfirmation)
            {
                LastMutationGuardResult = guard;
                return false;
            }
        }

        if (undoRedo is not null)
            undoRedo.Execute(command);
        else
            command.Execute(canvas);

        LastGeneratedDdl = ddl;
        return true;
    }

    private static bool IsDestructive(ICanvasCommand command) =>
        command is ErDropEntityCommand
            or ErRemoveColumnCommand
            or ErRemoveForeignKeyCommand;
}
