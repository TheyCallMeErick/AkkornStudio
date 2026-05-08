using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Material.Icons;
using System.ComponentModel;
using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.UI.Serialization;
using AkkornStudio.UI.Controls;
using AkkornStudio.UI.Controls.Ddl;
using AkkornStudio.UI.Controls.Shell;
using AkkornStudio.UI.Services.Ddl;
using AkkornStudio.UI.Services;
using AkkornStudio.UI.Services.CommandPalette;
using AkkornStudio.UI.Services.Connection;
using AkkornStudio.UI.Services.Input.ShortcutRegistry;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.Services.Modal;
using AkkornStudio.UI.Services.Observability;
using AkkornStudio.UI.Services.Settings;
using AkkornStudio.UI.Services.Theming;
using AkkornStudio.UI.Services.Workspace.Models;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.Shortcuts;
using AkkornStudio.UI.ViewModels.Validation.Conventions;
using AkkornStudio.UI.ViewModels.Validation.Conventions.Implementations;

namespace AkkornStudio.UI;

public partial class MainWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly IGlobalModalManager _globalModalManager;

    private ShellViewModel CurrentShell => DataContext as ShellViewModel
        ?? throw new InvalidOperationException(
            L("error.mainWindow.invalidDataContext", "MainWindow DataContext must be a ShellViewModel.")
        );


    private bool _canvasInitialized;
    private ContextMenu? _titleMenu;
    private readonly HashSet<SidebarViewModel> _wiredSidebars = [];
    private readonly HashSet<ConnectionManagerViewModel> _wiredConnectionManagers = [];
    private ConnectionWorkspaceModule? _connectionModule;
    private SettingsWorkspaceModule? _settingsModule;
    private static readonly GridLength CollapsedRailWidth = new(24);
    private bool _queryModeLeftSidebarCollapsed;
    private bool _queryModeRightSidebarCollapsed;
    private bool _ddlModeLeftSidebarCollapsed;
    private bool _ddlModeRightSidebarCollapsed;

    // Services
    private MainWindowLayoutService? _layoutService;
    private SessionManagementService? _sessionService;
    private KeyboardInputHandler? _keyboardHandler;
    private FileOperationsService? _fileOps;
    private ExportService? _export;
    private PreviewService? _preview;
    private CommandPaletteFactory? _commandFactory;
    private ICommandPaletteService? _commandPaletteService;
    private IShortcutRegistry? _shortcutRegistry;
    private KeyboardShortcutsViewModel? _keyboardShortcutsViewModel;
    private PropertyChangedEventHandler? _windowTitleChangedHandler;
    private PropertyChangedEventHandler? _shellPropertyChangedHandler;
    private PropertyChangedEventHandler? _toastCenterPropertyChangedHandler;
    private PropertyChangedEventHandler? _outputPreviewPropertyChangedHandler;
    private readonly ThemeJsonSettingsService _themeJsonSettings;
    private readonly ICriticalFlowTelemetryService? _criticalFlowTelemetry;
    private readonly ICriticalFlowBaselineReportService? _criticalFlowBaselineReportService;
    private readonly ICriticalFlowRegressionAlertService? _criticalFlowRegressionAlertService;
    private bool _isGlobalModalManagerWired;
    private const double PreviewDockBaseBottomMargin = 44;
    private const double PreviewDockToastBottomMargin = 134;

    public MainWindow()
        : this(
            new ServiceCollection()
                .AddAkkornStudio()
                .AddSingleton<IAliasConvention, SnakeCaseConvention>()
                .AddSingleton<IAliasConvention, CamelCaseConvention>()
                .AddSingleton<IAliasConvention, PascalCaseConvention>()
                .AddSingleton<IAliasConvention, ScreamingSnakeCaseConvention>()
                .AddSingleton<IAliasConventionRegistry, AliasConventionRegistry>()
                .AddSingleton<IGlobalModalManager>(_ => GlobalModalManager.Instance)
                .BuildServiceProvider(),
            new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault()),
            new ThemeJsonSettingsService(),
            GlobalModalManager.Instance)
    {
    }

    public MainWindow(
        IServiceProvider services,
        ShellViewModel shell,
        ThemeJsonSettingsService themeJsonSettings,
        IGlobalModalManager? globalModalManager = null)
    {
        _services = services;
        _themeJsonSettings = themeJsonSettings;
        _globalModalManager = globalModalManager ?? GlobalModalManager.Instance;
        _criticalFlowTelemetry = _services.GetService<ICriticalFlowTelemetryService>();
        _criticalFlowBaselineReportService = _services.GetService<ICriticalFlowBaselineReportService>();
        _criticalFlowRegressionAlertService = _services.GetService<ICriticalFlowRegressionAlertService>();

        InitializeComponent();
        DataContext = shell;
        WireGlobalModalManager();

        WireHeaderMenus();
        WireToolbarMenuButtons();
        WireStartMenu();
        WireModeToggle();
        WirePreviewDock();
        Title = AppConstants.AppDisplayName;
    }

    private void WireGlobalModalManager()
    {
        if (_isGlobalModalManagerWired)
            return;

        _globalModalManager.ModalRequested += OnGlobalModalRequested;
        _isGlobalModalManagerWired = true;
        Closed += OnWindowClosed;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (!_isGlobalModalManagerWired)
            return;

        _globalModalManager.ModalRequested -= OnGlobalModalRequested;
        _isGlobalModalManagerWired = false;
        UnwirePreviewDock();
        Closed -= OnWindowClosed;
    }

    private void OnGlobalModalRequested(GlobalModalRequest request)
    {
        switch (request.Kind)
        {
            case GlobalModalKind.ConnectionManager:
                OpenConnectionsPanel(request.BeginNewProfile, request.KeepStartVisible);
                break;

            case GlobalModalKind.Settings:
                PopulateSettingsThemeJsonEditor();
                OpenSettings(request.KeepStartVisible);
                break;
        }
    }

    private void EnsureCanvasInitialized()
    {
        if (_canvasInitialized)
            return;

        CanvasViewModel vm = CurrentShell.EnsureCanvas();
        vm.SetCanvasContext(CurrentShell.ActiveCanvasContext);
        vm.ConnectionManager.IsVisible = false;

        InitializeServices(vm);
        AttachCanvasHandlers(vm);
        WireConnectionActivation(vm.ConnectionManager);
        WireSidebarActions(vm.Sidebar);
        WireSearchMenu();

        CurrentShell.StartMenu.RefreshData(vm.ConnectionManager.Profiles);
        Title = vm.WindowTitle;
        SyncModeToggleState();
        _canvasInitialized = true;
        TrackCriticalFlow("CF-02-navigate-shell", "canvas_initialized", "ok");
    }

    private void SyncCanvasContext()
    {
        if (!_canvasInitialized)
            return;

        if (CurrentShell.Canvas is not null)
            CurrentShell.Canvas.SetCanvasContext(CanvasContext.Query);

        if (CurrentShell.DdlCanvas is not null)
        {
            CanvasContext ddlContext = CurrentShell.ActiveCanvasContext == CanvasContext.ViewSubcanvas
                ? CanvasContext.ViewSubcanvas
                : CanvasContext.Ddl;

            CurrentShell.DdlCanvas.SetCanvasContext(ddlContext);
        }
    }

    private void WireSidebarActions(SidebarViewModel sidebar)
    {
        if (!_wiredSidebars.Add(sidebar))
            return;

        sidebar.AddNodeRequested += () =>
        {
            EnterCanvasMode();
            OpenSearch();
        };

        sidebar.AddConnectionRequested += () =>
            _globalModalManager.RequestConnectionManager(beginNewProfile: true, keepStartVisible: false);

        sidebar.TogglePreviewRequested += () =>
        {
            EnterCanvasMode();
            _ = OpenModeAwareOutputPreviewSafeAsync();
        };

    }

    private void WirePreviewDock()
    {
        _toastCenterPropertyChangedHandler = (_, e) =>
        {
            if (e.PropertyName is nameof(ToastCenterViewModel.IsVisible) or nameof(ToastCenterViewModel.IsDetailsOpen))
                UpdatePreviewDockLayout();
        };

        _outputPreviewPropertyChangedHandler = (_, e) =>
        {
            if (e.PropertyName == nameof(OutputPreviewModalViewModel.IsVisible))
                UpdatePreviewDockLayout();
        };

        CurrentShell.Toasts.PropertyChanged += _toastCenterPropertyChangedHandler;
        CurrentShell.OutputPreview.PropertyChanged += _outputPreviewPropertyChangedHandler;
        UpdatePreviewDockLayout();
    }

    private void UnwirePreviewDock()
    {
        if (_toastCenterPropertyChangedHandler is not null)
            CurrentShell.Toasts.PropertyChanged -= _toastCenterPropertyChangedHandler;

        if (_outputPreviewPropertyChangedHandler is not null)
            CurrentShell.OutputPreview.PropertyChanged -= _outputPreviewPropertyChangedHandler;

        _toastCenterPropertyChangedHandler = null;
        _outputPreviewPropertyChangedHandler = null;
    }

    private void UpdatePreviewDockLayout()
    {
        Border? dock = this.FindControl<Border>("PreviewDockHost");
        if (dock is null)
            return;

        bool isDiagramPage = CurrentShell.IsDiagramDocumentPageActive;
        bool isBlockedByModal = CurrentShell.OutputPreview.IsVisible || CurrentShell.Toasts.IsDetailsOpen;
        dock.IsVisible = isDiagramPage && !isBlockedByModal;

        double bottomMargin = CurrentShell.Toasts.IsVisible
            ? PreviewDockToastBottomMargin
            : PreviewDockBaseBottomMargin;

        dock.Margin = new Thickness(0, 0, 0, bottomMargin);
    }

    private void WireStartMenu()
    {
        CurrentShell.StartMenu.CreateNewDiagramRequested += OnStartCreateNewDiagramRequested;
        CurrentShell.StartMenu.OpenConnectionsRequested += OnStartOpenConnectionsRequested;
        CurrentShell.StartMenu.OpenFromDiskRequested += OnStartOpenFromDiskRequested;
        CurrentShell.StartMenu.OpenSchemaCompareRequested += OnStartOpenSchemaCompareRequested;
        CurrentShell.StartMenu.OpenSavedConnectionRequested += OnStartOpenSavedConnectionRequested;
        CurrentShell.StartMenu.OpenRecentProjectRequested += OnStartOpenRecentProjectRequested;
        CurrentShell.StartMenu.OpenTemplateRequested += OnStartOpenTemplateRequested;
        CurrentShell.StartMenu.OpenSettingsRequested += OnStartOpenSettingsRequested;
    }

    private void EnterCanvasMode()
    {
        EnsureCanvasInitialized();

        if (!CurrentShell.IsStartVisible)
            return;

        CurrentShell.EnterCanvas();
        SyncModeToggleState();
        SyncCanvasContext();
    }

    private void OnStartCreateNewDiagramRequested()
    {
        EnterCanvasMode();
        ResetCurrentCanvas();
    }

    private void OnStartOpenConnectionsRequested()
    {
        _globalModalManager.RequestConnectionManager(beginNewProfile: true, keepStartVisible: true);
    }

    private void OnStartOpenSchemaCompareRequested()
    {
        EnterCanvasMode();
        CurrentShell.OpenNewDocument(WorkspaceDocumentType.DdlSchemaCompare);
        CurrentShell.ActivateDocument(WorkspaceDocumentType.DdlSchemaCompare);
        SyncModeToggleState();
    }


    private void OnStartOpenSavedConnectionRequested(StartSavedConnectionItem item)
    {
        if (GetConnectionModule().ConnectFromStartItem(item.Id))
            return;

        _globalModalManager.RequestConnectionManager(beginNewProfile: false, keepStartVisible: true);
    }

    private void OpenConnectionsPanel(bool beginNewProfile, bool keepStartVisible)
    {
        CurrentShell.AttachConnectionModalToActiveDocument();
        GetConnectionModule().OpenManager(beginNewProfile, keepStartVisible);
    }

    private ConnectionWorkspaceModule GetConnectionModule()
    {
        _connectionModule ??= new ConnectionWorkspaceModule(
            getConnectionManager: () =>
            {
                ConnectionManagerViewModel manager = ResolveConnectionManagerForActiveSubscreen();
                WireConnectionActivation(manager);
                EnsureExclusiveConnectionManager(manager);
                return manager;
            },
            activateConnectionSidebar: () =>
            {
                CanvasViewModel canvas = ResolveDiagramCanvasForConnectionSidebar();
                canvas.Sidebar.ActiveTab = SidebarTab.Connection;
            },
            enterCanvas: EnterCanvasMode
        );

        return _connectionModule;
    }

    private void EnsureExclusiveConnectionManager(ConnectionManagerViewModel targetManager)
    {
        CanvasViewModel? queryCanvas = CurrentShell.Canvas;
        if (queryCanvas is not null && !ReferenceEquals(queryCanvas.ConnectionManager, targetManager))
            queryCanvas.ConnectionManager.IsVisible = false;

        CanvasViewModel? ddlCanvas = CurrentShell.DdlCanvas;
        if (ddlCanvas is not null && !ReferenceEquals(ddlCanvas.ConnectionManager, targetManager))
            ddlCanvas.ConnectionManager.IsVisible = false;
    }

    private ConnectionManagerViewModel ResolveConnectionManagerForActiveSubscreen()
    {
        if (CurrentShell.ActiveWorkspaceDocument?.DocumentViewModel is CanvasViewModel activeCanvas)
            return activeCanvas.ConnectionManager;

        return CurrentShell.ActiveWorkspaceDocumentType switch
        {
            WorkspaceDocumentType.DdlCanvas => GetDdlCanvasForInteraction().ConnectionManager,
            WorkspaceDocumentType.QueryCanvas => GetQueryCanvasForInteraction().ConnectionManager,
            WorkspaceDocumentType.SqlEditor => ResolveDiagramCanvasForConnectionSidebar().ConnectionManager,
            WorkspaceDocumentType.DdlSchemaCompare => CurrentShell.ActiveConnectionManager
                ?? ResolveDiagramCanvasForConnectionSidebar().ConnectionManager,
            _ => ResolveDiagramCanvasForConnectionSidebar().ConnectionManager,
        };
    }

    private CanvasViewModel ResolveDiagramCanvasForConnectionSidebar()
    {
        if (CurrentShell.ActiveWorkspaceDocument?.DocumentViewModel is CanvasViewModel activeCanvas)
            return activeCanvas;

        return CurrentShell.ActiveWorkspaceDocumentType switch
        {
            WorkspaceDocumentType.DdlCanvas => GetDdlCanvasForInteraction(),
            WorkspaceDocumentType.QueryCanvas => GetQueryCanvasForInteraction(),
            WorkspaceDocumentType.DdlSchemaCompare => CurrentShell.DdlCanvas ?? CurrentShell.Canvas ?? GetQueryCanvasForInteraction(),
            _ => CurrentShell.DdlCanvas ?? CurrentShell.Canvas ?? GetQueryCanvasForInteraction(),
        };
    }

    private void WireConnectionActivation(ConnectionManagerViewModel connectionManager)
    {
        if (!_wiredConnectionManagers.Add(connectionManager))
            return;

        connectionManager.ConnectionActivated += _ =>
        {
            if (CurrentShell.IsStartVisible)
                EnterCanvasMode();

            CurrentShell.Toasts.ShowSuccess(
                L("toast.connectionActivated", "Conexao ativa."),
                L("toast.connectionActivatedDetails", "Pronto para executar consultas e operacoes de importacao."));
        };

        connectionManager.ConnectionFailed += (message, details) =>
        {
            string toastDetails = string.IsNullOrWhiteSpace(details)
                ? message
                : $"{message}{Environment.NewLine}{details}";

            CurrentShell.Toasts.ShowError(
                L("toast.connectionFailed", "Falha ao conectar."),
                toastDetails);

            CurrentShell.SqlEditor.NotifyConnectionContextChanged();
        };

        connectionManager.ProfilesChanged += () =>
        {
            CurrentShell.StartMenu.RefreshData(
                connectionManager.Profiles,
                connectionManager.ActiveProfileId
            );

            CurrentShell.SqlEditor.NotifyConnectionContextChanged();
        };

    }

    private async void OnStartOpenFromDiskRequested()
    {
        TrackCriticalFlow("CF-01-open-app-load-project", "open_from_disk_started", "ok");
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = L("file.openDialog.title", "Abrir Canvas"),
                FileTypeFilter =
                [
                    new FilePickerFileType(L("file.openDialog.canvasType", "Canvas SQL Architect"))
                    {
                        Patterns = ["*.vsaq"],
                        MimeTypes = ["application/json"],
                    },
                ],
                AllowMultiple = false,
            }
        );

        string? selectedPath = files.FirstOrDefault()?.TryGetLocalPath();
        if (selectedPath is null)
        {
            TrackCriticalFlow("CF-01-open-app-load-project", "open_from_disk_picker", "cancelled");
            return;
        }

        EnterCanvasMode();
        if (_fileOps is not null)
        {
            await _fileOps.OpenPathAsync(selectedPath);
            TrackCriticalFlow(
                "CF-01-open-app-load-project",
                "open_from_disk_completed",
                "ok",
                new Dictionary<string, object?> { ["path"] = selectedPath });
        }
    }

    private void OnStartOpenRecentProjectRequested(StartRecentProjectItem recent)
    {
        EnterCanvasMode();
        TrackCriticalFlow(
            "CF-01-open-app-load-project",
            "open_recent_requested",
            "ok",
            new Dictionary<string, object?> { ["hasPath"] = !string.IsNullOrWhiteSpace(recent.FilePath) });
        if (!string.IsNullOrWhiteSpace(recent.FilePath))
        {
            _ = _fileOps?.OpenPathAsync(recent.FilePath);
            return;
        }

        _ = _fileOps?.OpenAsync();
    }

    private void TrackCriticalFlow(
        string flowId,
        string step,
        string outcome,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        _criticalFlowTelemetry?.Track(flowId, step, outcome, properties);
    }

    private void OnStartOpenTemplateRequested(StartTemplateItem item)
    {
        QueryTemplate? template = QueryTemplateCatalog.Find(item.TemplateId ?? item.Name);
        if (template is null)
            return;

        EnterCanvasMode();
        GetQueryCanvasForInteraction().LoadTemplate(template);
        InvalidateActiveDiagramCanvasWires();
    }

    private void AttachCanvasHandlers(CanvasViewModel vm)
    {
        _windowTitleChangedHandler = (_, e) =>
        {
            if (e.PropertyName == nameof(CanvasViewModel.WindowTitle))
                Title = vm.WindowTitle;

            if (e.PropertyName == nameof(CanvasViewModel.IsInViewEditor))
                CurrentShell.SetViewSubcanvasActive(vm.IsInViewEditor);
        };

        vm.PropertyChanged += _windowTitleChangedHandler;
    }

    private void DetachCanvasHandlers(CanvasViewModel vm)
    {
        if (_windowTitleChangedHandler is not null)
            vm.PropertyChanged -= _windowTitleChangedHandler;

        _windowTitleChangedHandler = null;
    }

    private void InitializeServices(CanvasViewModel vm)
    {
        _layoutService = ActivatorUtilities.CreateInstance<MainWindowLayoutService>(_services, this, vm);
        _sessionService = new SessionManagementService(
            window: this,
            vm: vm,
            ddlVmResolver: () => CurrentShell.DdlCanvas,
            activeDocumentTypeResolver: () => CurrentShell.ActiveWorkspaceDocumentType ?? WorkspaceDocumentType.QueryCanvas,
            applyActiveDocumentType: documentType =>
            {
                CurrentShell.ActivateDocument(documentType);
                SyncModeToggleState();
            },
            workspaceDocumentsResolver: () => CurrentShell.OpenWorkspaceDocuments,
            activeWorkspaceDocumentIdResolver: () => CurrentShell.ActiveWorkspaceDocumentId,
            applyWorkspaceDocumentsSnapshot: snapshot =>
            {
                CurrentShell.RestoreWorkspaceDocuments(snapshot);
                SyncModeToggleState();
            },
            invalidateActiveCanvasWires: InvalidateActiveDiagramCanvasWires);
        _fileOps = new FileOperationsService(
            window: this,
            vm: vm,
            ddlVmResolver: () => CurrentShell.DdlCanvas,
            afterLoadCanvasStateChanged: (hasQueryContent, hasDdlContent) =>
            {
                if (!hasQueryContent && hasDdlContent)
                {
                    CurrentShell.ActivateDocument(WorkspaceDocumentType.DdlCanvas);
                    SyncModeToggleState();
                }
            },
            activeDocumentTypeResolver: () => CurrentShell.ActiveWorkspaceDocumentType ?? WorkspaceDocumentType.QueryCanvas,
            applyActiveDocumentType: documentType =>
            {
                CurrentShell.ActivateDocument(documentType);
                SyncModeToggleState();
            },
            workspaceDocumentsResolver: () => CurrentShell.OpenWorkspaceDocuments,
            activeWorkspaceDocumentIdResolver: () => CurrentShell.ActiveWorkspaceDocumentId,
            applyWorkspaceDocumentsSnapshot: snapshot =>
            {
                CurrentShell.RestoreWorkspaceDocuments(snapshot);
                SyncModeToggleState();
            },
            applySqlEditorSeedScripts: scripts =>
            {
                CurrentShell.ImportMigratedSqlScriptsToSqlEditor(scripts);
                SyncModeToggleState();
            },
            invalidateActiveCanvasWires: InvalidateActiveDiagramCanvasWires,
            logger: _services.GetService<ILogger<FileOperationsService>>()
        );
        _export = ActivatorUtilities.CreateInstance<ExportService>(_services, this, vm);
        _preview = ActivatorUtilities.CreateInstance<PreviewService>(_services, this, vm);
        _shortcutRegistry ??= new global::AkkornStudio.UI.Services.Input.ShortcutRegistry.ShortcutRegistry();
        _commandFactory = new CommandPaletteFactory(
            this,
            () => ResolveActiveDiagramCanvasStrict(),
            () => CurrentShell,
            _fileOps,
            _export,
            _preview,
            CreateNewQueryTab,
            _shortcutRegistry
        );
        _commandPaletteService = new CommandPaletteService(_commandFactory);
        _commandPaletteService.Refresh();
        CurrentShell.SetCommandPalette(_commandPaletteService.ViewModel);
        var canvasProvider = new ActiveCanvasProvider(() => ResolveActiveDiagramCanvasStrict());
        _keyboardHandler = new KeyboardInputHandler(
            this,
            canvasProvider,
            _fileOps,
            CurrentShell.CommandPalette,
            CreateNewQueryTab,
            showShortcutsAction: OpenKeyboardShortcutsWindow,
            canHandleCanvasShortcuts: () => CurrentShell.IsDiagramDocumentPageActive,
            openConnectionManagerAction: () => _globalModalManager.RequestConnectionManager(beginNewProfile: false, keepStartVisible: false),
            shortcutRegistry: _shortcutRegistry
        );

        _layoutService.Wire();
        _sessionService.Wire();
        _sessionService.CheckForSession();
        _keyboardHandler.Wire();
        _preview.Wire();
    }


    private static IBrush ResourceBrush(string key, string fallbackHex)
    {
        if (Application.Current?.TryFindResource(key, out object? resource) == true && resource is IBrush brush)
            return brush;

        return new SolidColorBrush(Color.Parse(fallbackHex));
    }

    private static string L(string key, string fallback)
    {
        string value = LocalizationService.Instance[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    private static string LF(string key, string fallbackFormat, params object[] args)
    {
        return string.Format(L(key, fallbackFormat), args);
    }

    private KeyboardShortcutsViewModel GetKeyboardShortcutsViewModel()
    {
        _shortcutRegistry ??= new global::AkkornStudio.UI.Services.Input.ShortcutRegistry.ShortcutRegistry();
        _keyboardShortcutsViewModel ??= new KeyboardShortcutsViewModel(_shortcutRegistry);
        return _keyboardShortcutsViewModel;
    }

    private void OpenKeyboardShortcutsWindow()
    {
        KeyboardShortcutsViewModel viewModel = GetKeyboardShortcutsViewModel();
        viewModel.Refresh();
        new KeyboardShortcutsWindow(viewModel).Show(this);
    }

    private void PreviewDockOpenBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        EnterCanvasMode();
        _ = OpenModeAwareOutputPreviewSafeAsync();
        e.Handled = true;
    }

    private void PreviewDockDiagnosticsBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        EnterCanvasMode();
        _ = OpenOutputDiagnosticsTabSafeAsync();
        e.Handled = true;
    }

    private async Task OpenOutputDiagnosticsTabSafeAsync()
    {
        try
        {
            await OpenModeAwareOutputPreviewAsync();
            CurrentShell.OutputPreview.ShowCanvasDiagnosticsTabCommand.Execute(null);
        }
        catch (Exception ex)
        {
            CurrentShell.Toasts.ShowError(L("toast.previewOpenFailed", "Falha ao abrir preview."), ex.Message);
        }
    }

    private void ToastDetailsBackdrop_PointerPressed(object? s, PointerPressedEventArgs e)
    {
        CurrentShell.Toasts.CloseDetailsCommand.Execute(null);
        e.Handled = true;
    }

    private void ToastDetailsDialog_PointerPressed(object? s, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    private void ClearCanvasPromptBackdrop_PointerPressed(object? s, PointerPressedEventArgs e)
    {
        ResolveActiveDiagramCanvasStrict().ConnectionManager.CloseClearCanvasPromptCommand.Execute(null);
        e.Handled = true;
    }

    private void ClearCanvasPromptDialog_PointerPressed(object? s, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    private void WireSearchMenu()
    {
        WireQuerySearchOverlay(this.FindControl<DiagramDocumentPageControl>("QueryDocumentPage")?.SearchOverlayControl);
        WireDdlSearchOverlay(this.FindControl<DiagramDocumentPageControl>("DdlDocumentPage")?.SearchOverlayControl);
    }

    private void WireQuerySearchOverlay(SearchMenuControl? overlay)
    {
        if (overlay is null)
            return;

        overlay.SpawnRequested += (_, def) =>
        {
            CanvasViewModel queryCanvas = GetQueryCanvasForInteraction();
            queryCanvas.SpawnNode(def, queryCanvas.SearchMenu.SpawnPosition);
            InvalidateActiveDiagramCanvasWires();
        };

        overlay.SpawnTableRequested += (_, args) =>
        {
            CanvasViewModel queryCanvas = GetQueryCanvasForInteraction();
            queryCanvas.SpawnTableNode(
                args.FullName,
                args.Cols.Select(c => (c.Name, c.Type)),
                queryCanvas.SearchMenu.SpawnPosition
            );
            InvalidateActiveDiagramCanvasWires();
            queryCanvas.TriggerAutoJoinAnalysis(args.FullName);
        };

        overlay.SnippetRequested += (_, snippet) =>
        {
            CanvasViewModel queryCanvas = GetQueryCanvasForInteraction();
            queryCanvas.InsertSnippet(snippet, queryCanvas.SearchMenu.SpawnPosition);
            InvalidateActiveDiagramCanvasWires();
        };
    }

    private void WireDdlSearchOverlay(SearchMenuControl? overlay)
    {
        if (overlay is null)
            return;

        overlay.SpawnRequested += (_, def) =>
        {
            CanvasViewModel ddlCanvas = GetDdlCanvasForInteraction();
            ddlCanvas.SpawnNode(def, ddlCanvas.SearchMenu.SpawnPosition);
            InvalidateActiveDiagramCanvasWires();
        };

        overlay.SpawnTableRequested += (_, args) =>
        {
            CanvasViewModel ddlCanvas = GetDdlCanvasForInteraction();
            TableMetadata? table = ResolveTableMetadataForDdl(args.FullName, ddlCanvas);
            if (table is null)
            {
                CurrentShell.Toasts.ShowWarning(
                    L("toast.ddlTableMetadataUnavailable", "Nao foi possivel resolver metadados da tabela para importar no DDL."));
                return;
            }

            ImportSingleTableToDdl(table, ddlCanvas.SearchMenu.SpawnPosition);
            InvalidateActiveDiagramCanvasWires();
        };
    }

    private void OpenSearch()
    {
        Action openSearch = CurrentShell.ActiveWorkspaceDocumentType switch
        {
            WorkspaceDocumentType.DdlCanvas => OpenSearchInDdl,
            WorkspaceDocumentType.QueryCanvas => OpenSearchInQuery,
            _ => OpenSearchInQuery,
        };
        openSearch();
    }

    private void OpenSearchInQuery()
    {
        CanvasViewModel queryCanvas = GetQueryCanvasForInteraction();
        InfiniteCanvas? canvas = this.FindControl<DiagramDocumentPageControl>("QueryDocumentPage")?.CanvasControl;
        Point center = canvas is not null
            ? queryCanvas.ScreenToCanvas(new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2))
            : new Point(400, 300);
        queryCanvas.SearchMenu.Open(center);
    }

    private void OpenSearchInDdl()
    {
        CanvasViewModel ddlCanvas = GetDdlCanvasForInteraction();
        InfiniteCanvas? canvas = this.FindControl<DiagramDocumentPageControl>("DdlDocumentPage")?.CanvasControl;
        Point center = canvas is not null
            ? ddlCanvas.ScreenToCanvas(new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2))
            : new Point(400, 300);
        ddlCanvas.SearchMenu.Open(center);
    }

    private CanvasViewModel GetQueryCanvasForInteraction() =>
        WireSidebarAndReturn(
            CurrentShell.ActiveQueryCanvasDocument
            ?? CurrentShell.Canvas
            ?? CurrentShell.EnsureCanvas());

    private CanvasViewModel GetDdlCanvasForInteraction() =>
        WireSidebarAndReturn(
            CurrentShell.ActiveDdlCanvasDocument
            ?? CurrentShell.DdlCanvas
            ?? CurrentShell.EnsureDdlCanvas());

    private CanvasViewModel WireSidebarAndReturn(CanvasViewModel canvas)
    {
        WireSidebarActions(canvas.Sidebar);
        return canvas;
    }

    private CanvasViewModel ResolveActiveDiagramCanvasStrict()
    {
        return CurrentShell.ActiveWorkspaceDocumentType switch
        {
            WorkspaceDocumentType.QueryCanvas => GetQueryCanvasForInteraction(),
            WorkspaceDocumentType.DdlCanvas => GetDdlCanvasForInteraction(),
            _ => throw new InvalidOperationException(
                L("error.mainWindow.canvasNotInitialized", "CanvasViewModel was not initialized.")),
        };
    }

    private ConnectionConfig? ResolveActiveConnectionConfigForWorkspace()
    {
        return CurrentShell.ActiveWorkspaceDocumentType switch
        {
            WorkspaceDocumentType.DdlCanvas => GetDdlCanvasForInteraction().ActiveConnectionConfig,
            WorkspaceDocumentType.QueryCanvas => GetQueryCanvasForInteraction().ActiveConnectionConfig,
            WorkspaceDocumentType.SqlEditor => ResolveDiagramCanvasForConnectionSidebar().ActiveConnectionConfig,
            WorkspaceDocumentType.DdlSchemaCompare => null,
            _ => GetQueryCanvasForInteraction().ActiveConnectionConfig
                ?? CurrentShell.DdlCanvas?.ActiveConnectionConfig,
        };
    }

    private DbMetadata? ResolveMetadataForActiveWorkspace()
    {
        return CurrentShell.ActiveWorkspaceDocumentType switch
        {
            WorkspaceDocumentType.DdlCanvas => GetDdlCanvasForInteraction().DatabaseMetadata,
            WorkspaceDocumentType.QueryCanvas => GetQueryCanvasForInteraction().DatabaseMetadata,
            WorkspaceDocumentType.SqlEditor => ResolveDiagramCanvasForConnectionSidebar().DatabaseMetadata,
            WorkspaceDocumentType.DdlSchemaCompare => null,
            _ => GetQueryCanvasForInteraction().DatabaseMetadata
                ?? CurrentShell.DdlCanvas?.DatabaseMetadata,
        };
    }

    private static TableMetadata? ResolveTableMetadataForDdl(string fullName, CanvasViewModel ddlCanvas)
    {
        DbMetadata? metadata = ddlCanvas.DatabaseMetadata;
        if (metadata is null)
            return null;

        return metadata.Schemas
            .SelectMany(schema => schema.Tables)
            .FirstOrDefault(table => string.Equals(table.FullName, fullName, StringComparison.OrdinalIgnoreCase));
    }

    private bool TryCloseTopModalOnEscape()
    {
        CanvasViewModel activeCanvas = ResolveActiveDiagramCanvasStrict();
        if (activeCanvas.ConnectionManager.IsClearCanvasPromptVisible)
        {
            activeCanvas.ConnectionManager.CloseClearCanvasPromptCommand.Execute(null);
            return true;
        }

        if (CurrentShell.Toasts.IsDetailsOpen)
        {
            CurrentShell.Toasts.CloseDetailsCommand.Execute(null);
            return true;
        }

        return false;
    }

    private void LeftSidebarHideBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        bool isCollapsed = CurrentShell.IsDdlDocumentPageActive
            ? _ddlModeLeftSidebarCollapsed
            : _queryModeLeftSidebarCollapsed;
        SetLeftSidebarCollapsed(!isCollapsed);
        e.Handled = true;
    }

    private void RightSidebarHideBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        bool isCollapsed = CurrentShell.IsDdlDocumentPageActive
            ? _ddlModeRightSidebarCollapsed
            : _queryModeRightSidebarCollapsed;
        SetRightSidebarCollapsed(!isCollapsed);
        e.Handled = true;
    }

    private void SetLeftSidebarCollapsed(bool collapsed)
    {
        Grid? bodyGrid = this.FindControl<Grid>("BodyGrid");
        if (bodyGrid is null || bodyGrid.ColumnDefinitions.Count < 5)
            return;

        ColumnDefinition sidebarColumn = bodyGrid.ColumnDefinitions[0];
        ColumnDefinition splitterColumn = bodyGrid.ColumnDefinitions[1];
        Border? sidebarHost = this.FindControl<Border>("LeftSidebarHost");
        Button? hideBtn = this.FindControl<Button>("LeftSidebarHideBtn");
        Material.Icons.Avalonia.MaterialIcon? toggleIcon = this.FindControl<Material.Icons.Avalonia.MaterialIcon>("LeftSidebarToggleIcon");

        if (sidebarHost is null)
            return;

        if (CurrentShell.IsQueryDocumentPageActive)
            _queryModeLeftSidebarCollapsed = collapsed;
        else if (CurrentShell.IsDdlDocumentPageActive)
            _ddlModeLeftSidebarCollapsed = collapsed;

        if (collapsed)
        {
            sidebarColumn.MinWidth = CollapsedRailWidth.Value;
            sidebarColumn.MaxWidth = CollapsedRailWidth.Value;
            sidebarColumn.Width = CollapsedRailWidth;
            splitterColumn.Width = new GridLength(0);
            sidebarHost.IsVisible = false;
            if (hideBtn is not null)
                hideBtn.IsVisible = true;
            if (toggleIcon is not null)
                toggleIcon.Kind = MaterialIconKind.ChevronRight;
            return;
        }

        sidebarColumn.MinWidth = 344;
        sidebarColumn.MaxWidth = 344;
        sidebarColumn.Width = new GridLength(344);
        splitterColumn.Width = new GridLength(0);
        sidebarHost.IsVisible = true;
        if (hideBtn is not null)
            hideBtn.IsVisible = true;
        if (toggleIcon is not null)
            toggleIcon.Kind = MaterialIconKind.ChevronLeft;
    }

    private void SetRightSidebarCollapsed(bool collapsed)
    {
        Grid? bodyGrid = this.FindControl<Grid>("BodyGrid");
        if (bodyGrid is null || bodyGrid.ColumnDefinitions.Count < 5)
            return;

        ColumnDefinition splitterColumn = bodyGrid.ColumnDefinitions[3];
        ColumnDefinition sidebarColumn = bodyGrid.ColumnDefinitions[4];
        Border? sidebarHost = this.FindControl<Border>("RightSidebarHost");
        Button? hideBtn = this.FindControl<Button>("RightSidebarHideBtn");
        Material.Icons.Avalonia.MaterialIcon? toggleIcon = this.FindControl<Material.Icons.Avalonia.MaterialIcon>("RightSidebarToggleIcon");

        if (sidebarHost is null)
            return;

        if (CurrentShell.IsQueryDocumentPageActive)
            _queryModeRightSidebarCollapsed = collapsed;
        else if (CurrentShell.IsDdlDocumentPageActive)
            _ddlModeRightSidebarCollapsed = collapsed;

        if (collapsed)
        {
            sidebarColumn.MinWidth = CollapsedRailWidth.Value;
            sidebarColumn.MaxWidth = CollapsedRailWidth.Value;
            sidebarColumn.Width = CollapsedRailWidth;
            splitterColumn.Width = new GridLength(0);
            sidebarHost.IsVisible = false;
            if (hideBtn is not null)
                hideBtn.IsVisible = true;
            if (toggleIcon is not null)
                toggleIcon.Kind = MaterialIconKind.ChevronLeft;
            return;
        }

        sidebarColumn.MinWidth = 344;
        sidebarColumn.MaxWidth = 344;
        sidebarColumn.Width = new GridLength(344);
        splitterColumn.Width = new GridLength(0);
        sidebarHost.IsVisible = true;
        if (hideBtn is not null)
            hideBtn.IsVisible = true;
        if (toggleIcon is not null)
            toggleIcon.Kind = MaterialIconKind.ChevronRight;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && TryCloseTopModalOnEscape())
        {
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
        _keyboardHandler?.OnKeyDown(this, e);
    }
}
