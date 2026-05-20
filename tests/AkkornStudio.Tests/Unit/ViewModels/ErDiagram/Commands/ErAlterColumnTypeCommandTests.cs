using AkkornStudio.Ddl;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.ErDiagram;
using AkkornStudio.UI.ViewModels.ErDiagram.Commands;

namespace AkkornStudio.Tests.Unit.ViewModels.ErDiagram.Commands;

public sealed class ErAlterColumnTypeCommandTests
{
    [Fact]
    public void Constructor_WithNullEntity_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ErAlterColumnTypeCommand(
            null!,
            "id",
            "bigint"));
    }

    [Fact]
    public void Constructor_WithNullColumnName_Throws()
    {
        ErEntityNodeViewModel entity = CreateEntity(
            "public",
            "orders",
            new ErColumnRowViewModel("id", "int", true, false, false, false, null));

        Assert.Throws<ArgumentNullException>(() => new ErAlterColumnTypeCommand(
            entity,
            null!,
            "bigint"));
    }

    [Fact]
    public void Constructor_WithNullNewDataType_Throws()
    {
        ErEntityNodeViewModel entity = CreateEntity(
            "public",
            "orders",
            new ErColumnRowViewModel("id", "int", true, false, false, false, null));

        Assert.Throws<ArgumentNullException>(() => new ErAlterColumnTypeCommand(
            entity,
            "id",
            null!));
    }

    [Fact]
    public void ExecuteUndo_WhenColumnExists_ChangesTypeAndRestoresOriginal()
    {
        using var canvas = new CanvasViewModel();
        ErEntityNodeViewModel entity = CreateEntity(
            "public",
            "orders",
            new ErColumnRowViewModel(
                columnName: "id",
                dataType: "int",
                isNullable: false,
                isPrimaryKey: true,
                isForeignKey: false,
                isUnique: true,
                comment: "pk"));
        var sut = new ErAlterColumnTypeCommand(entity, "id", "bigint");

        sut.Execute(canvas);

        ErColumnRowViewModel changed = Assert.Single(entity.Columns);
        Assert.Equal("bigint", changed.DataType);
        Assert.False(changed.IsNullable);
        Assert.True(changed.IsPrimaryKey);
        Assert.True(changed.IsUnique);

        sut.Undo(canvas);

        ErColumnRowViewModel restored = Assert.Single(entity.Columns);
        Assert.Equal("int", restored.DataType);
        Assert.False(restored.IsNullable);
        Assert.Equal("ER: alter column type", sut.Description);
    }

    [Fact]
    public void ExecuteUndo_WhenColumnIsMissing_IsNoOp()
    {
        using var canvas = new CanvasViewModel();
        ErEntityNodeViewModel entity = CreateEntity(
            "public",
            "orders",
            new ErColumnRowViewModel("id", "int", false, true, false, true, null));
        var sut = new ErAlterColumnTypeCommand(entity, "missing_col", "bigint");

        sut.Execute(canvas);
        Assert.Equal("int", Assert.Single(entity.Columns).DataType);

        sut.Undo(canvas);
        Assert.Equal("int", Assert.Single(entity.Columns).DataType);
    }

    [Fact]
    public void Undo_WhenIndexIsOutOfRange_DoesNotThrow()
    {
        using var canvas = new CanvasViewModel();
        ErEntityNodeViewModel entity = CreateEntity(
            "public",
            "orders",
            new ErColumnRowViewModel("id", "int", false, true, false, true, null));
        var sut = new ErAlterColumnTypeCommand(entity, "id", "bigint");
        sut.Execute(canvas);

        entity.Columns.Clear();

        sut.Undo(canvas);
        Assert.Empty(entity.Columns);
    }

    [Fact]
    public void ToDdlExpression_WhenColumnExistsWithoutExecute_UsesCurrentNullability()
    {
        ErEntityNodeViewModel entity = CreateEntity(
            "public",
            "orders",
            new ErColumnRowViewModel("id", "int", false, true, false, true, null));
        var sut = new ErAlterColumnTypeCommand(entity, "id", "bigint");

        var ddl = Assert.IsType<AlterTableExpr>(sut.ToDdlExpression());
        var op = Assert.IsType<AlterColumnTypeOpExpr>(Assert.Single(ddl.Operations));

        Assert.Equal("public", ddl.SchemaName);
        Assert.Equal("orders", ddl.TableName);
        Assert.Equal("id", op.ColumnName);
        Assert.Equal("bigint", op.NewDataType);
        Assert.False(op.IsNullable);
    }

    [Fact]
    public void ToDdlExpression_WhenColumnIsMissing_Throws()
    {
        ErEntityNodeViewModel entity = CreateEntity(
            string.Empty,
            "orders",
            new ErColumnRowViewModel("id", "int", true, false, false, false, null));
        var sut = new ErAlterColumnTypeCommand(entity, "missing_col", "bigint");

        var ex = Assert.Throws<InvalidOperationException>(() => sut.ToDdlExpression());
        Assert.Contains("missing_col", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToDdlExpression_WhenEntityIdHasNoSchema_UsesEmptySchema()
    {
        ErEntityNodeViewModel entity = CreateEntity(
            string.Empty,
            "orders",
            new ErColumnRowViewModel("id", "int", true, false, false, false, null));
        var sut = new ErAlterColumnTypeCommand(entity, "id", "varchar(32)");

        var ddl = Assert.IsType<AlterTableExpr>(sut.ToDdlExpression());

        Assert.Equal(string.Empty, ddl.SchemaName);
        Assert.Equal("orders", ddl.TableName);
    }

    [Fact]
    public void ToDdlExpression_AfterExecute_UsesSnapshotNullabilityFromOriginalColumn()
    {
        using var canvas = new CanvasViewModel();
        ErEntityNodeViewModel entity = CreateEntity(
            "public",
            "orders",
            new ErColumnRowViewModel("id", "int", false, true, false, true, null));
        var sut = new ErAlterColumnTypeCommand(entity, "id", "bigint");

        sut.Execute(canvas);
        ErColumnRowViewModel changed = Assert.Single(entity.Columns);
        Assert.False(changed.IsNullable);

        var ddl = Assert.IsType<AlterTableExpr>(sut.ToDdlExpression());
        var op = Assert.IsType<AlterColumnTypeOpExpr>(Assert.Single(ddl.Operations));
        Assert.False(op.IsNullable);
    }

    private static ErEntityNodeViewModel CreateEntity(
        string schema,
        string name,
        params ErColumnRowViewModel[] columns)
    {
        return new ErEntityNodeViewModel(
            schema: schema,
            name: name,
            isView: false,
            estimatedRowCount: null,
            columns: columns,
            dependencies: null,
            createStatementSql: null);
    }
}
