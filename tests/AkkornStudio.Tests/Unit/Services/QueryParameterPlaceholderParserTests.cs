using AkkornStudio.UI.Services;

namespace AkkornStudio.Tests.Unit.Services;

public sealed class QueryParameterPlaceholderParserTests
{
    [Fact]
    public void Parse_ReturnsDistinctNamedAndOrderedPositionalPlaceholders()
    {
        IReadOnlyList<QueryParameterPlaceholder> placeholders =
            QueryParameterPlaceholderParser.Parse(
                "SELECT * FROM users WHERE id = @id AND org_id = :orgId AND flag = ? AND code = $2 AND id = @id");

        Assert.Collection(
            placeholders,
            item =>
            {
                Assert.Equal("@id", item.Token);
                Assert.Equal(QueryParameterPlaceholderKind.Named, item.Kind);
            },
            item =>
            {
                Assert.Equal(":orgId", item.Token);
                Assert.Equal(QueryParameterPlaceholderKind.Named, item.Kind);
            },
            item =>
            {
                Assert.Equal("?", item.Token);
                Assert.Equal(QueryParameterPlaceholderKind.Positional, item.Kind);
                Assert.Equal(1, item.Position);
            },
            item =>
            {
                Assert.Equal("$2", item.Token);
                Assert.Equal(QueryParameterPlaceholderKind.Positional, item.Kind);
                Assert.Equal(2, item.Position);
            });
    }

    [Theory]
    [InlineData("@customer_id", "customer_id")]
    [InlineData(":customer_id", "customer_id")]
    [InlineData("$1", "1")]
    public void NormalizeName_RemovesPlaceholderPrefix(string token, string expected)
    {
        Assert.Equal(expected, QueryParameterPlaceholderParser.NormalizeName(token));
    }

    [Fact]
    public void GetStorageKey_DifferentiatesNamedAndPositionalPlaceholders()
    {
        QueryParameterPlaceholder named = new("@id", QueryParameterPlaceholderKind.Named);
        QueryParameterPlaceholder positionalQuestion = new("?", QueryParameterPlaceholderKind.Positional, 1);
        QueryParameterPlaceholder positionalDollar = new("$1", QueryParameterPlaceholderKind.Positional, 1);

        Assert.Equal("named:id", QueryParameterPlaceholderParser.GetStorageKey(named));
        Assert.Equal("pos:1:?", QueryParameterPlaceholderParser.GetStorageKey(positionalQuestion));
        Assert.Equal("pos:1:$1", QueryParameterPlaceholderParser.GetStorageKey(positionalDollar));
    }
}
