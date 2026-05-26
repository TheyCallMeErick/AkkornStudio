using AkkornStudio.Core;
using AkkornStudio.Nodes.LogicalPlan;
using AkkornStudio.Registry;

namespace AkkornStudio.Tests.Unit.Nodes.LogicalPlan;

public sealed class LogicalPlannerTests
{
    [Fact]
    public void Plan_SingleTableOutput_ReturnsLogicalScanAsSource()
    {
        NodeGraph graph = new()
        {
            Nodes = [CreateTable("t1", "public.employees"), CreateOutput("out")],
            Connections = [new Connection("t1", "id", "out", "column")],
        };

        LogicalOutput output = CreatePlanner(graph).Plan();
        LogicalScan scan = Assert.IsType<LogicalScan>(output.Source);
        Assert.Equal("public.employees", scan.TableFullName);
        Assert.False(string.IsNullOrWhiteSpace(scan.Alias));
    }

    [Fact]
    public void Plan_MultipleTopLevelOutputs_ThrowsOutputSourceAmbiguous()
    {
        NodeGraph graph = new()
        {
            Nodes = [CreateTable("t1", "public.employees"), CreateOutput("out1"), CreateOutput("out2")],
            Connections =
            [
                new Connection("t1", "id", "out1", "column"),
                new Connection("t1", "id", "out2", "column"),
            ],
        };

        PlanningException ex = Assert.Throws<PlanningException>(() => CreatePlanner(graph).Plan());
        Assert.Equal(PlannerErrorKind.OutputSourceAmbiguous, ex.Kind);
    }

    [Fact]
    public void Plan_JoinWithoutConditionAndNonCross_ThrowsJoinWithoutCondition()
    {
        NodeInstance left = CreateTable("left", "public.employees");
        NodeInstance right = CreateTable("right", "public.departments");
        NodeInstance join = new(
            "j1",
            NodeType.Join,
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["join_type"] = "INNER" });
        NodeInstance output = CreateOutput("out");

        NodeGraph graph = new()
        {
            Nodes = [left, right, join, output],
            Connections =
            [
                new Connection("left", "department_id", "j1", "left"),
                new Connection("right", "id", "j1", "right"),
                new Connection("left", "id", "out", "column"),
                new Connection("right", "name", "out", "column"),
            ],
        };

