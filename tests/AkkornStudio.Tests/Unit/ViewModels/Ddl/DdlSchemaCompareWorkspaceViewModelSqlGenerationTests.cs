using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.ViewModels.Ddl;

public sealed class DdlSchemaCompareWorkspaceViewModelSqlGenerationTests
{
    [Fact]
    public void Load_GeneratesExecutableSqlForAddedColumn()
    {
        using var vm = LoadAddedColumn();
        IncludeAll(vm);
        ConfigurePlainScript(vm);

        DdlSchemaCompareDifferenceItemViewModel diff = Assert.Single(vm.Differences);
        Assert.Contains("ALTER TABLE", diff.SuggestedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ADD", diff.SuggestedSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TODO", diff.SuggestedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"ano\"", vm.GeneratedSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RebuildGeneratedSql_EmitsCommentBlockBeforeInstruction()
    {
        using var vm = LoadAddedColumn();
        IncludeAll(vm);
        ConfigurePlainScript(vm);

        int commentIndex = vm.GeneratedSql.IndexOf("-- [Coluna ausente] ano", StringComparison.OrdinalIgnoreCase);
        int sqlIndex = vm.GeneratedSql.IndexOf("ALTER TABLE", StringComparison.OrdinalIgnoreCase);

        Assert.True(commentIndex >= 0);
        Assert.True(sqlIndex > commentIndex);
    }

    [Fact]
    public void TransactionWrapper_IsProviderSpecificForPostgres()
    {
        using var vm = LoadAddedColumn();
        IncludeAll(vm);
        vm.SqlGenerationMode = DdlSchemaCompareSqlGenerationMode.Complete;
        vm.IncludeHeader = false;
        vm.IncludeDiffSummary = false;
        vm.IncludeTransaction = true;
        vm.RebuildGeneratedSqlFromWizardForTesting();

        Assert.Contains("BEGIN;", vm.GeneratedSql, StringComparison.Ordinal);
        Assert.Contains("COMMIT;", vm.GeneratedSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN TRANSACTION", vm.GeneratedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void SafeMode_CommentsOutDestructiveDrop()
    {
        // Target has an extra column → a destructive DROP COLUMN difference.
        using var vm = LoadComparison(
            Table("t", Col("id", "int")),
            Table("t", new[] { Col("id", "int"), Col("legacy", "int") }));

        DdlSchemaCompareDifferenceItemViewModel drop = Assert.Single(vm.Differences);
        Assert.True(drop.IsDestructive);
        drop.IsIncluded = true;

        vm.SqlGenerationMode = DdlSchemaCompareSqlGenerationMode.Safe;
        vm.IncludeHeader = false;
        vm.IncludeDiffSummary = false;
        vm.IncludeTransaction = false;
        vm.RebuildGeneratedSqlFromWizardForTesting();

        // Present, but every executable line commented out.
        Assert.Contains("DROP COLUMN", vm.GeneratedSql, StringComparison.OrdinalIgnoreCase);
        foreach (string line in vm.GeneratedSql.Split('\n'))
        {
            if (line.Contains("DROP COLUMN", StringComparison.OrdinalIgnoreCase))
                Assert.StartsWith("--", line.TrimStart());
        }
    }

    [Fact]
    public void TogglingExistenceOption_RecomputesSqlAndPreservesReviewState()
    {
        // Source has a secondary index missing in target → CREATE INDEX.
        using var vm = LoadComparison(
            Table(
                "t",
                new[] { Col("email", "varchar") },
                new[] { new IndexMetadata("ix_email", IsUnique: false, IsClustered: false, IsPrimaryKey: false, new[] { "email" }) }),
            Table("t", Col("email", "varchar")));

        DdlSchemaCompareDifferenceItemViewModel index = Assert.Single(vm.Differences);
        index.IsIncluded = true;
        index.ReviewStatus = DdlSchemaCompareDiffReviewStatus.Reviewed;

        Assert.Contains("IF NOT EXISTS", index.SuggestedSql, StringComparison.OrdinalIgnoreCase);

        vm.UseIfExistsChecks = false;
        vm.UseCatalogChecks = false;

        Assert.DoesNotContain("IF NOT EXISTS", index.SuggestedSql, StringComparison.OrdinalIgnoreCase);
        // Review state preserved across the live recompute.
        Assert.Equal(DdlSchemaCompareDiffReviewStatus.Reviewed, index.ReviewStatus);
        Assert.True(index.IsIncluded);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────
    private static DdlSchemaCompareWorkspaceViewModel LoadAddedColumn() =>
        LoadComparison(
            Table("destino", new[] { Col("id", "int"), Col("ano", "int") }),
            Table("destino", Col("id", "int")));

    private static DdlSchemaCompareWorkspaceViewModel LoadComparison(TableMetadata source, TableMetadata target)
    {
        var vm = new DdlSchemaCompareWorkspaceViewModel(new ConnectionManagerViewModel());
        vm.LoadComparisonForTesting(source, target, DatabaseProvider.Postgres);
        return vm;
    }

    private static void IncludeAll(DdlSchemaCompareWorkspaceViewModel vm)
    {
        foreach (DdlSchemaCompareDifferenceItemViewModel diff in vm.Differences)
            diff.IsIncluded = true;
    }

    private static void ConfigurePlainScript(DdlSchemaCompareWorkspaceViewModel vm)
    {
        vm.SqlGenerationMode = DdlSchemaCompareSqlGenerationMode.Complete;
        vm.CommentDestructiveOperations = false;
        vm.IncludeTransaction = false;
        vm.IncludeHeader = false;
        vm.IncludeDiffSummary = false;
        vm.RebuildGeneratedSqlFromWizardForTesting();
    }

    private static ColumnMetadata Col(string name, string nativeType, int ordinal = 1) =>
        new(name, nativeType, nativeType, true, false, false, false, false, ordinal);

    private static TableMetadata Table(string name, params ColumnMetadata[] columns) =>
        Table(name, columns, Array.Empty<IndexMetadata>());

    private static TableMetadata Table(string name, IReadOnlyList<ColumnMetadata> columns, IReadOnlyList<IndexMetadata> indexes) =>
        new(
            "public",
            name,
            TableKind.Table,
            null,
            columns,
            indexes,
            Array.Empty<ForeignKeyRelation>(),
            Array.Empty<ForeignKeyRelation>());
}
