using AkkornStudio.UI.Services.SqlEditor;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.Services.SqlEditor;

public sealed class SqlExecutionErrorClassifierTests
{
    [Theory]
    [InlineData("SQL execution was canceled.", SqlExecutionErrorCategory.Cancelled)]
    [InlineData("Operation timed out while reading results.", SqlExecutionErrorCategory.Timeout)]
    [InlineData("No SQL statement selected for execution.", SqlExecutionErrorCategory.Validation)]
    [InlineData("permission denied for relation users", SqlExecutionErrorCategory.Security)]
    [InlineData("connection failed to host", SqlExecutionErrorCategory.Operational)]
    [InlineData("very unusual condition", SqlExecutionErrorCategory.Unexpected)]
    public void Classify_MapsExpectedCategory(string errorMessage, SqlExecutionErrorCategory expected)
    {
        SqlExecutionErrorCategory category = SqlExecutionErrorClassifier.Classify(errorMessage);

        Assert.Equal(expected, category);
    }
}