        PlanningException ex = Assert.Throws<PlanningException>(() => CreatePlanner(graph).Plan());
        Assert.Equal(PlannerErrorKind.JoinWithoutCondition, ex.Kind);
    }

    [Fact]
    public void Plan_CrossJoinWithoutCondition_IsAccepted()
    {
        NodeInstance left = CreateTable("left", "public.employees");
        NodeInstance right = CreateTable("right", "public.departments");
        NodeInstance join = new(
            "j1",
            NodeType.Join,
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["join_type"] = "CROSS" });
        NodeInstance output = CreateOutput("out");

        NodeGraph graph = new()
        {
            Nodes = [left, right, join, output],
            Connections =
            [
                new Connection("left", "department_id", "j1", "left"),
                new Connection("right", "id", "j1", "right"),
                new Connection("left", "id", "out", "column"),
                new Connection("right", "name", "out", "column"),
            ],
        };

        LogicalOutput planned = CreatePlanner(graph).Plan();
        LogicalJoin source = Assert.IsType<LogicalJoin>(planned.Source);
        Assert.Equal(JoinKind.Cross, source.Kind);
    }

    [Fact]
    public void Plan_JoinFromParameters_WithInvalidOperator_ThrowsInvalidJoinOperator()
    {
        NodeInstance left = CreateTable("left", "public.employees");
        NodeInstance join = new(
            "j1",
            NodeType.Join,
            new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["join_type"] = "INNER",
                ["right_source"] = "public.departments d",
                ["left_expr"] = "employees.department_id",
                ["right_expr"] = "d.id",
                ["operator"] = "INVALID_OP",
            });
        NodeInstance output = CreateOutput("out");

        NodeGraph graph = new()
        {
            Nodes = [left, join, output],
            Connections = [new Connection("left", "id", "out", "column")],
        };

        PlanningException ex = Assert.Throws<PlanningException>(() => CreatePlanner(graph).Plan());
        Assert.Equal(PlannerErrorKind.InvalidJoinOperator, ex.Kind);
        Assert.Contains("INVALID_OP", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_DuplicateExplicitAliases_ThrowsDuplicateAlias()
    {
        NodeInstance first = CreateTable("t1", "public.employees", alias: "dup");
        NodeInstance second = CreateTable("t2", "public.departments", alias: "dup");
        NodeInstance join = new(
            "j1",
            NodeType.Join,
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["join_type"] = "CROSS" });
        NodeInstance output = CreateOutput("out");

        NodeGraph graph = new()
        {
            Nodes = [first, second, join, output],
            Connections =
            [
                new Connection("t1", "department_id", "j1", "left"),
                new Connection("t2", "id", "j1", "right"),
                new Connection("t1", "id", "out", "column"),
                new Connection("t2", "name", "out", "column"),
            ],
        };

        PlanningException ex = Assert.Throws<PlanningException>(() => CreatePlanner(graph).Plan());
        Assert.Equal(PlannerErrorKind.DuplicateAlias, ex.Kind);
    }

    [Fact]
    public void Plan_OutputSelectingSingleTableButWithExplicitJoin_UsesJoinAsSource()
    {
        NodeInstance left = CreateTable("left", "public.employees");
        NodeInstance right = CreateTable("right", "public.departments");
        NodeInstance join = new(
            "j1",
            NodeType.Join,
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["join_type"] = "INNER" });
        NodeInstance eq = new(
            "eq1",
            NodeType.Equals,
            new Dictionary<string, string>(),
            new Dictionary<string, string>());
        NodeInstance output = CreateOutput("out");

        NodeGraph graph = new()
        {
            Nodes = [left, right, join, eq, output],
            Connections =
            [
                new Connection("left", "department_id", "j1", "left"),
                new Connection("right", "id", "j1", "right"),
                new Connection("left", "department_id", "eq1", "left"),
                new Connection("right", "id", "eq1", "right"),
                new Connection("eq1", "result", "j1", "condition"),
                new Connection("left", "id", "out", "column"),
            ],
        };

        LogicalOutput planned = CreatePlanner(graph).Plan();
        LogicalJoin source = Assert.IsType<LogicalJoin>(planned.Source);
        Assert.Equal(JoinKind.Inner, source.Kind);
    }

    [Fact]
    public void Plan_DefinedCteWithoutReference_DoesNotBlockPlanWhenNoCteSourceIsUsed()
    {
        NodeGraph graph = new()
        {
            Nodes = [CreateTable("t1", "public.employees"), CreateOutput("out")],
            Connections = [new Connection("t1", "id", "out", "column")],
            Ctes =
            [
                new CteBinding(
                    Name: "cte_unused",
                    FromTable: "public.departments",
                    Graph: new NodeGraph())
            ],
        };

        LogicalOutput output = CreatePlanner(graph).Plan();
        Assert.NotNull(output);
    }

    [Fact]
    public void Plan_CteCycle_ThrowsCyclicDependency()
    {
        NodeInstance cteSource = new(
            "cte_src",
            NodeType.CteSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["cte_name"] = "cte_a" });
        NodeInstance output = CreateOutput("out");

        NodeGraph graph = new()
        {
            Nodes = [cteSource, output],
            Connections = [new Connection("cte_src", "id", "out", "column")],
            Ctes =
            [
                new CteBinding("cte_a", "cte_b", new NodeGraph()),
                new CteBinding("cte_b", "cte_a", new NodeGraph()),
            ],
        };

        PlanningException ex = Assert.Throws<PlanningException>(() => CreatePlanner(graph).Plan());
        Assert.Equal(PlannerErrorKind.CyclicDependency, ex.Kind);
    }

    [Fact]
    public void Plan_OrderByConnectedFromNonDatasetNode_ThrowsExplicitError()
    {
        NodeInstance table = CreateTable("t1", "public.employees");
        NodeInstance nonDataset = new(
            "sum1",
            NodeType.Sum,
            new Dictionary<string, string>(),
            new Dictionary<string, string>());
        NodeInstance output = CreateOutput("out");

        NodeGraph graph = new()
        {
            Nodes = [table, nonDataset, output],
            Connections =
            [
                new Connection("t1", "id", "out", "column"),
                new Connection("sum1", "total", "out", "order_by"),
            ],
        };

        PlanningException ex = Assert.Throws<PlanningException>(() => CreatePlanner(graph).Plan());
        Assert.Equal(PlannerErrorKind.UnconnectedColumnSource, ex.Kind);
        Assert.Contains("is not a dataset source", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static LogicalPlanner CreatePlanner(NodeGraph graph)
    {
        var emitContext = new EmitContext(
            DatabaseProvider.Postgres,
            new SqlFunctionRegistry(DatabaseProvider.Postgres));
        return new LogicalPlanner(graph, emitContext);
    }

    private static NodeInstance CreateOutput(string id)
    {
        return new NodeInstance(
            id,
            NodeType.ResultOutput,
            new Dictionary<string, string>(),
            new Dictionary<string, string>());
    }

    private static NodeInstance CreateTable(string id, string tableFullName, string? alias = null)
    {
        return new NodeInstance(
            id,
            NodeType.TableSource,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            Alias: alias,
            TableFullName: tableFullName,
            ColumnPins: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = "id",
                ["department_id"] = "department_id",
                ["name"] = "name",
            },
            ColumnPinTypes: new Dictionary<string, PinDataType>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = PinDataType.Integer,
                ["department_id"] = PinDataType.Integer,
                ["name"] = PinDataType.Text,
            });
    }
}
