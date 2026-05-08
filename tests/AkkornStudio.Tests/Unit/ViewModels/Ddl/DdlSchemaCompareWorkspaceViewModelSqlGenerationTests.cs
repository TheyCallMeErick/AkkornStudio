using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.ViewModels.Ddl;

public sealed class DdlSchemaCompareWorkspaceViewModelSqlGenerationTests
{
    [Fact]
    public void BuildWizardDifferencesFromRows_UsesExecutableSqlWhenOperationIsMapped()
    {
        using var vm = new DdlSchemaCompareWorkspaceViewModel(new ConnectionManagerViewModel());
        vm.SetComparisonSqlOperationsForTesting(
        [
            new DdlSchemaCompareWorkspaceViewModel.DdlSchemaCompareSqlOperation(
                "Coluna ausente",
                "ano",
                "Adicionar no destino",
                "ALTER TABLE [dbo].[destino] ADD [ano] INT NULL;",
                IsDestructive: false),
        ]);

        vm.ColumnDiffs.Add(new DdlSchemaCompareDiffRowViewModel(
            "Coluna ausente",
            "ano",
            "int | NULL | default=",
            "(nao existe)",
            "Medio",
            "Adicionar no destino"));

        vm.BuildWizardDifferencesFromRowsForTesting();

        DdlSchemaCompareDifferenceItemViewModel diff = Assert.Single(vm.Differences);
        Assert.Contains("ALTER TABLE", diff.SuggestedSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TODO", diff.SuggestedSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RebuildGeneratedSqlFromWizard_DeduplicatesMappedStatements()
    {
        using var vm = new DdlSchemaCompareWorkspaceViewModel(new ConnectionManagerViewModel());
        vm.SetComparisonSqlOperationsForTesting(
        [
            new DdlSchemaCompareWorkspaceViewModel.DdlSchemaCompareSqlOperation(
                "Tipo",
                "id",
                "ALTER COLUMN",
                "ALTER TABLE [dbo].[destino] ALTER COLUMN [id] BIGINT NOT NULL;",
                IsDestructive: true),
        ]);

        vm.ColumnDiffs.Add(new DdlSchemaCompareDiffRowViewModel(
            "Tipo",
            "id",
            "bigint",
            "int",
            "Alto",
            "ALTER COLUMN"));
        vm.ColumnDiffs.Add(new DdlSchemaCompareDiffRowViewModel(
            "Nullable",
            "id",
            "NO",
            "YES",
            "Alto",
            "ALTER COLUMN"));

        vm.BuildWizardDifferencesFromRowsForTesting();
        foreach (DdlSchemaCompareDifferenceItemViewModel diff in vm.Differences)
            diff.IsIncluded = true;

        vm.SqlGenerationMode = DdlSchemaCompareSqlGenerationMode.Complete;
        vm.CommentDestructiveOperations = false;
        vm.IncludeTransaction = false;
        vm.IncludeHeader = false;
        vm.IncludeDiffSummary = false;
        vm.RebuildGeneratedSqlFromWizardForTesting();

        Assert.False(string.IsNullOrWhiteSpace(vm.GeneratedSql));
        Assert.Equal(1, CountOccurrences(vm.GeneratedSql, "ALTER COLUMN [id] BIGINT NOT NULL"));
    }

    [Fact]
    public void RebuildGeneratedSqlFromWizard_EmitsCommentBlockBeforeInstruction()
    {
        using var vm = new DdlSchemaCompareWorkspaceViewModel(new ConnectionManagerViewModel());
        vm.SetComparisonSqlOperationsForTesting(
        [
            new DdlSchemaCompareWorkspaceViewModel.DdlSchemaCompareSqlOperation(
                "Coluna ausente",
                "ano",
                "Adicionar no destino",
                "ALTER TABLE [dbo].[destino] ADD [ano] INT NULL;",
                IsDestructive: false),
        ]);

        vm.ColumnDiffs.Add(new DdlSchemaCompareDiffRowViewModel(
            "Coluna ausente",
            "ano",
            "int | NULL | default=",
            "(nao existe)",
            "Medio",
            "Adicionar no destino"));

        vm.BuildWizardDifferencesFromRowsForTesting();
        Assert.Single(vm.Differences).IsIncluded = true;

        vm.SqlGenerationMode = DdlSchemaCompareSqlGenerationMode.Complete;
        vm.CommentDestructiveOperations = false;
        vm.IncludeTransaction = false;
        vm.IncludeHeader = false;
        vm.IncludeDiffSummary = false;
        vm.RebuildGeneratedSqlFromWizardForTesting();

        int commentIndex = vm.GeneratedSql.IndexOf("-- [Coluna ausente] ano", StringComparison.OrdinalIgnoreCase);
        int sqlIndex = vm.GeneratedSql.IndexOf("ALTER TABLE [dbo].[destino] ADD [ano] INT NULL;", StringComparison.OrdinalIgnoreCase);

        Assert.True(commentIndex >= 0);
        Assert.True(sqlIndex > commentIndex);
    }

    private static int CountOccurrences(string source, string value)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value))
            return 0;

        int count = 0;
        int index = 0;
        while (true)
        {
            index = source.IndexOf(value, index, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                break;

            count++;
            index += value.Length;
        }

        return count;
    }
}
