using AkkornStudio.Ddl;
using AkkornStudio.Metadata;
using AkkornStudio.UI.ViewModels.UndoRedo;

namespace AkkornStudio.UI.ViewModels.ErDiagram.Commands;

/// <summary>
/// Adds a simple foreign key edge to the ER canvas and exposes equivalent DDL.
/// </summary>
public sealed class ErAddForeignKeyCommand(
    ErCanvasViewModel erCanvas,
    string? constraintName,
    string childEntityId,
    string parentEntityId,
    string childColumn,
    string parentColumn,
    ReferentialAction onDelete,
    ReferentialAction onUpdate) : ICanvasCommand
{
    private readonly ErCanvasViewModel _erCanvas = erCanvas ?? throw new ArgumentNullException(nameof(erCanvas));
    private readonly ErRelationEdgeViewModel _edge = new(
        ValidateConstraintName(constraintName),
        childEntityId,
        parentEntityId,
        childColumn,
        parentColumn,
        onDelete,
        onUpdate);

    public string Description => "ER: add foreign key";

    public void Execute(CanvasViewModel canvas)
    {
        _ = canvas;
        EnsureEntitiesExist();

        if (!_erCanvas.Edges.Contains(_edge))
            _erCanvas.Edges.Add(_edge);
    }

    public void Undo(CanvasViewModel canvas)
    {
        _ = canvas;
        _erCanvas.Edges.Remove(_edge);
    }

    public IDdlExpression ToDdlExpression()
    {
        EnsureEntitiesExist();

        (string childSchema, string childTable) = SplitEntityId(_edge.ChildEntityId);
        (string parentSchema, string parentTable) = SplitEntityId(_edge.ParentEntityId);

        return new AlterTableExpr(
            schemaName: childSchema,
            tableName: childTable,
            operations:
            [
                new AddForeignKeyOpExpr(
                    constraintName: _edge.ConstraintName,
                    childColumn: _edge.ChildColumn,
                    parentSchema: parentSchema,
                    parentTable: parentTable,
                    parentColumn: _edge.ParentColumn,
                    onDelete: _edge.OnDelete,
                    onUpdate: _edge.OnUpdate),
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

    private void EnsureEntitiesExist()
    {
        if (_erCanvas.FindEntity(_edge.ChildEntityId) is null)
            throw new InvalidOperationException(
                $"Child entity '{_edge.ChildEntityId}' was not found in ER canvas."
            );

        if (_erCanvas.FindEntity(_edge.ParentEntityId) is null)
            throw new InvalidOperationException(
                $"Parent entity '{_edge.ParentEntityId}' was not found in ER canvas."
            );
    }

    private static string? ValidateConstraintName(string? constraintName)
    {
        if (constraintName is null)
            return null;

        string trimmed = constraintName.Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("Constraint name cannot be empty or whitespace.", nameof(constraintName));

        if (!IsIdentifierStart(trimmed[0]) || trimmed.Skip(1).Any(c => !IsIdentifierPart(c)))
        {
            throw new ArgumentException(
                $"Constraint name '{trimmed}' must be a valid SQL identifier ([A-Za-z_][A-Za-z0-9_]*).",
                nameof(constraintName));
        }

        return trimmed;
    }

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';
}
