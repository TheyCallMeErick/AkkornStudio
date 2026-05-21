using AkkornStudio.UI.Services.SqlEditor;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.Services.SqlEditor;

public sealed class MutationGuardServiceTests
{
    [Fact]
    public void Analyze_NullOrWhitespace_ReturnsSafe()
    {
        var sut = new MutationGuardService();

        MutationGuardResult nullResult = sut.Analyze(null);
        MutationGuardResult whitespaceResult = sut.Analyze("   ");

        Assert.True(nullResult.IsSafe);
        Assert.False(nullResult.RequiresConfirmation);
        Assert.Empty(nullResult.Issues);
        Assert.True(whitespaceResult.IsSafe);
        Assert.False(whitespaceResult.RequiresConfirmation);
        Assert.Empty(whitespaceResult.Issues);
    }

    [Fact]
    public void Analyze_DeleteWithoutWhere_RequiresConfirmation()
    {
        var sut = new MutationGuardService();

        MutationGuardResult result = sut.Analyze("DELETE FROM orders;");

        Assert.False(result.IsSafe);
        Assert.True(result.RequiresConfirmation);
        Assert.Contains(result.Issues, i => i.Code == "NO_WHERE" && i.Severity == MutationGuardSeverity.Critical);
        Assert.Equal("SELECT COUNT(*) FROM orders", result.CountQuery);
    }

    [Fact]
    public void Analyze_DeleteWithTrivialWhere_RequiresConfirmation()
    {
        var sut = new MutationGuardService();

        MutationGuardResult result = sut.Analyze("DELETE FROM orders WHERE TRUE;");

        Assert.False(result.IsSafe);
        Assert.True(result.RequiresConfirmation);
        Assert.Contains(result.Issues, i => i.Code == "TRIVIAL_WHERE");
        Assert.Equal("SELECT COUNT(*) FROM orders WHERE TRUE", result.CountQuery);
    }

    [Fact]
    public void Analyze_UpdateWithTrivialWhere_RequiresConfirmation()
    {
        var sut = new MutationGuardService();

        MutationGuardResult result = sut.Analyze("UPDATE orders SET status = 'x' WHERE 1 = 1;");

        Assert.False(result.IsSafe);
        Assert.True(result.RequiresConfirmation);
        Assert.Contains(result.Issues, i => i.Code == "TRIVIAL_WHERE");
        Assert.Equal("SELECT COUNT(*) FROM orders WHERE 1 = 1", result.CountQuery);
    }

    [Theory]
    [InlineData("DELETE FROM orders WHERE ( 1 = 1 );")]
    [InlineData("DELETE FROM orders WHERE ((TRUE));")]
    [InlineData("DELETE FROM orders WHERE 1=1 AND TRUE;")]
    [InlineData("UPDATE orders SET status = 'x' WHERE (1=1) AND (TRUE);")]
    public void Analyze_TrivialWhereVariants_RequireConfirmation(string sql)
    {
        var sut = new MutationGuardService();

        MutationGuardResult result = sut.Analyze(sql);

        Assert.False(result.IsSafe);
        Assert.True(result.RequiresConfirmation);
        Assert.Contains(result.Issues, i => i.Code == "TRIVIAL_WHERE" && i.Severity == MutationGuardSeverity.Critical);
    }

    [Fact]
    public void Analyze_NonTrivialOrPredicate_DoesNotFlagTrivialWhere()
    {
        var sut = new MutationGuardService();

        MutationGuardResult result = sut.Analyze("DELETE FROM orders WHERE 1=1 OR id = 10;");

        Assert.True(result.IsSafe);
        Assert.False(result.RequiresConfirmation);
        Assert.DoesNotContain(result.Issues, i => i.Code == "TRIVIAL_WHERE");
    }

