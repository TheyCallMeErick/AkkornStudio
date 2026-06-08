using System.IO;
using Xunit;

namespace AkkornStudio.Tests.Unit.Controls;

public class SidebarControlCodeBehindRegressionTests
{
    [Fact]
    public void SidebarCodeBehind_DoesNotManuallyWireTabButtonsOrChildDataContexts()
    {
        string source = ReadSidebarCodeBehind();

        Assert.DoesNotContain("_buttonsWired", source);
        Assert.DoesNotContain("nodesButton.Click +=", source);
        Assert.DoesNotContain("connectionButton.Click +=", source);
        Assert.DoesNotContain("nodesControl.DataContext =", source);
        Assert.DoesNotContain("connectionControl.DataContext =", source);
        Assert.DoesNotContain("AnimateActiveTabAsync", source);
    }

    [Fact]
    public void SidebarCodeBehind_AttachesAndDetachesAddNodeSubscriptionSafely()
    {
        string source = ReadSidebarCodeBehind();

        Assert.Contains("AttachVmSubscriptions(DataContext as SidebarViewModel);", source);
        Assert.Contains("_subscribedVm.AddNodeRequested += OnAddNodeRequested;", source);
        Assert.Contains("_subscribedVm.AddNodeRequested -= OnAddNodeRequested;", source);
        Assert.Contains("search?.Focus();", source);
    }

    private static string ReadSidebarCodeBehind()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(
                dir.FullName,
                "src",
                "AkkornStudio.UI",
                "Controls",
                "SidebarLeft",
                "SidebarControl.axaml.cs"
            );

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate SidebarControl.axaml.cs from test base directory.");
    }
}
