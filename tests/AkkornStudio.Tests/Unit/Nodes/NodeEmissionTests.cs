using AkkornStudio.Core;
using AkkornStudio.Nodes;
using AkkornStudio.QueryEngine;
using AkkornStudio.Registry;
using Xunit;
using System.Text.RegularExpressions;

namespace AkkornStudio.Tests.Unit.Nodes;

// ─── Shared fixtures ─────────────────────────────────────────────────────────

internal static class NodeFixtures
{
    public static EmitContext Ctx(DatabaseProvider p) => new(p, new SqlFunctionRegistry(p));

    public static EmitContext Postgres => Ctx(DatabaseProvider.Postgres);
    public static EmitContext MySQL => Ctx(DatabaseProvider.MySql);
    public static EmitContext SqlServer => Ctx(DatabaseProvider.SqlServer);

    // A ColumnExpr for orders.total
    public static ColumnExpr OrderTotal => new("orders", "total", PinDataType.Number);

    // A ColumnExpr for users.email
    public static ColumnExpr UserEmail => new("users", "email", PinDataType.Text);

    // A ColumnExpr for events.payload (JSON)
    public static ColumnExpr EventPayload => new("events", "payload", PinDataType.Json);
}

// ═════════════════════════════════════════════════════════════════════════════
// EMIT CONTEXT
// ═════════════════════════════════════════════════════════════════════════════

public class EmitContextTests
{
    [Theory]
    [InlineData(DatabaseProvider.Postgres, "\"my_col\"")]
    [InlineData(DatabaseProvider.MySql, "`my_col`")]
    [InlineData(DatabaseProvider.SqlServer, "[my_col]")]
    [InlineData(DatabaseProvider.SQLite, "\"my_col\"")]
    public void QuoteIdentifier_ProducesCorrectDialect(DatabaseProvider p, string expected)
    {
        EmitContext ctx = NodeFixtures.Ctx(p);
        Assert.Equal(expected, ctx.QuoteIdentifier("my_col"));
    }

