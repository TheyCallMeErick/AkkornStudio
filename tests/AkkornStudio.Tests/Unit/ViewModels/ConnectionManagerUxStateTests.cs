using AkkornStudio.UI.Services.ConnectionManager;
using AkkornStudio.UI.Services.Benchmark;
using System.Reflection;
using AkkornStudio.Core;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.Services.Modal;
using AkkornStudio.UI.ViewModels;
using Xunit;

namespace AkkornStudio.Tests.Unit.ViewModels;

public class ConnectionManagerUxStateTests
{
    [Fact]
    public void OpenNewProfileCommand_OpensDialogAndStartsEditing()
    {
        var vm = new ConnectionManagerViewModel
        {
            IsVisible = false,
        };

        vm.OpenNewProfileCommand.Execute(null);

        Assert.True(vm.IsVisible);
        Assert.True(vm.IsEditing);
    }

    [Fact]
    public void ConnectOrOpenManagerCommand_WithoutProfiles_OpensDialogAndStartsEditing()
    {
        var modalManager = new RecordingModalManager();
        var vm = new ConnectionManagerViewModel(globalModalManager: modalManager)
        {
            IsVisible = false,
        };
        vm.Profiles.Clear();
        vm.SelectedProfile = null;

        vm.ConnectOrOpenManagerCommand.Execute(null);

        Assert.Equal(GlobalModalKind.ConnectionManager, modalManager.LastRequest?.Kind);
        Assert.True(modalManager.LastRequest?.BeginNewProfile);
        Assert.False(modalManager.LastRequest?.KeepStartVisible);
    }

    [Fact]
    public void ConnectOrOpenManagerCommand_WithInvalidProfile_OnlyOpensManagerWithoutConnecting()
    {
        var modalManager = new RecordingModalManager();
        var vm = new ConnectionManagerViewModel(globalModalManager: modalManager);
        vm.Profiles.Clear();
        var profile = new ConnectionProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Local",
            Provider = DatabaseProvider.Postgres,
            Host = "localhost",
            Port = 5432,
            Database = string.Empty,
            Username = "u",
            Password = "p",
        };

        vm.Profiles.Add(profile);
        vm.SelectedProfile = profile;
        vm.IsVisible = false;
        vm.IsConnecting = false;

        vm.ConnectOrOpenManagerCommand.Execute(null);

