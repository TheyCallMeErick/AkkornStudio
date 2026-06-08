using AkkornStudio.UI.Services.Benchmark;
using Avalonia;
using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.Nodes;
using AkkornStudio.UI.ViewModels;
using Xunit;

namespace AkkornStudio.Tests.Unit.ViewModels;

public class ShellViewModelDdlModeTests
{
    [Fact]
    public void Shell_StartsInQueryMode_WithoutDdlCanvas()
    {
        var vm = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        vm.EnterCanvas();

        Assert.Equal(ShellViewModel.AppMode.Query, vm.ActiveMode);
        Assert.True(vm.IsQueryModeActive);
        Assert.False(vm.IsDdlModeActive);
        Assert.Null(vm.DdlCanvas);
    }

    [Fact]
    public void SetActiveDocumentType_Ddl_InitializesStubCanvas()
    {
        var vm = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());

        vm.ActivateDocument(AkkornStudio.UI.Services.Workspace.Models.WorkspaceDocumentType.DdlCanvas);

        Assert.Equal(ShellViewModel.AppMode.Ddl, vm.ActiveMode);
        Assert.False(vm.IsQueryModeActive);
        Assert.True(vm.IsDdlModeActive);
        Assert.NotNull(vm.DdlCanvas);
        Assert.True(vm.DdlCanvas!.IsEmpty);
    }

    [Fact]
    public void EnsureDdlCanvas_ReturnsSameInstance_OnSubsequentCalls()
    {
        var vm = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());

        var first = vm.EnsureDdlCanvas();
        var second = vm.EnsureDdlCanvas();

        Assert.Same(first, second);
    }

    [Fact]
    public void ModeCommands_SwitchBetweenQueryAndDdl()
    {
        var vm = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());

        vm.DdlModeCommand.Execute(null);
        Assert.Equal(ShellViewModel.AppMode.Ddl, vm.ActiveMode);

        vm.QueryModeCommand.Execute(null);
        Assert.Equal(ShellViewModel.AppMode.Query, vm.ActiveMode);
    }

    [Fact]
    public void SettingsSections_AllExpectedTitlesAndSubtitles_AreMapped()
    {
        var vm = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());

        vm.SelectSettingsSection(ShellViewModel.ESettingsSection.Editor);
        Assert.True(vm.IsEditorSectionSelected);
        Assert.False(string.IsNullOrWhiteSpace(vm.SettingsSectionTitle));
        Assert.False(string.IsNullOrWhiteSpace(vm.SettingsSectionSubtitle));

        vm.SelectSettingsSection(ShellViewModel.ESettingsSection.DateTime);
        string dateTimeTitle = vm.SettingsSectionTitle;
        Assert.False(string.IsNullOrWhiteSpace(dateTimeTitle));

        vm.SelectSettingsSection(ShellViewModel.ESettingsSection.KeyboardShortcuts);
        string keyboardTitle = vm.SettingsSectionTitle;
        string keyboardSubtitle = vm.SettingsSectionSubtitle;
        Assert.False(string.IsNullOrWhiteSpace(keyboardTitle));
        Assert.False(string.IsNullOrWhiteSpace(keyboardSubtitle));
        Assert.NotEqual(dateTimeTitle, keyboardTitle);

        vm.SelectSettingsSection(ShellViewModel.ESettingsSection.Notification);
        string notificationTitle = vm.SettingsSectionTitle;
        string notificationSubtitle = vm.SettingsSectionSubtitle;
        Assert.False(string.IsNullOrWhiteSpace(notificationTitle));
        Assert.NotEqual(keyboardTitle, notificationTitle);
        Assert.NotEqual(keyboardSubtitle, notificationSubtitle);

        vm.SelectSettingsSection(ShellViewModel.ESettingsSection.Accessibility);
        Assert.False(string.IsNullOrWhiteSpace(vm.SettingsSectionTitle));
        Assert.NotEqual(notificationTitle, vm.SettingsSectionTitle);
        Assert.False(string.IsNullOrWhiteSpace(vm.SettingsSectionSubtitle));
    }

    [Fact]
    public void AppVersionLabel_IsResolvedToNonEmptyValue()
    {
        var vm = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());

        Assert.False(string.IsNullOrWhiteSpace(vm.AppVersionLabel));
    }

    [Fact]
    public void ActiveCanvasContext_DerivesFromModeAndSubcanvasState()
    {
        var vm = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());

        Assert.Equal(CanvasContext.Query, vm.ActiveCanvasContext);

        vm.ActivateDocument(AkkornStudio.UI.Services.Workspace.Models.WorkspaceDocumentType.DdlCanvas);
        Assert.Equal(CanvasContext.Ddl, vm.ActiveCanvasContext);

        vm.SetViewSubcanvasActive(true);
        Assert.Equal(CanvasContext.ViewSubcanvas, vm.ActiveCanvasContext);

        vm.SetViewSubcanvasActive(false);
        Assert.Equal(CanvasContext.Ddl, vm.ActiveCanvasContext);

        vm.ActivateDocument(AkkornStudio.UI.Services.Workspace.Models.WorkspaceDocumentType.QueryCanvas);
        Assert.Equal(CanvasContext.Query, vm.ActiveCanvasContext);
    }

    [Fact]
    public void ModeSwitch_UsesIndependentActiveCanvasInstances_WithoutLeakingNodesAcrossModes()
    {
        var vm = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());

        CanvasViewModel queryCanvas = vm.EnsureCanvas();
        queryCanvas.SpawnTableNode("public.orders", [("id", PinDataType.Number)], new Point(10, 10));
        Assert.Same(queryCanvas, vm.ActiveCanvas);
        Assert.Single(queryCanvas.Nodes);

        vm.ActivateDocument(AkkornStudio.UI.Services.Workspace.Models.WorkspaceDocumentType.DdlCanvas);
        CanvasViewModel ddlCanvas = Assert.IsType<CanvasViewModel>(vm.ActiveCanvas);
        Assert.Same(vm.DdlCanvas, ddlCanvas);
        Assert.Empty(ddlCanvas.Nodes);

        ddlCanvas.SpawnNode(NodeDefinitionRegistry.Get(NodeType.TableDefinition), new Point(40, 20));
        Assert.Single(ddlCanvas.Nodes);
        Assert.Single(queryCanvas.Nodes);

        vm.ActivateDocument(AkkornStudio.UI.Services.Workspace.Models.WorkspaceDocumentType.QueryCanvas);
        Assert.Same(queryCanvas, vm.ActiveCanvas);
        Assert.Single(queryCanvas.Nodes);
        Assert.Single(ddlCanvas.Nodes);
    }

    [Fact]
    public void EnsureCanvasAndDdlCanvas_UseSharedShellToastCenter()
    {
        var vm = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());

        CanvasViewModel queryCanvas = vm.EnsureCanvas();
        CanvasViewModel ddlCanvas = vm.EnsureDdlCanvas();

        Assert.Same(vm.Toasts, queryCanvas.Toasts);
        Assert.Same(vm.Toasts, ddlCanvas.Toasts);
    }

    [Fact]
    public void EnsureDdlCanvas_DoesNotInheritMetadataAndConnection_FromQueryCanvasContext()
    {
        var vm = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());

        CanvasViewModel queryCanvas = vm.EnsureCanvas();
        DbMetadata metadata = CreateMetadata();
        ConnectionConfig config = new(
            Provider: DatabaseProvider.Postgres,
            Host: "localhost",
            Port: 5432,
            Database: "akkornstudio",
            Username: "user",
            Password: "pwd"
        );
        queryCanvas.SetDatabaseContext(metadata, config);

        CanvasViewModel ddlCanvas = vm.EnsureDdlCanvas();

        Assert.Null(ddlCanvas.DatabaseMetadata);
        Assert.Null(ddlCanvas.ActiveConnectionConfig);
    }

    [Fact]
    public void EnsureDdlCanvas_WhenAlreadyCreated_DoesNotSynchronizeMetadataFromQueryCanvas()
    {
        var vm = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());

        CanvasViewModel ddlCanvas = vm.EnsureDdlCanvas();
        LiveDdlBarViewModel liveDdl = Assert.IsType<LiveDdlBarViewModel>(ddlCanvas.LiveDdl);
        Assert.False(liveDdl.RunSchemaAnalysisCommand.CanExecute(null));

        CanvasViewModel queryCanvas = vm.EnsureCanvas();
        DbMetadata metadata = CreateMetadata();
        ConnectionConfig config = new(
            Provider: DatabaseProvider.Postgres,
            Host: "localhost",
            Port: 5432,
            Database: "akkornstudio",
            Username: "user",
            Password: "pwd"
        );
        queryCanvas.SetDatabaseContext(metadata, config);

        CanvasViewModel ensuredDdl = vm.EnsureDdlCanvas();

        Assert.Same(ddlCanvas, ensuredDdl);
        Assert.Null(ensuredDdl.DatabaseMetadata);
        Assert.Null(ensuredDdl.ActiveConnectionConfig);
        Assert.False(liveDdl.RunSchemaAnalysisCommand.CanExecute(null));
    }

    [Fact]
    public void SqlEditor_InheritsActiveConnectionContext_FromActiveCanvas()
    {
        // Per-tab model: a tab has one connection shared by its modes, so the SQL editor inherits
        // the active canvas's connection/metadata (instead of starting empty/independent). Query and
        // DDL still own separate managers; switching to SQL surfaces the live connection.
        var vm = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());

        CanvasViewModel queryCanvas = vm.EnsureCanvas();
        CanvasViewModel ddlCanvas = vm.EnsureDdlCanvas();

        queryCanvas.SetDatabaseContext(
            CreateMetadata(),
            new ConnectionConfig(
                Provider: DatabaseProvider.Postgres,
                Host: "query-host",
                Port: 5432,
                Database: "query_db",
                Username: "query_user",
                Password: "pwd"));

        ddlCanvas.SetDatabaseContext(
            CreateMetadata(),
            new ConnectionConfig(
                Provider: DatabaseProvider.SqlServer,
                Host: "ddl-host",
                Port: 1433,
                Database: "ddl_db",
                Username: "ddl_user",
                Password: "pwd"));

        vm.ActivateDocument(AkkornStudio.UI.Services.Workspace.Models.WorkspaceDocumentType.SqlEditor);
        SqlEditorViewModel sqlEditor = Assert.IsType<SqlEditorViewModel>(vm.ActiveSqlEditorDocument);

        // Query and DDL keep independent connection managers.
        Assert.NotSame(queryCanvas.ConnectionManager, ddlCanvas.ConnectionManager);

        // The SQL editor inherits the active canvas connection so its connection/metadata fill in.
        Assert.Same(queryCanvas.ConnectionManager, sqlEditor.SharedConnectionManager);
        ConnectionConfig? toolsConfig = sqlEditor.GetActiveConnectionConfigForTools();
        Assert.NotNull(toolsConfig);
        Assert.Equal("query-host", toolsConfig!.Host);
    }

    private static DbMetadata CreateMetadata() =>
        new(
            DatabaseName: "akkornstudio",
            Provider: DatabaseProvider.Postgres,
            ServerVersion: "16",
            CapturedAt: DateTimeOffset.UtcNow,
            Schemas: [new SchemaMetadata("public", [])],
            AllForeignKeys: []
        );
}
