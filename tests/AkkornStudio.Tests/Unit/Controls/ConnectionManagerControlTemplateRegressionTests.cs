using System.IO;
using Xunit;

namespace AkkornStudio.Tests.Unit.Controls;

public sealed class ConnectionManagerControlTemplateRegressionTests
{
    [Fact]
    public void ConnectionManagerTemplate_HidesLegacyProviderFieldFromForm()
    {
        string xaml = ReadTemplate();

        Assert.DoesNotContain("Text=\"{Binding [connection.provider]", xaml);
        Assert.DoesNotContain("SelectedItem=\"{Binding SelectedProviderOption}\"", xaml);
    }

    [Fact]
    public void ConnectionManagerTemplate_ShowsConnectOnlyForSavedSelectedProfile()
    {
        string xaml = ReadTemplate();

        Assert.Contains("IsVisible=\"{Binding CanShowConnectAction}\"", xaml);
        Assert.Contains("IsVisible=\"{Binding CanShowDisconnectAction}\"", xaml);
    }

    private static string ReadTemplate()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(
                dir.FullName,
                "src",
                "AkkornStudio.UI",
                "Controls",
                "ConnectionManager",
                "ConnectionManagerControl.axaml");

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate ConnectionManagerControl.axaml.");
    }
}