    [Fact]
    public void Analyze_DeleteWithWhereKeywordInsideStringLiteral_UsesTopLevelWhereOnly()
    {
        var sut = new MutationGuardService();

        MutationGuardResult result = sut.Analyze("DELETE FROM orders WHERE (msg = 'UPDATE WHERE id=1' OR id = 1);");

        Assert.True(result.IsSafe);
        Assert.False(result.RequiresConfirmation);
        Assert.DoesNotContain(result.Issues, i => i.Code == "TRIVIAL_WHERE");
        Assert.Equal("SELECT COUNT(*) FROM orders WHERE (msg = 'UPDATE WHERE id=1' OR id = 1)", result.CountQuery);
    }

    [Fact]
    public void Analyze_UpdateWithoutWhere_RequiresConfirmation()
    {
        var sut = new MutationGuardService();

        MutationGuardResult result = sut.Analyze("UPDATE orders SET status = 'x';");

        Assert.False(result.IsSafe);
        Assert.True(result.RequiresConfirmation);
        Assert.Contains(result.Issues, i => i.Code == "NO_WHERE");
        Assert.Equal("SELECT COUNT(*) FROM orders", result.CountQuery);
    }

    [Fact]
    public void Analyze_UpdateFrom_BuildsJoinedCountQuery()
    {
        var sut = new MutationGuardService();

        MutationGuardResult result = sut.Analyze("UPDATE orders o SET status = c.status FROM customers c WHERE o.customer_id = c.id AND c.active = true;");

        Assert.True(result.IsSafe);
        Assert.False(result.RequiresConfirmation);
        Assert.True(result.SupportsDiff);
        Assert.Equal("SELECT COUNT(*) FROM orders o, customers c WHERE o.customer_id = c.id AND c.active = true", result.CountQuery);
    }

    [Fact]
    public void Analyze_DeleteUsing_BuildsJoinedCountQuery()
    {
        var sut = new MutationGuardService();

        MutationGuardResult result = sut.Analyze("DELETE FROM orders o USING customers c WHERE o.customer_id = c.id AND c.blocked = true;");

        Assert.True(result.IsSafe);
        Assert.False(result.RequiresConfirmation);
        Assert.True(result.SupportsDiff);
        Assert.Equal("SELECT COUNT(*) FROM orders o, customers c WHERE o.customer_id = c.id AND c.blocked = true", result.CountQuery);
    }

    [Fact]
    public void Analyze_DeleteWithAlias_PreservesAliasInCountQuery()
    {
        var sut = new MutationGuardService();

        MutationGuardResult result = sut.Analyze("DELETE FROM users u WHERE u.id > 5;");

        Assert.True(result.IsSafe);
        Assert.False(result.RequiresConfirmation);
        Assert.Equal("SELECT COUNT(*) FROM users u WHERE u.id > 5", result.CountQuery);
    }

    [Fact]
    public void Analyze_DeleteWithQuotedTargetAndAlias_PreservesAliasInCountQuery()
    {
        var sut = new MutationGuardService();

        MutationGuardResult result = sut.Analyze("DELETE FROM \"public\".\"users\" u WHERE u.id > 5;");

        Assert.True(result.IsSafe);
        Assert.False(result.RequiresConfirmation);
        Assert.Equal("SELECT COUNT(*) FROM \"public\".\"users\" u WHERE u.id > 5", result.CountQuery);
    }

    [Fact]
    public void Analyze_DeleteWithQuotedTargetContainingSpaces_PreservesFullQualifiedIdentifier()
    {
        var sut = new MutationGuardService();

        MutationGuardResult result = sut.Analyze("DELETE FROM \"sales region\".\"order items\" oi WHERE oi.id > 5;");

        Assert.True(result.IsSafe);
        Assert.False(result.RequiresConfirmation);
        Assert.Equal("SELECT COUNT(*) FROM \"sales region\".\"order items\" oi WHERE oi.id > 5", result.CountQuery);
    }