    [Fact]
    public void QuoteLiteral_EscapesSingleQuotes()
    {
        EmitContext ctx = NodeFixtures.Postgres;
        Assert.Equal("'O''Brien'", EmitContext.QuoteLiteral("O'Brien"));
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// LEAF EXPRESSIONS
// ═════════════════════════════════════════════════════════════════════════════

public class LeafExpressionTests
{
    [Fact]
    public void ColumnExpr_EmitsQualifiedName()
    {
        var expr = new ColumnExpr("users", "email");
        string sql = expr.Emit(NodeFixtures.Postgres);
        Assert.Equal("\"users\".\"email\"", sql);
    }

    [Fact]
    public void ColumnExpr_NoTable_EmitsUnqualified()
    {
        var expr = new ColumnExpr("", "name");
        Assert.Equal("\"name\"", expr.Emit(NodeFixtures.Postgres));
    }

    [Fact]
    public void LiteralExpr_PassesRawValueThrough() =>
        Assert.Equal("42", new LiteralExpr("42").Emit(NodeFixtures.Postgres));

    [Fact]
    public void StringLiteralExpr_WrapsInSingleQuotes() =>
        Assert.Equal("'hello'", new StringLiteralExpr("hello").Emit(NodeFixtures.Postgres));

    [Fact]
    public void NumberLiteralExpr_UsesInvariantCulture() =>
        // Must use '.' not ',' regardless of thread culture
        Assert.Equal("3.14", new NumberLiteralExpr(3.14).Emit(NodeFixtures.Postgres));

    [Fact]
    public void NullExpr_EmitsNULL() =>
        Assert.Equal("NULL", NullExpr.Instance.Emit(NodeFixtures.Postgres));
}

// ═════════════════════════════════════════════════════════════════════════════
// STRING TRANSFORM NODES (via FunctionCallExpr)
// ═════════════════════════════════════════════════════════════════════════════

public class StringTransformTests
{
    [Theory]
    [InlineData(DatabaseProvider.Postgres, "UPPER(\"users\".\"email\")")]
    [InlineData(DatabaseProvider.MySql, "UPPER(`users`.`email`)")]
    [InlineData(DatabaseProvider.SqlServer, "UPPER([users].[email])")]
    public void Upper_AllProviders(DatabaseProvider p, string expected)
    {
        var expr = new FunctionCallExpr(SqlFn.Upper, [NodeFixtures.UserEmail], PinDataType.Text);
        Assert.Equal(expected, expr.Emit(NodeFixtures.Ctx(p)));
    }

    [Fact]
    public void Lower_Postgres()
    {
        var expr = new FunctionCallExpr(SqlFn.Lower, [NodeFixtures.UserEmail], PinDataType.Text);
        Assert.Equal("LOWER(\"users\".\"email\")", expr.Emit(NodeFixtures.Postgres));
    }

    [Fact]
    public void Trim_Postgres()
    {
        var expr = new FunctionCallExpr(SqlFn.Trim, [NodeFixtures.UserEmail], PinDataType.Text);
        Assert.Equal("TRIM(\"users\".\"email\")", expr.Emit(NodeFixtures.Postgres));
    }

    [Fact]
    public void Length_MySQL_UsesCHAR_LENGTH()
    {
        var expr = new FunctionCallExpr(SqlFn.Length, [NodeFixtures.UserEmail], PinDataType.Number);
        Assert.Equal("CHAR_LENGTH(`users`.`email`)", expr.Emit(NodeFixtures.MySQL));
    }

    [Fact]
    public void Length_SqlServer_UsesLEN()
    {
        var expr = new FunctionCallExpr(SqlFn.Length, [NodeFixtures.UserEmail], PinDataType.Number);
        Assert.Equal("LEN([users].[email])", expr.Emit(NodeFixtures.SqlServer));
    }

    [Fact]
    public void Concat_ThreeArgs_AllProviders()
    {
        var a = new StringLiteralExpr("Hello ");
        var b = NodeFixtures.UserEmail as ISqlExpression;
        var c = new StringLiteralExpr("!");
        var expr = new FunctionCallExpr(SqlFn.Concat, [a, b, c], PinDataType.Text);

        foreach (
            DatabaseProvider p in new[]
            {
                DatabaseProvider.Postgres,
                DatabaseProvider.MySql,
                DatabaseProvider.SqlServer,
            }
        )
            Assert.StartsWith("CONCAT(", expr.Emit(NodeFixtures.Ctx(p)));
    }

    [Fact]
    public void RegexMatch_Postgres_UsesTilde()
    {
        EmitContext ctx = NodeFixtures.Postgres;
        string sql = ctx.Registry.GetFunction(SqlFn.Regex, "\"users\".\"email\"", "'@corp\\.io'");
        Assert.Equal("\"users\".\"email\" ~ '@corp\\.io'", sql);
    }

    [Fact]
    public void RegexMatch_MySQL_UsesREGEXP()
    {
        EmitContext ctx = NodeFixtures.MySQL;
        string sql = ctx.Registry.GetFunction(SqlFn.Regex, "`users`.`email`", "'@corp'");
        Assert.Equal("`users`.`email` REGEXP '@corp'", sql);
    }

    [Fact]
    public void RegexMatch_SqlServer_UsesPATINDEX()
    {
        EmitContext ctx = NodeFixtures.SqlServer;
        string sql = ctx.Registry.GetFunction(SqlFn.Regex, "[users].[email]", "'%@corp%'");
        Assert.Contains("PATINDEX", sql);
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// MATH NODES
// ═════════════════════════════════════════════════════════════════════════════

public class MathNodeTests
{
    [Fact]
    public void Arithmetic_Add_ProducesParenthesised()
    {
        var expr = new RawSqlExpr(
            $"({NodeFixtures.OrderTotal.Emit(NodeFixtures.Postgres)} + 10)",
            PinDataType.Number
        );
        Assert.Equal("(\"orders\".\"total\" + 10)", expr.Emit(NodeFixtures.Postgres));
    }

    [Fact]
    public void AggregateExpr_Sum_WrapsInSUM()
    {
        var expr = new AggregateExpr(AggregateFunction.Sum, NodeFixtures.OrderTotal);
        Assert.Equal("SUM(\"orders\".\"total\")", expr.Emit(NodeFixtures.Postgres));
    }

    [Fact]
    public void AggregateExpr_CountStar_EmitsCountStar()
    {
        var expr = new AggregateExpr(AggregateFunction.Count, null);
        Assert.Equal("COUNT(*)", expr.Emit(NodeFixtures.Postgres));
    }

    [Fact]
    public void AggregateExpr_CountDistinct_IncludesDistinct()
    {
        var expr = new AggregateExpr(
            AggregateFunction.Count,
            NodeFixtures.UserEmail,
            Distinct: true
        );
        Assert.Contains("DISTINCT", expr.Emit(NodeFixtures.Postgres));
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// CAST NODE
// ═════════════════════════════════════════════════════════════════════════════

public class CastNodeTests
{
    public static IEnumerable<object[]> CastTypeCases()
    {
        yield return [DatabaseProvider.SqlServer, CastTargetType.Text, "NVARCHAR(MAX)"];
        yield return [DatabaseProvider.Postgres, CastTargetType.Text, "TEXT"];
        yield return [DatabaseProvider.MySql, CastTargetType.Text, "TEXT"];
        yield return [DatabaseProvider.Postgres, CastTargetType.Integer, "INTEGER"];
        yield return [DatabaseProvider.MySql, CastTargetType.Integer, "INT"];
        yield return [DatabaseProvider.Postgres, CastTargetType.BigInt, "BIGINT"];
        yield return [DatabaseProvider.Postgres, CastTargetType.Decimal, "DECIMAL(18,4)"];
        yield return [DatabaseProvider.Postgres, CastTargetType.Float, "DOUBLE PRECISION"];
        yield return [DatabaseProvider.SqlServer, CastTargetType.Float, "FLOAT"];
        yield return [DatabaseProvider.SqlServer, CastTargetType.Boolean, "BIT"];
        yield return [DatabaseProvider.MySql, CastTargetType.Boolean, "BOOLEAN"];
        yield return [DatabaseProvider.Postgres, CastTargetType.Date, "DATE"];
        yield return [DatabaseProvider.Postgres, CastTargetType.DateTime, "TIMESTAMP"];
        yield return [DatabaseProvider.MySql, CastTargetType.DateTime, "DATETIME"];
        yield return [DatabaseProvider.SqlServer, CastTargetType.Timestamp, "DATETIMEOFFSET"];
        yield return [DatabaseProvider.Postgres, CastTargetType.Timestamp, "TIMESTAMPTZ"];
        yield return [DatabaseProvider.SqlServer, CastTargetType.Uuid, "UNIQUEIDENTIFIER"];
        yield return [DatabaseProvider.Postgres, CastTargetType.Uuid, "UUID"];
    }

    [Fact]
    public void Cast_ToText_Postgres_EmitsTEXT()
    {
        var expr = new CastExpr(NodeFixtures.OrderTotal, CastTargetType.Text);
        Assert.Equal("CAST(\"orders\".\"total\" AS TEXT)", expr.Emit(NodeFixtures.Postgres));
    }

    [Fact]
    public void Cast_ToText_SqlServer_EmitsNVARCHAR()
    {
        var expr = new CastExpr(NodeFixtures.OrderTotal, CastTargetType.Text);
        Assert.Contains("NVARCHAR", expr.Emit(NodeFixtures.SqlServer));
    }

    [Fact]
    public void Cast_ToBoolean_SqlServer_EmitsBIT()
    {
        var expr = new CastExpr(new LiteralExpr("1"), CastTargetType.Boolean);
        Assert.Equal("CAST(1 AS BIT)", expr.Emit(NodeFixtures.SqlServer));
    }

    [Fact]
    public void Cast_ToTimestamp_Postgres_EmitsTIMESTAMPTZ()
    {
        var expr = new CastExpr(new StringLiteralExpr("2024-01-01"), CastTargetType.Timestamp);
        Assert.Contains("TIMESTAMPTZ", expr.Emit(NodeFixtures.Postgres));
    }

    [Fact]
    public void Cast_OutputType_MatchesTarget()
    {
        Assert.Equal(
            PinDataType.Text,
            new CastExpr(NullExpr.Instance, CastTargetType.Text).OutputType
        );
        Assert.Equal(
            PinDataType.Number,
            new CastExpr(NullExpr.Instance, CastTargetType.Integer).OutputType
        );
        Assert.Equal(
            PinDataType.DateTime,
            new CastExpr(NullExpr.Instance, CastTargetType.Date).OutputType
        );
        Assert.Equal(
            PinDataType.Expression,
            new CastExpr(NullExpr.Instance, CastTargetType.Uuid).OutputType
        );
    }

    [Theory]
    [MemberData(nameof(CastTypeCases))]
    public void Cast_TranslateType_MatchesProviderMapping(
        DatabaseProvider provider,
        CastTargetType targetType,
        string expectedSqlType)
    {
        var expr = new CastExpr(new LiteralExpr("1"), targetType);
        string sql = expr.Emit(NodeFixtures.Ctx(provider));
        Assert.Equal($"CAST(1 AS {expectedSqlType})", sql);
    }

}

// ═════════════════════════════════════════════════════════════════════════════
// COMPARISON NODES
// ═════════════════════════════════════════════════════════════════════════════

public class ComparisonNodeTests
{
    [Fact]
    public void Equals_EmitsEqualSign()
    {
        var expr = new ComparisonExpr(
            NodeFixtures.OrderTotal,
            ComparisonOperator.Eq,
            new NumberLiteralExpr(100)
        );
        Assert.Equal("(\"orders\".\"total\" = 100)", expr.Emit(NodeFixtures.Postgres));
    }

    [Fact]
    public void NotEquals_EmitsAngleBrackets()
    {
        var expr = new ComparisonExpr(
            NodeFixtures.UserEmail,
            ComparisonOperator.Neq,
            new StringLiteralExpr("admin")
        );
        Assert.Contains("<>", expr.Emit(NodeFixtures.Postgres));
    }

    [Fact]
    public void GreaterThan_EmitsGt()
    {
        var expr = new ComparisonExpr(
            NodeFixtures.OrderTotal,
            ComparisonOperator.Gt,
            new NumberLiteralExpr(0)
        );
        Assert.Contains(">", expr.Emit(NodeFixtures.Postgres));
        Assert.DoesNotContain(">=", expr.Emit(NodeFixtures.Postgres));
    }

    [Fact]
    public void Between_EmitsBetweenAndClause()
    {
        var expr = new BetweenExpr(
            NodeFixtures.OrderTotal,
            new NumberLiteralExpr(100),
            new NumberLiteralExpr(999)
        );
        string sql = expr.Emit(NodeFixtures.Postgres);
        Assert.Contains("BETWEEN", sql);
        Assert.Contains("100", sql);
        Assert.Contains("999", sql);
        Assert.Contains("AND", sql);
    }

    [Fact]
    public void NotBetween_EmitsNOT_BETWEEN()
    {
        var expr = new BetweenExpr(
            NodeFixtures.OrderTotal,
            new NumberLiteralExpr(0),
            new NumberLiteralExpr(10),
            Negate: true
        );
        Assert.Contains("NOT BETWEEN", expr.Emit(NodeFixtures.Postgres));
    }

    [Fact]
    public void IsNull_EmitsISNull()
    {
        var expr = new IsNullExpr(NodeFixtures.UserEmail);
        Assert.Contains("IS NULL", expr.Emit(NodeFixtures.Postgres));
    }

    [Fact]
    public void IsNotNull_EmitsISNotNull()
    {
        var expr = new IsNullExpr(NodeFixtures.UserEmail, Negate: true);
        Assert.Contains("IS NOT NULL", expr.Emit(NodeFixtures.Postgres));
    }

    [Fact]
    public void Like_EmitsLIKE()
    {
        var expr = new ComparisonExpr(
            NodeFixtures.UserEmail,
            ComparisonOperator.Like,
            new StringLiteralExpr("%@corp%")
        );
        Assert.Contains("LIKE", expr.Emit(NodeFixtures.MySQL));
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// LOGIC GATES
// ═════════════════════════════════════════════════════════════════════════════

public class LogicGateTests
{
    private static ISqlExpression TrueExpr => new LiteralExpr("TRUE", PinDataType.Boolean);
    private static ISqlExpression FalseExpr => new LiteralExpr("FALSE", PinDataType.Boolean);

    [Fact]
    public void And_TwoOperands_JoinsWithAND()
    {
        var expr = new LogicGateExpr(LogicOperator.And, [TrueExpr, FalseExpr]);
        Assert.Equal("(TRUE AND FALSE)", expr.Emit(NodeFixtures.Postgres));
    }

    [Fact]
    public void Or_TwoOperands_JoinsWithOR()
    {
        var expr = new LogicGateExpr(LogicOperator.Or, [TrueExpr, FalseExpr]);
        Assert.Equal("(TRUE OR FALSE)", expr.Emit(NodeFixtures.Postgres));
    }

    [Fact]
    public void Not_NegatesOperand()
    {
        var inner = new ComparisonExpr(
            NodeFixtures.OrderTotal,
            ComparisonOperator.Gt,
            new NumberLiteralExpr(0)
        );
        var expr = new NotExpr(inner);
        Assert.StartsWith("(NOT ", expr.Emit(NodeFixtures.Postgres));
    }

    [Fact]
    public void And_ThreeOperands_AllJoined()
    {
        var a = new LiteralExpr("A", PinDataType.Boolean);
        var b = new LiteralExpr("B", PinDataType.Boolean);
        var c = new LiteralExpr("C", PinDataType.Boolean);
        var expr = new LogicGateExpr(LogicOperator.And, [a, b, c]);
        Assert.Equal("(A AND B AND C)", expr.Emit(NodeFixtures.Postgres));
    }

    [Fact]
    public void Nested_AndOr_ProducesCorrectPrecedence()
    {
        // (A OR B) AND C
        var orExpr = new LogicGateExpr(
            LogicOperator.Or,
            [
                new LiteralExpr("A", PinDataType.Boolean) as ISqlExpression,
                new LiteralExpr("B", PinDataType.Boolean),
            ]
        );
        var andExpr = new LogicGateExpr(
            LogicOperator.And,
            [orExpr as ISqlExpression, new LiteralExpr("C", PinDataType.Boolean)]
        );

        Assert.Equal("((A OR B) AND C)", andExpr.Emit(NodeFixtures.Postgres));
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// JSON EXTRACT NODE
// ═════════════════════════════════════════════════════════════════════════════

public class JsonExtractTests
{
    // ── Postgres ->> operator ────────────────────────────────────────────────

    [Fact]
    public void JsonExtract_Postgres_SimpleKey_UsesArrowArrow()
    {
        var reg = new SqlFunctionRegistry(DatabaseProvider.Postgres);
        string sql = reg.GetFunction(SqlFn.JsonExtract, "payload", "'$.city'");
        Assert.Equal("payload->>'city'", sql);
    }

    [Fact]
    public void JsonExtract_Postgres_NestedPath_BuildsChain()
    {
        var reg = new SqlFunctionRegistry(DatabaseProvider.Postgres);
        string sql = reg.GetFunction(SqlFn.JsonExtract, "payload", "'$.address.city'");
        Assert.Equal("payload->'address'->>'city'", sql);
    }

    [Fact]
    public void JsonQuery_Postgres_NestedPath_UsesArrowOnly()
    {
        var reg = new SqlFunctionRegistry(DatabaseProvider.Postgres);
        string sql = reg.GetFunction(SqlFn.JsonQuery, "payload", "'$.address'");
        Assert.Equal("payload->'address'", sql);
    }

    [Fact]
    public void JsonExtract_Postgres_ArrayIndex_UsesNumericNav()
    {
        var reg = new SqlFunctionRegistry(DatabaseProvider.Postgres);
        string sql = reg.GetFunction(SqlFn.JsonExtract, "payload", "'$.items[0].name'");
        // Should contain ->0 for array index navigation
        Assert.Contains("->0", sql);
        Assert.Contains("->>'name'", sql);
    }

    [Fact]
    public void JsonArrayLength_Postgres_EmitsJsonbArrayLength()
    {
        var reg = new SqlFunctionRegistry(DatabaseProvider.Postgres);
        string sql = reg.GetFunction(SqlFn.JsonArrayLength, "payload", "'$'");
        Assert.Contains("jsonb_array_length", sql);
    }

    // ── MySQL JSON_EXTRACT ────────────────────────────────────────────────────

    [Fact]
    public void JsonExtract_MySQL_UsesJsonUnquoteJsonExtract()
    {
        var reg = new SqlFunctionRegistry(DatabaseProvider.MySql);
        string sql = reg.GetFunction(SqlFn.JsonExtract, "payload", "'$.city'");
        Assert.Contains("JSON_UNQUOTE", sql);
        Assert.Contains("JSON_EXTRACT", sql);
        Assert.Contains("$.city", sql);
    }

    [Fact]
    public void JsonQuery_MySQL_UsesJsonExtractWithoutUnquote()
    {
        var reg = new SqlFunctionRegistry(DatabaseProvider.MySql);
        string sql = reg.GetFunction(SqlFn.JsonQuery, "payload", "'$.address'");
        Assert.StartsWith("JSON_EXTRACT(", sql);
        Assert.DoesNotContain("JSON_UNQUOTE", sql);
    }

    [Fact]
    public void JsonArrayLength_MySQL_UsesJsonLength()
    {
        var reg = new SqlFunctionRegistry(DatabaseProvider.MySql);
        string sql = reg.GetFunction(SqlFn.JsonArrayLength, "payload", "'$'");
        Assert.Contains("JSON_LENGTH", sql);
    }

    // ── SQL Server JSON_VALUE ─────────────────────────────────────────────────

    [Fact]
    public void JsonExtract_SqlServer_UsesJsonValue()
    {
        var reg = new SqlFunctionRegistry(DatabaseProvider.SqlServer);
        string sql = reg.GetFunction(SqlFn.JsonExtract, "payload", "'$.city'");
        Assert.StartsWith("JSON_VALUE(", sql);
        Assert.Contains("lax $.city", sql);
    }

    [Fact]
    public void JsonQuery_SqlServer_UsesJsonQuery()
    {
        var reg = new SqlFunctionRegistry(DatabaseProvider.SqlServer);
        string sql = reg.GetFunction(SqlFn.JsonQuery, "payload", "'$.address'");
        Assert.StartsWith("JSON_QUERY(", sql);
    }

    [Fact]
    public void JsonArrayLength_SqlServer_UsesOPENJSON()
    {
        var reg = new SqlFunctionRegistry(DatabaseProvider.SqlServer);
        string sql = reg.GetFunction(SqlFn.JsonArrayLength, "payload", "'$'");
        Assert.Contains("OPENJSON", sql);
        Assert.Contains("COUNT(*)", sql);
    }

    [Fact]
    public void JsonExtract_SqlServer_AddsLaxPrefix()
    {
        var reg = new SqlFunctionRegistry(DatabaseProvider.SqlServer);
        // Path without lax prefix should be auto-prefixed
        string sql = reg.GetFunction(SqlFn.JsonExtract, "payload", "'$.name'");
        Assert.Contains("lax", sql);
    }

    // ── FunctionCallExpr integration ──────────────────────────────────────────

    [Fact]
    public void JsonExtract_ViaFunctionCallExpr_Postgres()
    {
        var expr = new FunctionCallExpr(
            SqlFn.JsonExtract,
            [NodeFixtures.EventPayload, new StringLiteralExpr("$.user.name")],
            PinDataType.Text
        );

        string sql = expr.Emit(NodeFixtures.Postgres);
        // "events"."payload"->'user'->>'name'
        Assert.Contains("->'user'", sql);
        Assert.Contains("->>'name'", sql);
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// NODE GRAPH COMPILER
// ═════════════════════════════════════════════════════════════════════════════

public class NodeGraphCompilerTests
{
    private static NodeGraph BuildUpperEmailGraph()
    {
        var tableNode = new NodeInstance(
            Id: "tbl1",
            Type: NodeType.TableSource,
            PinLiterals: new Dictionary<string, string>(),
            Parameters: new Dictionary<string, string>(),
            TableFullName: "public.users"
        );

        var upperNode = new NodeInstance(
            Id: "upper1",
            Type: NodeType.Upper,
            PinLiterals: new Dictionary<string, string>(),
            Parameters: new Dictionary<string, string>(),
            Alias: "EmailUpper"
        );

        var graph = new NodeGraph
        {
            Nodes = [tableNode, upperNode],
            Connections = [new Connection("tbl1", "email", "upper1", "text")],
            SelectOutputs = [new SelectBinding("upper1", "result", "EmailUpper")],
        };

        return graph;
    }

    [Fact]
    public void Compile_UpperOnColumn_EmitsUpperFunction()
    {
        NodeGraph graph = BuildUpperEmailGraph();
        var compiler = new NodeGraphCompiler(graph, NodeFixtures.Postgres);
        CompiledNodeGraph result = compiler.Compile();

        Assert.Single(result.SelectExprs);
        string sql = result.SelectExprs[0].Expr.Emit(NodeFixtures.Postgres);
        Assert.Contains("UPPER", sql);
        Assert.Contains("email", sql);
    }

    [Fact]
    public void Compile_TableSource_WithAlias_UsesAliasInColumnReference()
    {
        var tableNode = new NodeInstance(
            Id: "tbl_alias",
            Type: NodeType.TableSource,
            PinLiterals: new Dictionary<string, string>(),
            Parameters: new Dictionary<string, string>(),
            Alias: "u",
            TableFullName: "public.users"
        );

        var graph = new NodeGraph
        {
            Nodes = [tableNode],
            SelectOutputs = [new SelectBinding("tbl_alias", "email", "email")],
        };

        var compiler = new NodeGraphCompiler(graph, NodeFixtures.Postgres);
        CompiledNodeGraph result = compiler.Compile();

        Assert.Single(result.SelectExprs);
        string sql = result.SelectExprs[0].Expr.Emit(NodeFixtures.Postgres);
        Assert.Equal("\"u\".\"email\"", sql);
    }

    [Fact]
    public void Compile_AliasNode_UsesAliasTextInputWhenConnected()
    {
        var tableNode = new NodeInstance(
            Id: "tbl1",
            Type: NodeType.TableSource,
            PinLiterals: new Dictionary<string, string>(),
            Parameters: new Dictionary<string, string>(),
            TableFullName: "public.users"
        );

        var aliasInput = new NodeInstance(
            Id: "txt1",
            Type: NodeType.ValueString,
            PinLiterals: new Dictionary<string, string>(),
            Parameters: new Dictionary<string, string> { ["value"] = "email_custom" }
        );

        var aliasNode = new NodeInstance(
            Id: "as1",
            Type: NodeType.Alias,
            PinLiterals: new Dictionary<string, string>(),
            Parameters: new Dictionary<string, string> { ["alias"] = "fallback_alias" }
        );

        var graph = new NodeGraph
        {
            Nodes = [tableNode, aliasInput, aliasNode],
            Connections =
            [
                new Connection("tbl1", "email", "as1", "expression"),
                new Connection("txt1", "result", "as1", "alias_text"),
            ],
            SelectOutputs = [new SelectBinding("as1", "result")],
        };

        CompiledNodeGraph result = new NodeGraphCompiler(graph, NodeFixtures.Postgres).Compile();
        string sql = result.SelectExprs[0].Expr.Emit(NodeFixtures.Postgres);

        Assert.Contains("AS \"email_custom\"", sql);
    }

    [Fact]
    public void Compile_BetweenFilter_EmitsBetweenClause()
    {
        var tableNode = new NodeInstance(
            "tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            TableFullName: "orders"
        );

        var betweenNode = new NodeInstance(
            "bt",
            NodeType.Between,
            new Dictionary<string, string> { ["low"] = "100", ["high"] = "999" },
            new Dictionary<string, string>()
        );

        var graph = new NodeGraph
        {
            Nodes = [tableNode, betweenNode],
            Connections = [new Connection("tbl", "total", "bt", "value")],
            WhereConditions = [new WhereBinding("bt", "result")],
        };

        var compiler = new NodeGraphCompiler(graph, NodeFixtures.Postgres);
        CompiledNodeGraph compiled = compiler.Compile();

        Assert.Single(compiled.WhereExprs);
        string sql = compiled.WhereExprs[0].Emit(NodeFixtures.Postgres);
        Assert.Contains("BETWEEN", sql);
        Assert.Contains("100", sql);
        Assert.Contains("999", sql);
    }

    [Fact]
    public void Compile_AndGate_CombinesTwoConditions()
    {
        var tbl = new NodeInstance(
            "tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            TableFullName: "orders"
        );

        var gt = new NodeInstance(
            "gt",
            NodeType.GreaterThan,
            new Dictionary<string, string> { ["right"] = "0" },
            new Dictionary<string, string>()
        );

        var isnn = new NodeInstance(
            "isnn",
            NodeType.IsNotNull,
            new Dictionary<string, string>(),
            new Dictionary<string, string>()
        );

        var and = new NodeInstance(
            "and",
            NodeType.And,
            new Dictionary<string, string>(),
            new Dictionary<string, string>()
        );

        var graph = new NodeGraph
        {
            Nodes = [tbl, gt, isnn, and],
            Connections =
            [
                new Connection("tbl", "total", "gt", "left"),
                new Connection("tbl", "status", "isnn", "value"),
                new Connection("gt", "result", "and", "conditions"),
                new Connection("isnn", "result", "and", "conditions"),
            ],
            WhereConditions = [new WhereBinding("and", "result")],
        };

        CompiledNodeGraph compiled = new NodeGraphCompiler(graph, NodeFixtures.Postgres).Compile();
        string sql = compiled.WhereExprs[0].Emit(NodeFixtures.Postgres);
        Assert.Contains("AND", sql);
        Assert.Contains("IS NOT NULL", sql);
    }

    [Fact]
    public void Compile_JsonExtract_ProducesArrowOp_Postgres()
    {
        var tbl = new NodeInstance(
            "tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            TableFullName: "events"
        );

        var json = new NodeInstance(
            "je",
            NodeType.JsonExtract,
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["path"] = "$.city", ["outputType"] = "Text" },
            Alias: "City"
        );

        var graph = new NodeGraph
        {
            Nodes = [tbl, json],
            Connections = [new Connection("tbl", "payload", "je", "json")],
            SelectOutputs = [new SelectBinding("je", "value", "City")],
        };

        CompiledNodeGraph compiled = new NodeGraphCompiler(graph, NodeFixtures.Postgres).Compile();
        string sql = compiled.SelectExprs[0].Expr.Emit(NodeFixtures.Postgres);

        // Postgres JSON_EXTRACT → ->> operator
        Assert.Contains("->>", sql);
    }

    [Fact]
    public void Compile_CteSource_EmitsAliasedColumnReference()
    {
        var cte = new NodeInstance(
            "cte",
            NodeType.CteSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["cte_name"] = "processos_elegiveis",
                ["alias"] = "pe",
            }
        );

        var graph = new NodeGraph
        {
            Nodes = [cte],
            SelectOutputs = [new SelectBinding("cte", "id_processo", "id_processo")],
        };

        CompiledNodeGraph compiled = new NodeGraphCompiler(graph, NodeFixtures.Postgres).Compile();
        string sql = compiled.SelectExprs[0].Expr.Emit(NodeFixtures.Postgres);

        Assert.Equal("\"pe\".\"id_processo\"", sql);
    }

    [Fact]
    public void Compile_CteSource_ResolvesNameFromConnectedCteDefinition()
    {
        var cteDef = new NodeInstance(
            "cte_def",
            NodeType.CteDefinition,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["name"] = "processos_elegiveis",
                ["source_table"] = "orders",
            }
        );

        var cteSource = new NodeInstance(
            "cte_src",
            NodeType.CteSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["alias"] = "pe",
            }
        );

        var graph = new NodeGraph
        {
            Nodes = [cteDef, cteSource],
            Connections = [new Connection("cte_def", "table", "cte_src", "cte")],
            SelectOutputs = [new SelectBinding("cte_src", "id_processo", "id_processo")],
        };

        CompiledNodeGraph compiled = new NodeGraphCompiler(graph, NodeFixtures.Postgres).Compile();
        string sql = compiled.SelectExprs[0].Expr.Emit(NodeFixtures.Postgres);

        Assert.Equal("\"pe\".\"id_processo\"", sql);
    }

    [Fact]
    public void Compile_CteSource_UsesAliasTextInputWhenConnected()
    {
        var aliasInput = new NodeInstance(
            "txt_alias",
            NodeType.ValueString,
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["value"] = "cte_alias_input" }
        );

        var cteSource = new NodeInstance(
            "cte_src",
            NodeType.CteSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["cte_name"] = "processos_elegiveis",
                ["alias"] = "cte_alias_param",
            }
        );

        var graph = new NodeGraph
        {
            Nodes = [aliasInput, cteSource],
            Connections = [new Connection("txt_alias", "result", "cte_src", "alias_text")],
            SelectOutputs = [new SelectBinding("cte_src", "id_processo", "id_processo")],
        };

        CompiledNodeGraph compiled = new NodeGraphCompiler(graph, NodeFixtures.Postgres).Compile();
        string sql = compiled.SelectExprs[0].Expr.Emit(NodeFixtures.Postgres);

        Assert.Equal("\"cte_alias_input\".\"id_processo\"", sql);
    }

    [Fact]
    public void Compile_CteSource_UsesCteNameTextInputWhenConnected()
    {
        var cteNameInput = new NodeInstance(
            "txt_cte_name",
            NodeType.ValueString,
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["value"] = "cte_name_from_input" }
        );

        var cteSource = new NodeInstance(
            "cte_src",
            NodeType.CteSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["cte_name"] = "cte_name_from_param",
            }
        );

        var graph = new NodeGraph
        {
            Nodes = [cteNameInput, cteSource],
            Connections = [new Connection("txt_cte_name", "result", "cte_src", "cte_name_text")],
            SelectOutputs = [new SelectBinding("cte_src", "id_processo", "id_processo")],
        };

        CompiledNodeGraph compiled = new NodeGraphCompiler(graph, NodeFixtures.Postgres).Compile();
        string sql = compiled.SelectExprs[0].Expr.Emit(NodeFixtures.Postgres);

        Assert.Equal("\"cte_name_from_input\".\"id_processo\"", sql);
    }

    [Fact]
    public void Compile_CteSource_UsesNormalizedAliasTextPinLiteralWithEscapedDoubleQuote()
    {
        var cteSource = new NodeInstance(
            "cte_src",
            NodeType.CteSource,
            new Dictionary<string, string> { ["alias_text"] = "\"foo\\\"bar\"" },
            new Dictionary<string, string> { ["cte_name"] = "processos_elegiveis" }
        );

        var graph = new NodeGraph
        {
            Nodes = [cteSource],
            SelectOutputs = [new SelectBinding("cte_src", "id_processo", "id_processo")],
        };

        CompiledNodeGraph compiled = new NodeGraphCompiler(graph, NodeFixtures.Postgres).Compile();
        string sql = compiled.SelectExprs[0].Expr.Emit(NodeFixtures.Postgres);

        Assert.Equal("\"foo\"\"bar\".\"id_processo\"", sql);
    }

    [Fact]
    public void Compile_CteSource_ResolvesConnectedCteDefinitionNameTextPinLiteralWithEscapedDoubleQuote()
    {
        var cteDef = new NodeInstance(
            "cte_def",
            NodeType.CteDefinition,
            new Dictionary<string, string> { ["name_text"] = "\"cte\\\"name\"" },
            new Dictionary<string, string> { ["source_table"] = "orders" }
        );

        var cteSource = new NodeInstance(
            "cte_src",
            NodeType.CteSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>()
        );

        var graph = new NodeGraph
        {
            Nodes = [cteDef, cteSource],
            Connections = [new Connection("cte_def", "table", "cte_src", "cte")],
            SelectOutputs = [new SelectBinding("cte_src", "id_processo", "id_processo")],
        };

        CompiledNodeGraph compiled = new NodeGraphCompiler(graph, NodeFixtures.Postgres).Compile();
        string sql = compiled.SelectExprs[0].Expr.Emit(NodeFixtures.Postgres);

        Assert.Equal("\"cte\"\"name\".\"id_processo\"", sql);
    }

    [Fact]
    public void Compile_CteSource_ResolvesConnectedCteDefinitionNameTextInput()
    {
        var cteNameInput = new NodeInstance(
            "txt_cte_def_name",
            NodeType.ValueString,
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["value"] = "cte_def_name_input" }
        );

        var cteDef = new NodeInstance(
            "cte_def",
            NodeType.CteDefinition,
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["source_table"] = "orders" }
        );

        var cteSource = new NodeInstance(
            "cte_src",
            NodeType.CteSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>()
        );

        var graph = new NodeGraph
        {
            Nodes = [cteNameInput, cteDef, cteSource],
            Connections =
            [
                new Connection("txt_cte_def_name", "result", "cte_def", "name_text"),
                new Connection("cte_def", "table", "cte_src", "cte"),
            ],
            SelectOutputs = [new SelectBinding("cte_src", "id_processo", "id_processo")],
        };

        CompiledNodeGraph compiled = new NodeGraphCompiler(graph, NodeFixtures.Postgres).Compile();
        string sql = compiled.SelectExprs[0].Expr.Emit(NodeFixtures.Postgres);

        Assert.Equal("\"cte_def_name_input\".\"id_processo\"", sql);
    }

    [Fact]
    public void Compile_CteSource_WithoutResolvableName_ThrowsExplicitError()
    {
        var cteSource = new NodeInstance(
            "cte_src",
            NodeType.CteSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>()
        );

        var graph = new NodeGraph
        {
            Nodes = [cteSource],
            SelectOutputs = [new SelectBinding("cte_src", "id_processo", "id_processo")],
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new NodeGraphCompiler(graph, NodeFixtures.Postgres).Compile()
        );

        Assert.Contains("could not resolve a CTE name", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DatabaseProvider.Postgres, "TRUE")]
    [InlineData(DatabaseProvider.SqlServer, "1 = 1")]
    [InlineData(DatabaseProvider.MySql, "1 = 1")]
    [InlineData(DatabaseProvider.SQLite, "1 = 1")]
    public void Compile_CompileWhereWithoutConditions_UsesProviderAwareTruePredicate(
        DatabaseProvider provider,
        string expectedSql
    )
    {
        var tableNode = new NodeInstance(
            "tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            TableFullName: "public.orders"
        );

        var compileWhereNode = new NodeInstance(
            "cw",
            NodeType.CompileWhere,
            new Dictionary<string, string>(),
            new Dictionary<string, string>()
        );

        var graph = new NodeGraph
        {
            Nodes = [tableNode, compileWhereNode],
            SelectOutputs = [new SelectBinding("tbl", "id", "id")],
            WhereConditions = [new WhereBinding("cw", "result")],
        };

        EmitContext ctx = NodeFixtures.Ctx(provider);
        CompiledNodeGraph compiled = new NodeGraphCompiler(graph, ctx).Compile();
        string sql = compiled.WhereExprs[0].Emit(ctx);

        Assert.Equal(expectedSql, sql);
    }

    [Fact]
    public void Compile_WindowFunction_RowNumber_EmitsOverClause()
    {
        var tbl = new NodeInstance(
            "tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            TableFullName: "orders"
        );

        var wf = new NodeInstance(
            "wf",
            NodeType.WindowFunction,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["function"] = "RowNumber",
                ["order_1_desc"] = "false",
            },
            Alias: "row_num"
        );

        var graph = new NodeGraph
        {
            Nodes = [tbl, wf],
            Connections =
            [
                new Connection("tbl", "customer_id", "wf", "partition_1"),
                new Connection("tbl", "created_at", "wf", "order_1"),
            ],
            SelectOutputs = [new SelectBinding("wf", "result", "row_num")],
        };

        CompiledNodeGraph compiled = new NodeGraphCompiler(graph, NodeFixtures.Postgres).Compile();
        string sql = compiled.SelectExprs[0].Expr.Emit(NodeFixtures.Postgres).ToUpperInvariant();

        Assert.Contains("ROW_NUMBER() OVER", sql);
        Assert.Contains("PARTITION BY", sql);
        Assert.Contains("ORDER BY", sql);
    }

    [Fact]
    public void Compile_WindowFunction_Rank_EmitsRankOverClause()
    {
        var tbl = new NodeInstance(
            "tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            TableFullName: "orders"
        );

        var wf = new NodeInstance(
            "wf",
            NodeType.WindowFunction,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["function"] = "Rank",
                ["order_1_desc"] = "true",
            },
            Alias: "rank_value"
        );

        var graph = new NodeGraph
        {
            Nodes = [tbl, wf],
            Connections = [new Connection("tbl", "created_at", "wf", "order_1")],
            SelectOutputs = [new SelectBinding("wf", "result", "rank_value")],
        };

        CompiledNodeGraph compiled = new NodeGraphCompiler(graph, NodeFixtures.Postgres).Compile();
        string sql = compiled.SelectExprs[0].Expr.Emit(NodeFixtures.Postgres).ToUpperInvariant();

        Assert.Contains("RANK() OVER", sql);
        Assert.Contains("ORDER BY", sql);
        Assert.Contains("DESC", sql);
    }

