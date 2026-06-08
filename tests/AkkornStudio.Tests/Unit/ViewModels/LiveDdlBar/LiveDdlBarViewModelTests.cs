using Avalonia;
using AkkornStudio.Core;
using AkkornStudio.Ddl.SchemaAnalysis.Application.Processing;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Contracts;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Enums;
using AkkornStudio.Metadata;
using AkkornStudio.Nodes;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.Canvas.Strategies;

namespace AkkornStudio.Tests.Unit.ViewModels.LiveDdlBar;

public class LiveDdlBarViewModelTests
{
    [Fact]
    public void Recompile_ProducesCreateTableSql_ForConnectedDdlGraph()
    {
        var ddlCanvas = new CanvasViewModel(null, null, null, null, new DdlDomainStrategy());

        var table = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.TableDefinition), new Point(0, 0));
        table.Parameters["SchemaName"] = "dbo";
        table.Parameters["TableName"] = "orders";
        table.Parameters["IfNotExists"] = "true";

        var col = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.ColumnDefinition), new Point(0, 0));
        col.Parameters["ColumnName"] = "id";
        col.Parameters["DataType"] = "INT";
        col.Parameters["IsNullable"] = "false";

        var output = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.CreateTableOutput), new Point(0, 0));

        ddlCanvas.Nodes.Add(table);
        ddlCanvas.Nodes.Add(col);
        ddlCanvas.Nodes.Add(output);

        ddlCanvas.Connections.Add(new ConnectionViewModel(
            col.OutputPins.First(p => p.Name == "column"),
            new Point(0, 0),
            new Point(10, 10)
        ) { ToPin = table.InputPins.First(p => p.Name == "column") });

        ddlCanvas.Connections.Add(new ConnectionViewModel(
            table.OutputPins.First(p => p.Name == "table"),
            new Point(0, 0),
            new Point(10, 10)
        ) { ToPin = output.InputPins.First(p => p.Name == "table") });

        Assert.NotNull(ddlCanvas.LiveDdl);
        ddlCanvas.LiveDdl!.Recompile();

        Assert.True(ddlCanvas.LiveDdl.IsValid);
        Assert.Contains("CREATE TABLE [dbo].[orders]", ddlCanvas.LiveDdl.RawSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recompile_GeneratesSql_WhenOnlyDefinitionsExist_AndNoOutputNode()
    {
        var ddlCanvas = new CanvasViewModel(null, null, null, null, new DdlDomainStrategy());

        var table = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.TableDefinition), new Point(0, 0));
        table.Parameters["SchemaName"] = "dbo";
        table.Parameters["TableName"] = "orders";

        var col = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.ColumnDefinition), new Point(0, 0));
        col.Parameters["ColumnName"] = "id";
        col.Parameters["DataType"] = "INT";
        col.Parameters["IsNullable"] = "false";

        ddlCanvas.Nodes.Add(table);
        ddlCanvas.Nodes.Add(col);

        ddlCanvas.Connections.Add(new ConnectionViewModel(
            col.OutputPins.First(p => p.Name == "column"),
            new Point(0, 0),
            new Point(10, 10)
        ) { ToPin = table.InputPins.First(p => p.Name == "column") });

        Assert.NotNull(ddlCanvas.LiveDdl);
        ddlCanvas.LiveDdl!.Recompile();

        Assert.True(ddlCanvas.LiveDdl.IsValid);
        Assert.Contains("CREATE TABLE [dbo].[orders]", ddlCanvas.LiveDdl.RawSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeSchemaStructureAsync_WhenMetadataMissing_SetsEmptyState()
    {
        var ddlCanvas = new CanvasViewModel(null, null, null, null, new DdlDomainStrategy());
        LiveDdlBarViewModel liveDdl = Assert.IsType<LiveDdlBarViewModel>(ddlCanvas.LiveDdl);

        await liveDdl.AnalyzeSchemaStructureAsync();

        Assert.Equal(SchemaAnalysisViewState.Empty, liveDdl.SchemaAnalysisPanel.State);
        Assert.Equal(
            L("preview.schemaAnalysis.state.empty", "Nenhum problema estrutural inferível foi detectado."),
            liveDdl.SchemaAnalysisPanel.StateMessage
        );
    }

    [Fact]
    public async Task AnalyzeSchemaStructureAsync_WithMetadata_ProducesTerminalState()
    {
        var ddlCanvas = new CanvasViewModel(null, null, null, null, new DdlDomainStrategy())
        {
            DatabaseMetadata = CreateMetadata(),
        };

        LiveDdlBarViewModel liveDdl = Assert.IsType<LiveDdlBarViewModel>(ddlCanvas.LiveDdl);

        await liveDdl.AnalyzeSchemaStructureAsync();

        Assert.False(liveDdl.IsRunningSchemaAnalysis);
        Assert.True(
            liveDdl.SchemaAnalysisPanel.State == SchemaAnalysisViewState.Completed
            || liveDdl.SchemaAnalysisPanel.State == SchemaAnalysisViewState.Empty
            || liveDdl.SchemaAnalysisPanel.State == SchemaAnalysisViewState.Partial
            || liveDdl.SchemaAnalysisPanel.State == SchemaAnalysisViewState.Failed
        );
    }

    [Fact]
    public async Task AnalyzeSchemaStructureAsync_WithCanceledToken_SetsCancelledState()
    {
        var ddlCanvas = new CanvasViewModel(null, null, null, null, new DdlDomainStrategy())
        {
            DatabaseMetadata = CreateMetadata(),
        };

        LiveDdlBarViewModel liveDdl = Assert.IsType<LiveDdlBarViewModel>(ddlCanvas.LiveDdl);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await liveDdl.AnalyzeSchemaStructureAsync(cts.Token);

        Assert.Equal(SchemaAnalysisViewState.Cancelled, liveDdl.SchemaAnalysisPanel.State);
    }

    [Fact]
    public void SchemaAnalysisPanel_ApplyToCanvas_RenamesTableForNamingConventionIssue()
    {
        var ddlCanvas = new CanvasViewModel(null, null, null, null, new DdlDomainStrategy());
        var table = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.TableDefinition), new Point(0, 0));
        table.Parameters["SchemaName"] = "public";
        table.Parameters["TableName"] = "SalesOrder";
        ddlCanvas.Nodes.Add(table);

        LiveDdlBarViewModel liveDdl = Assert.IsType<LiveDdlBarViewModel>(ddlCanvas.LiveDdl);
        SchemaIssue issue = CreateIssue(
            ruleCode: SchemaRuleCode.NAMING_CONVENTION_VIOLATION,
            targetType: SchemaTargetType.Table,
            tableName: "SalesOrder",
            columnName: null,
            evidence:
            [
                new SchemaEvidence(EvidenceKind.MetadataFact, "namingConvention", "SnakeCase", 1.0d),
            ]);

        liveDdl.SchemaAnalysisPanel.ApplyResult(CreateResult([issue]));
        liveDdl.SchemaAnalysisPanel.ApplyToCanvasCommand.Execute(null);

        Assert.Equal("sales_order", table.Parameters["TableName"]);
    }

    [Fact]
    public void SchemaAnalysisPanel_ApplyToCanvas_AppliesCommentCandidateToColumn()
    {
        var ddlCanvas = new CanvasViewModel(null, null, null, null, new DdlDomainStrategy());

        var table = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.TableDefinition), new Point(0, 0));
        table.Parameters["SchemaName"] = "public";
        table.Parameters["TableName"] = "orders";

        var column = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.ColumnDefinition), new Point(0, 0));
        column.Parameters["ColumnName"] = "status";
        column.Parameters["DataType"] = "text";

        ddlCanvas.Nodes.Add(table);
        ddlCanvas.Nodes.Add(column);
        ddlCanvas.Connections.Add(new ConnectionViewModel(
            column.OutputPins.First(p => p.Name == "column"),
            new Point(0, 0),
            new Point(10, 10))
        { ToPin = table.InputPins.First(p => p.Name == "column") });

        LiveDdlBarViewModel liveDdl = Assert.IsType<LiveDdlBarViewModel>(ddlCanvas.LiveDdl);
        SqlFixCandidate candidate = new(
            CandidateId: "c-1",
            Provider: DatabaseProvider.Postgres,
            Title: "Comment",
            Sql: "COMMENT ON COLUMN \"public\".\"orders\".\"status\" IS 'Current order status';",
            PreconditionsSql: [],
            Safety: SqlCandidateSafety.NonDestructive,
            Visibility: CandidateVisibility.VisibleActionable,
            IsAutoApplicable: true,
            Notes: []);
        SchemaSuggestion suggestion = new(
            SuggestionId: "s-1",
            Title: "Add comment",
            Description: "Apply comment to column",
            Confidence: 0.9d,
            SqlCandidates: [candidate]);
        SchemaIssue issue = CreateIssue(
            ruleCode: SchemaRuleCode.MISSING_REQUIRED_COMMENT,
            targetType: SchemaTargetType.Column,
            tableName: "orders",
            columnName: "status",
            suggestions: [suggestion]);

        liveDdl.SchemaAnalysisPanel.ApplyResult(CreateResult([issue]));
        liveDdl.SchemaAnalysisPanel.SelectedSqlCandidate = candidate;
        liveDdl.SchemaAnalysisPanel.ApplyToCanvasCommand.Execute(null);

        Assert.Equal("Current order status", column.Parameters["Comment"]);
    }

    private static DbMetadata CreateMetadata()
    {
        var idColumn = new ColumnMetadata(
            Name: "id",
            DataType: "int",
            NativeType: "int",
            IsNullable: false,
            IsPrimaryKey: true,
            IsForeignKey: false,
            IsUnique: true,
            IsIndexed: true,
            OrdinalPosition: 1,
            Comment: ""
        );

        var table = new TableMetadata(
            Schema: "public",
            Name: "customers",
            Kind: TableKind.Table,
            EstimatedRowCount: 100,
            Columns: [idColumn],
            Indexes:
            [
                new IndexMetadata(
                    Name: "pk_customers",
                    IsUnique: true,
                    IsClustered: false,
                    IsPrimaryKey: true,
                    Columns: ["id"]
                ),
            ],
            OutboundForeignKeys: [],
            InboundForeignKeys: [],
            Comment: ""
        );

        return new DbMetadata(
            DatabaseName: "db",
            Provider: DatabaseProvider.Postgres,
            ServerVersion: "16",
            CapturedAt: DateTimeOffset.UtcNow,
            Schemas: [new SchemaMetadata("public", [table])],
            AllForeignKeys: []
        );
    }

    private static string L(string key, string fallback)
    {
        string value = LocalizationService.Instance[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    private static SchemaAnalysisResult CreateResult(IReadOnlyList<SchemaIssue> issues)
    {
        return new SchemaAnalysisResult(
            AnalysisId: "analysis",
            Status: SchemaAnalysisStatus.Completed,
            Provider: DatabaseProvider.Postgres,
            DatabaseName: "db",
            StartedAtUtc: DateTimeOffset.UtcNow,
            CompletedAtUtc: DateTimeOffset.UtcNow,
            DurationMs: 10,
            MetadataFingerprint: "fingerprint",
            ProfileContentHash: "profile",
            ProfileVersion: 1,
            PartialState: new SchemaAnalysisPartialState(false, "NONE", 1, 1),
            Issues: issues,
            Diagnostics: [],
            Summary: new SchemaAnalysisSummary(
                TotalIssues: issues.Count,
                InfoCount: issues.Count(i => i.Severity == SchemaIssueSeverity.Info),
                WarningCount: issues.Count(i => i.Severity == SchemaIssueSeverity.Warning),
                CriticalCount: issues.Count(i => i.Severity == SchemaIssueSeverity.Critical),
                QuickWinCount: 0,
                OverallScore: 0d,
                PerRuleCount: new Dictionary<SchemaRuleCode, int>(),
                PerTableCount: new Dictionary<string, int>(),
                AreaScores: new Dictionary<string, double>(),
                ObservedPatterns: new SchemaObservedPatterns(NamingConvention.MixedAllowed, null, null)));
    }

    private static SchemaIssue CreateIssue(
        SchemaRuleCode ruleCode,
        SchemaTargetType targetType,
        string tableName,
        string? columnName,
        IReadOnlyList<SchemaEvidence>? evidence = null,
        IReadOnlyList<SchemaSuggestion>? suggestions = null)
    {
        return new SchemaIssue(
            IssueId: Guid.NewGuid().ToString("N"),
            RuleCode: ruleCode,
            Severity: SchemaIssueSeverity.Warning,
            Confidence: 0.95d,
            TargetType: targetType,
            SchemaName: "public",
            TableName: tableName,
            ColumnName: columnName,
            ConstraintName: null,
            Title: "Issue",
            Message: "Issue message",
            Evidence: evidence ?? [new SchemaEvidence(EvidenceKind.MetadataFact, "k", "v", 1.0d)],
            Suggestions: suggestions ?? [],
            IsAmbiguous: false);
    }
}

