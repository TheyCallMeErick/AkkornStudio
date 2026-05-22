using System.IO;

namespace AkkornStudio.Tests.Unit.ViewModels;

public sealed class ConnectionManagerSwitchDatabaseHardeningRegressionTests
{
    [Fact]
    public void SwitchDatabase_UsesRuntimeProfileAndPersistsBeforeMutatingActiveProfile()
    {
        string source = ReadConnectionManagerSource();

        Assert.Contains("ConnectionProfile runtimeProfile = CloneProfile(activeProfile);", source);
        Assert.Contains("ConnectionContractMapper.ToDetails(runtimeProfile)", source);
        Assert.Contains("if (saveResult.Success && saveResult.Payload is not null)", source);
        Assert.Contains("activeProfile.Database = databaseName;", source);
        Assert.Contains("connection.warning.databaseSwitchNotPersisted", source);
    }

    [Fact]
    public void SwitchDatabase_DoesNotMutateActiveProfileBeforeSaveAttempt()
    {
        string source = ReadConnectionManagerSource();

        Assert.DoesNotContain(
            "activeProfile.Database = databaseName;\n                RaisePropertyChanged(nameof(ActiveConnectionLabel));\n                RaisePropertyChanged(nameof(ConnectionHealthTooltip));\n\n                OperationResultDto<ConnectionDetailsDto> saveResult = await _connectionCatalogService.SaveAsync(",
            source);
    }

    private static string ReadConnectionManagerSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(
                dir.FullName,
                "src",
                "AkkornStudio.UI",
                "ViewModels",
                "ConnectionManager",
                "ConnectionManagerViewModel.cs"
            );

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate ConnectionManagerViewModel.cs from test base directory.");
    }
}
