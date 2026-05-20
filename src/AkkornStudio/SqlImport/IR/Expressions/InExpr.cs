using AkkornStudio.SqlImport.Contracts;
using AkkornStudio.SqlImport.IR.Metadata;
using AkkornStudio.SqlImport.Tracing;

namespace AkkornStudio.SqlImport.IR.Expressions;

public sealed record InExpr : SqlExpression
{
    public InExpr(
        string ExprId,
        SourceSpan? SourceSpan,
        SqlImportSemanticType SemanticType,
        SqlResolutionStatus ResolutionStatus,
        TraceMeta TraceMeta,
        SqlIrNodeMetadata NodeMetadata,
        SqlExpression Value,
        IReadOnlyList<SqlExpression> Values,
        QueryExpr? Subquery,
        bool IsNegated
    ) : base(ExprId, SourceSpan, SemanticType, ResolutionStatus, TraceMeta, NodeMetadata)
    {
        this.Value = Value ?? throw new ArgumentNullException(nameof(Value));
        this.Values = Values ?? throw new ArgumentNullException(nameof(Values));
        this.Subquery = Subquery;
        this.IsNegated = IsNegated;

        bool hasValues = Values.Count > 0;
        bool hasSubquery = Subquery is not null;
        if (hasValues == hasSubquery)
        {
            throw new InvalidOperationException(
                "IN expression must have either value list or subquery, but not both.");
        }
    }

    public SqlExpression Value { get; }
    public IReadOnlyList<SqlExpression> Values { get; }
    public QueryExpr? Subquery { get; }
    public bool IsNegated { get; }
}
