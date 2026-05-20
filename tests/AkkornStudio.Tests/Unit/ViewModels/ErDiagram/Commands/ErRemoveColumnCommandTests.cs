using AkkornStudio.Ddl;
using AkkornStudio.Metadata;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.ErDiagram;
using AkkornStudio.UI.ViewModels.ErDiagram.Commands;

namespace AkkornStudio.Tests.Unit.ViewModels.ErDiagram.Commands;

public sealed class ErRemoveColumnCommandTests
{
    [Fact]
    public void Constructor_WithNullEntity_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ErRemoveColumnCommand(null!, "id"));
    }

    [Fact]
    public void Constructor_WithNullColumnName_Throws()
    {
        ErEntityNodeViewModel entity = CreateEntity("public", "orders", "id", "status");

        Assert.Throws<ArgumentNullException>(() => new ErRemoveColumnCommand(entity, null!));
    }

    [Fact]
    public void Constructor_WithNullCanvasInOverload_Throws()
    {
        ErEntityNodeViewModel entity = CreateEntity("public", "orders", "id", "status");

        Assert.Throws<ArgumentNullException>(() => new ErRemoveColumnCommand(null!, entity, "status"));
    }

    [Fact]
    public void ExecuteUndo_WhenColumnExists_RemovesAndRestoresAtOriginalIndex()
    {
        using var canvas = new CanvasViewModel();
        ErEntityNodeViewModel entity = CreateEntity("public", "orders", "id", "status", "created_at");
        var sut = new ErRemoveColumnCommand(entity, "status");

        sut.Execute(canvas);

        Assert.Equal(["id", "created_at"], entity.Columns.Select(column => column.ColumnName));

        sut.Undo(canvas);

        Assert.Equal(["id", "status", "created_at"], entity.Columns.Select(column => column.ColumnName));
        Assert.Equal("ER: remove column", sut.Description);
    }

    [Fact]
    public void Execute_WhenColumnIsMissing_Throws()
    {
        using var canvas = new CanvasViewModel();
        ErEntityNodeViewModel entity = CreateEntity("public", "orders", "id");
        var sut = new ErRemoveColumnCommand(entity, "missing_col");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => sut.Execute(canvas));

        Assert.Contains("missing_col", ex.Message, StringComparison.Ordinal);
        Assert.Single(entity.Columns);
    }

    [Fact]
    public void Execute_WhenRemovingLastColumn_Throws()
    {
        using var canvas = new CanvasViewModel();
        ErEntityNodeViewModel entity = CreateEntity("public", "orders", "id");
        var sut = new ErRemoveColumnCommand(entity, "id");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => sut.Execute(canvas));

        Assert.Contains("must keep at least one column", ex.Message, StringComparison.Ordinal);
        Assert.Single(entity.Columns);
    }

    [Fact]
    public void Execute_WithReferencedChildColumn_Throws()
    {
        using var canvas = new CanvasViewModel();
        var erCanvas = new ErCanvasViewModel();
        ErEntityNodeViewModel orders = CreateEntity("public", "orders", "id", "customer_id");
        ErEntityNodeViewModel customers = CreateEntity("public", "customers", "id");
        erCanvas.Entities.Add(orders);
        erCanvas.Entities.Add(customers);
        erCanvas.Edges.Add(new ErRelationEdgeViewModel(
            constraintName: "fk_orders_customers",
            childEntityId: "public.orders",
            parentEntityId: "public.customers",
            childColumn: "customer_id",
            parentColumn: "id",
            onDelete: ReferentialAction.NoAction,
            onUpdate: ReferentialAction.NoAction));
        var sut = new ErRemoveColumnCommand(erCanvas, orders, "customer_id");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => sut.Execute(canvas));

        Assert.Contains("referenced by relation", ex.Message, StringComparison.Ordinal);
        Assert.Equal(["id", "customer_id"], orders.Columns.Select(column => column.ColumnName));
    }

    [Fact]
    public void Execute_WithReferencedParentColumn_Throws()
    {
        using var canvas = new CanvasViewModel();
        var erCanvas = new ErCanvasViewModel();
        ErEntityNodeViewModel orders = CreateEntity("public", "orders", "id", "customer_id");
        ErEntityNodeViewModel customers = CreateEntity("public", "customers", "id");
        erCanvas.Entities.Add(orders);
        erCanvas.Entities.Add(customers);
        erCanvas.Edges.Add(new ErRelationEdgeViewModel(
            constraintName: "fk_orders_customers",
            childEntityId: "public.orders",
            parentEntityId: "public.customers",
            childColumn: "customer_id",
            parentColumn: "id",
            onDelete: ReferentialAction.NoAction,
            onUpdate: ReferentialAction.NoAction));
        var sut = new ErRemoveColumnCommand(erCanvas, customers, "id");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => sut.Execute(canvas));

        Assert.Contains("referenced by relation", ex.Message, StringComparison.Ordinal);
        Assert.Single(customers.Columns);
    }

    [Fact]
    public void Execute_WithCanvasButNoReferencingRelation_RemovesColumn()
    {
        using var canvas = new CanvasViewModel();
        var erCanvas = new ErCanvasViewModel();
        ErEntityNodeViewModel orders = CreateEntity("public", "orders", "id", "customer_id");
        ErEntityNodeViewModel customers = CreateEntity("public", "customers", "id");
        erCanvas.Entities.Add(orders);
        erCanvas.Entities.Add(customers);
        erCanvas.Edges.Add(new ErRelationEdgeViewModel(
            constraintName: "fk_orders_customers",
            childEntityId: "public.orders",
            parentEntityId: "public.customers",
            childColumn: "customer_id",
            parentColumn: "id",
            onDelete: ReferentialAction.NoAction,
            onUpdate: ReferentialAction.NoAction));
        var sut = new ErRemoveColumnCommand(erCanvas, orders, "id");

        sut.Execute(canvas);

        Assert.Equal(["customer_id"], orders.Columns.Select(column => column.ColumnName));
    }

    [Fact]
    public void Undo_WhenNothingWasRemoved_IsNoOp()
    {
        using var canvas = new CanvasViewModel();
        ErEntityNodeViewModel entity = CreateEntity("public", "orders", "id");
        var sut = new ErRemoveColumnCommand(entity, "id");

        sut.Undo(canvas);

        Assert.Single(entity.Columns);
    }

    [Fact]
    public void ToDdlExpression_WhenColumnExistsWithoutExecute_ReturnsDropColumnOperation()
    {
        ErEntityNodeViewModel entity = CreateEntity("public", "orders", "id", "status");
        var sut = new ErRemoveColumnCommand(entity, "status");

        var ddl = Assert.IsType<AlterTableExpr>(sut.ToDdlExpression());
        var op = Assert.IsType<DropColumnOpExpr>(Assert.Single(ddl.Operations));

        Assert.Equal("public", ddl.SchemaName);
        Assert.Equal("orders", ddl.TableName);
        Assert.Equal("status", op.ColumnName);
        Assert.False(op.IfExists);
    }

    [Fact]
    public void ToDdlExpression_WithReferencedColumn_Throws()
    {
        var erCanvas = new ErCanvasViewModel();
        ErEntityNodeViewModel orders = CreateEntity("public", "orders", "id", "customer_id");
        ErEntityNodeViewModel customers = CreateEntity("public", "customers", "id");
        erCanvas.Entities.Add(orders);
        erCanvas.Entities.Add(customers);
        erCanvas.Edges.Add(new ErRelationEdgeViewModel(
            constraintName: "fk_orders_customers",
            childEntityId: "public.orders",
            parentEntityId: "public.customers",
            childColumn: "customer_id",
            parentColumn: "id",
            onDelete: ReferentialAction.NoAction,
            onUpdate: ReferentialAction.NoAction));
        var sut = new ErRemoveColumnCommand(erCanvas, orders, "customer_id");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => sut.ToDdlExpression());

        Assert.Contains("referenced by relation", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToDdlExpression_WhenColumnIsMissingAndWasNeverRemoved_Throws()
    {
        ErEntityNodeViewModel entity = CreateEntity("public", "orders", "id");
        var sut = new ErRemoveColumnCommand(entity, "missing_col");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => sut.ToDdlExpression());

        Assert.Contains("missing_col", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToDdlExpression_WhenRemovingLastColumn_Throws()
    {
        ErEntityNodeViewModel entity = CreateEntity("public", "orders", "id");
        var sut = new ErRemoveColumnCommand(entity, "id");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => sut.ToDdlExpression());

        Assert.Contains("must keep at least one column", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToDdlExpression_AfterExecute_UsesCapturedColumnState()
    {
        using var canvas = new CanvasViewModel();
        ErEntityNodeViewModel entity = CreateEntity(string.Empty, "orders", "id", "status");
        var sut = new ErRemoveColumnCommand(entity, "status");

        sut.Execute(canvas);

        var ddl = Assert.IsType<AlterTableExpr>(sut.ToDdlExpression());
        var op = Assert.IsType<DropColumnOpExpr>(Assert.Single(ddl.Operations));

        Assert.Equal(string.Empty, ddl.SchemaName);
        Assert.Equal("orders", ddl.TableName);
        Assert.Equal("status", op.ColumnName);
    }

    private static ErEntityNodeViewModel CreateEntity(string schema, string name, params string[] columns)
    {
        return new ErEntityNodeViewModel(
            schema: schema,
            name: name,
            isView: false,
            estimatedRowCount: null,
            columns: columns.Select(static column => new ErColumnRowViewModel(column, "text", true, false, false, false, null)),
            dependencies: null,
            createStatementSql: null);
    }
}
