using System.IO;
using Xunit;

namespace AkkornStudio.Tests.Unit.Controls;

public sealed class ConnectionManagerControlTemplateRegressionTests
{
    [Fact]
    public void ConnectionManagerTemplate_UsesFilteredConnectionCardsAndSearchInput()
    {
        string xaml = ReadTemplate();

        Assert.Contains("ItemsSource=\"{Binding FilteredConnectionCards}\"", xaml);
        Assert.Contains("Text=\"{Binding ConnectionPickerSearchQuery}\"", xaml);
    }

    [Fact]
    public void ConnectionManagerTemplate_RendersProviderSvgImageAndFullWidthList()
    {
        string xaml = ReadTemplate();

        Assert.Contains("Source=\"{Binding ProviderIconAssetUri}\"", xaml);
        Assert.DoesNotContain("WrapPanel Orientation=\"Horizontal\" ItemWidth=\"260\"", xaml);
        Assert.Contains("<VirtualizingStackPanel/>", xaml);
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
