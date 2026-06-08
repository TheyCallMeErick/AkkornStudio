using System.IO;

namespace AkkornStudio.Tests.Unit.Views;

public sealed class MainWindowErDiagramSidebarRegressionTests
{
    [Fact]
    public void SyncFixedPageSidebars_HidesHostsWhenPageHasNoSidebars()
    {
        string source = ReadMainWindowModeSource();

        Assert.Contains("!CurrentShell.ActivePageContract.ShowsDiagramSidebar", source);
        Assert.Contains("&& !CurrentShell.ActivePageContract.ShowsSqlEditorSidebar", source);
        Assert.Contains("HideLeftSidebarForMode();", source);
        Assert.Contains("HideRightSidebarForMode();", source);
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
