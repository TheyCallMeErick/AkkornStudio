using AkkornStudio.Ddl;
using AkkornStudio.Metadata;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.ErDiagram;
using AkkornStudio.UI.ViewModels.ErDiagram.Commands;

namespace AkkornStudio.Tests.Unit.ViewModels.ErDiagram.Commands;

public sealed class ErAddForeignKeyCommandTests
{
    [Fact]
    public void Constructor_WithNullCanvas_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ErAddForeignKeyCommand(
            null!,
            constraintName: "fk_orders_customers",
            childEntityId: "public.orders",
            parentEntityId: "public.customers",
            childColumn: "customer_id",
            parentColumn: "id",
            onDelete: ReferentialAction.Cascade,
            onUpdate: ReferentialAction.NoAction));
    }

    [Fact]
    public void Constructor_WithWhitespaceConstraintName_Throws()
    {
        var erCanvas = new ErCanvasViewModel();

        ArgumentException ex = Assert.Throws<ArgumentException>(() => new ErAddForeignKeyCommand(
            erCanvas,
            constraintName: "   ",
            childEntityId: "public.orders",
            parentEntityId: "public.customers",
            childColumn: "customer_id",
            parentColumn: "id",
            onDelete: ReferentialAction.Cascade,
            onUpdate: ReferentialAction.NoAction));

        Assert.Equal("constraintName", ex.ParamName);
    }

    [Theory]
    [InlineData("9bad_name")]
    [InlineData("bad-name")]
    [InlineData("bad name")]
    public void Constructor_WithInvalidConstraintName_Throws(string constraintName)
    {
        var erCanvas = new ErCanvasViewModel();

        ArgumentException ex = Assert.Throws<ArgumentException>(() => new ErAddForeignKeyCommand(
            erCanvas,
            constraintName,
            childEntityId: "public.orders",
            parentEntityId: "public.customers",
            childColumn: "customer_id",
            parentColumn: "id",
            onDelete: ReferentialAction.Cascade,
            onUpdate: ReferentialAction.NoAction));

        Assert.Equal("constraintName", ex.ParamName);
    }

    [Fact]
    public void ExecuteUndo_WhenEntitiesExist_AddsEdgeAndRemovesOnUndo()
    {
        using var canvas = new CanvasViewModel();
        var erCanvas = new ErCanvasViewModel();
        erCanvas.Entities.Add(CreateEntity("public", "orders"));
        erCanvas.Entities.Add(CreateEntity("public", "customers"));
        var sut = CreateCommand(erCanvas, "public.orders", "public.customers");

        sut.Execute(canvas);
        sut.Execute(canvas);

        ErRelationEdgeViewModel edge = Assert.Single(erCanvas.Edges);
        Assert.Equal("public.orders", edge.ChildEntityId);
        Assert.Equal("public.customers", edge.ParentEntityId);

        sut.Undo(canvas);
        Assert.Empty(erCanvas.Edges);
        Assert.Equal("ER: add foreign key", sut.Description);
    }

    [Fact]
    public void Execute_WhenChildEntityMissing_Throws()
    {
        using var canvas = new CanvasViewModel();
        var erCanvas = new ErCanvasViewModel();
        erCanvas.Entities.Add(CreateEntity("public", "customers"));
        var sut = CreateCommand(erCanvas, "public.orders", "public.customers");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => sut.Execute(canvas));

        Assert.Contains("Child entity", ex.Message, StringComparison.Ordinal);
        Assert.Empty(erCanvas.Edges);
    }

    [Fact]
    public void Execute_WhenParentEntityMissing_Throws()
    {
        using var canvas = new CanvasViewModel();
        var erCanvas = new ErCanvasViewModel();
        erCanvas.Entities.Add(CreateEntity("public", "orders"));
        var sut = CreateCommand(erCanvas, "public.orders", "public.customers");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => sut.Execute(canvas));

        Assert.Contains("Parent entity", ex.Message, StringComparison.Ordinal);
        Assert.Empty(erCanvas.Edges);
    }

    [Fact]
    public void ToDdlExpression_WhenEntitiesExist_ParsesSchemaAndNames()
    {
        var erCanvas = new ErCanvasViewModel();
        erCanvas.Entities.Add(CreateEntity("public", "orders"));
        erCanvas.Entities.Add(CreateEntity("public", "customers"));
        var sut = CreateCommand(erCanvas, "public.orders", "public.customers");

        var ddl = Assert.IsType<AlterTableExpr>(sut.ToDdlExpression());
        var op = Assert.IsType<AddForeignKeyOpExpr>(Assert.Single(ddl.Operations));

        Assert.Equal("public", ddl.SchemaName);
        Assert.Equal("orders", ddl.TableName);
        Assert.Equal("public", op.ParentSchema);
        Assert.Equal("customers", op.ParentTable);
        Assert.Equal("customer_id", op.ChildColumn);
        Assert.Equal("id", op.ParentColumn);
    }

    [Fact]
    public void ToDdlExpression_WhenEntityIdHasNoSchema_UsesEmptySchema()
    {
        var erCanvas = new ErCanvasViewModel();
        erCanvas.Entities.Add(CreateEntity(string.Empty, "orders"));
        erCanvas.Entities.Add(CreateEntity(string.Empty, "customers"));
        var sut = CreateCommand(erCanvas, "orders", "customers");

        var ddl = Assert.IsType<AlterTableExpr>(sut.ToDdlExpression());
        var op = Assert.IsType<AddForeignKeyOpExpr>(Assert.Single(ddl.Operations));

        Assert.Equal(string.Empty, ddl.SchemaName);
        Assert.Equal("orders", ddl.TableName);
        Assert.Equal(string.Empty, op.ParentSchema);
        Assert.Equal("customers", op.ParentTable);
    }

    [Fact]
    public void ToDdlExpression_WhenParentEntityMissing_Throws()
    {
        var erCanvas = new ErCanvasViewModel();
        erCanvas.Entities.Add(CreateEntity("public", "orders"));
        var sut = CreateCommand(erCanvas, "public.orders", "public.customers");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => sut.ToDdlExpression());

        Assert.Contains("Parent entity", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToDdlExpression_WithNullConstraintName_EmitsUnnamedForeignKey()
    {
        var erCanvas = new ErCanvasViewModel();
        erCanvas.Entities.Add(CreateEntity("public", "orders"));
        erCanvas.Entities.Add(CreateEntity("public", "customers"));
        var sut = new ErAddForeignKeyCommand(
            erCanvas,
            constraintName: null,
            childEntityId: "public.orders",
            parentEntityId: "public.customers",
            childColumn: "customer_id",
            parentColumn: "id",
            onDelete: ReferentialAction.Cascade,
            onUpdate: ReferentialAction.NoAction);

        var ddl = Assert.IsType<AlterTableExpr>(sut.ToDdlExpression());
        var op = Assert.IsType<AddForeignKeyOpExpr>(Assert.Single(ddl.Operations));

        Assert.Null(op.ConstraintName);
    }

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

    private static ErAddForeignKeyCommand CreateCommand(
        ErCanvasViewModel erCanvas,
        string childEntityId,
        string parentEntityId)
    {
        return new ErAddForeignKeyCommand(
            erCanvas: erCanvas,
            constraintName: "fk_orders_customers",
            childEntityId: childEntityId,
            parentEntityId: parentEntityId,
            childColumn: "customer_id",
            parentColumn: "id",
            onDelete: ReferentialAction.Cascade,
            onUpdate: ReferentialAction.NoAction);
    }
}