        Assert.Equal(GlobalModalKind.ConnectionManager, modalManager.LastRequest?.Kind);
        Assert.False(modalManager.LastRequest?.BeginNewProfile);
        Assert.False(vm.IsConnecting);
    }

    [Fact]
    public void Open_WithSavedProfiles_AlwaysSelectsFirstProfile()
    {
        var vm = new ConnectionManagerViewModel();
        vm.Profiles.Clear();

        var first = new ConnectionProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Primary",
            Provider = DatabaseProvider.Postgres,
            Host = "localhost",
            Port = 5432,
            Database = "db1",
            Username = "u",
            Password = "p",
        };
        var second = new ConnectionProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Secondary",
            Provider = DatabaseProvider.Postgres,
            Host = "localhost",
            Port = 5432,
            Database = "db2",
            Username = "u",
            Password = "p",
        };

        vm.Profiles.Add(first);
        vm.Profiles.Add(second);
        vm.SelectedProfile = second;

        vm.Open();

        Assert.Same(first, vm.SelectedProfile);
    }

    [Fact]
    public void ConnectOrOpenManagerCommand_WithSavedProfiles_SelectsFirstProfileAndAvoidsEditorMode()
    {
        var modalManager = new RecordingModalManager();
        var vm = new ConnectionManagerViewModel(globalModalManager: modalManager);
        vm.Profiles.Clear();
        vm.SelectedProfile = null;
        var first = new ConnectionProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Primary",
            Provider = DatabaseProvider.Postgres,
            Host = "localhost",
            Port = 5432,
            Database = "db1",
            Username = "u",
            Password = "p",
        };
        var second = new ConnectionProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Secondary",
            Provider = DatabaseProvider.Postgres,
            Host = "localhost",
            Port = 5432,
            Database = "db2",
            Username = "u",
            Password = "p",
        };

        vm.Profiles.Add(first);
        vm.Profiles.Add(second);
        vm.SelectedProfile = null;
        vm.IsEditing = true;

        vm.ConnectOrOpenManagerCommand.Execute(null);

        Assert.Equal(GlobalModalKind.ConnectionManager, modalManager.LastRequest?.Kind);
        Assert.False(modalManager.LastRequest?.BeginNewProfile);
        Assert.False(vm.IsConnecting);
        Assert.Same(first, vm.SelectedProfile);
        Assert.False(vm.IsEditing);
    }

    [Fact]
    public void ConnectOrOpenManagerCommand_FallsBackToLocalState_WhenNoGlobalHostIsSubscribed()
    {
        var vm = new ConnectionManagerViewModel(globalModalManager: new RecordingModalManager(hasSubscriber: false));
        vm.Profiles.Clear();

        vm.ConnectOrOpenManagerCommand.Execute(null);

        Assert.True(vm.IsVisible);
        Assert.True(vm.IsEditing);
    }

    [Fact]
    public void Connect_DoesNotCloseDialogImmediately()
    {
        var vm = new ConnectionManagerViewModel
        {
            IsVisible = true,
        };

        var profile = new ConnectionProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Local",
            Provider = DatabaseProvider.Postgres,
            Host = "localhost",
            Port = 5432,
            Database = "db",
            Username = "u",
            Password = "p",
        };

        vm.Profiles.Add(profile);
        vm.SelectedProfile = profile;

        vm.ConnectCommand.Execute(null);

        Assert.True(vm.IsVisible);
    }

    [Fact]
    public void NewProfileFlow_RequiresSavedProfileBeforeConnect()
    {
        var vm = new ConnectionManagerViewModel();

        vm.OpenNewProfileCommand.Execute(null);

        Assert.False(vm.ConnectCommand.CanExecute(null));
        Assert.False(vm.CanShowConnectAction);
        Assert.False(vm.CanShowDisconnectAction);
    }

    [Fact]
    public void SavedProfileFlow_ShowsConnectOrDisconnectBasedOnActiveSelection()
    {
        var vm = new ConnectionManagerViewModel();
        var profile = new ConnectionProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Saved",
            Provider = DatabaseProvider.Postgres,
            Host = "localhost",
            Port = 5432,
            Database = "db",
            Username = "u",
            Password = "p",
        };

        vm.Profiles.Add(profile);
        vm.SelectedProfile = profile;

        Assert.True(vm.ConnectCommand.CanExecute(null));
        Assert.True(vm.CanShowConnectAction);
        Assert.False(vm.CanShowDisconnectAction);

        vm.ActiveProfileId = profile.Id;

        Assert.False(vm.CanShowConnectAction);
        Assert.True(vm.CanShowDisconnectAction);
    }

    [Fact]
    public async Task LoadDatabaseTablesAsync_WithoutSearchMenu_ReportsFailureAndKeepsDialogVisible()
    {
        var vm = new ConnectionManagerViewModel
        {
            IsVisible = true,
            SearchMenu = null,
        };

        var profile = new ConnectionProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Local",
            Provider = DatabaseProvider.Postgres,
            Host = "localhost",
            Port = 5432,
            Database = "db",
            Username = "u",
            Password = "p",
        };

        MethodInfo method = typeof(ConnectionManagerViewModel)
            .GetMethod(
                "LoadDatabaseTablesAsync",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(ConnectionProfile)],
                modifiers: null
            )!;

        var task = (Task)method.Invoke(vm, [profile])!;
        await task;

        Assert.True(vm.IsVisible);
        Assert.False(vm.IsConnecting);
        Assert.Contains(LocalizationService.Instance["connection.status.failedPrefix"], vm.TestStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConnectionPickerSearchQuery_FiltersCardsByDatabaseAndConnectionName()
    {
        var vm = new ConnectionManagerViewModel();
        vm.Profiles.Clear();

        var finance = new ConnectionProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Finance",
            Provider = DatabaseProvider.Postgres,
            Host = "localhost",
            Port = 5432,
            Database = "erp_finance",
            Username = "u",
            Password = "p",
        };
        var crm = new ConnectionProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = "CRM",
            Provider = DatabaseProvider.SqlServer,
            Host = "localhost",
            Port = 1433,
            Database = "crm_main",
            Username = "u",
            Password = "p",
        };

        vm.Profiles.Add(finance);
        vm.Profiles.Add(crm);

        vm.ConnectionPickerSearchQuery = "finance";

        Assert.Single(vm.FilteredConnectionCards);
        Assert.Equal("Finance", vm.FilteredConnectionCards[0].ConnectionName);

        vm.ConnectionPickerSearchQuery = "crm_main";

        Assert.Single(vm.FilteredConnectionCards);
        Assert.Equal("CRM", vm.FilteredConnectionCards[0].ConnectionName);
    }

    [Fact]
    public void ConnectionCards_ExposeSvgProviderIconForKnownDatabase()
    {
        var vm = new ConnectionManagerViewModel();
        vm.Profiles.Clear();

        vm.Profiles.Add(new ConnectionProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = "SQL",
            Provider = DatabaseProvider.SqlServer,
            Host = "localhost",
            Port = 1433,
            Database = "db",
            Username = "u",
            Password = "p",
        });

        Assert.Single(vm.ConnectionCards);
        ConnectionManagerViewModel.ConnectionProfileCardItem card = vm.ConnectionCards[0];
        Assert.Equal(
            "avares://AkkornStudio.UI/Assets/Images/Icons/Databases/Microsoft_SQL_Server.svg",
            card.ProviderIconAssetUri);
    }

    private sealed class RecordingModalManager(bool hasSubscriber = true) : IGlobalModalManager
    {
        public event Action<GlobalModalRequest>? ModalRequested;

        public GlobalModalRequest? LastRequest { get; private set; }

        public bool Request(GlobalModalRequest request)
        {
            LastRequest = request;
            if (!hasSubscriber)
                return false;

            ModalRequested?.Invoke(request);
            return true;
        }

        public bool RequestConnectionManager(bool beginNewProfile = false, bool keepStartVisible = false) =>
            Request(new GlobalModalRequest(
                Kind: GlobalModalKind.ConnectionManager,
                BeginNewProfile: beginNewProfile,
                KeepStartVisible: keepStartVisible
            ));

        public bool RequestSettings(bool keepStartVisible = false) =>
            Request(new GlobalModalRequest(
                Kind: GlobalModalKind.Settings,
                KeepStartVisible: keepStartVisible
            ));
    }
}