    [Fact]
    public void Analyze_DeleteWithBracketQuotedTargetContainingSpaces_PreservesFullQualifiedIdentifier()
    {
        var sut = new MutationGuardService();

        MutationGuardResult result = sut.Analyze("DELETE FROM [sales region].[order items] oi WHERE oi.id > 5;");

        Assert.True(result.IsSafe);
        Assert.False(result.RequiresConfirmation);
        Assert.Equal("SELECT COUNT(*) FROM [sales region].[order items] oi WHERE oi.id > 5", result.CountQuery);
    }

    [Fact]
    public void Analyze_DeleteWhenCountQueryCannotBeBuilt_AddsExplicitWarningIssue()
    {
        var sut = new MutationGuardService();

        MutationGuardResult result = sut.Analyze("DELETE FROM \"public\".\"users\" AS \"u\" WHERE \"u\".id > 5;");

        Assert.True(result.IsSafe);
        Assert.False(result.RequiresConfirmation);
        Assert.Null(result.CountQuery);
        Assert.Contains(result.Issues, i => i.Code == "COUNT_QUERY_UNAVAILABLE" && i.Severity == MutationGuardSeverity.Warning);
    }

    [Fact]
    public void Analyze_WithUpdateFrom_PrefixesCountQueryWithCte()
    {
        var sut = new MutationGuardService();

        MutationGuardResult result = sut.Analyze("WITH affected AS (SELECT id FROM customers WHERE active = true) UPDATE orders o SET status = 'active' FROM affected a WHERE o.customer_id = a.id;");

        Assert.True(result.IsSafe);
        Assert.False(result.RequiresConfirmation);
        Assert.True(result.SupportsDiff);
        Assert.Equal("WITH affected AS (SELECT id FROM customers WHERE active = true) SELECT COUNT(*) FROM orders o, affected a WHERE o.customer_id = a.id", result.CountQuery);
    }

    [Fact]
    public void Analyze_WithDeleteUsing_PrefixesCountQueryWithCte()
    {
        var sut = new MutationGuardService();

        MutationGuardResult result = sut.Analyze("WITH blocked AS (SELECT id FROM customers WHERE blocked = true) DELETE FROM orders o USING blocked b WHERE o.customer_id = b.id;");

        Assert.True(result.IsSafe);
        Assert.False(result.RequiresConfirmation);
        Assert.True(result.SupportsDiff);
        Assert.Equal("WITH blocked AS (SELECT id FROM customers WHERE blocked = true) SELECT COUNT(*) FROM orders o, blocked b WHERE o.customer_id = b.id", result.CountQuery);
    }

    [Fact]
    public void Analyze_WithDeleteAndDoubledSingleQuoteInCte_DoesNotBypassMutationDetection()
    {
        var sut = new MutationGuardService();

        MutationGuardResult result = sut.Analyze("WITH sample AS (SELECT 'it''s' AS txt) DELETE FROM orders;");

        Assert.False(result.IsSafe);
        Assert.True(result.RequiresConfirmation);
        Assert.Contains(result.Issues, i => i.Code == "NO_WHERE" && i.Severity == MutationGuardSeverity.Critical);
        Assert.Equal("WITH sample AS (SELECT 'it''s' AS txt) SELECT COUNT(*) FROM orders", result.CountQuery);
    }

    [Fact]
    public void Analyze_InsertWithoutColumnList_ReturnsInfoOnly()
    {
        var sut = new MutationGuardService();

        MutationGuardResult result = sut.Analyze("INSERT INTO orders VALUES (1, 'x');");

        Assert.True(result.IsSafe);
        Assert.False(result.RequiresConfirmation);
        Assert.Contains(result.Issues, i => i.Code == "INSERT_WITHOUT_COLUMN_LIST" && i.Severity == MutationGuardSeverity.Info);
    }

