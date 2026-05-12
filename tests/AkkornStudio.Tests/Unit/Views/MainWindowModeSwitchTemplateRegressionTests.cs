using System.IO;

namespace AkkornStudio.Tests.Unit.Views;

public sealed class MainWindowModeSwitchTemplateRegressionTests
{
    [Fact]
    public void ModeSwitch_DoesNotExposeSqlEditorButtonInHeader()
    {
        string xaml = ReadMainWindowXaml();

        Assert.DoesNotContain("Name=\"SqlEditorModeBtn\"", xaml);
        Assert.DoesNotContain("Click=\"SqlEditorModeBtn_Click\"", xaml);
    }

    [Fact]
    public void MainWindow_HostsSqlEditorControlInSqlEditorMode()
    {
        string xaml = ReadMainWindowXaml();

        Assert.Contains("xmlns:sqled=\"using:AkkornStudio.UI.Controls.SqlEditor\"", xaml);
        Assert.Contains("IsVisible=\"{ReflectionBinding DataContext.ActivePageContract.ShowsSqlEditorPage, ElementName=RootWindow}\"", xaml);
        Assert.Contains("<sqled:SqlEditorControl Grid.Column=\"2\"", xaml);
        Assert.Contains("DataContext=\"{ReflectionBinding DataContext.ActiveSqlEditorDocument, ElementName=RootWindow}\"", xaml);
    }

    [Fact]
    public void MainWindow_UsesModeAwareSidebarModules_ForQueryAndSqlEditor()
    {
        string xaml = ReadMainWindowXaml();

        Assert.Contains("DataContext=\"{ReflectionBinding DataContext.ActiveDiagramSidebar, ElementName=RootWindow}\"", xaml);
        Assert.Contains("DataContext=\"{ReflectionBinding DataContext.ActiveDiagramPropertyPanel, ElementName=RootWindow}\"", xaml);
        Assert.Contains("<sqled:SqlEditorLeftSidebarControl", xaml);
        Assert.Contains("DataContext=\"{ReflectionBinding DataContext.ActiveSqlEditorDocument, ElementName=RootWindow}\"", xaml);
        Assert.Contains("IsVisible=\"{ReflectionBinding DataContext.ActivePageContract.ShowsSqlEditorSidebar, ElementName=RootWindow}\"", xaml);
    }

    [Fact]
    public void MainWindow_UsesFloatingPreviewDock_AndRemovesTopPreviewButton()
    {
        string xaml = ReadMainWindowXaml();

        Assert.DoesNotContain("Name=\"TogglePreviewBtn\"", xaml);
        Assert.Contains("x:Name=\"PreviewDockHost\"", xaml);
        Assert.Contains("Name=\"PreviewDockOpenBtn\"", xaml);
        Assert.Contains("Name=\"PreviewDockDiagnosticsBtn\"", xaml);
    }

    private static string ReadMainWindowXaml()
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
                "MainWindow.axaml"
            );

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate MainWindow.axaml from test base directory.");
    }
}
