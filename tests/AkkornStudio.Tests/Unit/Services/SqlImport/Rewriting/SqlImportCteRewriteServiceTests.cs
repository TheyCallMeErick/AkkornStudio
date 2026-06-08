using AkkornStudio.UI.Services.SqlImport.Rewriting;

namespace AkkornStudio.Tests.Unit.Services.SqlImport.Rewriting;

public sealed class SqlImportCteRewriteServiceTests
{
    [Fact]
    public void TryRewriteSimpleCteQuery_FilteredCte_RewritesSuccessfully()
    {
        var service = new SqlImportCteRewriteService();
        const string sql =
            "WITH recent_orders AS (SELECT id, status FROM orders src WHERE src.status = 'OPEN') SELECT ro.id FROM recent_orders ro WHERE ro.id > 10";

        bool rewritten = service.TryRewriteSimpleCteQuery(sql, out string rewrittenSql, out int cteCount);

        Assert.True(rewritten, $"CTE filtered rewrite should succeed. Output: {rewrittenSql}");
        Assert.Equal(1, cteCount);
        Assert.Contains("FROM orders ro", rewrittenSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ro.status = 'OPEN'", rewrittenSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryRewriteSimpleCteQuery_OrderedLimitedCte_RewritesSuccessfully()
    {
        var service = new SqlImportCteRewriteService();
        const string sql =
            "WITH recent_orders AS (SELECT id AS order_id, status FROM orders WHERE status = 'OPEN' ORDER BY id DESC LIMIT 5) SELECT ro.order_id FROM recent_orders ro";

        bool rewritten = service.TryRewriteSimpleCteQuery(sql, out string rewrittenSql, out int cteCount);

        Assert.True(rewritten, $"Ordered/LIMIT CTE rewrite should succeed. Output: {rewrittenSql}");
        Assert.Equal(1, cteCount);
        Assert.Contains("ORDER BY", rewrittenSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT 5", rewrittenSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryRewriteSimpleFromSubquery_FilteredSubquery_RewritesSuccessfully()
    {
        var service = new SqlImportCteRewriteService();
        const string sql =
            "SELECT o.id FROM (SELECT id, status FROM orders src WHERE src.status = 'OPEN') o WHERE o.id > 10";

        bool rewritten = service.TryRewriteSimpleFromSubquery(sql, out string rewrittenSql);

        Assert.True(rewritten, $"FROM subquery rewrite should succeed. Output: {rewrittenSql}");
        Assert.Contains("FROM orders o", rewrittenSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("o.status = 'OPEN'", rewrittenSql, StringComparison.OrdinalIgnoreCase);
    }
}
