using AkkornStudio.SqlImport.Contracts;
using AkkornStudio.SqlImport.IR;
using AkkornStudio.SqlImport.IR.Expressions;
using AkkornStudio.SqlImport.IR.Metadata;
using AkkornStudio.SqlImport.IR.Sources;
using AkkornStudio.SqlImport.Tracing;
using SqlImportLiteralExpr = AkkornStudio.SqlImport.IR.Expressions.LiteralExpr;

namespace AkkornStudio.Tests.Unit.Services.SqlImport.Ir;

public sealed class JoinExprTests
{
    [Fact]
    public void Constructor_AllowsCrossJoinWithoutOnExpression()
    {
        SourceExpr rightSource = CreateSource();
        SqlIrNodeMetadata metadata = CreateNodeMetadata();
        var join = new JoinExpr(
            JoinId: "j1",
            JoinType: SqlJoinType.Cross,
            RightSource: rightSource,
            OnExpr: null,
            Ordinal: 1,
            ResolutionStatus: SqlResolutionStatus.Resolved,
            NodeMetadata: metadata);

        Assert.Equal("j1", join.JoinId);
        Assert.Equal(SqlJoinType.Cross, join.JoinType);
        Assert.Same(rightSource, join.RightSource);
        Assert.Null(join.OnExpr);
        Assert.Equal(1, join.Ordinal);
        Assert.Equal(SqlResolutionStatus.Resolved, join.ResolutionStatus);
        Assert.Same(metadata, join.NodeMetadata);
    }

    [Fact]
    public void Constructor_AllowsNonCrossJoin_WhenOnExpressionExists()
    {
        SqlExpression onExpr = CreateLiteral("1");

        var join = new JoinExpr(
            JoinId: "j1",
            JoinType: SqlJoinType.Inner,
            RightSource: CreateSource(),
            OnExpr: onExpr,
            Ordinal: 1,
            ResolutionStatus: SqlResolutionStatus.Resolved,
            NodeMetadata: CreateNodeMetadata());

        Assert.Same(onExpr, join.OnExpr);
        Assert.Equal(SqlResolutionStatus.Resolved, join.ResolutionStatus);
    }

    [Fact]
    public void Constructor_AllowsNonCrossJoinWithoutOn_WhenResolutionIsUnresolved()
    {
        var join = new JoinExpr(
            JoinId: "j1",
            JoinType: SqlJoinType.Left,
            RightSource: CreateSource(),
            OnExpr: null,
            Ordinal: 1,
            ResolutionStatus: SqlResolutionStatus.Unresolved,
            NodeMetadata: CreateNodeMetadata());

        Assert.Null(join.OnExpr);
        Assert.Equal(SqlResolutionStatus.Unresolved, join.ResolutionStatus);
    }

    [Fact]
    public void Constructor_Throws_WhenNonCrossJoinWithoutOn_IsMarkedResolved()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            new JoinExpr(
                JoinId: "j1",
                JoinType: SqlJoinType.Right,
                RightSource: CreateSource(),
                OnExpr: null,
                Ordinal: 1,
                ResolutionStatus: SqlResolutionStatus.Resolved,
                NodeMetadata: CreateNodeMetadata()));

        Assert.Equal("Non-CROSS JOIN without ON expression cannot be marked as resolved.", ex.Message);
    }

    [Fact]
    public void Constructor_ThrowsArgumentNull_WhenRightSourceIsNull()
    {
        ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() =>
            new JoinExpr(
                JoinId: "j1",
                JoinType: SqlJoinType.Inner,
                RightSource: null!,
                OnExpr: CreateLiteral("1"),
                Ordinal: 1,
                ResolutionStatus: SqlResolutionStatus.Resolved,
                NodeMetadata: CreateNodeMetadata()));

        Assert.Equal("RightSource", ex.ParamName);
    }

    [Fact]
    public void Constructor_ThrowsArgumentNull_WhenNodeMetadataIsNull()
    {
        ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() =>
            new JoinExpr(
                JoinId: "j1",
                JoinType: SqlJoinType.Inner,
                RightSource: CreateSource(),
                OnExpr: CreateLiteral("1"),
                Ordinal: 1,
                ResolutionStatus: SqlResolutionStatus.Resolved,
                NodeMetadata: null!));

        Assert.Equal("NodeMetadata", ex.ParamName);
    }

    private static SourceExpr CreateSource() =>
        new TableRefSourceExpr(
            SourceId: "src",
            Database: null,
            Schema: "public",
            Table: "orders",
            Alias: "o",
            ResolutionStatus: SqlResolutionStatus.Resolved,
            NodeMetadata: CreateNodeMetadata());

    private static SqlExpression CreateLiteral(string raw) =>
        new SqlImportLiteralExpr(
            ExprId: $"lit-{raw}",
            SourceSpan: null,
            SemanticType: SqlImportSemanticType.Text,
            ResolutionStatus: SqlResolutionStatus.Resolved,
            TraceMeta: new TraceMeta("q1", null, null, null),
            NodeMetadata: CreateNodeMetadata(),
            Raw: raw,
            Normalized: raw,
            IsNullLiteral: false);

    private static SqlIrNodeMetadata CreateNodeMetadata() => new(false, null, [], []);
}
