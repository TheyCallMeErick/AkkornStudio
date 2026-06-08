using AkkornStudio.Core;
using AkkornStudio.Registry;

namespace AkkornStudio.Expressions.Advanced;

public enum CastTargetType
{
    Text,
    Integer,
    BigInt,
    Decimal,
    Float,
    Boolean,
    Date,
    DateTime,
    Timestamp,
    Uuid,
}

/// <summary>
/// Canonical CAST — every provider uses the SQL-standard CAST(x AS type) syntax.
/// The target type is automatically translated to the provider's dialect.
/// </summary>
public sealed record CastExpr(ISqlExpression Input, CastTargetType TargetType) : ISqlExpression
{
    public PinDataType OutputType =>
        TargetType switch
        {
            CastTargetType.Text => PinDataType.Text,
            CastTargetType.Integer
            or CastTargetType.BigInt
            or CastTargetType.Decimal
            or CastTargetType.Float => PinDataType.Number,
            CastTargetType.Boolean => PinDataType.Boolean,
            CastTargetType.Date or CastTargetType.DateTime or CastTargetType.Timestamp =>
                PinDataType.DateTime,
            CastTargetType.Uuid => PinDataType.Expression,
            _ => throw new InvalidOperationException(
                $"Unsupported cast target type '{TargetType}'."),
        };

    public string Emit(EmitContext ctx)
    {
        string inner = Input.Emit(ctx);
        string providerType = TranslateType(ctx.Provider);
        return $"CAST({inner} AS {providerType})";
    }

    private string TranslateType(DatabaseProvider p) =>
        (TargetType, p) switch
        {
            (CastTargetType.Text, DatabaseProvider.SqlServer) => "NVARCHAR(MAX)",
            (CastTargetType.Text, _) => "TEXT",
            (CastTargetType.Integer, DatabaseProvider.Postgres) => "INTEGER",
            (CastTargetType.Integer, _) => "INT",
            (CastTargetType.BigInt, _) => "BIGINT",
            (CastTargetType.Decimal, _) => "DECIMAL(18,4)",
            (CastTargetType.Float, DatabaseProvider.Postgres) => "DOUBLE PRECISION",
            (CastTargetType.Float, _) => "FLOAT",
            (CastTargetType.Boolean, DatabaseProvider.SqlServer) => "BIT",
            (CastTargetType.Boolean, _) => "BOOLEAN",
            (CastTargetType.Date, _) => "DATE",
            (CastTargetType.DateTime, DatabaseProvider.Postgres) => "TIMESTAMP",
            (CastTargetType.DateTime, _) => "DATETIME",
            (CastTargetType.Timestamp, DatabaseProvider.SqlServer) => "DATETIMEOFFSET",
            (CastTargetType.Timestamp, _) => "TIMESTAMPTZ",
            (CastTargetType.Uuid, DatabaseProvider.SqlServer) => "UNIQUEIDENTIFIER",
            (CastTargetType.Uuid, _) => "UUID",
            _ => throw new InvalidOperationException(
                $"Unsupported cast target type '{TargetType}' for provider '{p}'."),
        };
}