    [Fact]
    public void Analyze_InsertWithColumnList_ReturnsSafe()
    {
        var sut = new MutationGuardService();

        MutationGuardResult result = sut.Analyze("INSERT INTO orders(id, status) VALUES (1, 'x');");

        Assert.True(result.IsSafe);
        Assert.False(result.RequiresConfirmation);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Analyze_Merge_RequiresConfirmationAndSupportsDiff()
    {
        var sut = new MutationGuardService();

        MutationGuardResult result = sut.Analyze("MERGE INTO orders USING orders_stage ON orders.id = orders_stage.id WHEN MATCHED THEN UPDATE SET status = orders_stage.status;");

        Assert.False(result.IsSafe);
        Assert.True(result.RequiresConfirmation);
        Assert.True(result.SupportsDiff);
        Assert.Equal("SELECT COUNT(*) FROM orders", result.CountQuery);
        Assert.Contains(result.Issues, i => i.Code == "MERGE_MUTATION");
    }

    [Fact]
    public void Analyze_Select_ReturnsSafe()
    {
        var sut = new MutationGuardService();

        MutationGuardResult result = sut.Analyze("SELECT * FROM orders WHERE id = 1;");

        Assert.True(result.IsSafe);
        Assert.False(result.RequiresConfirmation);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Analyze_DropTable_RequiresConfirmation()
    {
        var sut = new MutationGuardService();

        MutationGuardResult result = sut.Analyze("DROP TABLE orders;");

        Assert.False(result.IsSafe);
        Assert.True(result.RequiresConfirmation);
        Assert.Contains(result.Issues, i => i.Code == "DDL_MUTATION");
    }

    [Fact]
    public void Analyze_Truncate_RequiresConfirmationAndSupportsDiff()
    {
        var sut = new MutationGuardService();

        MutationGuardResult result = sut.Analyze("TRUNCATE TABLE orders;");

        Assert.False(result.IsSafe);
        Assert.True(result.RequiresConfirmation);
        Assert.True(result.SupportsDiff);
        Assert.Equal("SELECT COUNT(*) FROM orders", result.CountQuery);
        Assert.Contains(result.Issues, i => i.Code == "TRUNCATE_MUTATION");
    }

    [Fact]
    public void Analyze_TruncateLocalizedMessages_UsesLocalizationValues()
    {
        var sut = new MutationGuardService(new FakeLocalizationService(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sqlEditor.guard.truncate.message"] = "Truncate localized",
            ["sqlEditor.guard.truncate.recommendation"] = "Recommendation localized",
        }));

        MutationGuardResult result = sut.Analyze("TRUNCATE TABLE orders;");

        MutationGuardIssue issue = Assert.Single(result.Issues);
        Assert.Equal("Truncate localized", issue.Message);
        Assert.Equal("Recommendation localized", issue.Suggestion);
    }

    [Fact]
    public void Analyze_MergeLocalizedMessages_UsesLocalizationValues()
    {
        var sut = new MutationGuardService(new FakeLocalizationService(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sqlEditor.guard.merge.message"] = "Merge localized",
            ["sqlEditor.guard.merge.recommendation"] = "Recommendation localized",
        }));

        MutationGuardResult result = sut.Analyze("MERGE INTO orders USING orders_stage ON orders.id = orders_stage.id WHEN MATCHED THEN UPDATE SET status = orders_stage.status;");

        MutationGuardIssue issue = Assert.Single(result.Issues);
        Assert.Equal("Merge localized", issue.Message);
        Assert.Equal("Recommendation localized", issue.Suggestion);
    }

    private sealed class FakeLocalizationService(Dictionary<string, string> values) : AkkornStudio.UI.Services.Localization.ILocalizationService
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }
        public string CurrentCulture => "en-US";
        public string CurrentLanguageLabel => "EN-US";
        public string this[string key] => values.TryGetValue(key, out string? value) ? value : key;
        public bool ToggleCulture() => false;
        public bool SetCulture(string culture) => false;
    }
}
