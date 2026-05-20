using AkkornStudio.Ddl;
using AkkornStudio.UI.ViewModels.UndoRedo;

namespace AkkornStudio.UI.ViewModels.ErDiagram.Commands;

/// <summary>
/// Renames an ER entity and updates all connected edge endpoint ids.
/// </summary>
public sealed class ErRenameEntityCommand(
    ErCanvasViewModel erCanvas,
    ErEntityNodeViewModel entity,
    string newSchema,
    string newName) : ICanvasCommand
{
    private readonly ErCanvasViewModel _erCanvas = erCanvas ?? throw new ArgumentNullException(nameof(erCanvas));
    private readonly ErEntityNodeViewModel _entity = entity ?? throw new ArgumentNullException(nameof(entity));
    private readonly string _newSchema = newSchema ?? string.Empty;
    private readonly string _newName = newName ?? string.Empty;

    private string? _oldSchema;
    private string? _oldName;
    private string? _oldId;
    private bool _wasSelected;
    private bool _captured;

    public string Description => "ER: rename entity";

    public void Execute(CanvasViewModel canvas)
    {
        _ = canvas;

        if (!_captured)
        {
            _oldSchema = _entity.Schema;
            _oldName = _entity.Name;
            _oldId = _entity.Id;
            _wasSelected = ReferenceEquals(_erCanvas.SelectedEntity, _entity);
            _captured = true;
        }

        string previousSchema = _entity.Schema;
        string previousName = _entity.Name;
        string previousId = _entity.Id;
        var snapshots = new List<EdgeEndpointSnapshot>();

        try
        {
            _entity.Rename(_newSchema, _newName);
            string currentId = _entity.Id;

            UpdateEdgeEntityIds(previousId, currentId, snapshots);

            if (_wasSelected)
                _erCanvas.SelectedEntity = _entity;
        }
        catch
        {
            RestoreEdges(snapshots);

            if (!string.Equals(_entity.Id, previousId, StringComparison.OrdinalIgnoreCase))
                _entity.Rename(previousSchema, previousName);

            if (_wasSelected)
                _erCanvas.SelectedEntity = _entity;

            throw;
        }
    }

    public void Undo(CanvasViewModel canvas)
    {
        _ = canvas;
        if (!_captured || _oldSchema is null || _oldName is null || _oldId is null)
            return;

        string currentSchema = _entity.Schema;
        string currentName = _entity.Name;
        string currentId = _entity.Id;
        var snapshots = new List<EdgeEndpointSnapshot>();

        try
        {
            _entity.Rename(_oldSchema, _oldName);
            UpdateEdgeEntityIds(currentId, _oldId, snapshots);

            if (_wasSelected)
                _erCanvas.SelectedEntity = _entity;
        }
        catch
        {
            RestoreEdges(snapshots);

            if (!string.Equals(_entity.Id, currentId, StringComparison.OrdinalIgnoreCase))
                _entity.Rename(currentSchema, currentName);

            if (_wasSelected)
                _erCanvas.SelectedEntity = _entity;

            throw;
        }
    }

    public IDdlExpression ToDdlExpression()
    {
        if (!_captured || _oldSchema is null || _oldName is null)
            throw new InvalidOperationException("Execute command before generating rename DDL.");

        return new AlterTableExpr(
            schemaName: _oldSchema,
            tableName: _oldName,
            operations:
            [
                new RenameTableOpExpr(_newName, _newSchema),
            ],
            emitSeparateStatements: true);
    }

    private void UpdateEdgeEntityIds(
        string oldEntityId,
        string newEntityId,
        ICollection<EdgeEndpointSnapshot> snapshots)
    {
        foreach (ErRelationEdgeViewModel edge in _erCanvas.Edges)
        {
            bool updateChild = string.Equals(edge.ChildEntityId, oldEntityId, StringComparison.OrdinalIgnoreCase);
            bool updateParent = string.Equals(edge.ParentEntityId, oldEntityId, StringComparison.OrdinalIgnoreCase);
            if (!updateChild && !updateParent)
                continue;

            snapshots.Add(new EdgeEndpointSnapshot(edge, edge.ChildEntityId, edge.ParentEntityId));

            if (updateChild)
                edge.ChildEntityId = newEntityId;

            if (updateParent)
                edge.ParentEntityId = newEntityId;
        }
    }

    private static void RestoreEdges(IEnumerable<EdgeEndpointSnapshot> snapshots)
    {
        foreach (EdgeEndpointSnapshot snapshot in snapshots.Reverse())
        {
            snapshot.Edge.ChildEntityId = snapshot.ChildEntityId;
            snapshot.Edge.ParentEntityId = snapshot.ParentEntityId;
        }
    }

    private sealed record EdgeEndpointSnapshot(
        ErRelationEdgeViewModel Edge,
        string ChildEntityId,
        string ParentEntityId
    );
}
