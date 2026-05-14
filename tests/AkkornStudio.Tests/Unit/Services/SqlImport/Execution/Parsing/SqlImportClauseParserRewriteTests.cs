using System.Collections.ObjectModel;
using AkkornStudio.UI.Services.SqlImport.Execution.Parsing;
using AkkornStudio.UI.Services.SqlImport.Rewriting;
using AkkornStudio.UI.ViewModels.Canvas;

namespace AkkornStudio.Tests.Unit.Services.SqlImport.Execution.Parsing;

public sealed class SqlImportClauseParserRewriteTests
{
    [Fact]
    public void Parse_FilteredCte_RewritesAliasesToOuterSource()
    {
        var parser = new SqlImportClauseParser(new SqlImportCteRewriteService());
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportParseResult result = parser.Parse(
            "WITH recent_orders AS (SELECT id, status FROM orders src WHERE src.status = 'OPEN') SELECT ro.id FROM recent_orders ro WHERE ro.id > 10",
            report,
            CancellationToken.None);

        Assert.False(result.ShouldStop);
        Assert.NotNull(result.Query);
        Assert.Contains("ro.id > 10", result.Query!.WhereClause ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ro.status = 'OPEN'", result.Query.WhereClause ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("src.", result.Query.WhereClause ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_FilteredFromSubquery_RewritesAliasesToOuterSource()
    {
        var parser = new SqlImportClauseParser(new SqlImportCteRewriteService());
        var report = new ObservableCollection<ImportReportItem>();

        SqlImportParseResult result = parser.Parse(
            "SELECT o.id FROM (SELECT id, status FROM orders src WHERE src.status = 'OPEN') o WHERE o.id > 10",
            report,
            CancellationToken.None);

        Assert.False(result.ShouldStop);
        Assert.NotNull(result.Query);
        Assert.Contains("o.id > 10", result.Query!.WhereClause ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("o.status = 'OPEN'", result.Query.WhereClause ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("src.", result.Query.WhereClause ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
