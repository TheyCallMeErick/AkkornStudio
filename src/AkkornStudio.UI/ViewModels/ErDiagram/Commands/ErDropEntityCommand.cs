using AkkornStudio.Ddl;
using AkkornStudio.UI.ViewModels.UndoRedo;

namespace AkkornStudio.UI.ViewModels.ErDiagram.Commands;

/// <summary>
/// Drops an ER entity and all attached edges, and exposes equivalent DROP TABLE DDL.
/// </summary>
public sealed class ErDropEntityCommand(
    ErCanvasViewModel erCanvas,
    ErEntityNodeViewModel entity) : ICanvasCommand
{
    private readonly ErCanvasViewModel _erCanvas = erCanvas ?? throw new ArgumentNullException(nameof(erCanvas));
    private readonly ErEntityNodeViewModel _entity = entity ?? throw new ArgumentNullException(nameof(entity));
    private IReadOnlyList<ErRelationEdgeViewModel> _removedEdges = [];
    private Dictionary<ErRelationEdgeViewModel, int> _removedEdgeIndexes = [];
    private int _removedEntityIndex = -1;
    private bool _entityWasPresentOnExecute;

    public string Description => "ER: drop entity";

    public void Execute(CanvasViewModel canvas)
    {
        _ = canvas;

        _removedEdges = [.. _erCanvas.Edges.Where(edge =>
            string.Equals(edge.ChildEntityId, _entity.Id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(edge.ParentEntityId, _entity.Id, StringComparison.OrdinalIgnoreCase))];
        _removedEdgeIndexes = _removedEdges.ToDictionary(edge => edge, edge => _erCanvas.Edges.IndexOf(edge));
        _removedEntityIndex = _erCanvas.Entities.IndexOf(_entity);
        _entityWasPresentOnExecute = _removedEntityIndex >= 0;

        bool wasSelected = ReferenceEquals(_erCanvas.SelectedEntity, _entity);
        bool entityRemoved = false;

        try
        {
            foreach (ErRelationEdgeViewModel edge in _removedEdges)
            {
                _erCanvas.Edges.Remove(edge);
            }

            entityRemoved = _erCanvas.Entities.Remove(_entity);
            if (wasSelected)
                _erCanvas.ClearSelection();
        }
        catch
        {
            if (entityRemoved && !_erCanvas.Entities.Contains(_entity))
                InsertEntityAtOriginalIndex();

            RestoreRemovedEdges(_removedEdges);

            if (wasSelected && _erCanvas.Entities.Contains(_entity))
                _erCanvas.SelectedEntity = _entity;

            throw;
        }
    }

    public void Undo(CanvasViewModel canvas)
    {
        _ = canvas;

        if (_entityWasPresentOnExecute && !_erCanvas.Entities.Contains(_entity))
            InsertEntityAtOriginalIndex();

        foreach (ErRelationEdgeViewModel edge in _removedEdges.OrderBy(edge => _removedEdgeIndexes.GetValueOrDefault(edge, int.MaxValue)))
        {
            if (!_erCanvas.Edges.Contains(edge))
                InsertEdgeAtOriginalIndex(edge);
        }
    }

    public IDdlExpression ToDdlExpression()
    {
        (string schema, string table) = SplitEntityId(_entity.Id);
        return new DropTableExpr(schema, table, ifExists: false);
    }

    private static (string Schema, string Table) SplitEntityId(string entityId)
    {
        int separator = entityId.IndexOf('.', StringComparison.Ordinal);
        if (separator <= 0)
            return (string.Empty, entityId.Trim());

        return (entityId[..separator].Trim(), entityId[(separator + 1)..].Trim());
    }

    private void InsertEntityAtOriginalIndex()
    {
        int insertIndex = Math.Min(_removedEntityIndex, _erCanvas.Entities.Count);
        _erCanvas.Entities.Insert(insertIndex, _entity);
    }

    private void RestoreRemovedEdges(IReadOnlyList<ErRelationEdgeViewModel> removedEdges)
    {
        foreach (ErRelationEdgeViewModel edge in removedEdges.OrderBy(edge => _removedEdgeIndexes.GetValueOrDefault(edge, int.MaxValue)))
        {
            if (!_erCanvas.Edges.Contains(edge))
                InsertEdgeAtOriginalIndex(edge);
        }
    }

    private void InsertEdgeAtOriginalIndex(ErRelationEdgeViewModel edge)
    {
        int originalIndex = _removedEdgeIndexes[edge];
        int insertIndex = Math.Min(originalIndex, _erCanvas.Edges.Count);
        _erCanvas.Edges.Insert(insertIndex, edge);
    }
}
