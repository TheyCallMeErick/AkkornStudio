using AkkornStudio.Ddl;
using AkkornStudio.UI.ViewModels.UndoRedo;

namespace AkkornStudio.UI.ViewModels.ErDiagram.Commands;

/// <summary>
/// Removes an entity column while preserving the original index for undo.
/// </summary>
public sealed class ErRemoveColumnCommand(
    ErEntityNodeViewModel entity,
    string columnName) : ICanvasCommand
{
    private readonly ErCanvasViewModel? _erCanvas;
    private readonly ErEntityNodeViewModel _entity = entity ?? throw new ArgumentNullException(nameof(entity));
    private readonly string _columnName = columnName ?? throw new ArgumentNullException(nameof(columnName));

    private int _removedIndex = -1;
    private ErColumnRowViewModel? _removedColumn;

    public ErRemoveColumnCommand(
        ErCanvasViewModel erCanvas,
        ErEntityNodeViewModel entity,
        string columnName)
        : this(entity, columnName)
    {
        _erCanvas = erCanvas ?? throw new ArgumentNullException(nameof(erCanvas));
    }

    public string Description => "ER: remove column";

    public void Execute(CanvasViewModel canvas)
    {
        _ = canvas;

        EnsureColumnNotReferencedByRelations();
        int index = FindColumnIndex();
        if (index < 0)
            throw new InvalidOperationException(
                $"Column '{_columnName}' was not found in entity '{_entity.Id}'."
            );
        EnsureEntityWillKeepAtLeastOneColumn();

        _removedIndex = index;
        _removedColumn = _entity.Columns[index];
        _entity.Columns.RemoveAt(index);
    }

    public void Undo(CanvasViewModel canvas)
    {
        _ = canvas;
        if (_removedColumn is null || _removedIndex < 0)
            return;

        int restoreIndex = Math.Min(_removedIndex, _entity.Columns.Count);
        _entity.Columns.Insert(restoreIndex, _removedColumn);
    }

    public IDdlExpression ToDdlExpression()
    {
        EnsureColumnNotReferencedByRelations();
        EnsureEntityWillKeepAtLeastOneColumn();
        EnsureColumnExistsForDdl();

        (string schema, string table) = SplitEntityId(_entity.Id);

        return new AlterTableExpr(
            schemaName: schema,
            tableName: table,
            operations:
            [
                new DropColumnOpExpr(_columnName, ifExists: false),
            ],
            emitSeparateStatements: true);
    }

    private static (string Schema, string Table) SplitEntityId(string entityId)
    {
        int separator = entityId.IndexOf('.', StringComparison.Ordinal);
        if (separator <= 0)
            return (string.Empty, entityId.Trim());

        return (entityId[..separator].Trim(), entityId[(separator + 1)..].Trim());
    }

    private int FindColumnIndex() =>
        _entity.Columns
            .Select((col, idx) => new { col, idx })
            .FirstOrDefault(x => string.Equals(x.col.ColumnName, _columnName, StringComparison.OrdinalIgnoreCase))
            ?.idx ?? -1;

    private void EnsureColumnExistsForDdl()
    {
        bool existsInEntity = FindColumnIndex() >= 0;
        if (!existsInEntity && _removedColumn is null)
            throw new InvalidOperationException(
                $"Column '{_columnName}' was not found in entity '{_entity.Id}'."
            );
    }

    private void EnsureEntityWillKeepAtLeastOneColumn()
    {
        bool existsInEntity = FindColumnIndex() >= 0;
        if (existsInEntity && _entity.Columns.Count <= 1)
        {
            throw new InvalidOperationException(
                $"Column '{_columnName}' cannot be removed because entity '{_entity.Id}' must keep at least one column."
            );
        }
    }

    private void EnsureColumnNotReferencedByRelations()
    {
        if (_erCanvas is null)
            return;

        ErRelationEdgeViewModel? relation = _erCanvas.Edges.FirstOrDefault(edge =>
            (string.Equals(edge.ChildEntityId, _entity.Id, StringComparison.OrdinalIgnoreCase)
             && edge.ChildColumns.Any(column => string.Equals(column, _columnName, StringComparison.OrdinalIgnoreCase)))
            || (string.Equals(edge.ParentEntityId, _entity.Id, StringComparison.OrdinalIgnoreCase)
                && edge.ParentColumns.Any(column => string.Equals(column, _columnName, StringComparison.OrdinalIgnoreCase))));

        if (relation is null)
            return;

        throw new InvalidOperationException(
            $"Column '{_columnName}' cannot be removed because it is referenced by relation '{relation.ConstraintLabel}'."
        );
    }
}
