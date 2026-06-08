using System.Data;
using AkkornStudio.Core;
using AkkornStudio.UI.Services.SqlEditor;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.Services.SqlEditor;

public sealed class SqlMutationDiffServiceTests
{
    [Fact]
    public async Task BuildPreviewAsync_Delete_ReturnsRollbackSummary()
    {
        var sut = new SqlMutationDiffService(
            (sql, _, _, _) =>
            {
                if (sql.Contains("WHERE", StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(CreateScalarResult(4));

                return Task.FromResult(CreateScalarResult(10));
            });

        MutationGuardResult guard = new()
        {
            IsSafe = false,
            RequiresConfirmation = true,
            CountQuery = "SELECT COUNT(*) FROM orders WHERE status = 'x'",
            SupportsDiff = true,
        };

        SqlMutationDiffPreview preview = await sut.BuildPreviewAsync(
            "DELETE FROM orders WHERE status = 'x';",
            guard,
            BuildConfig(),
            estimatedAffectedRows: null);

        Assert.True(preview.Available);
        Assert.True(
            preview.Message.Contains("ROLLBACK garantido", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("ROLLBACK guaranteed", StringComparison.OrdinalIgnoreCase));
        Assert.True(
            preview.Message.Contains("antes 10", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("before 10", StringComparison.OrdinalIgnoreCase));
        Assert.True(
            preview.Message.Contains("afetadas 4", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("affected 4", StringComparison.OrdinalIgnoreCase));
        Assert.True(
            preview.Message.Contains("depois 6", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("after 6", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildPreviewAsync_UnsupportedOrMissingCountQuery_ReturnsUnavailable()
    {
        var sut = new SqlMutationDiffService((_, _, _, _) => Task.FromResult(CreateScalarResult(1)));
        MutationGuardResult guard = new()
        {
            IsSafe = false,
            RequiresConfirmation = true,
            CountQuery = null,
            SupportsDiff = false,
        };

        SqlMutationDiffPreview preview = await sut.BuildPreviewAsync(
            "TRUNCATE TABLE orders;",
            guard,
            BuildConfig(),
            estimatedAffectedRows: null);

        Assert.False(preview.Available);
        Assert.True(
            preview.Message.Contains("Sem previa de diff transacional", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("No transactional diff preview", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildPreviewAsync_Truncate_ReturnsRollbackSummary()
    {
        var sut = new SqlMutationDiffService((sql, _, _, _) =>
        {
            Assert.Contains("SELECT COUNT(*) FROM orders", sql, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(CreateScalarResult(14));
        });

        MutationGuardResult guard = new()
        {
            IsSafe = false,
            RequiresConfirmation = true,
            CountQuery = "SELECT COUNT(*) FROM orders",
            SupportsDiff = true,
        };

        SqlMutationDiffPreview preview = await sut.BuildPreviewAsync(
            "TRUNCATE TABLE orders;",
            guard,
            BuildConfig(),
            estimatedAffectedRows: null);

        Assert.True(preview.Available);
        Assert.Contains("orders", preview.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            preview.Message.Contains("before 14", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("antes 14", StringComparison.OrdinalIgnoreCase));
        Assert.True(
            preview.Message.Contains("removed 14", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("afetadas 14", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("removidas 14", StringComparison.OrdinalIgnoreCase));
        Assert.True(
            preview.Message.Contains("after 0", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("depois 0", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildPreviewAsync_UpdateFrom_ReturnsRollbackSummary()
    {
        var sut = new SqlMutationDiffService((sql, _, _, _) =>
        {
            if (sql.Contains("customers c", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(CreateScalarResult(3));

            Assert.Equal("SELECT COUNT(*) FROM orders", sql);
            return Task.FromResult(CreateScalarResult(12));
        });

        MutationGuardResult guard = new()
        {
            IsSafe = true,
            RequiresConfirmation = false,
            CountQuery = "SELECT COUNT(*) FROM orders o, customers c WHERE o.customer_id = c.id AND c.active = true",
            SupportsDiff = true,
        };

        SqlMutationDiffPreview preview = await sut.BuildPreviewAsync(
            "UPDATE orders o SET status = c.status FROM customers c WHERE o.customer_id = c.id AND c.active = true;",
            guard,
            BuildConfig(),
            estimatedAffectedRows: null);

        Assert.True(preview.Available);
        Assert.Contains("orders", preview.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            preview.Message.Contains("affected 3", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("candidate rows affected 3", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("afetadas 3", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildPreviewAsync_DeleteUsing_ReturnsRollbackSummary()
    {
        var sut = new SqlMutationDiffService((sql, _, _, _) =>
        {
            if (sql.Contains("customers c", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(CreateScalarResult(2));

            Assert.Equal("SELECT COUNT(*) FROM orders", sql);
            return Task.FromResult(CreateScalarResult(12));
        });

        MutationGuardResult guard = new()
        {
            IsSafe = true,
            RequiresConfirmation = false,
            CountQuery = "SELECT COUNT(*) FROM orders o, customers c WHERE o.customer_id = c.id AND c.blocked = true",
            SupportsDiff = true,
        };

        SqlMutationDiffPreview preview = await sut.BuildPreviewAsync(
            "DELETE FROM orders o USING customers c WHERE o.customer_id = c.id AND c.blocked = true;",
            guard,
            BuildConfig(),
            estimatedAffectedRows: null);

        Assert.True(preview.Available);
        Assert.Contains("orders", preview.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            preview.Message.Contains("affected 2", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("afetadas 2", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildPreviewAsync_Delete_AppendsRowLevelBeforeSample()
    {
        var sut = new SqlMutationDiffService((sql, _, _, _) =>
        {
            if (sql.StartsWith("SELECT * FROM orders", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(CreateRowsResult(("id", 10), ("status", "cancelled")));

            if (sql.Contains("WHERE", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(CreateScalarResult(1));

            return Task.FromResult(CreateScalarResult(8));
        });

        MutationGuardResult guard = new()
        {
            IsSafe = true,
            RequiresConfirmation = false,
            CountQuery = "SELECT COUNT(*) FROM orders WHERE status = 'cancelled'",
            SupportsDiff = true,
        };

        SqlMutationDiffPreview preview = await sut.BuildPreviewAsync(
            "DELETE FROM orders WHERE status = 'cancelled';",
            guard,
            BuildConfig(),
            estimatedAffectedRows: null);

        Assert.True(preview.Available);
        Assert.Contains("Row-level before", preview.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=10", preview.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("status=cancelled", preview.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildPreviewAsync_Update_AppendsRowLevelBeforeAfterSample()
    {
        var sut = new SqlMutationDiffService((sql, _, _, _) =>
        {
            if (sql.StartsWith("SELECT * FROM orders", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(CreateRowsResult(("id", 7), ("status", "pending")));

            if (sql.Contains("WHERE", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(CreateScalarResult(1));

            return Task.FromResult(CreateScalarResult(8));
        });

        MutationGuardResult guard = new()
        {
            IsSafe = true,
            RequiresConfirmation = false,
            CountQuery = "SELECT COUNT(*) FROM orders WHERE id = 7",
            SupportsDiff = true,
        };

        SqlMutationDiffPreview preview = await sut.BuildPreviewAsync(
            "UPDATE orders SET status = 'paid' WHERE id = 7;",
            guard,
            BuildConfig(),
            estimatedAffectedRows: null);

        Assert.True(preview.Available);
        Assert.Contains("before/after", preview.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=7", preview.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("status=pending", preview.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("status => 'paid'", preview.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildPreviewAsync_WithUpdateFrom_ReturnsRollbackSummary()
    {
        var sut = new SqlMutationDiffService((sql, _, _, _) =>
        {
            if (sql.StartsWith("WITH affected", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(CreateScalarResult(5));

            Assert.Equal("SELECT COUNT(*) FROM orders", sql);
            return Task.FromResult(CreateScalarResult(20));
        });

        MutationGuardResult guard = new()
        {
            IsSafe = true,
            RequiresConfirmation = false,
            CountQuery = "WITH affected AS (SELECT id FROM customers WHERE active = true) SELECT COUNT(*) FROM orders o, affected a WHERE o.customer_id = a.id",
            SupportsDiff = true,
        };

        SqlMutationDiffPreview preview = await sut.BuildPreviewAsync(
            "WITH affected AS (SELECT id FROM customers WHERE active = true) UPDATE orders o SET status = 'active' FROM affected a WHERE o.customer_id = a.id;",
            guard,
            BuildConfig(),
            estimatedAffectedRows: null);

        Assert.True(preview.Available);
        Assert.Contains("orders", preview.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            preview.Message.Contains("affected 5", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("candidate rows affected 5", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("afetadas 5", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildPreviewAsync_WithDeleteUsing_ReturnsRollbackSummary()
    {
        var sut = new SqlMutationDiffService((sql, _, _, _) =>
        {
            if (sql.StartsWith("WITH blocked", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(CreateScalarResult(4));

            Assert.Equal("SELECT COUNT(*) FROM orders", sql);
            return Task.FromResult(CreateScalarResult(20));
        });

        MutationGuardResult guard = new()
        {
            IsSafe = true,
            RequiresConfirmation = false,
            CountQuery = "WITH blocked AS (SELECT id FROM customers WHERE blocked = true) SELECT COUNT(*) FROM orders o, blocked b WHERE o.customer_id = b.id",
            SupportsDiff = true,
        };

        SqlMutationDiffPreview preview = await sut.BuildPreviewAsync(
            "WITH blocked AS (SELECT id FROM customers WHERE blocked = true) DELETE FROM orders o USING blocked b WHERE o.customer_id = b.id;",
            guard,
            BuildConfig(),
            estimatedAffectedRows: null);

        Assert.True(preview.Available);
        Assert.Contains("orders", preview.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            preview.Message.Contains("affected 4", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("afetadas 4", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildPreviewAsync_InsertValues_ReturnsEstimatedInsertSummary()
    {
        var sut = new SqlMutationDiffService((sql, _, _, _) =>
        {
            Assert.Contains("SELECT COUNT(*) FROM orders", sql, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(CreateScalarResult(10));
        });

        MutationGuardResult guard = new()
        {
            IsSafe = true,
            RequiresConfirmation = false,
            SupportsDiff = true,
        };

        SqlMutationDiffPreview preview = await sut.BuildPreviewAsync(
            "INSERT INTO orders (id, status) VALUES (1, 'new'), (2, 'paid');",
            guard,
            BuildConfig(),
            estimatedAffectedRows: null);

        Assert.True(preview.Available);
        Assert.Contains("orders", preview.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            preview.Message.Contains("before 10", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("antes 10", StringComparison.OrdinalIgnoreCase));
        Assert.True(
            preview.Message.Contains("inserted rows 2", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("inserted 2", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("inseridas 2", StringComparison.OrdinalIgnoreCase));
        Assert.True(
            preview.Message.Contains("after 12", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("depois 12", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildPreviewAsync_InsertSelect_ReturnsEstimatedInsertSummary()
    {
        var sut = new SqlMutationDiffService((sql, _, _, _) =>
        {
            if (sql.Contains("SELECT COUNT(*) FROM orders", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(CreateScalarResult(25));

            Assert.Contains("SELECT COUNT(*) FROM (SELECT id, status FROM orders_archive)", sql, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(CreateScalarResult(7));
        });

        MutationGuardResult guard = new()
        {
            IsSafe = true,
            RequiresConfirmation = false,
            SupportsDiff = true,
        };

        SqlMutationDiffPreview preview = await sut.BuildPreviewAsync(
            "INSERT INTO orders (id, status) SELECT id, status FROM orders_archive;",
            guard,
            BuildConfig(),
            estimatedAffectedRows: null);

        Assert.True(preview.Available);
        Assert.Contains("orders", preview.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            preview.Message.Contains("before 25", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("antes 25", StringComparison.OrdinalIgnoreCase));
        Assert.True(
            preview.Message.Contains("inserted rows 7", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("inserted 7", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("inseridas 7", StringComparison.OrdinalIgnoreCase));
        Assert.True(
            preview.Message.Contains("after 32", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("depois 32", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildPreviewAsync_WithInsertSelect_ReturnsEstimatedInsertSummary()
    {
        var sut = new SqlMutationDiffService((sql, _, _, _) =>
        {
            if (sql.Contains("SELECT COUNT(*) FROM orders", StringComparison.OrdinalIgnoreCase)
                && !sql.StartsWith("WITH src", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(CreateScalarResult(25));
            }

            Assert.StartsWith("WITH src AS (SELECT id, status FROM orders_archive)", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("SELECT COUNT(*) FROM (SELECT id, status FROM src)", sql, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(CreateScalarResult(7));
        });

        MutationGuardResult guard = new()
        {
            IsSafe = true,
            RequiresConfirmation = false,
            SupportsDiff = true,
        };

        SqlMutationDiffPreview preview = await sut.BuildPreviewAsync(
            "WITH src AS (SELECT id, status FROM orders_archive) INSERT INTO orders (id, status) SELECT id, status FROM src;",
            guard,
            BuildConfig(),
            estimatedAffectedRows: null);

        Assert.True(preview.Available);
        Assert.True(
            preview.Message.Contains("inserted rows 7", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("inserted 7", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("inseridas 7", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildPreviewAsync_InsertDefaultValues_ReturnsSingleRowInsertSummary()
    {
        var sut = new SqlMutationDiffService((sql, _, _, _) =>
        {
            Assert.Contains("SELECT COUNT(*) FROM orders", sql, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(CreateScalarResult(25));
        });

        MutationGuardResult guard = new()
        {
            IsSafe = true,
            RequiresConfirmation = false,
            SupportsDiff = true,
        };

        SqlMutationDiffPreview preview = await sut.BuildPreviewAsync(
            "INSERT INTO orders DEFAULT VALUES;",
            guard,
            BuildConfig(),
            estimatedAffectedRows: null);

        Assert.True(preview.Available);
        Assert.True(
            preview.Message.Contains("inserted rows 1", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("inserted 1", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("inseridas 1", StringComparison.OrdinalIgnoreCase));
        Assert.True(
            preview.Message.Contains("after 26", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("depois 26", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildPreviewAsync_MergeUsingTable_ReturnsPartialSummary()
    {
        var sut = new SqlMutationDiffService((sql, _, _, _) =>
        {
            if (sql.Contains("INNER JOIN orders_stage", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(CreateScalarResult(4));

            if (sql.Contains("SELECT COUNT(*) FROM orders_stage", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(CreateScalarResult(6));

            Assert.Contains("SELECT COUNT(*) FROM orders", sql, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(CreateScalarResult(14));
        });

        MutationGuardResult guard = new()
        {
            IsSafe = false,
            RequiresConfirmation = true,
            CountQuery = "SELECT COUNT(*) FROM orders",
            SupportsDiff = true,
        };

        SqlMutationDiffPreview preview = await sut.BuildPreviewAsync(
            "MERGE INTO orders USING orders_stage ON orders.id = orders_stage.id WHEN MATCHED THEN UPDATE SET status = orders_stage.status;",
            guard,
            BuildConfig(),
            estimatedAffectedRows: null);

        Assert.True(preview.Available);
        Assert.Contains("orders", preview.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            preview.Message.Contains("before 14", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("antes 14", StringComparison.OrdinalIgnoreCase));
        Assert.True(
            preview.Message.Contains("source candidate rows 6", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("source rows 6", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("fonte 6", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("matched 4", preview.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not matched by target 2", preview.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not matched by source 10", preview.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MATCHED", preview.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildPreviewAsync_MergeUsingSelect_ReturnsPartialSummary()
    {
        var sut = new SqlMutationDiffService((sql, _, _, _) =>
        {
            if (sql.Contains("INNER JOIN (SELECT id, status FROM orders_stage)", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(CreateScalarResult(3));

            if (sql.Contains("SELECT COUNT(*) FROM (SELECT id, status FROM orders_stage)", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(CreateScalarResult(4));

            Assert.Contains("SELECT COUNT(*) FROM orders", sql, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(CreateScalarResult(10));
        });

        MutationGuardResult guard = new()
        {
            IsSafe = false,
            RequiresConfirmation = true,
            CountQuery = "SELECT COUNT(*) FROM orders",
            SupportsDiff = true,
        };

        SqlMutationDiffPreview preview = await sut.BuildPreviewAsync(
            "MERGE INTO orders USING (SELECT id, status FROM orders_stage) src ON orders.id = src.id WHEN MATCHED THEN UPDATE SET status = src.status WHEN NOT MATCHED THEN INSERT (id, status) VALUES (src.id, src.status) WHEN NOT MATCHED BY SOURCE THEN DELETE;",
            guard,
            BuildConfig(),
            estimatedAffectedRows: null);

        Assert.True(preview.Available);
        Assert.True(
            preview.Message.Contains("before 10", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("antes 10", StringComparison.OrdinalIgnoreCase));
        Assert.True(
            preview.Message.Contains("source candidate rows 4", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("source rows 4", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("fonte 4", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("matched 3", preview.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not matched by target 1", preview.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not matched by source 7", preview.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MATCHED", preview.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NOT MATCHED", preview.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NOT MATCHED BY SOURCE", preview.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildPreviewAsync_WithMerge_ReturnsPartialSummary()
    {
        var sut = new SqlMutationDiffService((sql, _, _, _) =>
        {
            if (sql.StartsWith("WITH src", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("INNER JOIN src", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(CreateScalarResult(3));
            }

            if (sql.StartsWith("WITH src", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("SELECT COUNT(*) FROM src", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(CreateScalarResult(5));
            }

            Assert.Contains("SELECT COUNT(*) FROM orders", sql, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(CreateScalarResult(12));
        });

        MutationGuardResult guard = new()
        {
            IsSafe = false,
            RequiresConfirmation = true,
            CountQuery = "WITH src AS (SELECT id, status FROM orders_stage) SELECT COUNT(*) FROM orders",
            SupportsDiff = true,
        };

        SqlMutationDiffPreview preview = await sut.BuildPreviewAsync(
            "WITH src AS (SELECT id, status FROM orders_stage) MERGE INTO orders USING src ON orders.id = src.id WHEN MATCHED THEN UPDATE SET status = src.status WHEN NOT MATCHED THEN INSERT (id, status) VALUES (src.id, src.status);",
            guard,
            BuildConfig(),
            estimatedAffectedRows: null);

        Assert.True(preview.Available);
        Assert.True(
            preview.Message.Contains("source candidate rows 5", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("source rows 5", StringComparison.OrdinalIgnoreCase)
            || preview.Message.Contains("fonte 5", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("matched 3", preview.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not matched by target 2", preview.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not matched by source 9", preview.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static SqlEditorResultSet CreateScalarResult(long value)
    {
        var table = new DataTable();
        table.Columns.Add("count", typeof(long));
        table.Rows.Add(value);

        return new SqlEditorResultSet
        {
            StatementSql = "SELECT COUNT(*)",
            Success = true,
            Data = table,
            RowsAffected = 1,
            ExecutionTime = TimeSpan.FromMilliseconds(1),
            ExecutedAt = DateTimeOffset.UtcNow,
        };
    }

    private static SqlEditorResultSet CreateRowsResult(params (string Column, object? Value)[] values)
    {
        var table = new DataTable();
        foreach ((string column, object? value) in values)
            table.Columns.Add(column, value?.GetType() ?? typeof(string));

        table.Rows.Add(values.Select(static item => item.Value ?? DBNull.Value).ToArray());

        return new SqlEditorResultSet
        {
            StatementSql = "SELECT *",
            Success = true,
            Data = table,
            RowsAffected = table.Rows.Count,
            ExecutionTime = TimeSpan.FromMilliseconds(1),
            ExecutedAt = DateTimeOffset.UtcNow,
        };
    }

    private static ConnectionConfig BuildConfig() =>
        new(
            Provider: DatabaseProvider.Postgres,
            Host: "localhost",
            Port: 5432,
            Database: "vsa",
            Username: "u",
            Password: "p");
}
