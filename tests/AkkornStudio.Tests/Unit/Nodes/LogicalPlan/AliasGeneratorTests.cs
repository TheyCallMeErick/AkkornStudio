using AkkornStudio.Nodes.LogicalPlan;

namespace AkkornStudio.Tests.Unit.Nodes.LogicalPlan;

public class AliasGeneratorTests
{
    [Fact]
    public void GenerateFor_WhenSuggestionRepeats_ProducesUniqueAliases()
    {
        var sut = new AliasGenerator();

        string first = sut.GenerateFor("orders");
        string second = sut.GenerateFor("orders");
        string third = sut.GenerateFor("orders");

        Assert.Equal("orders", first);
        Assert.Equal("orders_1", second);
        Assert.Equal("orders_2", third);
    }

    [Fact]
    public void GenerateFor_WhenSuggestionIsBlank_UsesDatasetPrefix()
    {
        var sut = new AliasGenerator();

        string alias = sut.GenerateFor(" ");

        Assert.Equal("ds", alias);
    }

    [Fact]
    public void GenerateFor_WhenAliasCountExceedsThousand_ContinuesGenerating()
    {
        var sut = new AliasGenerator();

        for (int i = 0; i <= 1000; i++)
            _ = sut.GenerateFor("orders");

        string alias = sut.GenerateFor("orders");

        Assert.Equal("orders_1001", alias);
    }

    [Fact]
    public void Reset_ClearsPreviouslyUsedAliases()
    {
        var sut = new AliasGenerator();
        _ = sut.GenerateFor("orders");
        _ = sut.GenerateFor("orders");

        sut.Reset();

        string alias = sut.GenerateFor("orders");
        Assert.Equal("orders", alias);
    }

    [Fact]
    public void GenerateFor_WhenSuggestionOnlyDiffersByCase_UsesUniqueSuffix()
    {
        var sut = new AliasGenerator();

        string first = sut.GenerateFor("Orders");
        string second = sut.GenerateFor("orders");

        Assert.Equal("Orders", first);
        Assert.Equal("orders_1", second);
    }

    [Fact]
    public void GenerateFor_WhenSuggestedAliasAlreadyHasSuffix_AvoidsDuplicateInSession()
    {
        var sut = new AliasGenerator();
        _ = sut.GenerateFor("orders");
        _ = sut.GenerateFor("orders");

        string alias = sut.GenerateFor("orders_1");

        Assert.Equal("orders_1_1", alias);
    }
}
