namespace AkkornStudio.UI.Services.QueryPreview;

internal sealed class QueryCompilationImplicitCrossJoinValidator
{
    public void Validate(IReadOnlyList<JoinDefinition> joins, List<string> errors)
    {
        int implicitCrossJoinCount = joins.Count(j =>
            j.Type.Equals("CROSS", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(j.OnRaw));

        if (implicitCrossJoinCount == 0)
            return;

        string noun = implicitCrossJoinCount == 1 ? "join" : "joins";
        errors.Add(
            $"{implicitCrossJoinCount} CROSS JOIN {noun} detected without explicit condition. This may create a Cartesian product and large result sets."
        );
    }
}
