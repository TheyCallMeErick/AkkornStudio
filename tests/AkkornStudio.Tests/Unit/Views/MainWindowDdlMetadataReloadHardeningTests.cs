using System.IO;

namespace AkkornStudio.Tests.Unit.Views;

public sealed class MainWindowDdlMetadataReloadHardeningTests
{
    [Fact]
    public void ExecuteDdlAsync_ReloadsMetadata_WhenDdlAppliesSchemaChanges()
    {
        string source = ReadMainWindowModeSource();

        Assert.Contains("DdlExecutionResult? executionResult = null;", source);
        Assert.Contains("executionResult = await orchestrator.ExecuteDdlAsync(sql, stopOnError, ct);", source);
        Assert.Contains("if (dialogVm.IsSuccess || HasSuccessfulDdlStatements(executionResult))", source);
        Assert.Contains("TryReloadActiveMetadataAfterDdl();", source);
        Assert.Contains("manager.ReloadMetadataCommand.Execute(null);", source);
    }

    private static string ReadMainWindowModeSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(
                dir.FullName,
                "src",
                "AkkornStudio.UI",
                "Views",
                "Shell",
                "MainWindow.ModeAndDdl.cs");

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate MainWindow.ModeAndDdl.cs from test base directory.");
    }
}
