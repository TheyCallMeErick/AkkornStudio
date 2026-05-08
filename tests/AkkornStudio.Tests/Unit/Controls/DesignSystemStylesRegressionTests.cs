using System.IO;
using Xunit;

namespace AkkornStudio.Tests.Unit.Controls;

public class DesignSystemStylesRegressionTests
{
    [Fact]
    public void AppStyles_Defines_SharedProjectComponentStyles()
    {
        string styles = ReadRepoFile("src", "AkkornStudio.UI", "Assets", "Themes", "AppStyles.axaml");

        Assert.Contains("Style Selector=\"Border.surface-panel\"", styles);
        Assert.Contains("Style Selector=\"Border.surface-card\"", styles);
        Assert.Contains("Style Selector=\"Border.callout-subtle\"", styles);
        Assert.Contains("Style Selector=\"Border.badge\"", styles);
        Assert.Contains("Style Selector=\"Border.surface-floating\"", styles);
        Assert.Contains("Style Selector=\"Border.surface-floating-header\"", styles);
        Assert.Contains("Style Selector=\"Border.surface-floating-footer\"", styles);
        Assert.Contains("Style Selector=\"Border.kbd-chip\"", styles);
        Assert.Contains("Style Selector=\"Border.list-row\"", styles);
        Assert.Contains("Style Selector=\"TextBox.field\"", styles);
        Assert.Contains("Style Selector=\"ComboBox.field\"", styles);
        Assert.Contains("Style Selector=\"Button.tb\"", styles);
        Assert.Contains("Style Selector=\"Button.icon-ghost\"", styles);
        Assert.Contains("Style Selector=\"Button.panel-tab\"", styles);
        Assert.Contains("Style Selector=\"ToggleButton.secondary\"", styles);
        Assert.Contains("Style Selector=\"Expander.app-accordion\"", styles);
    }