    [Fact]
    public void Compile_WindowFunction_Lag_UsesValueOffsetAndDefault()
    {
        var tbl = new NodeInstance(
            "tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            TableFullName: "orders"
        );

        var wf = new NodeInstance(
            "wf",
            NodeType.WindowFunction,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["function"] = "Lag",
                ["offset"] = "2",
                ["default_value"] = "0",
            },
            Alias: "prev_total"
        );

        var graph = new NodeGraph
        {
            Nodes = [tbl, wf],
            Connections =
            [
                new Connection("tbl", "total", "wf", "value"),
                new Connection("tbl", "created_at", "wf", "order_1"),
            ],
            SelectOutputs = [new SelectBinding("wf", "result", "prev_total")],
        };

        CompiledNodeGraph compiled = new NodeGraphCompiler(graph, NodeFixtures.Postgres).Compile();
        string sql = compiled.SelectExprs[0].Expr.Emit(NodeFixtures.Postgres).ToUpperInvariant();

        Assert.Contains("LAG(", sql);
        Assert.Contains(", 2", sql);
        Assert.Contains(", 0)", sql);
        Assert.Contains("OVER", sql);
    }

    [Fact]
    public void Compile_WindowFunction_FirstValue_EmitsFirstValueOver()
    {
        var tbl = new NodeInstance(
            "tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            TableFullName: "orders"
        );

        var wf = new NodeInstance(
            "wf",
            NodeType.WindowFunction,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["function"] = "FirstValue",
                ["frame"] = "UnboundedPreceding_CurrentRow",
            },
            Alias: "first_total"
        );

        var graph = new NodeGraph
        {
            Nodes = [tbl, wf],
            Connections =
            [
                new Connection("tbl", "total", "wf", "value"),
                new Connection("tbl", "created_at", "wf", "order_1"),
            ],
            SelectOutputs = [new SelectBinding("wf", "result", "first_total")],
        };

        CompiledNodeGraph compiled = new NodeGraphCompiler(graph, NodeFixtures.Postgres).Compile();
        string sql = compiled.SelectExprs[0].Expr.Emit(NodeFixtures.Postgres).ToUpperInvariant();

        Assert.Contains("FIRST_VALUE(", sql);
        Assert.Contains("OVER", sql);
        Assert.Contains("ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW", sql);
    }

    [Fact]
    public void Compile_WindowFunction_VariadicPartitionAndOrderPins_AreCollectedByPrefix()
    {
        var tbl = new NodeInstance(
            "tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            TableFullName: "orders"
        );

        var wf = new NodeInstance(
            "wf",
            NodeType.WindowFunction,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["function"] = "RowNumber",
                ["order_1_desc"] = "true",
                ["order_2_desc"] = "false",
            },
            Alias: "rn"
        );

        var graph = new NodeGraph
        {
            Nodes = [tbl, wf],
            Connections =
            [
                new Connection("tbl", "customer_id", "wf", "partition_1"),
                new Connection("tbl", "status", "wf", "partition_1"),
                new Connection("tbl", "created_at", "wf", "order_1"),
                new Connection("tbl", "id", "wf", "order_2"),
            ],
            SelectOutputs = [new SelectBinding("wf", "result", "rn")],
        };

        CompiledNodeGraph compiled = new NodeGraphCompiler(graph, NodeFixtures.Postgres).Compile();
        string sql = compiled.SelectExprs[0].Expr.Emit(NodeFixtures.Postgres).ToUpperInvariant();

        Assert.Contains("PARTITION BY", sql);
        Assert.Contains("\"CUSTOMER_ID\"", sql);
        Assert.Contains("\"STATUS\"", sql);
        Assert.Contains("ORDER BY", sql);
        Assert.Contains("\"CREATED_AT\" DESC", sql);
        Assert.Contains("\"ID\"", sql);
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// QUERY GENERATOR SERVICE (end-to-end)
// ═════════════════════════════════════════════════════════════════════════════

