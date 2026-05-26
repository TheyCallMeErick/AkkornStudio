using AkkornStudio.Core;
using AkkornStudio.UI.Services;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.ViewModels.Canvas;

public class CanvasPreviewParameterInputsPruningTests
{
    [Fact]
    public void PrunePreviewParameterInputs_RemovesOnlyObsoleteEntriesFromCurrentConnectionScope()
    {
        var canvas = new CanvasViewModel();
        ConnectionConfig postgres = BuildConfig(DatabaseProvider.Postgres, "analytics");
        ConnectionConfig mysql = BuildConfig(DatabaseProvider.MySql, "analytics");

        string pgA = PreviewParameterInputScopeKey.BuildScopedKey(postgres, "named:a");
        string pgB = PreviewParameterInputScopeKey.BuildScopedKey(postgres, "named:b");
        string myA = PreviewParameterInputScopeKey.BuildScopedKey(mysql, "named:a");

        canvas.RememberPreviewParameterInputs(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [pgA] = "1",
            [pgB] = "2",
            [myA] = "3",
        });

        int removed = canvas.PrunePreviewParameterInputs(postgres, ["named:a"]);

        Assert.Equal(1, removed);
        Assert.True(canvas.PreviewParameterInputs.ContainsKey(pgA));
        Assert.False(canvas.PreviewParameterInputs.ContainsKey(pgB));
        Assert.True(canvas.PreviewParameterInputs.ContainsKey(myA));
    }

    [Fact]
    public void PrunePreviewParameterInputs_WhenNoActivePlaceholders_ClearsCurrentConnectionScope()
    {
        var canvas = new CanvasViewModel();
        ConnectionConfig postgres = BuildConfig(DatabaseProvider.Postgres, "sales");

        string pgA = PreviewParameterInputScopeKey.BuildScopedKey(postgres, "named:a");
        string pgB = PreviewParameterInputScopeKey.BuildScopedKey(postgres, "pos:1:?");

        canvas.RememberPreviewParameterInputs(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [pgA] = "42",
            [pgB] = "true",
        });

        int removed = canvas.PrunePreviewParameterInputs(postgres, []);

        Assert.Equal(2, removed);
        Assert.Empty(canvas.PreviewParameterInputs);
    }

    private static ConnectionConfig BuildConfig(DatabaseProvider provider, string database) =>
        new(
            provider,
            Host: "localhost",
            Port: 5432,
            Database: database,
            Username: "user",
            Password: "pwd");
}
