using Avalonia;
using AkkornStudio.Nodes;
using AkkornStudio.UI.Services.Validation;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.UndoRedo.Commands;

namespace AkkornStudio.Tests.Unit.ViewModels.UndoRedo.Commands;

public sealed class AutoFixNamingCommandTests
{
    [Fact]
    public void Constructor_WhenNoFixableAliases_ReportsNoChanges()
    {
        NodeViewModel valid = new("public.orders", [("id", PinDataType.Integer)], new Point(0, 0))
        {
            Alias = "orders",
        };
        NodeViewModel blank = new("public.customers", [("id", PinDataType.Integer)], new Point(220, 0))
        {
            Alias = "   ",
        };
        NodeViewModel tooLongButSameNormalization = new(
            "public.invoices",
            [("id", PinDataType.Integer)],
            new Point(440, 0))
        {
            Alias = new string('a', 65),
        };

        var sut = new AutoFixNamingCommand([valid, blank, tooLongButSameNormalization]);

        Assert.False(sut.HasChanges);
        Assert.Equal("Fix 0 alias name(s)", sut.Description);
    }

    [Fact]
    public void ExecuteAndUndo_WhenSingleAliasNeedsFix_UpdatesAndRestoresAlias()
    {
        using var canvas = new CanvasViewModel();
        NodeViewModel node = new("public.orders", [("order id", PinDataType.Integer)], new Point(0, 0))
        {
            Alias = "Order Total",
        };
        canvas.Nodes.Add(node);

        var sut = new AutoFixNamingCommand([node], NamingConventionPolicy.Default);

        Assert.True(sut.HasChanges);
        Assert.Equal("Fix alias 'Order Total' → 'order_total'", sut.Description);

        sut.Execute(canvas);
        Assert.Equal("order_total", node.Alias);

        sut.Undo(canvas);
        Assert.Equal("Order Total", node.Alias);
    }

    [Fact]
    public void Description_WhenMultipleRenames_UsesPluralText()
    {
        NodeViewModel a = new("public.orders", [("id", PinDataType.Integer)], new Point(0, 0))
        {
            Alias = "Order Total",
        };
        NodeViewModel b = new("public.customers", [("id", PinDataType.Integer)], new Point(220, 0))
        {
            Alias = "Customer Total",
        };

        var sut = new AutoFixNamingCommand([a, b], NamingConventionPolicy.Default);

        Assert.True(sut.HasChanges);
        Assert.Equal("Fix 2 alias name(s)", sut.Description);
    }

    [Fact]
    public void Constructor_WhenFixCreatesAliasCollision_Throws()
    {
        NodeViewModel willBeNormalized = new("public.orders", [("id", PinDataType.Integer)], new Point(0, 0))
        {
            Alias = "Order Total",
        };
        NodeViewModel alreadyUsingAlias = new("public.customers", [("id", PinDataType.Integer)], new Point(220, 0))
        {
            Alias = "order_total",
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new AutoFixNamingCommand([willBeNormalized, alreadyUsingAlias], NamingConventionPolicy.Default));

        Assert.Contains("order_total", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_WhenDuplicateAliasAlreadyExistsWithoutNewCollision_DoesNotThrow()
    {
        NodeViewModel a = new("public.orders", [("id", PinDataType.Integer)], new Point(0, 0))
        {
            Alias = "dup_alias",
        };
        NodeViewModel b = new("public.customers", [("id", PinDataType.Integer)], new Point(220, 0))
        {
            Alias = "dup_alias",
        };

        var sut = new AutoFixNamingCommand([a, b], NamingConventionPolicy.Default);

        Assert.False(sut.HasChanges);
        Assert.Equal("Fix 0 alias name(s)", sut.Description);
    }
}