public class QueryGeneratorServiceTests
{
    [Fact]
    public void Generate_EmptyGraph_ProducesSelectStar()
    {
        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        var graph = new NodeGraph();
        GeneratedQuery result = GenerateGraphFirst(svc, "users", graph);
        Assert.Contains("SELECT", result.Sql.ToUpper());
        Assert.Contains("users", result.Sql);
    }

    [Fact]
    public void Generate_EmptyGraph_SQLite_ProducesSelectStar()
    {
        var svc = QueryGeneratorService.Create(DatabaseProvider.SQLite);
        var graph = new NodeGraph();
        GeneratedQuery result = GenerateGraphFirst(svc, "users", graph);
        Assert.Contains("SELECT", result.Sql.ToUpperInvariant());
        Assert.Contains("users", result.Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_WithCast_ProducesCorrectSQL()
    {
        var tbl = new NodeInstance(
            "tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            TableFullName: "orders"
        );

        var cast = new NodeInstance(
            "cast1",
            NodeType.Cast,
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["targetType"] = "Text" },
            Alias: "TotalText"
        );

        var graph = new NodeGraph
        {
            Nodes = [tbl, cast],
            Connections = [new Connection("tbl", "total", "cast1", "value")],
            SelectOutputs = [new SelectBinding("cast1", "result", "TotalText")],
        };

        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        GeneratedQuery result = GenerateGraphFirst(svc, "orders", graph);

        Assert.Contains("CAST", result.Sql);
        Assert.Contains("TEXT", result.Sql);
    }

    [Fact]
    public void Generate_DebugTree_ContainsSelectSection()
    {
        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        GeneratedQuery result = GenerateGraphFirst(svc, "users", new NodeGraph());
        Assert.Contains("SELECT", result.DebugTree);
    }

    [Fact]
    public void Generate_DistinctFlag_ProducesSelectDistinct()
    {
        var tbl = new NodeInstance(
            "tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            TableFullName: "orders"
        );

        var graph = new NodeGraph
        {
            Nodes = [tbl],
            SelectOutputs = [new SelectBinding("tbl", "status")],
            Distinct = true,
        };

        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        GeneratedQuery result = GenerateGraphFirst(svc, "orders", graph);

        Assert.Contains("SELECT DISTINCT", result.Sql.ToUpperInvariant());
    }

    [Fact]
    public void Generate_HavingClause_EmitsHaving()
    {
        var tbl = new NodeInstance(
            "tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            TableFullName: "orders"
        );

        var sum = new NodeInstance(
            "sum",
            NodeType.Sum,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            Alias: "total_sum"
        );

        var gt = new NodeInstance(
            "gt",
            NodeType.GreaterThan,
            new Dictionary<string, string> { ["right"] = "100" },
            new Dictionary<string, string>()
        );

        var graph = new NodeGraph
        {
            Nodes = [tbl, sum, gt],
            Connections =
            [
                new Connection("tbl", "total", "sum", "value"),
                new Connection("sum", "total", "gt", "left"),
            ],
            SelectOutputs = [new SelectBinding("sum", "total", "total_sum")],
            GroupBys = [new GroupByBinding("tbl", "status")],
            Havings = [new HavingBinding("gt", "result")],
        };

        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        GeneratedQuery result = GenerateGraphFirst(svc, "orders", graph);

        Assert.Contains("HAVING", result.Sql.ToUpperInvariant());
        Assert.Contains(">", result.Sql);
        Assert.Contains("100", result.Sql);
    }

    [Fact]
    public void Generate_HavingWithNonGroupedColumn_ThrowsExplicitError()
    {
        var tbl = new NodeInstance(
            "tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            TableFullName: "orders"
        );

        var gt = new NodeInstance(
            "gt",
            NodeType.GreaterThan,
            new Dictionary<string, string> { ["right"] = "100" },
            new Dictionary<string, string>()
        );

        var graph = new NodeGraph
        {
            Nodes = [tbl, gt],
            Connections = [new Connection("tbl", "total", "gt", "left")],
            SelectOutputs = [new SelectBinding("tbl", "status")],
            GroupBys = [new GroupByBinding("tbl", "status")],
            Havings = [new HavingBinding("gt", "result")],
        };

        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            GenerateGraphFirst(svc, "orders", graph)
        );

        Assert.Contains("non-aggregated columns not present in GROUP BY", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_OrderByAscending_Postgres_EmitsExplicitNullsLast()
    {
        var tbl = new NodeInstance(
            "tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            TableFullName: "orders"
        );

        var graph = new NodeGraph
        {
            Nodes = [tbl],
            SelectOutputs = [new SelectBinding("tbl", "id")],
            OrderBys = [new OrderBinding("tbl", "created_at", Descending: false)],
        };

        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        GeneratedQuery result = GenerateGraphFirst(svc, "orders", graph);

        Assert.Contains("ORDER BY", result.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NULLS LAST", result.Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_OrderByAscending_SqlServer_EmitsCaseNullOrderingFallback()
    {
        var tbl = new NodeInstance(
            "tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            TableFullName: "orders"
        );

        var graph = new NodeGraph
        {
            Nodes = [tbl],
            SelectOutputs = [new SelectBinding("tbl", "id")],
            OrderBys = [new OrderBinding("tbl", "created_at", Descending: false)],
        };

        var svc = QueryGeneratorService.Create(DatabaseProvider.SqlServer);
        GeneratedQuery result = GenerateGraphFirst(svc, "orders", graph);

        Assert.Contains("CASE WHEN", result.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IS NULL", result.Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_ComparisonWithTextLiteral_QuotesLiteralInSql()
    {
        var tbl = new NodeInstance(
            "tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            TableFullName: "orders"
        );

        var eq = new NodeInstance(
            "eq",
            NodeType.Equals,
            new Dictionary<string, string> { ["right"] = "COMPLETED" },
            new Dictionary<string, string>()
        );

        var graph = new NodeGraph
        {
            Nodes = [tbl, eq],
            Connections = [new Connection("tbl", "status", "eq", "left")],
            SelectOutputs = [new SelectBinding("tbl", "id")],
            WhereConditions = [new WhereBinding("eq", "result")],
        };

        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        GeneratedQuery result = GenerateGraphFirst(svc, "orders", graph);

        Assert.Contains("'COMPLETED'", result.Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DatabaseProvider.Postgres, "WHERE TRUE")]
    [InlineData(DatabaseProvider.SqlServer, "WHERE 1 = 1")]
    [InlineData(DatabaseProvider.MySql, "WHERE 1 = 1")]
    [InlineData(DatabaseProvider.SQLite, "WHERE 1 = 1")]
    public void Generate_CompileWhereWithoutConditions_NormalizesTruePredicateByProvider(
        DatabaseProvider provider,
        string expectedWhereSql
    )
    {
        var tableNode = new NodeInstance(
            "tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            TableFullName: "orders"
        );

        var compileWhereNode = new NodeInstance(
            "cw",
            NodeType.CompileWhere,
            new Dictionary<string, string>(),
            new Dictionary<string, string>()
        );

        var graph = new NodeGraph
        {
            Nodes = [tableNode, compileWhereNode],
            SelectOutputs = [new SelectBinding("tbl", "id")],
            WhereConditions = [new WhereBinding("cw", "result")],
        };

        var svc = QueryGeneratorService.Create(provider);
        GeneratedQuery result = GenerateGraphFirst(svc, "orders", graph);

        Assert.Contains(expectedWhereSql, result.Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_AggregatesWithoutExplicitGroupBy_ThrowsWhenNonAggregateSelectIsPresent()
    {
        var tbl = new NodeInstance(
            "tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            TableFullName: "orders"
        );

        var count = new NodeInstance(
            "count",
            NodeType.CountStar,
            new Dictionary<string, string>(),
            new Dictionary<string, string>()
        );

        var sum = new NodeInstance(
            "sum",
            NodeType.Sum,
            new Dictionary<string, string>(),
            new Dictionary<string, string>()
        );

        var graph = new NodeGraph
        {
            Nodes = [tbl, count, sum],
            Connections = [new Connection("tbl", "total", "sum", "value")],
            SelectOutputs =
            [
                new SelectBinding("tbl", "status"),
                new SelectBinding("count", "count", "order_count"),
                new SelectBinding("sum", "total", "total_revenue"),
            ],
        };

        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => GenerateGraphFirst(svc, "orders", graph));
        Assert.Contains("explicit GROUP BY", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_CountStarWithoutExplicitGroupBy_RemainsValid()
    {
        var count = new NodeInstance(
            "count",
            NodeType.CountStar,
            new Dictionary<string, string>(),
            new Dictionary<string, string>()
        );

        var graph = new NodeGraph
        {
            Nodes = [count],
            SelectOutputs = [new SelectBinding("count", "count", "total")],
        };

        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        GeneratedQuery result = GenerateGraphFirst(svc, "orders", graph);

        Assert.Contains("COUNT(*)", result.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GROUP BY", result.Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_FromTableEmpty_UsesPlannerDerivedTableSource()
    {
        var table = new NodeInstance(
            "tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            Alias: "orders",
            TableFullName: "public.orders",
            ColumnPins: new Dictionary<string, string> { ["id"] = "id" },
            ColumnPinTypes: new Dictionary<string, PinDataType> { ["id"] = PinDataType.Integer }
        );

        var output = new NodeInstance(
            "out",
            NodeType.ResultOutput,
            new Dictionary<string, string>(),
            new Dictionary<string, string>());

        var graph = new NodeGraph
        {
            Nodes = [table, output],
            Connections = [new Connection("tbl", "id", "out", "column")],
        };

        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        GeneratedQuery result = GenerateGraphFirst(svc, string.Empty, graph);

        Assert.Contains("FROM", result.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("public", result.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("orders", result.Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_JoinNodeInGraphWithoutExternalJoins_UsesPlannerDerivedJoin()
    {
        var left = new NodeInstance(
            "orders_tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            Alias: "orders",
            TableFullName: "public.orders",
            ColumnPins: new Dictionary<string, string>
            {
                ["id"] = "id",
                ["customer_id"] = "customer_id",
            },
            ColumnPinTypes: new Dictionary<string, PinDataType>
            {
                ["id"] = PinDataType.Integer,
                ["customer_id"] = PinDataType.Integer,
            }
        );

        var right = new NodeInstance(
            "customers_tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            Alias: "customers",
            TableFullName: "public.customers",
            ColumnPins: new Dictionary<string, string>
            {
                ["id"] = "id",
                ["name"] = "name",
            },
            ColumnPinTypes: new Dictionary<string, PinDataType>
            {
                ["id"] = PinDataType.Integer,
                ["name"] = PinDataType.Text,
            }
        );

        var eq = new NodeInstance(
            "eq",
            NodeType.Equals,
            new Dictionary<string, string>(),
            new Dictionary<string, string>());
        var join = new NodeInstance(
            "join",
            NodeType.Join,
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["join_type"] = "INNER" });
        var output = new NodeInstance(
            "out",
            NodeType.ResultOutput,
            new Dictionary<string, string>(),
            new Dictionary<string, string>());

        var graph = new NodeGraph
        {
            Nodes = [left, right, eq, join, output],
            Connections =
            [
                new Connection("orders_tbl", "customer_id", "eq", "left"),
                new Connection("customers_tbl", "id", "eq", "right"),
                new Connection("eq", "result", "join", "condition"),
                new Connection("orders_tbl", "customer_id", "join", "left"),
                new Connection("customers_tbl", "id", "join", "right"),
                new Connection("orders_tbl", "id", "out", "column"),
                new Connection("customers_tbl", "name", "out", "column"),
            ],
        };

        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        GeneratedQuery result = GenerateGraphFirst(svc, "public.orders", graph);

        Assert.Contains("JOIN", result.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("customers", result.Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_SystemDateNode_EmitsProviderCurrentTimestamp()
    {
        var nowNode = new NodeInstance(
            "now",
            NodeType.SystemDate,
            new Dictionary<string, string>(),
            new Dictionary<string, string>()
        );

        var graph = new NodeGraph
        {
            Nodes = [nowNode],
            SelectOutputs = [new SelectBinding("now", "result", "now_value")],
        };

        var svcPg = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        GeneratedQuery pgSql = GenerateGraphFirst(svcPg, "orders", graph);
        Assert.Contains("CURRENT_TIMESTAMP", pgSql.Sql.ToUpperInvariant());

        var svcMy = QueryGeneratorService.Create(DatabaseProvider.MySql);
        GeneratedQuery mySql = GenerateGraphFirst(svcMy, "orders", graph);
        Assert.Contains("NOW()", mySql.Sql.ToUpperInvariant());

        var svcSs = QueryGeneratorService.Create(DatabaseProvider.SqlServer);
        GeneratedQuery ssSql = GenerateGraphFirst(svcSs, "orders", graph);
        Assert.Contains("GETDATE()", ssSql.Sql.ToUpperInvariant());
    }

    [Fact]
    public void Generate_DateAddNode_EmitsProviderSpecificSql()
    {
        var nowNode = new NodeInstance(
            "now",
            NodeType.SystemDate,
            new Dictionary<string, string>(),
            new Dictionary<string, string>()
        );

        var addNode = new NodeInstance(
            "dateadd",
            NodeType.DateAdd,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["amount"] = "7",
                ["unit"] = "day",
            },
            Alias: "next_week"
        );

        var graph = new NodeGraph
        {
            Nodes = [nowNode, addNode],
            Connections = [new Connection("now", "result", "dateadd", "date")],
            SelectOutputs = [new SelectBinding("dateadd", "result", "next_week")],
        };

        var svcPg = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        GeneratedQuery pgSql = GenerateGraphFirst(svcPg, "orders", graph);
        Assert.Contains("INTERVAL", pgSql.Sql.ToUpperInvariant());

        var svcMy = QueryGeneratorService.Create(DatabaseProvider.MySql);
        GeneratedQuery mySql = GenerateGraphFirst(svcMy, "orders", graph);
        Assert.Contains("DATE_ADD", mySql.Sql.ToUpperInvariant());

        var svcSs = QueryGeneratorService.Create(DatabaseProvider.SqlServer);
        GeneratedQuery ssSql = GenerateGraphFirst(svcSs, "orders", graph);
        Assert.Contains("DATEADD", ssSql.Sql.ToUpperInvariant());
    }

    [Fact]
    public void Generate_DateDiffDatePartAndFormat_EmitDialectFunctions()
    {
        var nowNode = new NodeInstance(
            "now",
            NodeType.SystemDate,
            new Dictionary<string, string>(),
            new Dictionary<string, string>()
        );

        var dateOnlyNode = new NodeInstance(
            "today",
            NodeType.CurrentDate,
            new Dictionary<string, string>(),
            new Dictionary<string, string>()
        );

        var diffNode = new NodeInstance(
            "datediff",
            NodeType.DateDiff,
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["unit"] = "day" },
            Alias: "days_diff"
        );

        var partNode = new NodeInstance(
            "datepart",
            NodeType.DatePart,
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["part"] = "month" },
            Alias: "month_num"
        );

        var formatNode = new NodeInstance(
            "datefmt",
            NodeType.DateFormat,
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["format"] = "yyyy-MM-dd" },
            Alias: "date_text"
        );

        var graph = new NodeGraph
        {
            Nodes = [nowNode, dateOnlyNode, diffNode, partNode, formatNode],
            Connections =
            [
                new Connection("today", "result", "datediff", "start"),
                new Connection("now", "result", "datediff", "end"),
                new Connection("now", "result", "datepart", "value"),
                new Connection("today", "result", "datefmt", "value"),
            ],
            SelectOutputs =
            [
                new SelectBinding("datediff", "result", "days_diff"),
                new SelectBinding("datepart", "result", "month_num"),
                new SelectBinding("datefmt", "result", "date_text"),
            ],
        };

        var svcPg = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        GeneratedQuery pgSql = GenerateGraphFirst(svcPg, "orders", graph);
        Assert.Contains("EXTRACT", pgSql.Sql.ToUpperInvariant());
        Assert.Contains("TO_CHAR", pgSql.Sql.ToUpperInvariant());

        var svcMy = QueryGeneratorService.Create(DatabaseProvider.MySql);
        GeneratedQuery mySql = GenerateGraphFirst(svcMy, "orders", graph);
        Assert.Contains("TIMESTAMPDIFF", mySql.Sql.ToUpperInvariant());
        Assert.Contains("DATE_FORMAT", mySql.Sql.ToUpperInvariant());

        var svcSs = QueryGeneratorService.Create(DatabaseProvider.SqlServer);
        GeneratedQuery ssSql = GenerateGraphFirst(svcSs, "orders", graph);
        Assert.Contains("DATEDIFF", ssSql.Sql.ToUpperInvariant());
        Assert.Contains("DATEPART", ssSql.Sql.ToUpperInvariant());
        Assert.Contains("FORMAT", ssSql.Sql.ToUpperInvariant());
    }

    [Fact]
    public void Generate_StringAgg_WithDistinctAndOrderBy_EmitsAggregate()
    {
        var tbl = new NodeInstance(
            "tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            TableFullName: "orders"
        );

        var agg = new NodeInstance(
            "agg",
            NodeType.StringAgg,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["separator"] = ", ",
                ["distinct"] = "true",
            },
            Alias: "statuses"
        );

        var graph = new NodeGraph
        {
            Nodes = [tbl, agg],
            Connections =
            [
                new Connection("tbl", "status", "agg", "value"),
                new Connection("tbl", "created_at", "agg", "order_by"),
            ],
            SelectOutputs = [new SelectBinding("agg", "result", "statuses")],
            GroupBys = [new GroupByBinding("tbl", "customer_id")],
        };

        var svcPg = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        GeneratedQuery pgSql = GenerateGraphFirst(svcPg, "orders", graph);
        Assert.Contains("STRING_AGG", pgSql.Sql.ToUpperInvariant());
        Assert.Contains("DISTINCT", pgSql.Sql.ToUpperInvariant());
        Assert.Contains("ORDER BY", pgSql.Sql.ToUpperInvariant());

        var svcMy = QueryGeneratorService.Create(DatabaseProvider.MySql);
        GeneratedQuery mySql = GenerateGraphFirst(svcMy, "orders", graph);
        Assert.Contains("GROUP_CONCAT", mySql.Sql.ToUpperInvariant());
        Assert.Contains("DISTINCT", mySql.Sql.ToUpperInvariant());
        Assert.Contains("ORDER BY", mySql.Sql.ToUpperInvariant());
    }

    [Fact]
    public void Generate_WindowFunction_RowNumber_ProducesSql()
    {
        var tbl = new NodeInstance(
            "tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            TableFullName: "orders"
        );

        var wf = new NodeInstance(
            "wf",
            NodeType.WindowFunction,
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["function"] = "RowNumber" },
            Alias: "rn"
        );

        var graph = new NodeGraph
        {
            Nodes = [tbl, wf],
            Connections =
            [
                new Connection("tbl", "customer_id", "wf", "partition_1"),
                new Connection("tbl", "created_at", "wf", "order_1"),
            ],
            SelectOutputs = [new SelectBinding("wf", "result", "rn")],
        };

        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        GeneratedQuery result = GenerateGraphFirst(svc, "orders", graph);

        Assert.Contains("ROW_NUMBER() OVER", result.Sql.ToUpperInvariant());
        Assert.Contains("PARTITION BY", result.Sql.ToUpperInvariant());
        Assert.Contains("ORDER BY", result.Sql.ToUpperInvariant());
    }

    [Fact]
    public void Generate_WindowFunction_DenseRank_ProducesSql()
    {
        var tbl = new NodeInstance(
            "tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            TableFullName: "orders"
        );

        var wf = new NodeInstance(
            "wf",
            NodeType.WindowFunction,
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["function"] = "DenseRank" },
            Alias: "dr"
        );

        var graph = new NodeGraph
        {
            Nodes = [tbl, wf],
            Connections = [new Connection("tbl", "created_at", "wf", "order_1")],
            SelectOutputs = [new SelectBinding("wf", "result", "dr")],
        };

        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        GeneratedQuery result = GenerateGraphFirst(svc, "orders", graph);

        Assert.Contains("DENSE_RANK() OVER", result.Sql.ToUpperInvariant());
    }

    [Fact]
    public void Generate_WindowFunction_Lead_ProducesSql()
    {
        var tbl = new NodeInstance(
            "tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            TableFullName: "orders"
        );

        var wf = new NodeInstance(
            "wf",
            NodeType.WindowFunction,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["function"] = "Lead",
                ["offset"] = "1",
                ["default_value"] = "pending",
            },
            Alias: "next_status"
        );

        var graph = new NodeGraph
        {
            Nodes = [tbl, wf],
            Connections =
            [
                new Connection("tbl", "status", "wf", "value"),
                new Connection("tbl", "created_at", "wf", "order_1"),
            ],
            SelectOutputs = [new SelectBinding("wf", "result", "next_status")],
        };

        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        GeneratedQuery result = GenerateGraphFirst(svc, "orders", graph);

        string sql = result.Sql.ToUpperInvariant();
        Assert.Contains("LEAD(", sql);
        Assert.Contains("'PENDING'", sql);
        Assert.Contains("OVER", sql);
    }

    [Fact]
    public void Generate_WindowFunction_Ntile_ProducesSql()
    {
        var tbl = new NodeInstance(
            "tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            TableFullName: "orders"
        );

        var wf = new NodeInstance(
            "wf",
            NodeType.WindowFunction,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["function"] = "Ntile",
                ["ntile_groups"] = "5",
            },
            Alias: "bucket"
        );

        var graph = new NodeGraph
        {
            Nodes = [tbl, wf],
            Connections = [new Connection("tbl", "created_at", "wf", "order_1")],
            SelectOutputs = [new SelectBinding("wf", "result", "bucket")],
        };

        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        GeneratedQuery result = GenerateGraphFirst(svc, "orders", graph);

        string sql = result.Sql.ToUpperInvariant();
        Assert.Contains("NTILE(5) OVER", sql);
        Assert.Contains("ORDER BY", sql);
    }

    [Fact]
    public void Generate_WindowFunction_LastValue_ProducesSql()
    {
        var tbl = new NodeInstance(
            "tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            TableFullName: "orders"
        );

        var wf = new NodeInstance(
            "wf",
            NodeType.WindowFunction,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["function"] = "LastValue",
                ["frame"] = "CurrentRow_UnboundedFollowing",
            },
            Alias: "last_status"
        );

        var graph = new NodeGraph
        {
            Nodes = [tbl, wf],
            Connections =
            [
                new Connection("tbl", "status", "wf", "value"),
                new Connection("tbl", "created_at", "wf", "order_1"),
            ],
            SelectOutputs = [new SelectBinding("wf", "result", "last_status")],
        };

        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        GeneratedQuery result = GenerateGraphFirst(svc, "orders", graph);

        string sql = result.Sql.ToUpperInvariant();
        Assert.Contains("LAST_VALUE(", sql);
        Assert.Contains("ROWS BETWEEN CURRENT ROW AND UNBOUNDED FOLLOWING", sql);
    }

    [Fact]
    public void Generate_CteSourceSelect_ProducesFromCteName()
    {
        var cte = new NodeInstance(
            "cte",
            NodeType.CteSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["cte_name"] = "processos_elegiveis",
                ["alias"] = "pe",
            }
        );

        var graph = new NodeGraph
        {
            Nodes = [cte],
            SelectOutputs = [new SelectBinding("cte", "id_processo", "id_processo")],
        };

        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        GeneratedQuery result = GenerateGraphFirst(svc, "processos_elegiveis", graph);

        Assert.Contains("FROM", result.Sql.ToUpperInvariant());
        Assert.Contains("processos_elegiveis", result.Sql);
        Assert.Contains("\"pe\".\"id_processo\"", result.Sql);
    }

    [Fact]
    public void Generate_WithSingleCte_ProducesWithClauseAndSelectsFromCte()
    {
        var cteTable = new NodeInstance(
            "tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            TableFullName: "orders"
        );

        var cteGraph = new NodeGraph
        {
            Nodes = [cteTable],
            SelectOutputs = [new SelectBinding("tbl", "id", "id")],
        };

        var cteSource = new NodeInstance(
            "cte",
            NodeType.CteSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["cte_name"] = "orders_cte",
                ["alias"] = "oc",
            }
        );

        var mainGraph = new NodeGraph
        {
            Nodes = [cteSource],
            Ctes = [new CteBinding("orders_cte", "orders", cteGraph)],
            SelectOutputs = [new SelectBinding("cte", "id", "id")],
        };

        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        GeneratedQuery result = GenerateGraphFirst(svc, "orders_cte", mainGraph);

        string sql = result.Sql.ToUpperInvariant();
        Assert.Contains("WITH", sql);
        Assert.Contains("ORDERS_CTE", sql);
        Assert.Contains("FROM \"ORDERS\"", sql);
        Assert.Contains("FROM \"ORDERS_CTE\"", sql);
        Assert.Contains("\"OC\".\"ID\"", sql);
    }

    [Fact]
    public void Generate_WithRecursiveCte_Postgres_PrefixesWithRecursive()
    {
        var cteTable = new NodeInstance(
            "tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            TableFullName: "orders"
        );

        var cteGraph = new NodeGraph
        {
            Nodes = [cteTable],
            SelectOutputs = [new SelectBinding("tbl", "id", "id")],
        };

        var cteSource = new NodeInstance(
            "cte",
            NodeType.CteSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["cte_name"] = "orders_cte",
                ["alias"] = "oc",
            }
        );

        var mainGraph = new NodeGraph
        {
            Nodes = [cteSource],
            Ctes = [new CteBinding("orders_cte", "orders", cteGraph, Recursive: true)],
            SelectOutputs = [new SelectBinding("cte", "id", "id")],
        };

        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        GeneratedQuery result = GenerateGraphFirst(svc, "orders_cte", mainGraph);

        Assert.Contains("WITH RECURSIVE", result.Sql.ToUpperInvariant());
    }

    [Fact]
    public void Generate_WithRecursiveCte_SqlServer_DoesNotUseRecursiveKeyword()
    {
        var cteTable = new NodeInstance(
            "tbl",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            TableFullName: "orders"
        );

        var cteGraph = new NodeGraph
        {
            Nodes = [cteTable],
            SelectOutputs = [new SelectBinding("tbl", "id", "id")],
        };

        var cteSource = new NodeInstance(
            "cte",
            NodeType.CteSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["cte_name"] = "orders_cte",
                ["alias"] = "oc",
            }
        );

        var mainGraph = new NodeGraph
        {
            Nodes = [cteSource],
            Ctes = [new CteBinding("orders_cte", "orders", cteGraph, Recursive: true)],
            SelectOutputs = [new SelectBinding("cte", "id", "id")],
        };

        var svc = QueryGeneratorService.Create(DatabaseProvider.SqlServer);
        GeneratedQuery result = GenerateGraphFirst(svc, "orders_cte", mainGraph);

        string sql = result.Sql.ToUpperInvariant();
        Assert.Contains("WITH", sql);
        Assert.DoesNotContain("WITH RECURSIVE", sql);
    }

    [Fact]
    public void Generate_WithDependentCtes_EmitsDependenciesBeforeDependents()
    {
        var cteSourceA = new NodeInstance(
            "a_src",
            NodeType.CteSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["cte_name"] = "cte_a",
                ["alias"] = "a",
            }
        );

        var cteGraphA = new NodeGraph
        {
            Nodes = [],
        };

        var cteGraphB = new NodeGraph
        {
            Nodes = [cteSourceA],
            SelectOutputs = [new SelectBinding("a_src", "id", "id")],
        };

        var mainSource = new NodeInstance(
            "main_src",
            NodeType.CteSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["cte_name"] = "cte_b",
                ["alias"] = "b",
            }
        );

        var mainGraph = new NodeGraph
        {
            Nodes = [mainSource],
            // Intentionally reversed to verify topological ordering.
            Ctes =
            [
                new CteBinding("cte_b", "cte_a", cteGraphB),
                new CteBinding("cte_a", "orders", cteGraphA),
            ],
            SelectOutputs = [new SelectBinding("main_src", "id", "id")],
        };

        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        GeneratedQuery result = GenerateGraphFirst(svc, "cte_b", mainGraph);

        Match aDef = Regex.Match(
            result.Sql,
            "[\\\"`\\[]?cte_a[\\\"`\\]]?\\s+AS\\s*\\(",
            RegexOptions.IgnoreCase
        );
        Match bDef = Regex.Match(
            result.Sql,
            "[\\\"`\\[]?cte_b[\\\"`\\]]?\\s+AS\\s*\\(",
            RegexOptions.IgnoreCase
        );

        Assert.True(aDef.Success, "Expected cte_a definition in SQL.");
        Assert.True(bDef.Success, "Expected cte_b definition in SQL.");
        Assert.True(aDef.Index < bDef.Index, "cte_a must be emitted before cte_b.");
    }

    [Fact]
    public void Generate_WithCteDependencyCycle_ThrowsInvalidOperation()
    {
        var srcA = new NodeInstance(
            "srcA",
            NodeType.CteSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["cte_name"] = "cte_b",
                ["alias"] = "b",
            }
        );

        var srcB = new NodeInstance(
            "srcB",
            NodeType.CteSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["cte_name"] = "cte_a",
                ["alias"] = "a",
            }
        );

