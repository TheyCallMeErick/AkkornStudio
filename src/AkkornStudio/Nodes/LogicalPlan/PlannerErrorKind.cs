namespace AkkornStudio.Nodes.LogicalPlan;

public enum PlannerErrorKind
{
    UnconnectedColumnSource = 0,
    JoinWithoutCondition = 1,
    AggregateWithoutGroupBy = 2,
    DatasetNotReachableFromOutput = 3,
    DuplicateAlias = 4,
    CteNotReferencedInPlan = 5,
    CyclicDependency = 6,
    OutputSourceAmbiguous = 7,
    GroupByImplicit = 8,
    InvalidJoinOperator = 9,
}
