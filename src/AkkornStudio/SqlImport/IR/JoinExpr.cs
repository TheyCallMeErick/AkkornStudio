using AkkornStudio.SqlImport.Contracts;
using AkkornStudio.SqlImport.IR.Expressions;
using AkkornStudio.SqlImport.IR.Sources;
using AkkornStudio.SqlImport.IR.Metadata;

namespace AkkornStudio.SqlImport.IR;

public sealed record JoinExpr
{
    public JoinExpr(
        string JoinId,
        SqlJoinType JoinType,
        SourceExpr RightSource,
        SqlExpression? OnExpr,
        int Ordinal,
        SqlResolutionStatus ResolutionStatus,
        SqlIrNodeMetadata NodeMetadata
    )
    {
        this.JoinId = JoinId;
        this.JoinType = JoinType;
        this.RightSource = RightSource ?? throw new ArgumentNullException(nameof(RightSource));
        this.OnExpr = OnExpr;
        this.Ordinal = Ordinal;
        this.ResolutionStatus = ResolutionStatus;
        this.NodeMetadata = NodeMetadata ?? throw new ArgumentNullException(nameof(NodeMetadata));

        if (JoinType != SqlJoinType.Cross
            && OnExpr is null
            && ResolutionStatus == SqlResolutionStatus.Resolved)
        {
            throw new InvalidOperationException(
                "Non-CROSS JOIN without ON expression cannot be marked as resolved.");
        }
    }

    public string JoinId { get; }
    public SqlJoinType JoinType { get; }
    public SourceExpr RightSource { get; }
    public SqlExpression? OnExpr { get; }
    public int Ordinal { get; }
    public SqlResolutionStatus ResolutionStatus { get; }
    public SqlIrNodeMetadata NodeMetadata { get; }
}