        var graphA = new NodeGraph
        {
            Nodes = [srcA],
            SelectOutputs = [new SelectBinding("srcA", "id", "id")],
        };

        var graphB = new NodeGraph
        {
            Nodes = [srcB],
            SelectOutputs = [new SelectBinding("srcB", "id", "id")],
        };

        var mainSource = new NodeInstance(
            "main",
            NodeType.CteSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["cte_name"] = "cte_a",
                ["alias"] = "a",
            }
        );

        var mainGraph = new NodeGraph
        {
            Nodes = [mainSource],
            Ctes =
            [
                new CteBinding("cte_a", "cte_b", graphA),
                new CteBinding("cte_b", "cte_a", graphB),
            ],
            SelectOutputs = [new SelectBinding("main", "id", "id")],
        };

        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => GenerateGraphFirst(svc, "cte_a", mainGraph)
        );
        Assert.Contains("Cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_WithDependentCtes_ByFromTable_EmitsDependenciesBeforeDependents()
    {
        var mainSource = new NodeInstance(
            "main_src",
            NodeType.CteSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["cte_name"] = "cte_b",
                ["alias"] = "b",
            }
        );

        var mainGraph = new NodeGraph
        {
            Nodes = [mainSource],
            // Intentionally reversed order to validate topological emission from FromTable dependencies.
            Ctes =
            [
                new CteBinding("cte_b", "cte_a", new NodeGraph()),
                new CteBinding("cte_a", "orders", new NodeGraph()),
            ],
            SelectOutputs = [new SelectBinding("main_src", "id", "id")],
        };

        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        GeneratedQuery result = GenerateGraphFirst(svc, "cte_b", mainGraph);

        Match aDef = Regex.Match(
            result.Sql,
            "[\\\"`\\[]?cte_a[\\\"`\\]]?\\s+AS\\s*\\(",
            RegexOptions.IgnoreCase
        );
        Match bDef = Regex.Match(
            result.Sql,
            "[\\\"`\\[]?cte_b[\\\"`\\]]?\\s+AS\\s*\\(",
            RegexOptions.IgnoreCase
        );

        Assert.True(aDef.Success, "Expected cte_a definition in SQL.");
        Assert.True(bDef.Success, "Expected cte_b definition in SQL.");
        Assert.True(aDef.Index < bDef.Index, "cte_a must be emitted before cte_b.");
    }

    [Fact]
    public void Generate_WithSelfReferenceByFromTable_WithoutRecursive_ThrowsInvalidOperation()
    {
        var mainSource = new NodeInstance(
            "main_src",
            NodeType.CteSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["cte_name"] = "cte_self",
                ["alias"] = "s",
            }
        );

        var mainGraph = new NodeGraph
        {
            Nodes = [mainSource],
            Ctes = [new CteBinding("cte_self", "cte_self", new NodeGraph(), Recursive: false)],
            SelectOutputs = [new SelectBinding("main_src", "id", "id")],
        };

        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => GenerateGraphFirst(svc, "cte_self", mainGraph)
        );
        Assert.Contains("not marked recursive", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_WithDependentCtes_ByAliasedFromTable_EmitsDependenciesBeforeDependents()
    {
        var mainSource = new NodeInstance(
            "main_src",
            NodeType.CteSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["cte_name"] = "cte_b",
                ["alias"] = "b",
            }
        );

        var mainGraph = new NodeGraph
        {
            Nodes = [mainSource],
            // Intentionally reversed to ensure aliased FROM refs are dependency-aware.
            Ctes =
            [
                new CteBinding("cte_b", "cte_a a", new NodeGraph()),
                new CteBinding("cte_a", "orders", new NodeGraph()),
            ],
            SelectOutputs = [new SelectBinding("main_src", "id", "id")],
        };

        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        GeneratedQuery result = GenerateGraphFirst(svc, "cte_b", mainGraph);

        Match aDef = Regex.Match(
            result.Sql,
            "[\\\"`\\[]?cte_a[\\\"`\\]]?\\s+AS\\s*\\(",
            RegexOptions.IgnoreCase
        );
        Match bDef = Regex.Match(
            result.Sql,
            "[\\\"`\\[]?cte_b[\\\"`\\]]?\\s+AS\\s*\\(",
            RegexOptions.IgnoreCase
        );

        Assert.True(aDef.Success, "Expected cte_a definition in SQL.");
        Assert.True(bDef.Success, "Expected cte_b definition in SQL.");
        Assert.True(aDef.Index < bDef.Index, "cte_a must be emitted before cte_b.");
    }

    [Fact]
    public void Generate_WithSelfReferenceByAliasedFromTable_WithoutRecursive_ThrowsInvalidOperation()
    {
        var mainSource = new NodeInstance(
            "main_src",
            NodeType.CteSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["cte_name"] = "cte_self",
                ["alias"] = "s",
            }
        );

        var mainGraph = new NodeGraph
        {
            Nodes = [mainSource],
            Ctes = [new CteBinding("cte_self", "cte_self s", new NodeGraph(), Recursive: false)],
            SelectOutputs = [new SelectBinding("main_src", "id", "id")],
        };

        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => GenerateGraphFirst(svc, "cte_self", mainGraph)
        );
        Assert.Contains("not marked recursive", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_CompiledGraphOverload_WithSqlServerPivot_AppliesPivotClause()
    {
        var svc = QueryGeneratorService.Create(DatabaseProvider.SqlServer);
        var compiled = new CompiledNodeGraph(
            SelectExprs:
            [
                (new ColumnExpr("dbo.Sales", "Region", PinDataType.Text), (string?)null),
                (new ColumnExpr("dbo.Sales", "Amount", PinDataType.Number), (string?)null),
                (new ColumnExpr("dbo.Sales", "Month", PinDataType.Text), (string?)null),
            ],
            WhereExprs: [],
            HavingExprs: [],
            QualifyExprs: [],
            OrderExprs: [],
            GroupByExprs: [],
            Distinct: false,
            Limit: null,
            Offset: null);

        GeneratedQuery result = svc.Generate(
            fromTable: "dbo.Sales",
            compiled: compiled,
            pivotMode: "PIVOT",
            pivotConfig: "SUM(Amount) FOR Month IN ([Jan],[Feb])");

        Assert.Contains("PIVOT", result.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SUM(Amount)", result.Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_CompiledGraphOverload_WithPostgresPivot_DoesNotApplyPivotClause()
    {
        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        var compiled = new CompiledNodeGraph(
            SelectExprs:
            [
                (new ColumnExpr("public.sales", "region", PinDataType.Text), (string?)null),
                (new ColumnExpr("public.sales", "amount", PinDataType.Number), (string?)null),
                (new ColumnExpr("public.sales", "month", PinDataType.Text), (string?)null),
            ],
            WhereExprs: [],
            HavingExprs: [],
            QualifyExprs: [],
            OrderExprs: [],
            GroupByExprs: [],
            Distinct: false,
            Limit: null,
            Offset: null);

        GeneratedQuery result = svc.Generate(
            fromTable: "public.sales",
            compiled: compiled,
            pivotMode: "PIVOT",
            pivotConfig: "SUM(amount) FOR month IN ('Jan','Feb')");

        Assert.DoesNotContain("PIVOT", result.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UNPIVOT", result.Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_CompiledGraphOverload_WithUnsafeSqlServerPivotConfig_ThrowsExplicitError()
    {
        var svc = QueryGeneratorService.Create(DatabaseProvider.SqlServer);
        var compiled = new CompiledNodeGraph(
            SelectExprs:
            [
                (new ColumnExpr("dbo.Sales", "Region", PinDataType.Text), (string?)null),
                (new ColumnExpr("dbo.Sales", "Amount", PinDataType.Number), (string?)null),
                (new ColumnExpr("dbo.Sales", "Month", PinDataType.Text), (string?)null),
            ],
            WhereExprs: [],
            HavingExprs: [],
            QualifyExprs: [],
            OrderExprs: [],
            GroupByExprs: [],
            Distinct: false,
            Limit: null,
            Offset: null);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            svc.Generate(
                fromTable: "dbo.Sales",
                compiled: compiled,
                pivotMode: "PIVOT",
                pivotConfig: "SUM(Amount) FOR Month IN ([Jan],`x`)"
            )
        );

        Assert.Contains("Unsafe PIVOT/UNPIVOT configuration", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_WithInvalidSetOperator_ThrowsExplicitError()
    {
        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        var graph = new NodeGraph();
        var setOperation = new SetOperationDefinition(
            Operator: "INTERSEC",
            QuerySql: "SELECT id FROM archived_orders"
        );

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            GenerateGraphFirst(svc, "orders", graph, setOperation: setOperation)
        );

        Assert.Contains("Unsupported set operation operator", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INTERSEC", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_WithWhereBindingsCountLowerThanWhereExpressions_ThrowsExplicitError()
    {
        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        var graph = new NodeGraph
        {
            Nodes = [],
            WhereConditions = [new WhereBinding("legacy_only", "result", "OR")],
        };

        var compiled = new CompiledNodeGraph(
            SelectExprs: [],
            WhereExprs:
            [
                new LiteralExpr("1 = 1", PinDataType.Boolean),
                new LiteralExpr("2 = 2", PinDataType.Boolean),
            ],
            HavingExprs: [],
            QualifyExprs: [],
            OrderExprs: [],
            GroupByExprs: [],
            Distinct: false,
            Limit: null,
            Offset: null
        );

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            svc.Generate("orders", compiled, whereMeta: graph.WhereConditions)
        );

        Assert.Contains("WHERE metadata mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_WithQualifyExpressionsThatNormalizeToEmpty_DoesNotEmitDanglingWhere()
    {
        var svc = QueryGeneratorService.Create(DatabaseProvider.Postgres);
        var compiled = new CompiledNodeGraph(
            SelectExprs:
            [
                (new ColumnExpr("orders", "id", PinDataType.Integer), (string?)null),
            ],
            WhereExprs: [],
            HavingExprs: [],
            QualifyExprs:
            [
                new LiteralExpr("NULL", PinDataType.Expression),
                new LiteralExpr("   ", PinDataType.Expression),
            ],
            OrderExprs: [],
            GroupByExprs: [],
            Distinct: false,
            Limit: null,
            Offset: null
        );

        GeneratedQuery result = svc.Generate("orders", compiled);

        Assert.DoesNotContain("AS _qualify", result.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WHERE\n", result.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WHERE ;", result.Sql, StringComparison.OrdinalIgnoreCase);
    }

    private static GeneratedQuery GenerateGraphFirst(
        QueryGeneratorService service,
        string fromTable,
        NodeGraph graph,
        IReadOnlyList<JoinDefinition>? joins = null,
        SetOperationDefinition? setOperation = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(graph);

        NodeGraph normalizedGraph = NormalizeLegacyBindingsForGraphFirst(graph, fromTable);
        bool hasExplicitOutputSink = graph.Nodes.Any(node => node.Type == NodeType.ResultOutput);
        QueryGenerationStructure? structure = (string.IsNullOrWhiteSpace(fromTable) && joins is null) || (hasExplicitOutputSink && joins is null)
            ? null
            : new QueryGenerationStructure(fromTable, joins);

        return service.Generate(normalizedGraph, structure, setOperation);
    }

    private static NodeGraph NormalizeLegacyBindingsForGraphFirst(NodeGraph graph, string fallbackFromTable)
    {
        IReadOnlyList<CteBinding> normalizedCtes = graph.Ctes
            .Select(cte => cte with { Graph = NormalizeLegacyBindingsForGraphFirst(cte.Graph, cte.FromTable) })
            .ToList();

        bool hasOutputNode = graph.Nodes.Any(node => node.Type == NodeType.ResultOutput);
        if (hasOutputNode)
        {
            return new NodeGraph
            {
                Nodes = graph.Nodes,
                Connections = graph.Connections,
                Ctes = normalizedCtes,
                SelectOutputs = graph.SelectOutputs,
                WhereConditions = graph.WhereConditions,
                Havings = graph.Havings,
                Qualifies = graph.Qualifies,
                QueryHints = graph.QueryHints,
                PivotMode = graph.PivotMode,
                PivotConfig = graph.PivotConfig,
                OrderBys = graph.OrderBys,
                GroupBys = graph.GroupBys,
                Distinct = graph.Distinct,
                Limit = graph.Limit,
                Offset = graph.Offset,
            };
        }

        List<NodeInstance> nodes = graph.Nodes.ToList();
        List<Connection> connections = graph.Connections.ToList();

        bool hasDatasetSourceNode = nodes.Any(node =>
            node.Type is NodeType.TableSource or NodeType.CteSource or NodeType.Subquery or NodeType.SubqueryReference);

        if (!hasDatasetSourceNode && !string.IsNullOrWhiteSpace(fallbackFromTable))
        {
            nodes.Add(new NodeInstance(
                "auto_src",
                NodeType.TableSource,
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                Alias: "src",
                TableFullName: fallbackFromTable,
                ColumnPins: new Dictionary<string, string> { ["*"] = "*" },
                ColumnPinTypes: new Dictionary<string, PinDataType> { ["*"] = PinDataType.RowSet }));
        }

        string outputNodeId = BuildUniqueNodeId(nodes, "auto_result");
        nodes.Add(new NodeInstance(
            outputNodeId,
            NodeType.ResultOutput,
            new Dictionary<string, string>(),
            new Dictionary<string, string>()));

        foreach (SelectBinding select in graph.SelectOutputs)
        {
            string outputPin = select.PinName == "*" ? "columns" : "column";
            connections.Add(new Connection(select.NodeId, select.PinName, outputNodeId, outputPin));
        }

        foreach (WhereBinding where in graph.WhereConditions)
            connections.Add(new Connection(where.NodeId, where.PinName, outputNodeId, "where"));

        foreach (HavingBinding having in graph.Havings)
            connections.Add(new Connection(having.NodeId, having.PinName, outputNodeId, "having"));

        foreach (QualifyBinding qualify in graph.Qualifies)
            connections.Add(new Connection(qualify.NodeId, qualify.PinName, outputNodeId, "qualify"));

        foreach (GroupByBinding groupBy in graph.GroupBys)
            connections.Add(new Connection(groupBy.NodeId, groupBy.PinName, outputNodeId, "group_by"));

        foreach (OrderBinding order in graph.OrderBys)
            connections.Add(new Connection(order.NodeId, order.PinName, outputNodeId, order.Descending ? "order_by_desc" : "order_by"));

        bool hasSelectBindings = graph.SelectOutputs.Count > 0;
        if (!hasSelectBindings && !connections.Any(connection => connection.ToNodeId == outputNodeId && connection.ToPinName is "column" or "columns"))
        {
            NodeInstance? source = nodes.FirstOrDefault(node => node.Type == NodeType.TableSource);
            if (source is not null)
                connections.Add(new Connection(source.Id, "*", outputNodeId, "columns"));
        }

        return new NodeGraph
        {
            Nodes = nodes,
            Connections = connections,
            Ctes = normalizedCtes,
            SelectOutputs = [],
            WhereConditions = [],
            Havings = [],
            Qualifies = [],
            QueryHints = graph.QueryHints,
            PivotMode = graph.PivotMode,
            PivotConfig = graph.PivotConfig,
            OrderBys = [],
            GroupBys = [],
            Distinct = graph.Distinct,
            Limit = graph.Limit,
            Offset = graph.Offset,
        };
    }

    private static string BuildUniqueNodeId(IEnumerable<NodeInstance> nodes, string baseId)
    {
        HashSet<string> ids = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        if (!ids.Contains(baseId))
            return baseId;

        for (int index = 1; index < 10_000; index++)
        {
            string candidate = $"{baseId}_{index}";
            if (!ids.Contains(candidate))
                return candidate;
        }

        throw new InvalidOperationException("Unable to allocate a unique node id for test graph normalization.");
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// NODE GRAPH TOPOLOGICAL SORT
// ═════════════════════════════════════════════════════════════════════════════

public class NodeGraphTopologicalSortTests
{
    [Fact]
    public void TopologicalOrder_Simple_SourceBeforeSink()
    {
        var src = new NodeInstance(
            "src",
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>()
        );
        var sink = new NodeInstance(
            "sink",
            NodeType.Upper,
            new Dictionary<string, string>(),
            new Dictionary<string, string>()
        );

        var graph = new NodeGraph
        {
            Nodes = [sink, src], // intentionally reversed in list
            Connections = [new Connection("src", "col", "sink", "text")],
        };

        IReadOnlyList<NodeInstance> order = graph.TopologicalOrder();
        var orderList = order.ToList();
        int srcIdx = orderList.IndexOf(src);
        int sinkIdx = orderList.IndexOf(sink);

        Assert.True(srcIdx < sinkIdx, "Source must come before sink in topological order");
    }

    [Fact]
    public void TopologicalOrder_Cycle_ThrowsInvalidOperation()
    {
        var a = new NodeInstance(
            "a",
            NodeType.Upper,
            new Dictionary<string, string>(),
            new Dictionary<string, string>()
        );
        var b = new NodeInstance(
            "b",
            NodeType.Lower,
            new Dictionary<string, string>(),
            new Dictionary<string, string>()
        );

        var graph = new NodeGraph
        {
            Nodes = [a, b],
            Connections =
            [
                new Connection("a", "result", "b", "text"),
                new Connection("b", "result", "a", "text"), // cycle!
            ],
        };

        Assert.Throws<InvalidOperationException>(() => graph.TopologicalOrder());
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// NODE DEFINITION REGISTRY
// ═════════════════════════════════════════════════════════════════════════════

public class NodeDefinitionRegistryTests
{
    [Fact]
    public void Get_KnownType_ReturnsDefinition()
    {
        NodeDefinition def = NodeDefinitionRegistry.Get(NodeType.Upper);
        Assert.Equal("UPPER", def.DisplayName);
        Assert.Equal(NodeCategory.StringTransform, def.Category);
    }

    [Fact]
    public void All_ContainsAllCategories()
    {
        var categories = NodeDefinitionRegistry.All.Select(d => d.Category).Distinct().ToHashSet();

        Assert.Contains(NodeCategory.StringTransform, categories);
        Assert.Contains(NodeCategory.MathTransform, categories);
        Assert.Contains(NodeCategory.Comparison, categories);
        Assert.Contains(NodeCategory.LogicGate, categories);
        Assert.Contains(NodeCategory.Json, categories);
    }

    [Fact]
    public void And_Node_HasMultiInputPin()
    {
        NodeDefinition def = NodeDefinitionRegistry.Get(NodeType.And);
        PinDescriptor condPin = def.InputPins.First(p => p.Name == "conditions");
        Assert.True(condPin.AllowMultiple);
    }

    [Fact]
    public void JsonExtract_Node_HasPathParameter()
    {
        NodeDefinition def = NodeDefinitionRegistry.Get(NodeType.JsonExtract);
        Assert.Contains(def.Parameters, p => p.Name == "path");
    }

    [Fact]
    public void ResultOutput_HasHavingPinAndDistinctParameter()
    {
        NodeDefinition def = NodeDefinitionRegistry.Get(NodeType.ResultOutput);
        Assert.Contains(def.InputPins, p => p.Name == "having");
        Assert.Contains(def.Parameters, p => p.Name == "distinct");
    }

    [Fact]
    public void Registry_ContainsStringAggSystemDateAndDateArithmeticNodes()
    {
        NodeDefinition alias = NodeDefinitionRegistry.Get(NodeType.Alias);
        Assert.Contains(alias.InputPins, p => p.Name == "expression");
        Assert.Contains(alias.InputPins, p => p.Name == "alias_text");

        NodeDefinition agg = NodeDefinitionRegistry.Get(NodeType.StringAgg);
        Assert.Contains(agg.Parameters, p => p.Name == "separator");
        Assert.Contains(agg.Parameters, p => p.Name == "distinct");

        NodeDefinition systemDate = NodeDefinitionRegistry.Get(NodeType.SystemDate);
        Assert.Contains(systemDate.OutputPins, p => p.Name == "result");

        NodeDefinition window = NodeDefinitionRegistry.Get(NodeType.WindowFunction);
        Assert.Contains(window.Parameters, p => p.Name == "function");
        NodeParameter? fnParam = window.Parameters.FirstOrDefault(p => p.Name == "function");
        Assert.NotNull(fnParam);
        Assert.Contains("Rank", fnParam!.EnumValues!);
        Assert.Contains("DenseRank", fnParam.EnumValues!);
        Assert.Contains("Ntile", fnParam.EnumValues!);
        Assert.Contains("Lag", fnParam.EnumValues!);
        Assert.Contains("Lead", fnParam.EnumValues!);
        Assert.Contains("FirstValue", fnParam.EnumValues!);
        Assert.Contains("LastValue", fnParam.EnumValues!);
        Assert.Contains(window.Parameters, p => p.Name == "offset");
        Assert.Contains(window.Parameters, p => p.Name == "ntile_groups");
        Assert.Contains(window.Parameters, p => p.Name == "default_value");
        Assert.Contains(window.Parameters, p => p.Name == "frame");
        Assert.Contains(window.InputPins, p => p.Name == "default");
        Assert.Contains(window.InputPins, p => p.Name == "partition_1");
        Assert.Contains(window.InputPins, p => p.Name == "order_1");
        Assert.True(window.InputPins.First(p => p.Name == "partition_1").AllowMultiple);
        Assert.True(window.InputPins.First(p => p.Name == "order_1").AllowMultiple);

        NodeDefinition cteSource = NodeDefinitionRegistry.Get(NodeType.CteSource);
        Assert.Contains(cteSource.Parameters, p => p.Name == "cte_name");
        Assert.Contains(cteSource.Parameters, p => p.Name == "alias");
        Assert.Contains(cteSource.InputPins, p => p.Name == "cte");
        Assert.Contains(cteSource.InputPins, p => p.Name == "cte_name_text");
        Assert.Contains(cteSource.InputPins, p => p.Name == "alias_text");
        Assert.Contains(cteSource.OutputPins, p => p.Name == "result");

        NodeDefinition cteDefinition = NodeDefinitionRegistry.Get(NodeType.CteDefinition);
        Assert.Contains(cteDefinition.InputPins, p => p.Name == "name_text");
        Assert.Contains(cteDefinition.InputPins, p => p.Name == "source_table_text");
        Assert.Contains(cteDefinition.Parameters, p => p.Name == "name");
        Assert.Contains(cteDefinition.Parameters, p => p.Name == "cte_name");
        Assert.Contains(cteDefinition.Parameters, p => p.Name == "source_table");
        Assert.Contains(cteDefinition.Parameters, p => p.Name == "recursive");
        Assert.Contains(cteDefinition.InputPins, p => p.Name == "query");
        Assert.Contains(cteDefinition.OutputPins, p => p.Name == "table");

        NodeDefinition dateAdd = NodeDefinitionRegistry.Get(NodeType.DateAdd);
        Assert.Contains(dateAdd.Parameters, p => p.Name == "unit");

        NodeDefinition dateDiff = NodeDefinitionRegistry.Get(NodeType.DateDiff);
        Assert.Contains(dateDiff.InputPins, p => p.Name == "start");
        Assert.Contains(dateDiff.InputPins, p => p.Name == "end");

        NodeDefinition datePart = NodeDefinitionRegistry.Get(NodeType.DatePart);
        Assert.Contains(datePart.Parameters, p => p.Name == "part");

        NodeDefinition dateFormat = NodeDefinitionRegistry.Get(NodeType.DateFormat);
        Assert.Contains(dateFormat.Parameters, p => p.Name == "format");
    }

    [Fact]
    public void LegacyNodeSymbols_AreRemovedFromRuntimeEnum()
    {
        Assert.False(Enum.TryParse<NodeType>("RawSqlQuery", out _));
        Assert.False(Enum.TryParse<NodeType>("SelectOutput", out _));
        Assert.False(Enum.TryParse<NodeType>("WhereOutput", out _));
        Assert.False(Enum.TryParse<NodeType>("ReportOutput", out _));
    }

    [Fact]
    public void RuntimeOutputNode_Definition_RemainsVisibleInCatalog()
    {
        Assert.True(NodeDefinitionRegistry.IsVisibleInCatalog(NodeType.ResultOutput));
    }
}
