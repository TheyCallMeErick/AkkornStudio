using System.IO;
using Xunit;

namespace AkkornStudio.Tests.Unit.Controls;

public class DatabaseConnectionCardTemplateRegressionTests
{
    [Fact]
    public void CardTemplate_ExposesConnectionAndDataPickersWithoutCombos()
    {
        string xaml = ReadCardXaml();

        Assert.DoesNotContain("x:Name=\"ConnectionComboBox\"", xaml);
        Assert.DoesNotContain("x:Name=\"DatabaseComboBox\"", xaml);
        Assert.DoesNotContain("x:Name=\"SchemaComboBox\"", xaml);
        Assert.Contains("Command=\"{Binding OpenConnectionManagerCommand, ElementName=Root}\"", xaml);
        Assert.Contains("<Flyout Placement=\"BottomEdgeAlignedRight\">", xaml);
        Assert.Contains("Click=\"OnDatabaseOptionClick\"", xaml);
        Assert.Contains("Click=\"OnSchemaOptionClick\"", xaml);
    }

    [Fact]
    public void CardTemplate_ExposesDisconnectButton()
    {
        string xaml = ReadCardXaml();

        Assert.Contains("x:Name=\"DisconnectButton\"", xaml);
        Assert.Contains("Command=\"{Binding DisconnectCommand", xaml);
    }

    [Fact]
    public void CardTemplate_ShowsConnectedStateBrandAccent()
    {
        string xaml = ReadCardXaml();

        Assert.Contains("AccentPrimaryHover", xaml);
    }

    [Fact]
    public void CardTemplate_ShowsVersionInMonospace()
    {
        string xaml = ReadCardXaml();

        Assert.Contains("x:Name=\"VersionText\"", xaml);
        Assert.Contains("MonoFont", xaml);
    }

    [Fact]
    public void CardTemplate_HasReloadingStateProgressBar()
    {
        string xaml = ReadCardXaml();

        Assert.Contains("x:Name=\"ReloadingProgressBar\"", xaml);
        Assert.Contains("IsVisible=\"{Binding IsReloading", xaml);
    }

    [Fact]
    public void CardTemplate_ShowsConnectionNameAndVersionFallback()
    {
        string xaml = ReadCardXaml();

        Assert.Contains("Text=\"{Binding ConnectionName, ElementName=Root}\"", xaml);
        Assert.Contains("Versao indisponivel", xaml);
    }

    private static string ReadCardXaml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(
                dir.FullName,
                "src", "AkkornStudio.UI", "Controls", "Shared",
                "DatabaseConnectionCard.axaml");

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate DatabaseConnectionCard.axaml.");
    }
}