    [Fact]
    public void CoreViews_Consume_SharedDesignSystemClasses()
    {
        string ddl = ReadRepoFile("src", "AkkornStudio.UI", "Controls", "Shell", "DdlSchemaAnalysisWorkspaceControl.axaml");
        string schemaBrowser = ReadRepoFile("src", "AkkornStudio.UI", "Controls", "SqlEditor", "SqlEditorSchemaBrowserControl.axaml");
        string diagnostics = ReadRepoFile("src", "AkkornStudio.UI", "Controls", "SidebarLeft", "SidebarDiagnosticsControl.axaml");
        string propertyPanel = ReadRepoFile("src", "AkkornStudio.UI", "Controls", "PropertyPanel", "PropertyPanelControl.axaml");
        string commandPalette = ReadRepoFile("src", "AkkornStudio.UI", "Controls", "CommandPalette", "CommandPaletteControl.axaml");
        string benchmarkOverlay = ReadRepoFile("src", "AkkornStudio.UI", "Controls", "Benchmark", "BenchmarkOverlay.axaml");
        string appDiagnostics = ReadRepoFile("src", "AkkornStudio.UI", "Controls", "AppDiagnostics", "AppDiagnosticsControl.axaml");
        string dataPreview = ReadRepoFile("src", "AkkornStudio.UI", "Views", "Panels", "DataPreviewPanel.axaml");
        string sqlResultPanel = ReadRepoFile("src", "AkkornStudio.UI", "Controls", "SqlEditor", "SqlEditorResultPanel.axaml");
        string sqlRightSidebar = ReadRepoFile("src", "AkkornStudio.UI", "Controls", "SqlEditor", "SqlEditorRightSidebarControl.axaml");
        string sqlEditor = ReadRepoFile("src", "AkkornStudio.UI", "Controls", "SqlEditor", "SqlEditorControl.axaml");
        string sqlTabBar = ReadRepoFile("src", "AkkornStudio.UI", "Controls", "SqlEditor", "SqlEditorTabBar.axaml");
        string mainWindow = ReadRepoFile("src", "AkkornStudio.UI", "Views", "Shell", "MainWindow.axaml");
        string flowVersions = ReadRepoFile("src", "AkkornStudio.UI", "Controls", "FlowVersions", "FlowVersionOverlay.axaml");
        string autoJoin = ReadRepoFile("src", "AkkornStudio.UI", "Controls", "AutoJoin", "AutoJoinOverlay.axaml");
        string outputPreview = ReadRepoFile("src", "AkkornStudio.UI", "Controls", "Shell", "OutputPreviewModalControl.axaml");
        string connectionManager = ReadRepoFile("src", "AkkornStudio.UI", "Controls", "ConnectionManager", "ConnectionManagerControl.axaml");
        string explainPlan = ReadRepoFile("src", "AkkornStudio.UI", "Controls", "ExplainPlan", "ExplainPlanOverlay.axaml");
        string searchMenu = ReadRepoFile("src", "AkkornStudio.UI", "Controls", "SearchMenu", "SearchMenuControl.axaml");
        string liveSqlBar = ReadRepoFile("src", "AkkornStudio.UI", "Controls", "LiveSqlBar", "LiveSqlBar.axaml");
        string startMenu = ReadRepoFile("src", "AkkornStudio.UI", "Controls", "Start", "StartMenuControl.axaml");
        string sidebar = ReadRepoFile("src", "AkkornStudio.UI", "Controls", "SidebarLeft", "SidebarControl.axaml");
        string connectionTab = ReadRepoFile("src", "AkkornStudio.UI", "Controls", "SidebarLeft", "ConnectionTabControl.axaml");
        string nodesList = ReadRepoFile("src", "AkkornStudio.UI", "Controls", "SidebarLeft", "NodesListControl.axaml");
        string schema = ReadRepoFile("src", "AkkornStudio.UI", "Controls", "SidebarLeft", "SchemaControl.axaml");
        string nodeControl = ReadRepoFile("src", "AkkornStudio.UI", "Controls", "Node", "NodeControl.axaml");
        string sqlImporter = ReadRepoFile("src", "AkkornStudio.UI", "Controls", "SqlImporter", "SqlImporterOverlay.axaml");

        Assert.Contains("Classes=\"surface-panel\"", ddl);
        Assert.Contains("Classes=\"app-accordion\"", ddl);
        Assert.Contains("Classes=\"secondary compact\"", ddl);

        Assert.Contains("Classes=\"field\"", schemaBrowser);
        Assert.Contains("Classes=\"app-accordion\"", schemaBrowser);
        Assert.Contains("Classes=\"badge info\"", schemaBrowser);

        Assert.Contains("Classes=\"app-accordion diag-category\"", diagnostics);
        Assert.Contains("Classes=\"badge danger\"", diagnostics);

        Assert.Contains("Classes=\"callout-subtle\"", propertyPanel);
        Assert.Contains("Classes=\"secondary compact\"", propertyPanel);
        Assert.Contains("Classes=\"surface-panel\"", propertyPanel);

        Assert.Contains("Classes=\"surface-floating\"", commandPalette);
        Assert.Contains("Classes=\"surface-floating-header\"", commandPalette);
        Assert.Contains("Classes=\"kbd-chip\"", commandPalette);

        Assert.Contains("surface-floating accented", benchmarkOverlay);
        Assert.Contains("surface-floating-header", benchmarkOverlay);
        Assert.Contains("surface-floating-footer", benchmarkOverlay);
        Assert.Contains("hist-row", benchmarkOverlay);
        Assert.Contains("list-row", benchmarkOverlay);

        Assert.Contains("Classes=\"surface-floating\"", appDiagnostics);
        Assert.Contains("Classes=\"surface-floating-header\"", appDiagnostics);
        Assert.Contains("Classes=\"kbd-chip\"", appDiagnostics);

        Assert.Contains("Classes=\"badge\"", dataPreview);
        Assert.Contains("Classes=\"kbd-chip\"", dataPreview);
        Assert.Contains("Classes=\"panel-tab\"", dataPreview);
        Assert.Contains("Classes=\"callout-subtle\"", dataPreview);
        Assert.Contains("Classes=\"surface-panel\"", dataPreview);
        Assert.Contains("Classes=\"surface-card\"", dataPreview);

        Assert.Contains("Classes=\"surface-panel\"", sqlResultPanel);
        Assert.Contains("Classes=\"surface-card\"", sqlResultPanel);
        Assert.Contains("Classes=\"secondary compact\"", sqlResultPanel);
        Assert.Contains("Classes=\"field\"", sqlResultPanel);

        Assert.Contains("Classes=\"surface-card\"", sqlRightSidebar);
        Assert.Contains("Classes=\"field\"", sqlRightSidebar);
        Assert.Contains("Classes=\"callout-subtle\"", sqlRightSidebar);
        Assert.Contains("Classes=\"secondary compact\"", sqlRightSidebar);

        Assert.Contains("Classes=\"surface-panel\"", sqlEditor);
        Assert.Contains("Classes=\"surface-floating\"", sqlEditor);
        Assert.Contains("Classes=\"field\"", sqlEditor);
        Assert.Contains("Classes=\"secondary compact\"", sqlEditor);
        Assert.Contains("Classes=\"icon-ghost\"", sqlEditor);

        Assert.Contains("Classes=\"surface-panel\"", sqlTabBar);
        Assert.Contains("Classes=\"tb\"", sqlTabBar);
        Assert.Contains("Classes=\"icon-ghost\"", sqlTabBar);

        Assert.Contains("Classes=\"surface-floating\"", mainWindow);
        Assert.Contains("Classes=\"surface-floating-header\"", mainWindow);
        Assert.Contains("Classes=\"surface-card\"", mainWindow);
        Assert.Contains("Classes=\"settings-nav\"", mainWindow);
        Assert.Contains("Classes=\"field\"", mainWindow);
        Assert.Contains("Classes=\"secondary compact\"", mainWindow);
        Assert.Contains("Classes=\"icon-ghost\"", mainWindow);

        Assert.Contains("Classes=\"surface-floating\"", flowVersions);
        Assert.Contains("Classes=\"surface-floating-header\"", flowVersions);
        Assert.Contains("Classes=\"surface-floating-footer\"", flowVersions);
        Assert.Contains("Classes=\"field\"", flowVersions);

        Assert.Contains("Classes=\"surface-floating\"", autoJoin);
        Assert.Contains("Classes=\"surface-floating-header\"", autoJoin);
        Assert.Contains("Classes=\"surface-floating-footer\"", autoJoin);
        Assert.Contains("surface-card", autoJoin);
        Assert.Contains("Classes=\"badge info\"", autoJoin);

        Assert.Contains("Classes=\"surface-floating\"", outputPreview);
        Assert.Contains("Classes=\"surface-floating-header\"", outputPreview);
        Assert.Contains("Classes=\"surface-floating-footer\"", outputPreview);
        Assert.Contains("Classes=\"panel-tab\"", outputPreview);
        Assert.Contains("Classes=\"icon-ghost\"", outputPreview);
        Assert.Contains("Classes=\"secondary compact\"", outputPreview);

        Assert.Contains("Classes=\"surface-floating\"", connectionManager);
        Assert.Contains("Classes=\"surface-floating-header\"", connectionManager);
        Assert.Contains("Classes=\"surface-card\"", connectionManager);
        Assert.Contains("Classes=\"field\"", connectionManager);
        Assert.Contains("Classes=\"icon-ghost\"", connectionManager);

        Assert.Contains("Classes=\"surface-floating\"", explainPlan);
        Assert.Contains("Classes=\"surface-floating-header\"", explainPlan);
        Assert.Contains("Classes=\"surface-floating-footer\"", explainPlan);
        Assert.Contains("Classes=\"badge info\"", explainPlan);
        Assert.Contains("Classes=\"badge warning\"", explainPlan);

        Assert.Contains("Classes=\"surface-floating\"", searchMenu);
        Assert.Contains("Classes=\"surface-floating-header\"", searchMenu);
        Assert.Contains("Classes=\"surface-floating-footer\"", searchMenu);
        Assert.Contains("Classes=\"field\"", searchMenu);
        Assert.Contains("Classes=\"kbd-chip\"", searchMenu);

        Assert.Contains("Classes=\"surface-panel\"", liveSqlBar);
        Assert.Contains("Classes=\"field\"", liveSqlBar);
        Assert.Contains("Classes=\"tb compact\"", liveSqlBar);
        Assert.Contains("Classes=\"surface-card\"", liveSqlBar);
        Assert.Contains("Classes=\"badge warning\"", liveSqlBar);

        Assert.Contains("Classes=\"field\"", startMenu);
        Assert.Contains("Classes=\"icon-ghost\"", startMenu);

        Assert.Contains("Classes=\"surface-panel\"", sidebar);
        Assert.Contains("Classes=\"panel-tab\"", sidebar);
        Assert.Contains("Classes=\"success compact\"", sidebar);

        Assert.Contains("Classes=\"surface-card\"", connectionTab);
        Assert.Contains("Classes=\"secondary compact\"", connectionTab);

        Assert.Contains("Classes=\"surface-panel\"", nodesList);
        Assert.Contains("Classes=\"icon-ghost\"", nodesList);

        Assert.Contains("surface-card", schema);
        Assert.Contains("Classes=\"success\"", schema);

        Assert.Contains("Classes=\"badge\"", nodeControl);
        Assert.Contains("Classes=\"badge info\"", nodeControl);
        Assert.Contains("Classes=\"callout-subtle\"", nodeControl);

        Assert.Contains("Classes=\"surface-floating\"", sqlImporter);
        Assert.Contains("Classes=\"surface-floating-header\"", sqlImporter);
        Assert.Contains("Classes=\"field\"", sqlImporter);
        Assert.Contains("Classes=\"badge success\"", sqlImporter);
        Assert.Contains("Classes=\"callout-subtle warning\"", sqlImporter);
    }

    private static string ReadRepoFile(params string[] relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var segments = new string[relativePath.Length + 1];
            segments[0] = dir.FullName;
            for (int i = 0; i < relativePath.Length; i++)
                segments[i + 1] = relativePath[i];

            string candidate = Path.Combine(segments);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(relativePath)} from test base directory.");
    }
}
