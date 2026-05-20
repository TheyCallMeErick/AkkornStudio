using AkkornStudio.Ddl;
using AkkornStudio.Ddl.Compilers;
using AkkornStudio.Nodes;

namespace AkkornStudio.Tests.Unit.Ddl.Compilers;

public sealed class AlterViewOutputCompilerTests
{
    [Fact]
    public void OutputType_IsAlterViewOutput()
    {
        var sut = new AlterViewOutputCompiler();

        Assert.Equal(NodeType.AlterViewOutput, sut.OutputType);
    }

    [Fact]
    public void Compile_WhenViewInputMissing_AddsErrorAndNoStatements()
    {
        NodeInstance outputNode = new("out", NodeType.AlterViewOutput, new Dictionary<string, string>(), new Dictionary<string, string>());
        NodeGraph graph = new() { Nodes = [outputNode], Connections = [] };
        List<(string Code, string Message, string? NodeId)> errors = [];
        List<IDdlExpression> statements = [];
        var sut = new AlterViewOutputCompiler();
        DdlOutputCompilationContext context = CreateContext(graph, errors);

        sut.Compile([outputNode], context, statements);

        Assert.Contains(errors, e => e.Code == "E-DDL-ALTERVIEW-OUTPUT-VIEW" && e.NodeId == "out");
        Assert.Empty(statements);
    }

    [Fact]
    public void Compile_WhenInputIsNotViewDefinition_AddsTypeError()
    {
        NodeInstance outputNode = new("out", NodeType.AlterViewOutput, new Dictionary<string, string>(), new Dictionary<string, string>());
        NodeInstance tableNode = new("tbl", NodeType.TableDefinition, new Dictionary<string, string>(), new Dictionary<string, string>());
        NodeGraph graph = new()
        {
            Nodes = [outputNode, tableNode],
            Connections = [new Connection("tbl", "table", "out", "view")],
        };
        List<(string Code, string Message, string? NodeId)> errors = [];
        List<IDdlExpression> statements = [];
        var sut = new AlterViewOutputCompiler();
        DdlOutputCompilationContext context = CreateContext(graph, errors);

        sut.Compile([outputNode], context, statements);

        Assert.Contains(errors, e => e.Code == "E-DDL-ALTERVIEW-OUTPUT-TYPE" && e.NodeId == "out");
        Assert.Empty(statements);
    }

    [Fact]
    public void Compile_WhenValidViewInput_AddsStatement()
    {
        NodeInstance outputNode = new("out", NodeType.AlterViewOutput, new Dictionary<string, string>(), new Dictionary<string, string>());
        NodeInstance viewNode = new("view", NodeType.ViewDefinition, new Dictionary<string, string>(), new Dictionary<string, string>());
        NodeGraph graph = new()
        {
            Nodes = [outputNode, viewNode],
            Connections = [new Connection("view", "view", "out", "view")],
        };
        List<(string Code, string Message, string? NodeId)> errors = [];
        List<IDdlExpression> statements = [];
        var sut = new AlterViewOutputCompiler();
        DdlOutputCompilationContext context = CreateContext(
            graph,
            errors,
            _ => new AlterViewExpr("public", "v_orders", "SELECT 1"));

        sut.Compile([outputNode], context, statements);

        var expr = Assert.IsType<AlterViewExpr>(Assert.Single(statements));
        Assert.Equal("public", expr.SchemaName);
        Assert.Equal("v_orders", expr.ViewName);
        Assert.Empty(errors);
    }

    [Fact]
    public void Compile_WhenLookupThrows_AddsCompileError()
    {
        NodeInstance outputNode = new("out", NodeType.AlterViewOutput, new Dictionary<string, string>(), new Dictionary<string, string>());
        NodeGraph graph = new()
        {
            Nodes = [outputNode],
            Connections = [new Connection("missing", "view", "out", "view")],
        };
        List<(string Code, string Message, string? NodeId)> errors = [];
        List<IDdlExpression> statements = [];
        var sut = new AlterViewOutputCompiler();
        DdlOutputCompilationContext context = CreateContext(graph, errors);

        sut.Compile([outputNode], context, statements);

        Assert.Contains(errors, e => e.Code == "E-DDL-COMPILE-ALTERVIEW" && e.NodeId == "out");
        Assert.Empty(statements);
    }

    private static DdlOutputCompilationContext CreateContext(
        NodeGraph graph,
        List<(string Code, string Message, string? NodeId)> errors,
        Func<NodeInstance, AlterViewExpr>? compileAlterView = null)
    {
        return new DdlOutputCompilationContext(
            graph,
            _ => DdlIdempotentMode.None,
            (_, _) => throw new NotSupportedException(),
            (_, _) => throw new NotSupportedException(),
            (_, _) => throw new NotSupportedException(),
            _ => throw new NotSupportedException(),
            (_, _) => throw new NotSupportedException(),
            compileAlterView ?? (_ => throw new NotSupportedException()),
            _ => throw new NotSupportedException(),
            (_, _) => throw new NotSupportedException(),
            (code, message, nodeId) => errors.Add((code, message, nodeId)));
    }
}
