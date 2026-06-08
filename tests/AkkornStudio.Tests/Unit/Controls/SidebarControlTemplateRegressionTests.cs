using System.IO;
using Xunit;

namespace AkkornStudio.Tests.Unit.Controls;

public class SidebarControlTemplateRegressionTests
{
    [Fact]
    public void SidebarTemplate_DefinesOnlyNodesAndConnectionTabs()
    {
        string xaml = ReadSidebarXaml();

        Assert.Contains("x:Name=\"NodesTabButton\"", xaml);
        Assert.Contains("x:Name=\"ConnectionTabButton\"", xaml);
        Assert.DoesNotContain("x:Name=\"SchemaTabButton\"", xaml);
    }

    [Fact]
    public void SidebarTemplate_HasNoSchemaContentSlot()
    {
        string xaml = ReadSidebarXaml();

        Assert.DoesNotContain("IsVisible=\"{Binding ShowSchema}\"", xaml);
        Assert.DoesNotContain("SchemaControl x:Name=\"SchemaControl\"", xaml);
    }

    [Fact]
    public void SidebarTemplate_DefinesTabContentVisibilityBindings()
    {
        string xaml = ReadSidebarXaml();

        Assert.Contains("x:Name=\"NodesControl\"", xaml);
        Assert.Contains("DataContext=\"{Binding NodesList}\"", xaml);
        Assert.Contains("<ctrl:ConnectionTabControl", xaml);
        Assert.Contains("x:Name=\"ConnectionControl\"", xaml);
        Assert.Contains("DataContext=\"{Binding ConnectionManager}\"", xaml);
        Assert.Contains("IsVisible=\"{Binding ShowNodes}\"", xaml);
        Assert.Contains("IsVisible=\"{Binding ShowConnection}\"", xaml);
    }

    [Fact]
    public void SidebarTemplate_UsesSidebarCommandsForTabSwitching()
    {
        string xaml = ReadSidebarXaml();

        Assert.Contains("Command=\"{Binding SelectNodesCommand}\"", xaml);
        Assert.Contains("Command=\"{Binding SelectConnectionCommand}\"", xaml);
    }

    [Fact]
    public void SidebarTemplate_BindsFooterAddNodeCommand()
    {
        string xaml = ReadSidebarXaml();

        Assert.Contains("Command=\"{Binding AddNodeCommand}\"", xaml);
        Assert.Contains("IsVisible=\"{Binding ShowNodes}\"", xaml);
    }

    [Fact]
    public void SidebarTemplate_ActiveTabUsesPrimaryAccentBackground()
    {
        string xaml = ReadSidebarXaml();

        Assert.Contains("Classes.active=\"{Binding ShowNodes}\"", xaml);
        Assert.Contains("Classes.active=\"{Binding ShowConnection}\"", xaml);
    }

    private static string ReadSidebarXaml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(
                dir.FullName, "src", "AkkornStudio.UI",
                "Controls", "SidebarLeft", "SidebarControl.axaml");

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate SidebarControl.axaml.");
    }
}
