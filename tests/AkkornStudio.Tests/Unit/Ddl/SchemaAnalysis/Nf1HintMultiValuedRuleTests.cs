using AkkornStudio.Core;
using AkkornStudio.Ddl.SchemaAnalysis.Application.Indexing;
using AkkornStudio.Ddl.SchemaAnalysis.Application.Rules;
using AkkornStudio.Ddl.SchemaAnalysis.Application.Validation;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Contracts;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Enums;
using AkkornStudio.Metadata;

namespace AkkornStudio.Tests.Unit.Ddl.SchemaAnalysis;

public sealed class Nf1HintMultiValuedRuleTests
{
    private readonly SchemaMetadataIndexBuilder _indexBuilder = new();
    private readonly Nf1HintMultiValuedRule _rule = new();

    [Fact]
    public async Task ExecuteAsync_Emits_WhenStrongTokenAndDefaultCommaExist()
    {
        DbMetadata metadata = CreateMetadata(
            new ColumnMetadata("tags", "text", "text", false, false, false, false, false, 1, DefaultValue: "a,b", Comment: "Tags")
        );

        SchemaRuleExecutionResult result = await _rule.ExecuteAsync(CreateContext(metadata));

        Assert.Single(result.Issues);
        Assert.Equal(SchemaRuleCode.NF1_HINT_MULTI_VALUED, result.Issues[0].RuleCode);
    }

    [Fact]
    public async Task ExecuteAsync_IncludesPrimaryKeyEvidence_WhenSinglePrimaryKeyExists()
    {
        DbMetadata metadata = CreateMetadata(
            new ColumnMetadata("tags", "text", "text", false, false, false, false, false, 2, DefaultValue: "a,b", Comment: "Tags"),
            includePrimaryKey: true
        );

        SchemaRuleExecutionResult result = await _rule.ExecuteAsync(CreateContext(metadata));

        SchemaIssue issue = Assert.Single(result.Issues);
        Assert.Contains(issue.Evidence, evidence => evidence.Key == "primaryKeyColumn" && evidence.Value == "id");
        Assert.Contains(issue.Evidence, evidence => evidence.Key == "primaryKeyNativeType" && evidence.Value == "integer");
        Assert.Contains(issue.Evidence, evidence => evidence.Key == "columnNativeType" && evidence.Value == "text");
    }

    [Fact]
    public async Task ExecuteAsync_Emits_WhenJsonContextAndRelationalContextExist()
    {
        DbMetadata metadata = CreateMetadata(
            new ColumnMetadata("payload_values", "json", "json", false, false, false, false, false, 1, Comment: "Payload"),
            includeOutboundFk: true
        );

        SchemaRuleExecutionResult result = await _rule.ExecuteAsync(CreateContext(metadata));

        Assert.Single(result.Issues);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotEmit_WithOnlyOneSignal()
    {
        DbMetadata metadata = CreateMetadata(
            new ColumnMetadata("tags", "text", "text", false, false, false, false, false, 1, Comment: "Tags")
        );

        SchemaRuleExecutionResult result = await _rule.ExecuteAsync(CreateContext(metadata));

        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task ExecuteAsync_AllowlistReducesEmission_WhenApplicable()
    {
        DbMetadata metadata = CreateMetadata(
            new ColumnMetadata("tags", "text", "text", false, false, false, false, false, 1, DefaultValue: "a,b", Comment: "Tags")
        );
        SchemaAnalysisProfile profile = SchemaAnalysisProfileNormalizer.CreateDefaultProfile() with
        {
            MinConfidenceGlobal = 0.70,
            SemiStructuredPayloadAllowlist = ["public.orders.tags"],
            RuleSettings = SchemaAnalysisProfileNormalizer.CreateDefaultProfile().RuleSettings
                .ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Key == SchemaRuleCode.NF1_HINT_MULTI_VALUED
                        ? new SchemaRuleSetting(true, 0.70, 500)
                        : pair.Value
                ),
        };

        SchemaRuleExecutionResult result = await _rule.ExecuteAsync(CreateContext(metadata, profile));

        Assert.Empty(result.Issues);
    }

    private SchemaAnalysisExecutionContext CreateContext(
        DbMetadata metadata,
        SchemaAnalysisProfile? profile = null
    )
    {
        SchemaAnalysisProfile effectiveProfile = profile ?? SchemaAnalysisProfileNormalizer.CreateDefaultProfile();
        SchemaMetadataIndexSnapshot indices = _indexBuilder.Build(metadata, effectiveProfile);
        return new SchemaAnalysisExecutionContext(metadata, effectiveProfile, indices, "fingerprint", "profile");
    }

    private static DbMetadata CreateMetadata(
        ColumnMetadata targetColumn,
        bool includeOutboundFk = false,
        bool includePrimaryKey = false
    )
    {
        ForeignKeyRelation foreignKey = new(
            ConstraintName: "fk_orders_customer",
            ChildSchema: "public",
            ChildTable: "orders",
            ChildColumn: "customer_id",
            ParentSchema: "public",
            ParentTable: "customers",
            ParentColumn: "id",
            OnDelete: ReferentialAction.NoAction,
            OnUpdate: ReferentialAction.NoAction
        );

        TableMetadata orders = new(
            Schema: "public",
            Name: "orders",
            Kind: TableKind.Table,
            EstimatedRowCount: 100,
            Columns:
            [
                .. includePrimaryKey
                    ? [new ColumnMetadata("id", "integer", "integer", false, true, false, false, true, 1, Comment: "Id")]
                    : Array.Empty<ColumnMetadata>(),
                targetColumn,
                new ColumnMetadata("customer_id", "integer", "integer", false, false, includeOutboundFk, false, true, 2, Comment: "Customer"),
            ],
            Indexes: [],
            OutboundForeignKeys: includeOutboundFk ? [foreignKey] : [],
            InboundForeignKeys: [],
            Comment: "Orders"
        );

        TableMetadata customers = new(
            Schema: "public",
            Name: "customers",
            Kind: TableKind.Table,
            EstimatedRowCount: 10,
            Columns:
            [
                new ColumnMetadata("id", "integer", "integer", false, true, false, false, true, 1, Comment: "Id"),
            ],
            Indexes: [],
            OutboundForeignKeys: [],
            InboundForeignKeys: includeOutboundFk ? [foreignKey] : [],
            Comment: "Customers"
        );

        return new DbMetadata(
            DatabaseName: "db",
            Provider: DatabaseProvider.Postgres,
            ServerVersion: "1",
            CapturedAt: DateTimeOffset.UtcNow,
            Schemas: [new SchemaMetadata("public", [orders, customers])],
            AllForeignKeys: includeOutboundFk ? [foreignKey] : []
        );
    }
}
