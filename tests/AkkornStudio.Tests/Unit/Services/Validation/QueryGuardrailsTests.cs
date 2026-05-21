namespace AkkornStudio.Tests.Unit.Services.Validation;

public sealed class QueryGuardrailsTests
{
    [Theory]
    [InlineData("SELECT/*comment*/* FROM users")]
    [InlineData("SELECT\t* FROM users")]
    [InlineData("SELECT * FROM (SELECT * FROM big) t")]
    public void Check_SelectStarVariants_AddsSelectStarWarning(string sql)
    {
        IReadOnlyList<GuardIssue> issues = QueryGuardrails.Check(sql);

        Assert.Contains(issues, issue => issue.Code == "SELECT_STAR" && issue.Severity == GuardSeverity.Warning);
    }

    [Fact]
    public void Check_CountStar_DoesNotAddSelectStarWarning()
    {
        IReadOnlyList<GuardIssue> issues = QueryGuardrails.Check("SELECT COUNT(*) FROM users");

        Assert.DoesNotContain(issues, issue => issue.Code == "SELECT_STAR");
    }
}
