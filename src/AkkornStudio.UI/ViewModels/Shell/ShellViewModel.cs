using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using AkkornStudio.Core;
using AkkornStudio.CanvasKit;
using AkkornStudio.Metadata;
using AkkornStudio.Nodes;
using Avalonia;
using AkkornStudio.UI.Services.ConnectionManager.Models;
using AkkornStudio.UI.Services.ConnectionManager;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.Services.Settings;
using AkkornStudio.UI.Services.SqlEditor;
using AkkornStudio.UI.Services.SqlEditor.Results;
using AkkornStudio.UI.Services.Ddl;
using AkkornStudio.UI.Services.Workspace;
using AkkornStudio.UI.Services.Workspace.Diagnostics;
using AkkornStudio.UI.Services.Workspace.Models;
using AkkornStudio.UI.Services.Workspace.Pages;
using AkkornStudio.UI.Services.Workspace.Preview;
using AkkornStudio.UI.Services.Preview;
using AkkornStudio.UI.Serialization;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.Canvas.Strategies;
using AkkornStudio.UI.ViewModels.ErDiagram;
using System.Diagnostics;
using System.IO;

namespace AkkornStudio.UI.ViewModels;

/// <summary>
/// Coordinates the application shell flow between Start Menu and Canvas work area.
/// </summary>
public sealed class ShellViewModel : ViewModelBase
{
    private const string ShellConnectionModalLogFile = "shell-connection-modal-debug.log";
    private const string AutoProjectionMarkerParameter = "__akkorn_auto_projection";
    private const string AutoProjectionChildEntityParameter = "__akkorn_auto_projection_child";
    private const string AutoProjectionParentEntityParameter = "__akkorn_auto_projection_parent";
    private const string AutoProjectionChildColumnsParameter = "__akkorn_auto_projection_child_columns";
    private const string AutoProjectionParentColumnsParameter = "__akkorn_auto_projection_parent_columns";

    public enum AppMode
    {
        Query,
        Ddl,
        SqlEditor,
        SqlResult,
        ErDiagram,
        DdlSchemaCompare,
        DdlSchemaAnalysis,
    }

    public enum ESettingsSection
    {
        Appearance,
        Editor,
        Project,
        LanguageRegion,
        DateTime,
        KeyboardShortcuts,
        Privacy,
        Notification,
        Accessibility,
    }

    public enum DdlWorkspaceTab
    {
        Canvas,
        SchemaAnalysis,
    }

    private bool _isStartVisible = true;
    private bool _isSettingsVisible;
    private ESettingsSection _selectedSettingsSection = ESettingsSection.Appearance;
    private DdlWorkspaceTab _activeDdlWorkspaceTab = DdlWorkspaceTab.Canvas;
    private bool _isViewSubcanvasActive;
    private CanvasViewModel? _canvas;
    private CanvasViewModel? _ddlCanvas;
    private ErCanvasViewModel? _erCanvas;
    private CanvasContext _activeCanvasContext = CanvasContext.Query;
    private ConnectionManagerViewModel? _observedQueryConnectionManager;
    private ConnectionManagerViewModel? _observedDdlConnectionManager;
    private ConnectionManagerViewModel? _observedSqlEditorConnectionManager;
    private CanvasViewModel? _observedQueryCanvas;
    private CanvasViewModel? _observedDdlCanvas;
    private PropertyChangedEventHandler? _activeConnectionManagerPropertyChanged;
    private PropertyChangedEventHandler? _canvasMetadataPropertyChanged;
    private readonly ILocalizationService _localization;
    private readonly ISqlEditorViewModelFactory _sqlEditorViewModelFactory;
    private readonly IWorkspaceRouter _workspaceRouter;
    private readonly IWorkspaceDocumentPageContractRegistry _pageContractRegistry;
    private readonly IWorkspaceDocumentPreviewContractRegistry _previewContractRegistry;
    private readonly IWorkspaceDocumentDiagnosticsContractRegistry _diagnosticsContractRegistry;
    private readonly IConnectionManagerViewModelFactory _connectionManagerViewModelFactory;
    private readonly ConnectionManagerViewModel _sqlEditorConnectionManager;
    private PropertyChangedEventHandler? _localizationPropertyChanged;
    private PropertyChangedEventHandler? _outputPreviewPropertyChanged;
    private PropertyChangedEventHandler? _quickDataPreviewPropertyChanged;
    private Guid? _queryDocumentId;
    private Guid? _ddlDocumentId;
    private Guid? _sqlResultDocumentId;
    private Guid? _erDiagramDocumentId;
    private Guid? _ddlSchemaCompareDocumentId;
    private Guid? _ddlSchemaAnalysisDocumentId;
    private Guid? _connectionModalOwnerDocumentId;
    private Guid? _lastSyncedWorkspaceDocumentId;
    private ProjectConventionSettings _projectConventionSettings = AppSettingsStore.LoadProjectConventionSettings();
    private readonly SqlResultSessionService _sqlResultSessionService = new();
    private readonly QuickDataPreviewService _quickDataPreviewService = new();
    private SqlResultPageViewModel? _sqlResultPage;

    public ShellViewModel(
        CanvasViewModel? canvas = null,
        ILocalizationService? localization = null,
        ISqlEditorViewModelFactory? sqlEditorViewModelFactory = null,
        IWorkspaceRouter? workspaceRouter = null,
        IWorkspaceDocumentPageContractRegistry? pageContractRegistry = null,
        IWorkspaceDocumentPreviewContractRegistry? previewContractRegistry = null,
        IWorkspaceDocumentDiagnosticsContractRegistry? diagnosticsContractRegistry = null,
        IConnectionManagerViewModelFactory? connectionManagerViewModelFactory = null)
    {
        _localization = localization ?? LocalizationService.Instance;
        _sqlEditorViewModelFactory = sqlEditorViewModelFactory ?? new SqlEditorViewModelFactory(_localization);
        _workspaceRouter = workspaceRouter ?? new WorkspaceRouter();
        _pageContractRegistry = pageContractRegistry ?? new WorkspaceDocumentPageContractRegistry();
        _previewContractRegistry = previewContractRegistry ?? new WorkspaceDocumentPreviewContractRegistry();
        _diagnosticsContractRegistry = diagnosticsContractRegistry ?? new WorkspaceDocumentDiagnosticsContractRegistry();
        _connectionManagerViewModelFactory = connectionManagerViewModelFactory
            ?? throw new ArgumentNullException(nameof(connectionManagerViewModelFactory));
        _sqlEditorConnectionManager = _connectionManagerViewModelFactory.Create();
        Toasts = canvas?.Toasts ?? new ToastCenterViewModel();
        _canvas = canvas;
        StartMenu = new StartMenuViewModel();
        LeftSidebar = new LeftSidebarViewModel();
        RightSidebar = new RightSidebarViewModel();
        OutputPreview = new OutputPreviewModalViewModel();
        QuickDataPreview = new QuickDataPreviewModalViewModel(
            _quickDataPreviewService,
            OpenSqlInEditorFromQuickPreview);
        SqlEditor = BuildSqlEditorDocument();
        QueryModeCommand = new RelayCommand(() => ActivateDocument(WorkspaceDocumentType.QueryCanvas));
        DdlModeCommand = new RelayCommand(() => ActivateDocument(WorkspaceDocumentType.DdlCanvas));
        SqlEditorModeCommand = new RelayCommand(() => ActivateDocument(WorkspaceDocumentType.SqlEditor));
        ErDiagramModeCommand = new RelayCommand(() => ActivateDocument(WorkspaceDocumentType.ErDiagram));
        ShowDdlCanvasWorkspaceTabCommand = new RelayCommand(() => SetActiveDdlWorkspaceTab(DdlWorkspaceTab.Canvas));
        ShowDdlSchemaAnalysisWorkspaceTabCommand = new RelayCommand(() => SetActiveDdlWorkspaceTab(DdlWorkspaceTab.SchemaAnalysis));
        RefreshConnectionManagerObservers();
        _localizationPropertyChanged = (_, e) =>
        {
            if (e.PropertyName is "" or "Item[]" or nameof(ILocalizationService.CurrentCulture))
            {
                RaisePropertyChanged(nameof(SettingsSectionTitle));
                RaisePropertyChanged(nameof(SettingsSectionSubtitle));
            }
        };
        _localization.PropertyChanged += _localizationPropertyChanged;
        _outputPreviewPropertyChanged = (_, e) =>
        {
            if (e.PropertyName == nameof(OutputPreviewModalViewModel.IsVisible))
                RaisePropertyChanged(nameof(IsOutputPreviewModalVisible));
        };
        OutputPreview.PropertyChanged += _outputPreviewPropertyChanged;
        _quickDataPreviewPropertyChanged = (_, e) =>
        {
            if (e.PropertyName == nameof(QuickDataPreviewModalViewModel.IsVisible))
                RaisePropertyChanged(nameof(IsQuickDataPreviewModalVisible));
        };
        QuickDataPreview.PropertyChanged += _quickDataPreviewPropertyChanged;
        if (_canvas is not null)
        {
            _canvas.ApplyProjectConventionSettings(_projectConventionSettings);
            RegisterOrUpdateQueryDocument(_canvas);
        }
        ActivateDocumentCore(WorkspaceDocumentType.QueryCanvas);
        SyncExtractedPanels();
    }

    public CanvasViewModel? Canvas
    {
        get => _canvas;
        private set
        {
            if (!Set(ref _canvas, value))
                return;

            AttachCanvasObservers(value);
            RaisePropertyChanged(nameof(ActiveCanvas));
            RaisePropertyChanged(nameof(ActiveConnectionManager));
            RaisePropertyChanged(nameof(ConnectionManagerOverlay));
            RaisePropertyChanged(nameof(IsConnectionManagerVisible));
            RaisePropertyChanged(nameof(IsConnectionManagerOverlayVisible));
            SqlEditor.NotifyConnectionContextChanged();
            if (value is not null)
                RegisterOrUpdateQueryDocument(value);
            SyncExtractedPanels();
        }
    }

    public StartMenuViewModel StartMenu { get; }

    public QueryTabManagerViewModel QueryTabs { get; } = new();

    public SqlEditorViewModel SqlEditor { get; }

    public IReadOnlyList<OpenWorkspaceDocument> OpenWorkspaceDocuments => _workspaceRouter.OpenDocuments;

    public Guid? ActiveWorkspaceDocumentId => _workspaceRouter.ActiveDocumentId;

    public OpenWorkspaceDocument? ActiveWorkspaceDocument => _workspaceRouter.ActiveDocument;

    public WorkspaceDocumentType? ActiveWorkspaceDocumentType => ActiveWorkspaceDocument?.Descriptor.DocumentType;

    public WorkspaceDocumentPageContract ActivePageContract =>
        _pageContractRegistry.Resolve(ActiveWorkspaceDocumentType ?? WorkspaceDocumentType.QueryCanvas);

    public WorkspaceDocumentPreviewContract ActivePreviewContract =>
        _previewContractRegistry.Resolve(ActiveWorkspaceDocumentType ?? WorkspaceDocumentType.QueryCanvas);

    public WorkspaceDocumentDiagnosticsContract ActiveDiagnosticsContract =>
        _diagnosticsContractRegistry.Resolve(ActiveWorkspaceDocumentType ?? WorkspaceDocumentType.QueryCanvas);

    public bool IsQueryDocumentPageActive => ActiveWorkspaceDocumentType == WorkspaceDocumentType.QueryCanvas;

    public bool IsDdlDocumentPageActive => ActiveWorkspaceDocumentType == WorkspaceDocumentType.DdlCanvas;

    public bool IsSqlEditorDocumentPageActive => ActiveWorkspaceDocumentType == WorkspaceDocumentType.SqlEditor;

    public bool IsSqlResultDocumentPageActive => ActiveWorkspaceDocumentType == WorkspaceDocumentType.SqlResult;

    public bool IsDiagramDocumentPageActive => IsQueryDocumentPageActive || IsDdlDocumentPageActive;

    public bool IsErDiagramDocumentPageActive => ActiveWorkspaceDocumentType == WorkspaceDocumentType.ErDiagram;

    public bool IsDdlSchemaCompareDocumentPageActive => ActiveWorkspaceDocumentType == WorkspaceDocumentType.DdlSchemaCompare;

    public bool IsDdlSchemaAnalysisDocumentPageActive => ActiveWorkspaceDocumentType == WorkspaceDocumentType.DdlSchemaAnalysis;

    public LeftSidebarViewModel LeftSidebar { get; }

    public RightSidebarViewModel RightSidebar { get; }

    public ToastCenterViewModel Toasts { get; }
    public CommandPaletteViewModel CommandPalette { get; private set; } = new();

    public OutputPreviewModalViewModel OutputPreview { get; }
    public QuickDataPreviewModalViewModel QuickDataPreview { get; }

    public RelayCommand QueryModeCommand { get; }

    public RelayCommand DdlModeCommand { get; }

    public RelayCommand SqlEditorModeCommand { get; }

    public RelayCommand ErDiagramModeCommand { get; }

    public RelayCommand ShowDdlCanvasWorkspaceTabCommand { get; }

    public RelayCommand ShowDdlSchemaAnalysisWorkspaceTabCommand { get; }

    public bool IsStartVisible
    {
        get => _isStartVisible;
        private set
        {
            if (!Set(ref _isStartVisible, value))
                return;

            RaisePropertyChanged(nameof(IsCanvasVisible));
            RaisePropertyChanged(nameof(IsDiagramOverlayLayerVisible));
            RaisePropertyChanged(nameof(IsConnectionManagerOverlayVisible));
            RaisePropertyChanged(nameof(IsOutputPreviewModalVisible));
            RaisePropertyChanged(nameof(IsQuickDataPreviewModalVisible));
        }
    }

    public bool IsCanvasVisible => !IsStartVisible;

    public AppMode ActiveMode => ActiveWorkspaceDocumentType switch
    {
        WorkspaceDocumentType.DdlCanvas => AppMode.Ddl,
        WorkspaceDocumentType.SqlEditor => AppMode.SqlEditor,
        WorkspaceDocumentType.SqlResult => AppMode.SqlResult,
        WorkspaceDocumentType.ErDiagram => AppMode.ErDiagram,
        WorkspaceDocumentType.DdlSchemaCompare => AppMode.DdlSchemaCompare,
        WorkspaceDocumentType.DdlSchemaAnalysis => AppMode.DdlSchemaAnalysis,
        _ => AppMode.Query,
    };

    public bool IsQueryModeActive => ActiveWorkspaceDocumentType == WorkspaceDocumentType.QueryCanvas;

    public bool IsDdlModeActive => ActiveWorkspaceDocumentType == WorkspaceDocumentType.DdlCanvas;

    public bool IsSqlEditorModeActive => ActiveWorkspaceDocumentType == WorkspaceDocumentType.SqlEditor;

    public bool IsSqlResultModeActive => ActiveWorkspaceDocumentType == WorkspaceDocumentType.SqlResult;

    public bool IsErDiagramModeActive => ActiveWorkspaceDocumentType == WorkspaceDocumentType.ErDiagram;

    public bool IsDdlSchemaCompareModeActive => ActiveWorkspaceDocumentType == WorkspaceDocumentType.DdlSchemaCompare;

    public bool IsDdlSchemaAnalysisModeActive => ActiveWorkspaceDocumentType == WorkspaceDocumentType.DdlSchemaAnalysis;

    public bool IsDiagramModeActive => IsDiagramDocumentPageActive;

    public DdlWorkspaceTab ActiveDdlWorkspaceTab
    {
        get => _activeDdlWorkspaceTab;
        private set
        {
            if (!Set(ref _activeDdlWorkspaceTab, value))
                return;

            RaisePropertyChanged(nameof(IsDdlCanvasWorkspaceTabActive));
            RaisePropertyChanged(nameof(IsDdlSchemaAnalysisWorkspaceTabActive));
        }
    }

    public bool IsDdlCanvasWorkspaceTabActive => ActiveDdlWorkspaceTab == DdlWorkspaceTab.Canvas;

    public bool IsDdlSchemaAnalysisWorkspaceTabActive => ActiveDdlWorkspaceTab == DdlWorkspaceTab.SchemaAnalysis;

    public CanvasContext ActiveCanvasContext
    {
        get => _activeCanvasContext;
        private set => Set(ref _activeCanvasContext, value);
    }

    public bool IsSettingsVisible
    {
        get => _isSettingsVisible;
        private set => Set(ref _isSettingsVisible, value);
    }

    public bool IsConnectionManagerVisible => ActiveConnectionManager?.IsVisible == true;

    public bool IsDiagramOverlayLayerVisible => IsCanvasVisible && IsDiagramDocumentPageActive;

    public bool IsConnectionManagerOverlayVisible =>
        IsCanvasVisible
        && (QueryConnectionManager?.IsVisible == true
            || DdlConnectionManager?.IsVisible == true
            || ConnectionManagerOverlay?.IsVisible == true);

    public bool IsOutputPreviewModalVisible =>
        IsCanvasVisible && OutputPreview.IsVisible;

    public bool IsQuickDataPreviewModalVisible =>
        IsCanvasVisible && QuickDataPreview.IsVisible;

    public CanvasViewModel? ActiveCanvas =>
        ActiveWorkspaceDocument?.DocumentViewModel as CanvasViewModel;

    public ConnectionManagerViewModel? QueryConnectionManager =>
        ActiveQueryCanvasDocument?.ConnectionManager
        ?? ResolveKnownQueryCanvas()?.ConnectionManager;

    public ConnectionManagerViewModel? DdlConnectionManager =>
        ActiveDdlCanvasDocument?.ConnectionManager
        ?? ResolveKnownDdlCanvas()?.ConnectionManager;

    public bool IsQueryConnectionManagerOverlayVisible =>
        IsCanvasVisible
        && ActiveWorkspaceDocumentType == WorkspaceDocumentType.QueryCanvas
        && QueryConnectionManager?.IsVisible == true;

    public bool IsDdlConnectionManagerOverlayVisible =>
        IsCanvasVisible
        && ActiveWorkspaceDocumentType == WorkspaceDocumentType.DdlCanvas
        && DdlConnectionManager?.IsVisible == true;

    public ConnectionManagerViewModel? ConnectionManagerOverlay =>
        ResolveConnectionManagerForModalRoute();

    public ConnectionManagerViewModel? ActiveConnectionManager => ResolveDocumentConnectionManager();

    public SidebarViewModel? ActiveDiagramSidebar => ActiveCanvas?.Sidebar;

    public PropertyPanelViewModel? ActiveDiagramPropertyPanel => ActiveCanvas?.PropertyPanel;

    public CanvasViewModel? ActiveQueryCanvasDocument =>
        ActiveWorkspaceDocumentType == WorkspaceDocumentType.QueryCanvas
            ? ActiveWorkspaceDocument?.DocumentViewModel as CanvasViewModel
            : null;

    public CanvasViewModel? ActiveDdlCanvasDocument =>
        ActiveWorkspaceDocumentType == WorkspaceDocumentType.DdlCanvas
            ? ActiveWorkspaceDocument?.DocumentViewModel as CanvasViewModel
            : null;

    public SqlEditorViewModel? ActiveSqlEditorDocument =>
        ActiveWorkspaceDocumentType == WorkspaceDocumentType.SqlEditor
            ? ActiveWorkspaceDocument?.DocumentViewModel as SqlEditorViewModel
            : null;

    public SqlResultPageViewModel? ActiveSqlResultDocument =>
        ActiveWorkspaceDocumentType == WorkspaceDocumentType.SqlResult
            ? ActiveWorkspaceDocument?.DocumentViewModel as SqlResultPageViewModel
            : null;

    public ErCanvasViewModel? ActiveErDiagramDocument =>
        ActiveWorkspaceDocumentType == WorkspaceDocumentType.ErDiagram
            ? ActiveWorkspaceDocument?.DocumentViewModel as ErCanvasViewModel
            : null;

    public DdlSchemaCompareWorkspaceViewModel? ActiveDdlSchemaCompareDocument =>
        ActiveWorkspaceDocumentType == WorkspaceDocumentType.DdlSchemaCompare
            ? ActiveWorkspaceDocument?.DocumentViewModel as DdlSchemaCompareWorkspaceViewModel
            : null;

    public DdlSchemaAnalysisWorkspaceViewModel? ActiveDdlSchemaAnalysisDocument =>
        ActiveWorkspaceDocumentType == WorkspaceDocumentType.DdlSchemaAnalysis
            ? ActiveWorkspaceDocument?.DocumentViewModel as DdlSchemaAnalysisWorkspaceViewModel
            : null;

    public CanvasViewModel? DdlCanvas
    {
        get => _ddlCanvas;
        private set
        {
            if (!Set(ref _ddlCanvas, value))
                return;

            AttachCanvasObservers(value);
            RefreshConnectionManagerObservers();
            RaisePropertyChanged(nameof(ActiveCanvas));
            RaisePropertyChanged(nameof(ActiveConnectionManager));
            RaisePropertyChanged(nameof(ConnectionManagerOverlay));
            RaisePropertyChanged(nameof(IsConnectionManagerVisible));
            RaisePropertyChanged(nameof(IsConnectionManagerOverlayVisible));
            SyncExtractedPanels();
        }
    }

    public string AppVersionLabel => ResolveAppVersionLabel();

    public ESettingsSection SelectedSettingsSection
    {
        get => _selectedSettingsSection;
        private set
        {
            if (!Set(ref _selectedSettingsSection, value))
                return;

            RaisePropertyChanged(nameof(IsAppearanceSectionSelected));
            RaisePropertyChanged(nameof(IsEditorSectionSelected));
            RaisePropertyChanged(nameof(IsProjectSectionSelected));
            RaisePropertyChanged(nameof(IsLanguageRegionSectionSelected));
            RaisePropertyChanged(nameof(IsDateTimeSectionSelected));
            RaisePropertyChanged(nameof(IsKeyboardShortcutsSectionSelected));
            RaisePropertyChanged(nameof(IsPrivacySectionSelected));
            RaisePropertyChanged(nameof(IsNotificationSectionSelected));
            RaisePropertyChanged(nameof(IsAccessibilitySectionSelected));
            RaisePropertyChanged(nameof(SettingsSectionTitle));
            RaisePropertyChanged(nameof(SettingsSectionSubtitle));
        }
    }

    public string SettingsSectionTitle => SelectedSettingsSection switch
    {
        ESettingsSection.Appearance => Localize("settings.section.appearance.title", "Themes"),
        ESettingsSection.Editor => Localize("settings.section.editor.title", "Editor"),
        ESettingsSection.Project => Localize("settings.section.project.title", "Project"),
        ESettingsSection.LanguageRegion => Localize("settings.section.languageRegion.title", "Language & Region"),
        ESettingsSection.DateTime => Localize("settings.section.dateTime.title", "Date & Time"),
        ESettingsSection.KeyboardShortcuts => Localize("settings.section.keyboard.title", "Keyboard Shortcuts"),
        ESettingsSection.Privacy => Localize("settings.section.privacy.title", "Privacy"),
        ESettingsSection.Notification => Localize("settings.section.notification.title", "Notification"),
        ESettingsSection.Accessibility => Localize("settings.section.accessibility.title", "Accessibility"),
        _ => Localize("settings.section.default.title", "Settings"),
    };

    public string SettingsSectionSubtitle => SelectedSettingsSection switch
    {
        ESettingsSection.Appearance => Localize("settings.section.appearance.subtitle", "Choose your style or customize your theme"),
        ESettingsSection.Editor => Localize("settings.section.editor.subtitle", "Control SQL safety defaults in editor execution."),
        ESettingsSection.Project => Localize("settings.section.project.subtitle", "Manage naming conventions and default wire style across Query and DDL."),
        ESettingsSection.LanguageRegion => Localize("settings.section.languageRegion.subtitle", "Manage language and regional formatting"),
        ESettingsSection.DateTime => Localize("settings.section.wip.subtitle", "Work in progress."),
        ESettingsSection.KeyboardShortcuts => Localize("settings.section.keyboard.subtitle", "Customize keyboard shortcuts used by command palette and canvas execution."),
        ESettingsSection.Privacy => Localize("settings.section.wip.subtitle", "Work in progress."),
        ESettingsSection.Notification => Localize("settings.section.wip.subtitle", "Work in progress."),
        ESettingsSection.Accessibility => Localize("settings.section.wip.subtitle", "Work in progress."),
        _ => Localize("settings.section.default.subtitle", "Application settings"),
    };

    public bool IsAppearanceSectionSelected => SelectedSettingsSection == ESettingsSection.Appearance;
    public bool IsEditorSectionSelected => SelectedSettingsSection == ESettingsSection.Editor;
    public bool IsProjectSectionSelected => SelectedSettingsSection == ESettingsSection.Project;
    public bool IsLanguageRegionSectionSelected => SelectedSettingsSection == ESettingsSection.LanguageRegion;
    public bool IsDateTimeSectionSelected => SelectedSettingsSection == ESettingsSection.DateTime;
    public bool IsKeyboardShortcutsSectionSelected => SelectedSettingsSection == ESettingsSection.KeyboardShortcuts;
    public bool IsPrivacySectionSelected => SelectedSettingsSection == ESettingsSection.Privacy;
    public bool IsNotificationSectionSelected => SelectedSettingsSection == ESettingsSection.Notification;
    public bool IsAccessibilitySectionSelected => SelectedSettingsSection == ESettingsSection.Accessibility;

    public CanvasViewModel EnsureCanvas(Func<bool>? isDdlModeActiveResolver = null, Action<TableMetadata, Point>? importDdlTableAction = null)
    {
        if (ActiveQueryCanvasDocument is not null)
        {
            BindPropertyPanelActions(ActiveQueryCanvasDocument);
            return ActiveQueryCanvasDocument;
        }

        if (Canvas is null)
        {
            Canvas = new CanvasViewModel(
                nodeManager: null,
                pinManager: null,
                selectionManager: null,
                localizationService: null,
                domainStrategy: new QueryDomainStrategy(isDdlModeActiveResolver, importDdlTableAction),
                toastCenter: Toasts,
                connectionManagerFactory: _connectionManagerViewModelFactory);
            Canvas.ApplyProjectConventionSettings(_projectConventionSettings);
            BindPropertyPanelActions(Canvas);
        }

        return Canvas;
    }

    public CanvasViewModel EnsureDdlCanvas()
    {
        if (ActiveDdlCanvasDocument is not null)
        {
            BindPropertyPanelActions(ActiveDdlCanvasDocument);
            return ActiveDdlCanvasDocument;
        }

        if (DdlCanvas is null)
        {
            DdlCanvas = new CanvasViewModel(
                nodeManager: null,
                pinManager: null,
                selectionManager: null,
                localizationService: null,
                domainStrategy: new DdlDomainStrategy(),
                toastCenter: Toasts,
                connectionManagerFactory: _connectionManagerViewModelFactory);
            DdlCanvas.ApplyProjectConventionSettings(_projectConventionSettings);
            BindPropertyPanelActions(DdlCanvas);
        }

        RegisterOrUpdateDdlDocument(DdlCanvas);

        return DdlCanvas;
    }

    public void SetActiveDdlWorkspaceTab(DdlWorkspaceTab tab)
    {
        if (!Enum.IsDefined(tab))
            throw new ArgumentOutOfRangeException(nameof(tab), tab, "Unsupported DDL workspace tab.");

        ActiveDdlWorkspaceTab = tab;
    }

    public void ActivateDocument(WorkspaceDocumentType documentType)
    {
        if (documentType == WorkspaceDocumentType.ErDiagram && !CanActivateErDiagram())
            return;

        if (documentType == WorkspaceDocumentType.SqlEditor
            && ActiveWorkspaceDocumentType != WorkspaceDocumentType.ErDiagram)
        {
            SqlEditor.ConfigureBackNavigation(null);
        }

        if (ActiveWorkspaceDocumentType == documentType)
        {
            if (documentType == WorkspaceDocumentType.ErDiagram)
                RefreshErDiagramDocument();
            return;
        }

        OpenWorkspaceDocument? target = _workspaceRouter.OpenDocuments
            .LastOrDefault(document => document.Descriptor.DocumentType == documentType);
        if (documentType == WorkspaceDocumentType.ErDiagram && target?.DocumentViewModel is ErCanvasViewModel erCanvas)
        {
            erCanvas.BindQueryNavigation(OpenErRelationInQuery);
            erCanvas.BindEntityDefinitionNavigation(OpenErEntityDefinitionInSqlEditor);
            erCanvas.BindSyncToDdl(SyncErToDdlCanvas);
            erCanvas.BindSourceMetadata(ResolveSharedMetadata());
        }
        bool changed = target is not null && _workspaceRouter.TryActivate(target.Descriptor.DocumentId);
        if (!changed)
        {
            _ = OpenNewDocument(documentType);
            return;
        }

        SyncStateFromActiveDocument();
        if (documentType == WorkspaceDocumentType.ErDiagram)
            RefreshErDiagramDocument();
        RaiseActiveDocumentPropertiesChanged();
        SyncExtractedPanels();
    }

    private bool CanActivateErDiagram()
    {
        ConnectionConfig? connection = ResolveSharedActiveConnectionConfig();
        if (connection is null)
        {
            Toasts.ShowWarning(
                "Conexao obrigatoria para o ER.",
                "Conecte-se a um banco antes de abrir o diagrama ER.");
            EnsureCanvas().ConnectionManager.IsVisible = true;
            RaisePropertyChanged(nameof(IsConnectionManagerOverlayVisible));
            RaisePropertyChanged(nameof(ConnectionManagerOverlay));
            return false;
        }

        DbMetadata? metadata = ResolveSharedMetadata();
        if (metadata is null)
        {
            Toasts.ShowWarning(
                "Metadata indisponivel para o ER.",
                "Atualize os metadados da conexao e tente novamente.");
            return false;
        }

        return true;
    }

    public Guid OpenNewDocument(WorkspaceDocumentType documentType)
    {
        OpenWorkspaceDocument? existing = _workspaceRouter.OpenDocuments
            .LastOrDefault(document => document.Descriptor.DocumentType == documentType);
        if (existing is not null)
        {
            _workspaceRouter.TryActivate(existing.Descriptor.DocumentId);
            SyncStateFromActiveDocument();
            RaiseActiveDocumentPropertiesChanged();
            SyncExtractedPanels();
            return existing.Descriptor.DocumentId;
        }

        return documentType switch
        {
            WorkspaceDocumentType.DdlCanvas => OpenNewDdlDocument(),
            WorkspaceDocumentType.SqlEditor => OpenNewSqlEditorDocument(),
            WorkspaceDocumentType.SqlResult => OpenNewSqlResultDocument(),
            WorkspaceDocumentType.ErDiagram => OpenNewErDiagramDocument(),
            WorkspaceDocumentType.DdlSchemaCompare => OpenNewDdlSchemaCompareDocument(),
            WorkspaceDocumentType.DdlSchemaAnalysis => OpenNewDdlSchemaAnalysisDocument(),
            _ => OpenNewQueryDocument(),
        };
    }

    public bool TryOpenSelectedQueryJoinInErDiagram()
    {
        CanvasViewModel? queryCanvas = ActiveQueryCanvasDocument ?? Canvas;
        if (queryCanvas is null)
            return false;

        NodeViewModel? selectedJoin = queryCanvas.Nodes.FirstOrDefault(node => node.IsSelected && node.IsJoin);
        if (selectedJoin is null)
            return false;

        if (!TryResolveJoinSelection(queryCanvas, selectedJoin, out ResolvedErRelation? relation))
            return false;

        ActivateDocument(WorkspaceDocumentType.ErDiagram);
        ErCanvasViewModel? erCanvas = ActiveErDiagramDocument;
        if (erCanvas is null)
            return false;

        return erCanvas.TryFocusRelation(
            relation!.ChildEntityId,
            relation.ParentEntityId,
            relation.ChildColumns,
            relation.ParentColumns);
    }

    public bool TryActivateWorkspaceDocument(Guid documentId)
    {
        if (!_workspaceRouter.TryActivate(documentId))
            return false;

        SyncStateFromActiveDocument();
        RaiseActiveDocumentPropertiesChanged();
        SyncExtractedPanels();
        return true;
    }

    public bool TryCloseWorkspaceDocument(Guid documentId)
    {
        bool closed = _workspaceRouter.TryClose(documentId);
        if (!closed)
            return false;

        if (_workspaceRouter.ActiveDocument is null)
            _ = OpenNewDocument(WorkspaceDocumentType.QueryCanvas);

        SyncStateFromActiveDocument();
        RaiseActiveDocumentPropertiesChanged();
        SyncExtractedPanels();
        return true;
    }

    public void RestoreWorkspaceDocuments(SavedWorkspaceDocumentsCanvas workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (workspace.Documents.Count == 0)
            return;

        var restoredDocuments = new List<OpenWorkspaceDocument>(workspace.Documents.Count);
        var restoredTypes = new HashSet<WorkspaceDocumentType>();
        foreach (SavedWorkspaceDocument savedDocument in workspace.Documents)
        {
            if (!Enum.TryParse(savedDocument.DocumentType, true, out WorkspaceDocumentType documentType))
                continue;
            if (!restoredTypes.Add(documentType))
                continue;

            object documentViewModel = documentType switch
            {
                WorkspaceDocumentType.QueryCanvas => BuildCanvasDocument(savedDocument, isDdl: false),
                WorkspaceDocumentType.DdlCanvas => BuildCanvasDocument(savedDocument, isDdl: true),
                WorkspaceDocumentType.SqlEditor => BuildSqlEditorDocument(),
                WorkspaceDocumentType.SqlResult => BuildSqlResultDocument(),
                WorkspaceDocumentType.ErDiagram => BuildErDiagramDocument(),
                WorkspaceDocumentType.DdlSchemaCompare => BuildDdlSchemaCompareDocument(),
                WorkspaceDocumentType.DdlSchemaAnalysis => BuildDdlSchemaAnalysisDocument(),
                _ => BuildCanvasDocument(savedDocument, isDdl: false),
            };

            WorkspaceDocumentDescriptor descriptor = new(
                DocumentId: savedDocument.DocumentId == Guid.Empty ? Guid.NewGuid() : savedDocument.DocumentId,
                DocumentType: documentType,
                Title: string.IsNullOrWhiteSpace(savedDocument.Title) ? documentType.ToString() : savedDocument.Title,
                IsDirty: savedDocument.IsDirty,
                PersistenceSchemaVersion: string.IsNullOrWhiteSpace(savedDocument.PersistenceSchemaVersion)
                    ? "1.0"
                    : savedDocument.PersistenceSchemaVersion,
                Payload: JsonSerializer.SerializeToElement(new { }));

            restoredDocuments.Add(new OpenWorkspaceDocument(
                Descriptor: descriptor,
                DocumentViewModel: documentViewModel,
                PageViewModel: documentViewModel,
                PageState: null));
        }

        if (restoredDocuments.Count == 0)
            return;

        _workspaceRouter.ReplaceDocuments(restoredDocuments, workspace.ActiveDocumentId);

        _queryDocumentId = _workspaceRouter.OpenDocuments
            .FirstOrDefault(document => document.Descriptor.DocumentType == WorkspaceDocumentType.QueryCanvas)
            ?.Descriptor.DocumentId;
        _ddlDocumentId = _workspaceRouter.OpenDocuments
            .FirstOrDefault(document => document.Descriptor.DocumentType == WorkspaceDocumentType.DdlCanvas)
            ?.Descriptor.DocumentId;
        _sqlResultDocumentId = _workspaceRouter.OpenDocuments
            .FirstOrDefault(document => document.Descriptor.DocumentType == WorkspaceDocumentType.SqlResult)
            ?.Descriptor.DocumentId;
        _erDiagramDocumentId = _workspaceRouter.OpenDocuments
            .FirstOrDefault(document => document.Descriptor.DocumentType == WorkspaceDocumentType.ErDiagram)
            ?.Descriptor.DocumentId;
        _ddlSchemaCompareDocumentId = _workspaceRouter.OpenDocuments
            .FirstOrDefault(document => document.Descriptor.DocumentType == WorkspaceDocumentType.DdlSchemaCompare)
            ?.Descriptor.DocumentId;
        _ddlSchemaAnalysisDocumentId = _workspaceRouter.OpenDocuments
            .FirstOrDefault(document => document.Descriptor.DocumentType == WorkspaceDocumentType.DdlSchemaAnalysis)
            ?.Descriptor.DocumentId;

        Canvas = _workspaceRouter.OpenDocuments
            .FirstOrDefault(document => document.Descriptor.DocumentType == WorkspaceDocumentType.QueryCanvas)
            ?.DocumentViewModel as CanvasViewModel;
        DdlCanvas = _workspaceRouter.OpenDocuments
            .FirstOrDefault(document => document.Descriptor.DocumentType == WorkspaceDocumentType.DdlCanvas)
            ?.DocumentViewModel as CanvasViewModel;
        _erCanvas = _workspaceRouter.OpenDocuments
            .FirstOrDefault(document => document.Descriptor.DocumentType == WorkspaceDocumentType.ErDiagram)
            ?.DocumentViewModel as ErCanvasViewModel;
        _sqlResultPage = _workspaceRouter.OpenDocuments
            .FirstOrDefault(document => document.Descriptor.DocumentType == WorkspaceDocumentType.SqlResult)
            ?.DocumentViewModel as SqlResultPageViewModel;

        SyncStateFromActiveDocument();
        RaiseActiveDocumentPropertiesChanged();
        SyncExtractedPanels();
    }

    public void ImportMigratedSqlScriptsToSqlEditor(IReadOnlyList<string> scripts)
    {
        if (scripts is null || scripts.Count == 0)
            return;

        Guid documentId = OpenNewDocument(WorkspaceDocumentType.SqlEditor);
        _workspaceRouter.TryActivate(documentId);
        SyncStateFromActiveDocument();
        RaiseActiveDocumentPropertiesChanged();

        SqlEditorViewModel targetEditor = ActiveSqlEditorDocument ?? SqlEditor;
        DatabaseProvider provider = ResolveSharedActiveConnectionConfig()?.Provider ?? DatabaseProvider.Postgres;
        foreach (string script in scripts.Where(static s => !string.IsNullOrWhiteSpace(s)))
            targetEditor.ReceiveFromCanvas(script, provider);

        ActivateDocument(WorkspaceDocumentType.SqlEditor);
        SyncExtractedPanels();
    }

    public void SetViewSubcanvasActive(bool isActive)
    {
        bool coerced = IsDdlDocumentPageActive && isActive;

        if (!Set(ref _isViewSubcanvasActive, coerced))
            return;

        UpdateActiveCanvasContext();
        SyncExtractedPanels();
    }

    public bool TryOpenSqlExplainPreview(string sql, DatabaseProvider provider, ConnectionConfig? connectionConfig)
    {
        CanvasViewModel? bridgeCanvas = ResolveKnownQueryCanvas() ?? ResolveKnownDdlCanvas();
        if (bridgeCanvas is null)
            return false;

        OutputPreview.OpenForSqlExplain(
            canvas: bridgeCanvas,
            sql: sql,
            provider: provider,
            connectionConfig: connectionConfig);
        return true;
    }

    public bool TryOpenSqlBenchmarkPreview(string sql, ConnectionConfig? connectionConfig)
    {
        CanvasViewModel? bridgeCanvas = ResolveKnownQueryCanvas() ?? ResolveKnownDdlCanvas();
        if (bridgeCanvas is null)
            return false;

        OutputPreview.OpenForSqlBenchmark(
            canvas: bridgeCanvas,
            sql: sql,
            connectionConfig: connectionConfig);
        return true;
    }

    public async Task<bool> OpenQuickDataPreviewForActiveModeAsync()
    {
        OutputPreview.Close();
        switch (ActiveWorkspaceDocumentType)
        {
            case WorkspaceDocumentType.QueryCanvas:
                return await OpenQuickDataPreviewForQueryAsync();
            case WorkspaceDocumentType.DdlCanvas:
                return await OpenQuickDataPreviewForDdlAsync();
            default:
                return false;
        }
    }

    public async Task<bool> OpenQuickDataPreviewForForeignKeyAsync(
        Guid? sourceDocumentId,
        string sql,
        DatabaseProvider provider,
        string? focusTableFullName,
        string? subtitle = null)
    {
        OutputPreview.Close();
        if (string.IsNullOrWhiteSpace(sql))
            return false;

        ConnectionConfig? connection = ResolveSqlEditorConnectionByProfileId(null);
        DbMetadata? metadata = ResolveSqlEditorMetadata();
        WorkspaceDocumentType? sourceType = ActiveWorkspaceDocumentType;
        if (!sourceType.HasValue && sourceDocumentId.HasValue)
        {
            sourceType = _workspaceRouter.OpenDocuments
                .FirstOrDefault(document => document.Descriptor.DocumentId == sourceDocumentId.Value)
                ?.Descriptor.DocumentType;
        }

        await QuickDataPreview.OpenSqlPreviewAsync(
            title: "Preview FK",
            subtitle: subtitle ?? (focusTableFullName ?? string.Empty),
            sql: sql,
            connection: connection,
            provider: provider,
            metadata: metadata,
            focusTableFullName: focusTableFullName,
            sourceDocumentType: sourceType,
            maxRows: 120);
        return true;
    }

    private async Task<bool> OpenQuickDataPreviewForQueryAsync()
    {
        CanvasViewModel queryCanvas = ActiveQueryCanvasDocument ?? EnsureCanvas();
        queryCanvas.LiveSql.Recompile();

        string sql = !string.IsNullOrWhiteSpace(queryCanvas.LiveSql.ExecutionSqlTemplate)
            ? queryCanvas.LiveSql.ExecutionSqlTemplate!
            : queryCanvas.LiveSql.RawSql;
        if (string.IsNullOrWhiteSpace(sql))
            return false;

        ConnectionConfig? connection = queryCanvas.ActiveConnectionConfig ?? ResolveSharedActiveConnectionConfig();
        DbMetadata? metadata = queryCanvas.DatabaseMetadata ?? ResolveSharedMetadata();
        DatabaseProvider provider = connection?.Provider ?? queryCanvas.Provider;
        string? focusTable = TryExtractFirstFromTable(sql);

        await QuickDataPreview.OpenSqlPreviewAsync(
            title: "Preview rapido",
            subtitle: "Modo Query",
            sql: sql,
            connection: connection,
            provider: provider,
            metadata: metadata,
            focusTableFullName: focusTable,
            sourceDocumentType: WorkspaceDocumentType.QueryCanvas,
            maxRows: 120);
        return true;
    }

    private async Task<bool> OpenQuickDataPreviewForDdlAsync()
    {
        CanvasViewModel ddlCanvas = ActiveDdlCanvasDocument ?? EnsureDdlCanvas();
        if (!TryResolveDdlPreviewTarget(ddlCanvas, out string? fullTableName))
            return false;

        ConnectionConfig? connection = ddlCanvas.ActiveConnectionConfig ?? ResolveSharedActiveConnectionConfig();
        DbMetadata? metadata = ddlCanvas.DatabaseMetadata ?? ResolveSharedMetadata();
        DatabaseProvider provider = connection?.Provider ?? ddlCanvas.Provider;
        if (string.IsNullOrWhiteSpace(fullTableName))
            return false;

        await QuickDataPreview.OpenTablePreviewAsync(
            fullTableName!,
            connection,
            provider,
            metadata,
            WorkspaceDocumentType.DdlCanvas,
            maxRows: 120);
        return true;
    }

    public void EnterCanvas()
    {
        EnsureCanvas();
        ActivateDocumentCore(ActiveWorkspaceDocumentType ?? WorkspaceDocumentType.QueryCanvas);
        IsStartVisible = false;
    }

    public void ReturnToStart() => IsStartVisible = true;

    public void OpenSettings() => IsSettingsVisible = true;

    public void CloseSettings() => IsSettingsVisible = false;

    public void SelectSettingsSection(ESettingsSection section) => SelectedSettingsSection = section;

    public ProjectConventionSettings CurrentProjectConventionSettings => new()
    {
        NamingConvention = _projectConventionSettings.NamingConvention,
        EnforceAliasNaming = _projectConventionSettings.EnforceAliasNaming,
        WarnOnReservedKeywords = _projectConventionSettings.WarnOnReservedKeywords,
        MaxAliasLength = _projectConventionSettings.MaxAliasLength,
        DefaultWireCurveMode = _projectConventionSettings.DefaultWireCurveMode,
    };

    public void ApplyProjectConventionSettings(ProjectConventionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _projectConventionSettings = new ProjectConventionSettings
        {
            NamingConvention = settings.NamingConvention,
            EnforceAliasNaming = settings.EnforceAliasNaming,
            WarnOnReservedKeywords = settings.WarnOnReservedKeywords,
            MaxAliasLength = settings.MaxAliasLength,
            DefaultWireCurveMode = settings.DefaultWireCurveMode,
        };

        foreach (OpenWorkspaceDocument document in _workspaceRouter.OpenDocuments)
        {
            if (document.DocumentViewModel is CanvasViewModel canvas)
                canvas.ApplyProjectConventionSettings(_projectConventionSettings);
        }

        Canvas?.ApplyProjectConventionSettings(_projectConventionSettings);
        DdlCanvas?.ApplyProjectConventionSettings(_projectConventionSettings);
    }

    public void SetSqlEditorExecutionSafetyOptions(bool top1000WithoutWhereEnabled, bool protectMutationWithoutWhereEnabled)
    {
        SqlEditor.SetExecutionSafetyOptions(top1000WithoutWhereEnabled, protectMutationWithoutWhereEnabled);

        foreach (OpenWorkspaceDocument document in _workspaceRouter.OpenDocuments)
        {
            if (document.DocumentViewModel is not SqlEditorViewModel sqlEditor)
                continue;

            if (ReferenceEquals(sqlEditor, SqlEditor))
                continue;

            sqlEditor.SetExecutionSafetyOptions(top1000WithoutWhereEnabled, protectMutationWithoutWhereEnabled);
        }
    }

    public void SetCommandPalette(CommandPaletteViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        CommandPalette = viewModel;
        RaisePropertyChanged(nameof(CommandPalette));
    }

    public void AttachConnectionModalToActiveDocument()
    {
        _connectionModalOwnerDocumentId = ActiveWorkspaceDocumentId;
        RaisePropertyChanged(nameof(ConnectionManagerOverlay));
        RaisePropertyChanged(nameof(IsConnectionManagerOverlayVisible));
    }

    public void ClearConnectionModalRoute()
    {
        _connectionModalOwnerDocumentId = null;
        CloseDiagramConnectionManager(ResolveKnownQueryCanvas());
        CloseDiagramConnectionManager(ResolveKnownDdlCanvas());
        RaisePropertyChanged(nameof(ConnectionManagerOverlay));
        RaisePropertyChanged(nameof(IsConnectionManagerOverlayVisible));
    }

    private static string ResolveAppVersionLabel()
    {
        Assembly asm = typeof(ShellViewModel).Assembly;

        string? informational = asm
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            string clean = informational.Split('+')[0].Trim();
            if (!string.IsNullOrWhiteSpace(clean))
                return clean;
        }

        string? fileVersion = asm
            .GetCustomAttribute<AssemblyFileVersionAttribute>()
            ?.Version;
        if (!string.IsNullOrWhiteSpace(fileVersion))
            return fileVersion;

        return asm.GetName().Version?.ToString() ?? "dev";
    }

    private CanvasViewModel? ResolveKnownQueryCanvas() =>
        Canvas
        ?? _workspaceRouter.OpenDocuments
            .FirstOrDefault(document => document.Descriptor.DocumentType == WorkspaceDocumentType.QueryCanvas)
            ?.DocumentViewModel as CanvasViewModel;

    private CanvasViewModel? ResolveKnownDdlCanvas() =>
        DdlCanvas
        ?? _workspaceRouter.OpenDocuments
            .FirstOrDefault(document => document.Descriptor.DocumentType == WorkspaceDocumentType.DdlCanvas)
            ?.DocumentViewModel as CanvasViewModel;

    private void AttachCanvasObservers(CanvasViewModel? _)
    {
        RefreshConnectionManagerObservers();
        RefreshCanvasMetadataObservers();
    }

    private void RefreshConnectionManagerObservers()
    {
        _activeConnectionManagerPropertyChanged ??= (_, e) =>
        {
            if (e.PropertyName == nameof(ConnectionManagerViewModel.IsVisible))
            {
                if (_ is ConnectionManagerViewModel manager && manager.IsVisible)
                    EnsureExclusiveVisibleConnectionManager(manager);

                RaisePropertyChanged(nameof(ActiveConnectionManager));
                RaisePropertyChanged(nameof(ConnectionManagerOverlay));
                RaisePropertyChanged(nameof(IsConnectionManagerVisible));
                RaisePropertyChanged(nameof(IsConnectionManagerOverlayVisible));
                RaisePropertyChanged(nameof(QueryConnectionManager));
                RaisePropertyChanged(nameof(DdlConnectionManager));
                RaisePropertyChanged(nameof(IsQueryConnectionManagerOverlayVisible));
                RaisePropertyChanged(nameof(IsDdlConnectionManagerOverlayVisible));
                LogShellConnectionOverlay("ConnectionManager.IsVisible changed");
            }

            SqlEditor.NotifyConnectionContextChanged();
        };

        ConnectionManagerViewModel? queryManager = QueryConnectionManager;
        if (!ReferenceEquals(_observedQueryConnectionManager, queryManager))
        {
            if (_observedQueryConnectionManager is not null)
                _observedQueryConnectionManager.PropertyChanged -= _activeConnectionManagerPropertyChanged;

            _observedQueryConnectionManager = queryManager;
            if (_observedQueryConnectionManager is not null)
                _observedQueryConnectionManager.PropertyChanged += _activeConnectionManagerPropertyChanged;
        }

        ConnectionManagerViewModel? ddlManager = DdlConnectionManager;
        if (!ReferenceEquals(_observedDdlConnectionManager, ddlManager))
        {
            if (_observedDdlConnectionManager is not null)
                _observedDdlConnectionManager.PropertyChanged -= _activeConnectionManagerPropertyChanged;

            _observedDdlConnectionManager = ddlManager;
            if (_observedDdlConnectionManager is not null)
                _observedDdlConnectionManager.PropertyChanged += _activeConnectionManagerPropertyChanged;
        }

        ConnectionManagerViewModel? sqlManager = ResolveSqlEditorConnectionManager();
        if (!ReferenceEquals(_observedSqlEditorConnectionManager, sqlManager))
        {
            if (_observedSqlEditorConnectionManager is not null)
                _observedSqlEditorConnectionManager.PropertyChanged -= _activeConnectionManagerPropertyChanged;

            _observedSqlEditorConnectionManager = sqlManager;
            if (_observedSqlEditorConnectionManager is not null)
                _observedSqlEditorConnectionManager.PropertyChanged += _activeConnectionManagerPropertyChanged;
        }

        // Keep overlay bindings in sync even when managers were already visible
        // before observer wiring completed.
        RaisePropertyChanged(nameof(ActiveConnectionManager));
        RaisePropertyChanged(nameof(ConnectionManagerOverlay));
        RaisePropertyChanged(nameof(IsConnectionManagerVisible));
        RaisePropertyChanged(nameof(IsConnectionManagerOverlayVisible));
        RaisePropertyChanged(nameof(QueryConnectionManager));
        RaisePropertyChanged(nameof(DdlConnectionManager));
        RaisePropertyChanged(nameof(IsQueryConnectionManagerOverlayVisible));
        RaisePropertyChanged(nameof(IsDdlConnectionManagerOverlayVisible));
        LogShellConnectionOverlay("RefreshConnectionManagerObservers");
    }

    private void EnsureExclusiveVisibleConnectionManager(ConnectionManagerViewModel visibleManager)
    {
        if (ResolveKnownQueryCanvas()?.ConnectionManager is ConnectionManagerViewModel queryManager
            && !ReferenceEquals(queryManager, visibleManager))
        {
            queryManager.IsVisible = false;
        }

        if (ResolveKnownDdlCanvas()?.ConnectionManager is ConnectionManagerViewModel ddlManager
            && !ReferenceEquals(ddlManager, visibleManager))
        {
            ddlManager.IsVisible = false;
        }

        if (!ReferenceEquals(_sqlEditorConnectionManager, visibleManager))
            _sqlEditorConnectionManager.IsVisible = false;
    }

    private void RefreshCanvasMetadataObservers()
    {
        _canvasMetadataPropertyChanged ??= (_, e) =>
        {
            if (e.PropertyName is nameof(CanvasViewModel.DatabaseMetadata) or nameof(CanvasViewModel.ActiveConnectionConfig))
                RefreshErDiagramDocument();
        };

        CanvasViewModel? queryCanvas = Canvas;
        if (!ReferenceEquals(_observedQueryCanvas, queryCanvas))
        {
            if (_observedQueryCanvas is not null)
                _observedQueryCanvas.PropertyChanged -= _canvasMetadataPropertyChanged;

            _observedQueryCanvas = queryCanvas;
            if (_observedQueryCanvas is not null)
                _observedQueryCanvas.PropertyChanged += _canvasMetadataPropertyChanged;
        }

        CanvasViewModel? ddlCanvas = DdlCanvas;
        if (!ReferenceEquals(_observedDdlCanvas, ddlCanvas))
        {
            if (_observedDdlCanvas is not null)
                _observedDdlCanvas.PropertyChanged -= _canvasMetadataPropertyChanged;

            _observedDdlCanvas = ddlCanvas;
            if (_observedDdlCanvas is not null)
                _observedDdlCanvas.PropertyChanged += _canvasMetadataPropertyChanged;
        }
    }

    private ConnectionManagerViewModel? ResolveDocumentConnectionManager() =>
        ActiveWorkspaceDocumentType switch
        {
            WorkspaceDocumentType.DdlCanvas => ActiveDdlCanvasDocument?.ConnectionManager
                ?? ResolveKnownDdlCanvas()?.ConnectionManager,
            WorkspaceDocumentType.QueryCanvas => ActiveQueryCanvasDocument?.ConnectionManager
                ?? ResolveKnownQueryCanvas()?.ConnectionManager,
            WorkspaceDocumentType.SqlEditor => ResolveSqlEditorConnectionManager(),
            WorkspaceDocumentType.SqlResult => ResolveSqlEditorConnectionManager(),
            WorkspaceDocumentType.DdlSchemaCompare => ResolveSqlEditorConnectionManager(),
            WorkspaceDocumentType.DdlSchemaAnalysis => ResolveSqlEditorConnectionManager(),
            _ => ActiveCanvas?.ConnectionManager ?? ResolveSharedConnectionManager(),
        };

    private ConnectionManagerViewModel? ResolveVisibleConnectionManager()
    {
        ConnectionManagerViewModel? activeManager = ResolveDocumentConnectionManager();
        if (activeManager?.IsVisible == true)
            return activeManager;

        ConnectionManagerViewModel? queryManager = QueryConnectionManager;
        if (queryManager?.IsVisible == true)
            return queryManager;

        ConnectionManagerViewModel? ddlManager = DdlConnectionManager;
        if (ddlManager?.IsVisible == true)
            return ddlManager;

        return null;
    }

    private ConnectionManagerViewModel? ResolveConnectionManagerForModalRoute()
    {
        if (_connectionModalOwnerDocumentId is Guid ownerId
            && TryResolveConnectionManagerByDocumentId(ownerId, out ConnectionManagerViewModel? ownerManager))
        {
            if (ownerManager?.IsVisible == true)
                return ownerManager;
        }

        return ResolveVisibleConnectionManager()
            ?? ResolveDocumentConnectionManager()
            ?? ResolveSharedConnectionManager();
    }

    private bool TryResolveConnectionManagerByDocumentId(Guid documentId, out ConnectionManagerViewModel? manager)
    {
        OpenWorkspaceDocument? document = _workspaceRouter.OpenDocuments
            .FirstOrDefault(candidate => candidate.Descriptor.DocumentId == documentId);

        if (document?.DocumentViewModel is CanvasViewModel canvas)
        {
            manager = canvas.ConnectionManager;
            return true;
        }

        manager = null;
        return false;
    }

    private void SyncExtractedPanels()
    {
        if (ActivePageContract.ShowsDiagramSidebar)
        {
            LeftSidebar.BindQuerySidebar(ActiveDiagramSidebar);
            RightSidebar.BindPropertyPanel(ActiveDiagramPropertyPanel);
            LeftSidebar.SyncVisibility(ActiveDiagramSidebar is not null);
            RightSidebar.SyncVisibility(ActiveDiagramPropertyPanel is not null);
            return;
        }

        LeftSidebar.BindQuerySidebar(null);
        RightSidebar.BindPropertyPanel(null);
        LeftSidebar.SyncVisibility(false);
        RightSidebar.SyncVisibility(false);
    }

    private void UpdateActiveCanvasContext()
    {
        ActiveCanvasContext = ActiveWorkspaceDocumentType switch
        {
            WorkspaceDocumentType.DdlCanvas when _isViewSubcanvasActive => CanvasContext.ViewSubcanvas,
            WorkspaceDocumentType.DdlCanvas => CanvasContext.Ddl,
            _ => CanvasContext.Query,
        };
    }

    private void ActivateDocumentCore(WorkspaceDocumentType documentType)
    {
        if (documentType == WorkspaceDocumentType.DdlCanvas)
            EnsureDdlCanvas();

        bool changed = documentType switch
        {
            WorkspaceDocumentType.QueryCanvas => _queryDocumentId.HasValue && _workspaceRouter.TryActivate(_queryDocumentId.Value),
            WorkspaceDocumentType.DdlCanvas => _ddlDocumentId.HasValue && _workspaceRouter.TryActivate(_ddlDocumentId.Value),
            WorkspaceDocumentType.SqlEditor => TryActivateLastDocumentByType(WorkspaceDocumentType.SqlEditor),
            WorkspaceDocumentType.SqlResult => TryActivateLastDocumentByType(WorkspaceDocumentType.SqlResult),
            WorkspaceDocumentType.ErDiagram => TryActivateLastDocumentByType(WorkspaceDocumentType.ErDiagram),
            WorkspaceDocumentType.DdlSchemaCompare => TryActivateLastDocumentByType(WorkspaceDocumentType.DdlSchemaCompare),
            WorkspaceDocumentType.DdlSchemaAnalysis => TryActivateLastDocumentByType(WorkspaceDocumentType.DdlSchemaAnalysis),
            _ => false,
        };

        if (!changed)
            return;

        if (documentType == WorkspaceDocumentType.ErDiagram)
            RefreshErDiagramDocument();

        SyncStateFromActiveDocument();
        RaiseActiveDocumentPropertiesChanged();
        SyncExtractedPanels();
    }

    private bool TryActivateLastDocumentByType(WorkspaceDocumentType documentType)
    {
        OpenWorkspaceDocument? target = _workspaceRouter.OpenDocuments
            .LastOrDefault(document => document.Descriptor.DocumentType == documentType);
        return target is not null && _workspaceRouter.TryActivate(target.Descriptor.DocumentId);
    }

    private void SyncStateFromActiveDocument()
    {
        bool changedDocumentRoute = _lastSyncedWorkspaceDocumentId != ActiveWorkspaceDocumentId;
        _lastSyncedWorkspaceDocumentId = ActiveWorkspaceDocumentId;
        if (changedDocumentRoute)
            ClearConnectionModalRoute();

        // Rebind observers whenever active workspace document changes so the
        // overlay visibility reacts to the active document manager instance.
        RefreshConnectionManagerObservers();

        if (ActiveWorkspaceDocumentType != WorkspaceDocumentType.DdlCanvas)
            _isViewSubcanvasActive = false;

        RaisePropertyChanged(nameof(ActiveCanvas));
        RaisePropertyChanged(nameof(ActiveDiagramSidebar));
        RaisePropertyChanged(nameof(ActiveDiagramPropertyPanel));
        switch (ActiveWorkspaceDocumentType)
        {
            case WorkspaceDocumentType.QueryCanvas:
                CloseDiagramConnectionManager(ResolveKnownDdlCanvas());
                break;
            case WorkspaceDocumentType.DdlCanvas:
                CloseDiagramConnectionManager(ResolveKnownQueryCanvas());
                break;
            case WorkspaceDocumentType.SqlEditor:
            case WorkspaceDocumentType.SqlResult:
            case WorkspaceDocumentType.ErDiagram:
            case WorkspaceDocumentType.DdlSchemaCompare:
            case WorkspaceDocumentType.DdlSchemaAnalysis:
                HideDiagramOnlyOverlays();
                break;
        }
        UpdateActiveCanvasContext();
    }

    private static void CloseDiagramConnectionManager(CanvasViewModel? canvas)
    {
        if (canvas is null)
            return;

        canvas.ConnectionManager.IsVisible = false;
        canvas.ConnectionManager.CloseClearCanvasPromptCommand.Execute(null);
    }

    private void RaiseActiveDocumentPropertiesChanged()
    {
        RaisePropertyChanged(nameof(OpenWorkspaceDocuments));
        RaisePropertyChanged(nameof(ActiveWorkspaceDocumentId));
        RaisePropertyChanged(nameof(ActiveWorkspaceDocument));
        RaisePropertyChanged(nameof(ActiveWorkspaceDocumentType));
        RaisePropertyChanged(nameof(ActiveMode));
        RaisePropertyChanged(nameof(ActivePageContract));
        RaisePropertyChanged(nameof(ActivePreviewContract));
        RaisePropertyChanged(nameof(ActiveDiagnosticsContract));
        RaisePropertyChanged(nameof(ActiveQueryCanvasDocument));
        RaisePropertyChanged(nameof(ActiveDdlCanvasDocument));
        RaisePropertyChanged(nameof(ActiveSqlEditorDocument));
        RaisePropertyChanged(nameof(ActiveSqlResultDocument));
        RaisePropertyChanged(nameof(ActiveErDiagramDocument));
        RaisePropertyChanged(nameof(ActiveDdlSchemaCompareDocument));
        RaisePropertyChanged(nameof(ActiveDdlSchemaAnalysisDocument));
        RaisePropertyChanged(nameof(IsQueryDocumentPageActive));
        RaisePropertyChanged(nameof(IsDdlDocumentPageActive));
        RaisePropertyChanged(nameof(IsSqlEditorDocumentPageActive));
        RaisePropertyChanged(nameof(IsSqlResultDocumentPageActive));
        RaisePropertyChanged(nameof(IsErDiagramDocumentPageActive));
        RaisePropertyChanged(nameof(IsDdlSchemaCompareDocumentPageActive));
        RaisePropertyChanged(nameof(IsDdlSchemaAnalysisDocumentPageActive));
        RaisePropertyChanged(nameof(IsQueryModeActive));
        RaisePropertyChanged(nameof(IsDdlModeActive));
        RaisePropertyChanged(nameof(IsSqlEditorModeActive));
        RaisePropertyChanged(nameof(IsSqlResultModeActive));
        RaisePropertyChanged(nameof(IsErDiagramModeActive));
        RaisePropertyChanged(nameof(IsDdlSchemaCompareModeActive));
        RaisePropertyChanged(nameof(IsDdlSchemaAnalysisModeActive));
        RaisePropertyChanged(nameof(IsDiagramDocumentPageActive));
        RaisePropertyChanged(nameof(IsDiagramModeActive));
        RaisePropertyChanged(nameof(IsDiagramOverlayLayerVisible));
        RaisePropertyChanged(nameof(ActiveConnectionManager));
        RaisePropertyChanged(nameof(ConnectionManagerOverlay));
        RaisePropertyChanged(nameof(IsConnectionManagerVisible));
        RaisePropertyChanged(nameof(IsConnectionManagerOverlayVisible));
        RaisePropertyChanged(nameof(QueryConnectionManager));
        RaisePropertyChanged(nameof(DdlConnectionManager));
        RaisePropertyChanged(nameof(IsQueryConnectionManagerOverlayVisible));
        RaisePropertyChanged(nameof(IsDdlConnectionManagerOverlayVisible));
        RaisePropertyChanged(nameof(IsOutputPreviewModalVisible));
        RaisePropertyChanged(nameof(IsQuickDataPreviewModalVisible));
        RaisePropertyChanged(nameof(ActiveCanvas));
        RaisePropertyChanged(nameof(ActiveDiagramSidebar));
        RaisePropertyChanged(nameof(ActiveDiagramPropertyPanel));
        LogShellConnectionOverlay("RaiseActiveDocumentPropertiesChanged");
    }

    private void HideDiagramOnlyOverlays()
    {
        OutputPreview.IsVisible = false;
        QuickDataPreview.Close();

        if (Canvas is not null)
        {
            Canvas.ManualJoinDialog.Close();
            Canvas.ConnectionManager.IsVisible = false;
            Canvas.ConnectionManager.CloseClearCanvasPromptCommand.Execute(null);
        }

        if (DdlCanvas is not null)
        {
            DdlCanvas.ManualJoinDialog.Close();
            DdlCanvas.ConnectionManager.IsVisible = false;
            DdlCanvas.ConnectionManager.CloseClearCanvasPromptCommand.Execute(null);
        }

        RaisePropertyChanged(nameof(IsConnectionManagerOverlayVisible));
        RaisePropertyChanged(nameof(ConnectionManagerOverlay));
        RaisePropertyChanged(nameof(IsOutputPreviewModalVisible));
        RaisePropertyChanged(nameof(IsQuickDataPreviewModalVisible));
        RaisePropertyChanged(nameof(QueryConnectionManager));
        RaisePropertyChanged(nameof(DdlConnectionManager));
        RaisePropertyChanged(nameof(IsQueryConnectionManagerOverlayVisible));
        RaisePropertyChanged(nameof(IsDdlConnectionManagerOverlayVisible));
    }

    private void RegisterOrUpdateQueryDocument(CanvasViewModel queryCanvas)
    {
        _queryDocumentId ??= Guid.NewGuid();
        bool shouldActivate = _workspaceRouter.ActiveDocument is null;
        RegisterOrUpdateDocument(
            _queryDocumentId.Value,
            WorkspaceDocumentType.QueryCanvas,
            title: "Query Canvas",
            documentViewModel: queryCanvas,
            activate: shouldActivate);

        if (shouldActivate)
        {
            SyncStateFromActiveDocument();
            SyncExtractedPanels();
        }
    }

    private void RegisterOrUpdateDdlDocument(CanvasViewModel ddlCanvas)
    {
        _ddlDocumentId ??= Guid.NewGuid();
        RegisterOrUpdateDocument(
            _ddlDocumentId.Value,
            WorkspaceDocumentType.DdlCanvas,
            title: "DDL Canvas",
            documentViewModel: ddlCanvas);
    }

    private Guid OpenNewQueryDocument()
    {
        int nextOrdinal = _workspaceRouter.OpenDocuments.Count(document =>
            document.Descriptor.DocumentType == WorkspaceDocumentType.QueryCanvas) + 1;
        string title = nextOrdinal == 1 ? "Query Canvas" : $"Query Canvas {nextOrdinal}";
        var canvas = new CanvasViewModel(
            nodeManager: null,
            pinManager: null,
            selectionManager: null,
            localizationService: null,
            domainStrategy: new QueryDomainStrategy(),
            toastCenter: Toasts,
            connectionManagerFactory: _connectionManagerViewModelFactory);
        canvas.ApplyProjectConventionSettings(_projectConventionSettings);
        BindPropertyPanelActions(canvas);
        Guid documentId = Guid.NewGuid();
        RegisterOrUpdateDocument(documentId, WorkspaceDocumentType.QueryCanvas, title, canvas, activate: true);
        _canvas ??= canvas;
        SyncStateFromActiveDocument();
        RaiseActiveDocumentPropertiesChanged();
        SyncExtractedPanels();
        return documentId;
    }

    private Guid OpenNewDdlDocument()
    {
        int nextOrdinal = _workspaceRouter.OpenDocuments.Count(document =>
            document.Descriptor.DocumentType == WorkspaceDocumentType.DdlCanvas) + 1;
        string title = nextOrdinal == 1 ? "DDL Canvas" : $"DDL Canvas {nextOrdinal}";
        var ddlCanvas = new CanvasViewModel(
            nodeManager: null,
            pinManager: null,
            selectionManager: null,
            localizationService: null,
            domainStrategy: new DdlDomainStrategy(),
            toastCenter: Toasts,
            connectionManagerFactory: _connectionManagerViewModelFactory);
        ddlCanvas.ApplyProjectConventionSettings(_projectConventionSettings);
        BindPropertyPanelActions(ddlCanvas);
        Guid documentId = Guid.NewGuid();
        RegisterOrUpdateDocument(documentId, WorkspaceDocumentType.DdlCanvas, title, ddlCanvas, activate: true);
        _ddlCanvas ??= ddlCanvas;
        SyncStateFromActiveDocument();
        RaiseActiveDocumentPropertiesChanged();
        SyncExtractedPanels();
        return documentId;
    }

    private Guid OpenNewSqlEditorDocument()
    {
        int nextOrdinal = _workspaceRouter.OpenDocuments.Count(document =>
            document.Descriptor.DocumentType == WorkspaceDocumentType.SqlEditor) + 1;
        string title = nextOrdinal == 1 ? "SQL Editor" : $"SQL Editor {nextOrdinal}";
        SqlEditorViewModel sqlEditorDocument = BuildSqlEditorDocument();
        Guid documentId = Guid.NewGuid();
        RegisterOrUpdateDocument(documentId, WorkspaceDocumentType.SqlEditor, title, sqlEditorDocument, activate: true);
        SyncStateFromActiveDocument();
        RaiseActiveDocumentPropertiesChanged();
        SyncExtractedPanels();
        return documentId;
    }

    private Guid OpenNewSqlResultDocument()
    {
        int nextOrdinal = _workspaceRouter.OpenDocuments.Count(document =>
            document.Descriptor.DocumentType == WorkspaceDocumentType.SqlResult) + 1;
        string title = nextOrdinal == 1 ? "SQL Result" : $"SQL Result {nextOrdinal}";
        SqlResultPageViewModel resultDocument = BuildSqlResultDocument();
        Guid documentId = Guid.NewGuid();
        RegisterOrUpdateDocument(documentId, WorkspaceDocumentType.SqlResult, title, resultDocument, activate: true);
        _sqlResultDocumentId ??= documentId;
        SyncStateFromActiveDocument();
        RaiseActiveDocumentPropertiesChanged();
        SyncExtractedPanels();
        return documentId;
    }

    private Guid OpenNewErDiagramDocument()
    {
        int nextOrdinal = _workspaceRouter.OpenDocuments.Count(document =>
            document.Descriptor.DocumentType == WorkspaceDocumentType.ErDiagram) + 1;
        string title = nextOrdinal == 1 ? "ER Diagram" : $"ER Diagram {nextOrdinal}";
        ErCanvasViewModel erCanvas = BuildErDiagramDocument();
        Guid documentId = Guid.NewGuid();
        RegisterOrUpdateDocument(documentId, WorkspaceDocumentType.ErDiagram, title, erCanvas, activate: true);
        _erCanvas ??= erCanvas;
        SyncStateFromActiveDocument();
        RaiseActiveDocumentPropertiesChanged();
        SyncExtractedPanels();
        return documentId;
    }

    private Guid OpenNewDdlSchemaCompareDocument()
    {
        int nextOrdinal = _workspaceRouter.OpenDocuments.Count(document =>
            document.Descriptor.DocumentType == WorkspaceDocumentType.DdlSchemaCompare) + 1;
        string title = nextOrdinal == 1 ? "Table Compare" : $"Table Compare {nextOrdinal}";
        DdlSchemaCompareWorkspaceViewModel compareDocument = BuildDdlSchemaCompareDocument();
        Guid documentId = Guid.NewGuid();
        RegisterOrUpdateDocument(documentId, WorkspaceDocumentType.DdlSchemaCompare, title, compareDocument, activate: true);
        _ddlSchemaCompareDocumentId ??= documentId;
        SyncStateFromActiveDocument();
        RaiseActiveDocumentPropertiesChanged();
        SyncExtractedPanels();
        return documentId;
    }

    private Guid OpenNewDdlSchemaAnalysisDocument()
    {
        int nextOrdinal = _workspaceRouter.OpenDocuments.Count(document =>
            document.Descriptor.DocumentType == WorkspaceDocumentType.DdlSchemaAnalysis) + 1;
        string title = nextOrdinal == 1 ? "Structure Analysis" : $"Structure Analysis {nextOrdinal}";
        DdlSchemaAnalysisWorkspaceViewModel analysisDocument = BuildDdlSchemaAnalysisDocument();
        Guid documentId = Guid.NewGuid();
        RegisterOrUpdateDocument(documentId, WorkspaceDocumentType.DdlSchemaAnalysis, title, analysisDocument, activate: true);
        _ddlSchemaAnalysisDocumentId ??= documentId;
        SyncStateFromActiveDocument();
        RaiseActiveDocumentPropertiesChanged();
        SyncExtractedPanels();
        return documentId;
    }

    private void RegisterOrUpdateDocument(
        Guid documentId,
        WorkspaceDocumentType documentType,
        string title,
        object documentViewModel,
        bool activate = false)
    {
        WorkspaceDocumentDescriptor descriptor = new(
            DocumentId: documentId,
            DocumentType: documentType,
            Title: title,
            IsDirty: false,
            PersistenceSchemaVersion: "1.0",
            Payload: JsonSerializer.SerializeToElement(new { }));

        _workspaceRouter.OpenDocument(new OpenWorkspaceDocument(
            Descriptor: descriptor,
            DocumentViewModel: documentViewModel,
            PageViewModel: documentViewModel,
            PageState: null), activate);

        RaiseActiveDocumentPropertiesChanged();
    }

    private DdlSchemaCompareWorkspaceViewModel BuildDdlSchemaCompareDocument()
    {
        return new DdlSchemaCompareWorkspaceViewModel(_sqlEditorConnectionManager);
    }

    private DdlSchemaAnalysisWorkspaceViewModel BuildDdlSchemaAnalysisDocument()
    {
        var viewModel = new DdlSchemaAnalysisWorkspaceViewModel(
            _sqlEditorConnectionManager,
            OpenMetadataInDdlCanvas);
        viewModel.OpenConnectionManagerRequested += () =>
        {
            ConnectionManagerViewModel? manager = ResolveSqlEditorConnectionManager();
            if (manager is not null)
                manager.IsVisible = true;
        };
        return viewModel;
    }

    private void OpenMetadataInDdlCanvas(DdlSchemaAnalysisOpenDdlRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        DbMetadata metadata = request.Metadata;
        ArgumentNullException.ThrowIfNull(metadata);
        CanvasViewModel ddlCanvas = EnsureDdlCanvas();

        var scratch = new CanvasViewModel(
            nodeManager: null,
            pinManager: null,
            selectionManager: null,
            localizationService: null,
            domainStrategy: new DdlDomainStrategy(),
            toastCenter: Toasts,
            connectionManagerFactory: _connectionManagerViewModelFactory)
        {
            Provider = metadata.Provider,
        };

        var importer = new DdlSchemaImporter();
        DdlImportResult result = importer.Import(metadata, scratch);

        ConnectionProfile? sourceProfile = !string.IsNullOrWhiteSpace(request.ProfileId)
            ? _sqlEditorConnectionManager.Profiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, request.ProfileId, StringComparison.OrdinalIgnoreCase))
            : null;

        ConnectionConfig? ddlConnectionConfig = sourceProfile?.ToConnectionConfig();
        if (ddlConnectionConfig is not null && !string.IsNullOrWhiteSpace(request.DatabaseName))
            ddlConnectionConfig = ddlConnectionConfig with { Database = request.DatabaseName! };

        ddlCanvas.Provider = metadata.Provider;
        ddlCanvas.SetDatabaseContext(metadata, ddlConnectionConfig);
        ddlCanvas.ReplaceGraph(scratch.Nodes.ToList(), scratch.Connections.ToList());

        if (sourceProfile is not null)
        {
            ConnectionManagerViewModel ddlConnectionManager = ddlCanvas.ConnectionManager;
            ConnectionProfile? ddlProfile = ddlConnectionManager.Profiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, sourceProfile.Id, StringComparison.OrdinalIgnoreCase));
            if (ddlProfile is not null)
            {
                ddlConnectionManager.SelectedProfile = ddlProfile;
                ddlConnectionManager.ActiveProfileId = ddlProfile.Id;
                if (!string.IsNullOrWhiteSpace(request.DatabaseName))
                    ddlConnectionManager.SelectedDatabase = request.DatabaseName;
                if (!string.IsNullOrWhiteSpace(request.SchemaName))
                    ddlConnectionManager.SelectedSchema = request.SchemaName;
            }
        }

        ActivateDocument(WorkspaceDocumentType.DdlCanvas);
        if (result.TableCount > 0)
        {
            Toasts.ShowSuccess(
                "Schema aberto no modo DDL.",
                $"{result.TableCount} tabela(s), {result.ColumnCount} coluna(s), {result.ForeignKeyCount} FK(s).");
        }
        else
        {
            Toasts.ShowWarning("Nenhuma tabela encontrada para abrir no modo DDL.");
        }
    }

    private void LogShellConnectionOverlay(string message)
    {
        string line =
            $"{DateTimeOffset.Now:O} | message={message} | activeType={(ActiveWorkspaceDocumentType?.ToString() ?? "<null>")} | activeId={(ActiveWorkspaceDocumentId?.ToString() ?? "<null>")} | startVisible={IsStartVisible} | canvasVisible={IsCanvasVisible} | queryVisible={(QueryConnectionManager?.IsVisible == true)} | ddlVisible={(DdlConnectionManager?.IsVisible == true)} | overlayVisible={IsConnectionManagerOverlayVisible} | overlayManager={(ConnectionManagerOverlay is null ? "<null>" : ConnectionManagerOverlay.GetHashCode().ToString())}";
        Debug.WriteLine(line);
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, ShellConnectionModalLogFile);
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch
        {
            // Diagnostics only.
        }
    }

    private CanvasViewModel BuildCanvasDocument(SavedWorkspaceDocument savedDocument, bool isDdl)
    {
        var canvas = new CanvasViewModel(
            nodeManager: null,
            pinManager: null,
            selectionManager: null,
            localizationService: null,
            domainStrategy: isDdl ? new DdlDomainStrategy() : new QueryDomainStrategy(),
            toastCenter: Toasts,
            connectionManagerFactory: _connectionManagerViewModelFactory);
        canvas.ApplyProjectConventionSettings(_projectConventionSettings);
        BindPropertyPanelActions(canvas);

        if (savedDocument.CanvasPayload is SavedCanvas payload)
        {
            string payloadJson = JsonSerializer.Serialize(payload);
            CanvasSerializer.Deserialize(payloadJson, canvas);
        }

        return canvas;
    }

    private SqlEditorViewModel BuildSqlEditorDocument()
    {
        SqlEditorViewModel viewModel = _sqlEditorViewModelFactory.Create(new SqlEditorViewModelFactoryContext
        {
            ConnectionConfigResolver = ResolveSqlEditorActiveConnectionConfig,
            ConnectionConfigByProfileIdResolver = ResolveSqlEditorConnectionByProfileId,
            ConnectionProfilesResolver = ResolveSqlEditorConnectionProfiles,
            MetadataResolver = ResolveSqlEditorMetadata,
            SharedConnectionManagerResolver = ResolveSqlEditorConnectionManager,
        });
        viewModel.SqlResultPageRequested -= OnSqlEditorResultPageRequested;
        viewModel.SqlResultPageRequested += OnSqlEditorResultPageRequested;
        return viewModel;
    }

    private SqlResultPageViewModel BuildSqlResultDocument()
    {
        _sqlResultPage ??= new SqlResultPageViewModel();
        _sqlResultPage.ConfigureSessionService(_sqlResultSessionService);
        _sqlResultPage.ConfigureConnectionResolver(ResolveSqlEditorConnectionByProfileId);
        _sqlResultPage.ConfigureMetadataResolver(ResolveSqlEditorMetadata);
        _sqlResultPage.ConfigureSqlAppendToEditor((sourceDocumentId, sql) =>
        {
            if (string.IsNullOrWhiteSpace(sql))
                return;

            SqlEditorViewModel? targetEditor = null;
            if (sourceDocumentId.HasValue)
            {
                targetEditor = _workspaceRouter.OpenDocuments
                    .FirstOrDefault(document => document.Descriptor.DocumentId == sourceDocumentId.Value)
                    ?.DocumentViewModel as SqlEditorViewModel;
            }

            targetEditor ??= ActiveSqlEditorDocument;
            targetEditor ??= _workspaceRouter.OpenDocuments
                .FirstOrDefault(document => document.Descriptor.DocumentType == WorkspaceDocumentType.SqlEditor)
                ?.DocumentViewModel as SqlEditorViewModel;

            if (targetEditor is null)
                return;

            targetEditor.AppendTextToEditor(sql);

            Guid? targetDocumentId = _workspaceRouter.OpenDocuments
                .FirstOrDefault(document => ReferenceEquals(document.DocumentViewModel, targetEditor))
                ?.Descriptor.DocumentId;
            if (targetDocumentId.HasValue && TryActivateWorkspaceDocument(targetDocumentId.Value))
                return;

            ActivateDocument(WorkspaceDocumentType.SqlEditor);
        });
        _sqlResultPage.ConfigureQuickPreview(async (sourceDocumentId, sql, provider, focusTableFullName, subtitle) =>
            await OpenQuickDataPreviewForForeignKeyAsync(
                sourceDocumentId,
                sql,
                provider,
                focusTableFullName,
                subtitle));
        _sqlResultPage.ConfigureBackNavigation(sourceDocumentId =>
        {
            if (sourceDocumentId.HasValue && TryActivateWorkspaceDocument(sourceDocumentId.Value))
                return;

            ActivateDocument(WorkspaceDocumentType.SqlEditor);
        });
        return _sqlResultPage;
    }

    private void OnSqlEditorResultPageRequested(object? sender, SqlResultPageRequestedEventArgs e)
    {
        if (sender is not SqlEditorViewModel sourceEditor)
            return;

        Guid? sourceDocumentId = _workspaceRouter.OpenDocuments
            .FirstOrDefault(document => ReferenceEquals(document.DocumentViewModel, sourceEditor))
            ?.Descriptor.DocumentId;

        SqlResultSession session = _sqlResultSessionService.Add(e.Request);
        SqlResultPageViewModel resultPage = EnsureSqlResultDocument();
        resultPage.SetSession(session, sourceDocumentId);
        ActivateDocument(WorkspaceDocumentType.SqlResult);
    }

    private SqlResultPageViewModel EnsureSqlResultDocument()
    {
        if (_sqlResultDocumentId.HasValue)
        {
            SqlResultPageViewModel? existing = _workspaceRouter.OpenDocuments
                .FirstOrDefault(document =>
                    document.Descriptor.DocumentType == WorkspaceDocumentType.SqlResult)
                ?.DocumentViewModel as SqlResultPageViewModel;
            if (existing is not null)
            {
                _sqlResultPage = existing;
                return existing;
            }
        }

        Guid id = OpenNewDocument(WorkspaceDocumentType.SqlResult);
        SqlResultPageViewModel? created = _workspaceRouter.OpenDocuments
            .FirstOrDefault(document => document.Descriptor.DocumentId == id)
            ?.DocumentViewModel as SqlResultPageViewModel;
        if (created is not null)
        {
            _sqlResultPage = created;
            return created;
        }

        _sqlResultPage ??= BuildSqlResultDocument();
        return _sqlResultPage;
    }

    private ErCanvasViewModel BuildErDiagramDocument()
    {
        DbMetadata? metadata = ResolveSharedMetadata();
        var erCanvas = new ErCanvasViewModel();
        erCanvas.BindQueryNavigation(OpenErRelationInQuery);
        erCanvas.BindEntityDefinitionNavigation(OpenErEntityDefinitionInSqlEditor);
        erCanvas.BindSyncToDdl(SyncErToDdlCanvas);
        erCanvas.BindSourceMetadata(metadata);
        return erCanvas;
    }

    private void RefreshErDiagramDocument()
    {
        ErCanvasViewModel? erCanvas = ActiveErDiagramDocument
            ?? _workspaceRouter.OpenDocuments
                .LastOrDefault(document => document.Descriptor.DocumentType == WorkspaceDocumentType.ErDiagram)
                ?.DocumentViewModel as ErCanvasViewModel;
        erCanvas?.BindQueryNavigation(OpenErRelationInQuery);
        erCanvas?.BindEntityDefinitionNavigation(OpenErEntityDefinitionInSqlEditor);
        erCanvas?.BindSyncToDdl(SyncErToDdlCanvas);
        erCanvas?.BindSourceMetadata(ResolveSharedMetadata());
    }

    private bool SyncErToDdlCanvas(ErCanvasSyncRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Entities.Count == 0)
            return false;

        DatabaseProvider provider = ResolveErSyncProvider();
        DbMetadata metadata = BuildMetadataFromErRequest(request, provider);
        CanvasViewModel ddlCanvas = EnsureDdlCanvas();

        var scratch = new CanvasViewModel(
            nodeManager: null,
            pinManager: null,
            selectionManager: null,
            localizationService: null,
            domainStrategy: new DdlDomainStrategy(),
            toastCenter: Toasts,
            connectionManagerFactory: _connectionManagerViewModelFactory)
        {
            Provider = metadata.Provider,
        };

        var importer = new DdlSchemaImporter();
        DdlImportResult result = importer.Import(metadata, scratch);

        ddlCanvas.Provider = metadata.Provider;
        ddlCanvas.ReplaceGraph(scratch.Nodes.ToList(), scratch.Connections.ToList());
        ActivateDocument(WorkspaceDocumentType.DdlCanvas);
        Toasts.ShowSuccess(
            "ER sincronizado para DDL.",
            $"{result.TableCount} tabela(s), {result.ColumnCount} coluna(s), {result.ForeignKeyCount} FK(s).");
        return true;
    }

    private DatabaseProvider ResolveErSyncProvider()
    {
        if (DdlCanvas is not null)
            return DdlCanvas.Provider;

        ConnectionConfig? connection = ResolveSharedActiveConnectionConfig();
        return connection?.Provider ?? DatabaseProvider.Postgres;
    }

    private static DbMetadata BuildMetadataFromErRequest(ErCanvasSyncRequest request, DatabaseProvider provider)
    {
        string defaultSchema = provider switch
        {
            DatabaseProvider.SqlServer => "dbo",
            DatabaseProvider.SQLite => "main",
            _ => "public",
        };

        string ResolveSchema(string? schema) =>
            string.IsNullOrWhiteSpace(schema) ? defaultSchema : schema.Trim();

        var tableLookup = request.Entities.ToDictionary(
            entity => entity.Id,
            StringComparer.OrdinalIgnoreCase);

        var outboundByTable = new Dictionary<string, List<ForeignKeyRelation>>(StringComparer.OrdinalIgnoreCase);
        var inboundByTable = new Dictionary<string, List<ForeignKeyRelation>>(StringComparer.OrdinalIgnoreCase);
        foreach (ErEntityNodeViewModel entity in request.Entities.Where(static e => !e.IsView))
        {
            outboundByTable[entity.Id] = [];
            inboundByTable[entity.Id] = [];
        }

        var allRelations = new List<ForeignKeyRelation>();
        foreach (ErRelationEdgeViewModel edge in request.Edges)
        {
            if (!tableLookup.TryGetValue(edge.ChildEntityId, out ErEntityNodeViewModel? childEntity)
                || !tableLookup.TryGetValue(edge.ParentEntityId, out ErEntityNodeViewModel? parentEntity)
                || childEntity.IsView
                || parentEntity.IsView)
            {
                continue;
            }

            (string childSchema, string childTable) = SplitEntityId(edge.ChildEntityId);
            (string parentSchema, string parentTable) = SplitEntityId(edge.ParentEntityId);
            string constraintName = string.IsNullOrWhiteSpace(edge.ConstraintName)
                ? $"fk_{childTable}_{parentTable}"
                : edge.ConstraintName!;

            int pairCount = Math.Min(edge.ChildColumns.Count, edge.ParentColumns.Count);
            for (int i = 0; i < pairCount; i++)
            {
                var relation = new ForeignKeyRelation(
                    ConstraintName: constraintName,
                    ChildSchema: ResolveSchema(childSchema),
                    ChildTable: childTable,
                    ChildColumn: edge.ChildColumns[i],
                    ParentSchema: ResolveSchema(parentSchema),
                    ParentTable: parentTable,
                    ParentColumn: edge.ParentColumns[i],
                    OnDelete: edge.OnDelete,
                    OnUpdate: edge.OnUpdate,
                    OrdinalPosition: i + 1);
                allRelations.Add(relation);
                outboundByTable[childEntity.Id].Add(relation);
                inboundByTable[parentEntity.Id].Add(relation);
            }
        }

        var schemaGroups = request.Entities
            .Where(static e => !e.IsView)
            .GroupBy(entity => ResolveSchema(entity.Schema), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                List<TableMetadata> tables = group.Select(entity =>
                {
                    IReadOnlyList<ColumnMetadata> columns = entity.Columns
                        .Select((column, index) => new ColumnMetadata(
                            Name: column.ColumnName,
                            DataType: column.DataType,
                            NativeType: column.DataType,
                            IsNullable: column.IsNullable,
                            IsPrimaryKey: column.IsPrimaryKey,
                            IsForeignKey: column.IsForeignKey,
                            IsUnique: column.IsUnique,
                            IsIndexed: column.IsUnique,
                            OrdinalPosition: index + 1,
                            DefaultValue: null,
                            Comment: column.Comment))
                        .ToList();

                    IReadOnlyList<IndexMetadata> uniqueIndexes = entity.Columns
                        .Where(static c => c.IsUnique)
                        .Select(column => new IndexMetadata(
                            Name: $"uq_{entity.Name}_{column.ColumnName}",
                            IsUnique: true,
                            IsClustered: false,
                            IsPrimaryKey: false,
                            Columns: [column.ColumnName]))
                        .ToList();

                    return new TableMetadata(
                        Schema: ResolveSchema(entity.Schema),
                        Name: entity.Name,
                        Kind: TableKind.Table,
                        EstimatedRowCount: entity.EstimatedRowCount,
                        Columns: columns,
                        Indexes: uniqueIndexes,
                        OutboundForeignKeys: outboundByTable.TryGetValue(entity.Id, out List<ForeignKeyRelation>? outbound)
                            ? outbound
                            : [],
                        InboundForeignKeys: inboundByTable.TryGetValue(entity.Id, out List<ForeignKeyRelation>? inbound)
                            ? inbound
                            : []);
                }).ToList();

                return new SchemaMetadata(group.Key, tables);
            })
            .ToList();

        return new DbMetadata(
            DatabaseName: "er_sync",
            Provider: provider,
            ServerVersion: "er",
            CapturedAt: DateTimeOffset.UtcNow,
            Schemas: schemaGroups,
            AllForeignKeys: allRelations);
    }

    private static (string Schema, string Name) SplitEntityId(string entityId)
    {
        int separator = entityId.IndexOf('.', StringComparison.Ordinal);
        if (separator <= 0)
            return ("public", entityId.Trim());

        return (entityId[..separator].Trim(), entityId[(separator + 1)..].Trim());
    }

    private void OpenSqlInEditorFromQuickPreview(string sql, WorkspaceDocumentType? sourceDocumentType)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return;

        SyncSqlEditorConnectionFromSharedManager();
        ActivateDocument(WorkspaceDocumentType.SqlEditor);
        SqlEditor.ReplaceTextInEditor(sql, markAsDirty: false);
        SqlEditor.ConfigureBackNavigation(
            sourceDocumentType.HasValue ? () => ActivateDocument(sourceDocumentType.Value) : null,
            sourceDocumentType switch
            {
                WorkspaceDocumentType.QueryCanvas => "Voltar ao Query",
                WorkspaceDocumentType.DdlCanvas => "Voltar ao DDL",
                WorkspaceDocumentType.SqlResult => "Voltar ao Resultado",
                _ => "Voltar",
            });
        SqlEditor.NotifyConnectionContextChanged();
    }

    private void SyncSqlEditorConnectionFromSharedManager()
    {
        ConnectionManagerViewModel? sharedManager = ResolveSharedConnectionManager();
        if (sharedManager is null)
            return;

        string? activeProfileId = string.IsNullOrWhiteSpace(sharedManager.ActiveProfileId)
            ? null
            : sharedManager.ActiveProfileId.Trim();
        if (!string.IsNullOrWhiteSpace(activeProfileId))
        {
            ConnectionProfile? sqlProfile = _sqlEditorConnectionManager.Profiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, activeProfileId, StringComparison.OrdinalIgnoreCase));
            if (sqlProfile is not null)
            {
                _sqlEditorConnectionManager.SelectedProfile = sqlProfile;
                _sqlEditorConnectionManager.ActiveProfileId = sqlProfile.Id;
            }
        }

        if (!string.IsNullOrWhiteSpace(sharedManager.SelectedDatabase))
            _sqlEditorConnectionManager.SelectedDatabase = sharedManager.SelectedDatabase;

        if (!string.IsNullOrWhiteSpace(sharedManager.SelectedSchema))
            _sqlEditorConnectionManager.SelectedSchema = sharedManager.SelectedSchema;
    }

    private void OpenErEntityDefinitionInSqlEditor(ErEntityNodeViewModel entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (!entity.IsView)
        {
            Toasts.ShowWarning(
                "Somente views podem ser editadas por este fluxo.",
                "Para tabelas, use sincronizacao ER -> DDL.");
            return;
        }

        SyncSqlEditorConnectionFromSharedManager();

        ActivateDocument(WorkspaceDocumentType.SqlEditor);
        SqlEditor.ReplaceTextInEditor(entity.CreateStatementSql ?? string.Empty, markAsDirty: false);
        SqlEditor.ConfigureBackNavigation(() => ActivateDocument(WorkspaceDocumentType.ErDiagram), "Voltar ao ER");
        SqlEditor.NotifyConnectionContextChanged();
        Toasts.ShowSuccess(
            "Definition aberta no SQL Editor.",
            $"{entity.DisplaySchema}.{entity.DisplayName}");
    }

    private void OpenErRelationInQuery(ErRelationEdgeViewModel edge)
    {
        ArgumentNullException.ThrowIfNull(edge);

        ActivateDocument(WorkspaceDocumentType.QueryCanvas);
        CanvasViewModel queryCanvas = ActiveQueryCanvasDocument ?? EnsureCanvas();
        queryCanvas.SetDatabaseContext(ResolveSharedMetadata(), ResolveSharedActiveConnectionConfig());

        // Smart vertical positioning: stack below existing nodes to avoid overlap
        double baseY = queryCanvas.Nodes.Count == 0
            ? 0d
            : queryCanvas.Nodes.Max(static n => n.Position.Y + 240d);
        NodeViewModel? childTable = FindOrInsertTableSourceNode(queryCanvas, edge.ChildEntityId, new Point(80, baseY + 80));
        NodeViewModel? parentTable = FindOrInsertTableSourceNode(queryCanvas, edge.ParentEntityId, new Point(80, baseY + 320));
        if (childTable is null || parentTable is null)
        {
            queryCanvas.NotifyWarning(
                "Não foi possível abrir o relacionamento no Query Canvas.",
                $"{edge.ChildEntityId} -> {edge.ParentEntityId}");
            return;
        }

        if (HasEquivalentJoin(queryCanvas, childTable, parentTable, edge))
        {
            queryCanvas.NotifyWarning(
                "Esse relacionamento já está representado no Query Canvas.",
                edge.JoinPredicateSql);
            return;
        }

        // Resolve which FK column pairs actually have matching pins on the canvas nodes.
        // This avoids silently creating partial join conditions (e.g. only "valor = valor"
        // when the intended join is "id_sancao = id AND valor = valor").
        IReadOnlyList<(string ChildCol, string ParentCol)> validPairs =
            ResolveValidColumnPairs(childTable, parentTable, edge);

        // For single-column FKs, require the primary column to resolve.
        // For composite FKs, require at least the FIRST (ordinal-0) pair to be valid.
        bool primaryPairValid = validPairs.Count > 0
            && (edge.ColumnPairCount <= 1 || validPairs.Any(p =>
                    string.Equals(p.ChildCol, edge.ChildColumn, StringComparison.OrdinalIgnoreCase)));

        if (!primaryPairValid)
        {
            queryCanvas.NotifyWarning(
                "Não foi possível resolver as colunas da relação no Query Canvas.",
                edge.JoinPredicateSql);
            return;
        }

        NodeViewModel joinNode = queryCanvas.SpawnNode(
            NodeDefinitionRegistry.Get(NodeType.Join),
            new Point(Math.Max(childTable.Position.X, parentTable.Position.X) + 360, (childTable.Position.Y + parentTable.Position.Y) / 2d));
        joinNode.Parameters["join_type"] = "INNER";
        joinNode.Parameters["right_source"] = GetTableIdentifier(parentTable);
        joinNode.RaiseParameterChanged("join_type");
        joinNode.RaiseParameterChanged("right_source");

        if (edge.ColumnPairCount <= 1)
        {
            // Set text expression parameters first (always configure the node fully)
            joinNode.Parameters["left_expr"] = $"{GetTableIdentifier(childTable)}.{edge.ChildColumn}";
            joinNode.Parameters["right_expr"] = $"{GetTableIdentifier(parentTable)}.{edge.ParentColumn}";
            joinNode.Parameters["operator"] = "=";
            joinNode.RaiseParameterChanged("left_expr");
            joinNode.RaiseParameterChanged("right_expr");
            joinNode.RaiseParameterChanged("operator");
            // Wire visual pin connections (pre-validated above)
            TryConnectJoinInputs(queryCanvas, joinNode, childTable, parentTable, edge);
        }
        else
        {
            // Composite FK: only pass the pairs that are confirmed valid to avoid wrong conditions
            BuildCompositeJoinCondition(queryCanvas, joinNode, childTable, parentTable, edge, validPairs);
        }

        EnsureSuggestedProjectionForRelation(queryCanvas, childTable, parentTable, edge);

        queryCanvas.FocusNodeById(joinNode.Id);
        queryCanvas.NotifySuccess(
            edge.ColumnPairCount <= 1
                ? "Relacionamento enviado para o Query Canvas."
                : "Relacionamento composto enviado para o Query Canvas.",
            edge.JoinPredicateSql);
    }

    private static NodeViewModel? FindOrInsertTableSourceNode(CanvasViewModel canvas, string entityId, Point fallbackPosition)
    {
        NodeViewModel? existing = FindTableSourceNode(canvas, entityId);
        if (existing is not null)
            return existing;

        if (!canvas.TryInsertSchemaTableNode(entityId, fallbackPosition))
            return null;

        return FindTableSourceNode(canvas, entityId);
    }

    private static NodeViewModel? FindTableSourceNode(CanvasViewModel canvas, string entityId)
    {
        string normalizedEntityId = NormalizeEntityId(entityId);
        string shortName = normalizedEntityId.Split('.').Last();

        return canvas.Nodes.FirstOrDefault(node =>
            node.IsTableSource
            && (string.Equals(node.Subtitle, normalizedEntityId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(node.Title, shortName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(node.Alias, shortName, StringComparison.OrdinalIgnoreCase)));
    }

    private static IReadOnlyList<(string ChildCol, string ParentCol)> ResolveValidColumnPairs(
        NodeViewModel childTable,
        NodeViewModel parentTable,
        ErRelationEdgeViewModel edge)
    {
        var result = new List<(string, string)>(edge.ColumnPairCount);
        for (int i = 0; i < edge.ColumnPairCount; i++)
        {
            string childCol = i < edge.ChildColumns.Count ? edge.ChildColumns[i] : string.Empty;
            string parentCol = i < edge.ParentColumns.Count ? edge.ParentColumns[i] : string.Empty;
            if (string.IsNullOrEmpty(childCol) || string.IsNullOrEmpty(parentCol))
                continue;

            bool childFound = childTable.OutputPins.Any(p =>
                string.Equals(p.Name, childCol, StringComparison.OrdinalIgnoreCase));
            bool parentFound = parentTable.OutputPins.Any(p =>
                string.Equals(p.Name, parentCol, StringComparison.OrdinalIgnoreCase));

            if (childFound && parentFound)
                result.Add((childCol, parentCol));
        }
        return result;
    }

    private static bool TryConnectJoinInputs(
        CanvasViewModel canvas,
        NodeViewModel joinNode,
        NodeViewModel childTable,
        NodeViewModel parentTable,
        ErRelationEdgeViewModel edge)
    {
        PinViewModel? joinLeft = joinNode.InputPins.FirstOrDefault(pin => pin.Name == "left");
        PinViewModel? joinRight = joinNode.InputPins.FirstOrDefault(pin => pin.Name == "right");
        PinViewModel? childPin = childTable.OutputPins.FirstOrDefault(pin =>
            string.Equals(pin.Name, edge.ChildColumn, StringComparison.OrdinalIgnoreCase));
        PinViewModel? parentPin = parentTable.OutputPins.FirstOrDefault(pin =>
            string.Equals(pin.Name, edge.ParentColumn, StringComparison.OrdinalIgnoreCase));

        if (joinLeft is null || joinRight is null || childPin is null || parentPin is null)
            return false;

        canvas.ConnectPins(childPin, joinLeft);
        canvas.ConnectPins(parentPin, joinRight);
        return true;
    }

    private static void BuildCompositeJoinCondition(
        CanvasViewModel canvas,
        NodeViewModel joinNode,
        NodeViewModel childTable,
        NodeViewModel parentTable,
        ErRelationEdgeViewModel edge,
        IReadOnlyList<(string ChildCol, string ParentCol)>? validPairsOverride = null)
    {
        // Use pre-validated pairs if provided; otherwise resolve on the fly
        IReadOnlyList<(string ChildCol, string ParentCol)> pairs =
            validPairsOverride ?? ResolveValidColumnPairs(childTable, parentTable, edge);

        List<NodeViewModel> comparisonNodes = [];
        double conditionX = joinNode.Position.X - 220;
        double conditionY = joinNode.Position.Y - ((pairs.Count - 1) * 70d / 2d);

        for (int i = 0; i < pairs.Count; i++)
        {
            string childColumn = pairs[i].ChildCol;
            string parentColumn = pairs[i].ParentCol;
            PinViewModel? childPin = childTable.OutputPins.FirstOrDefault(pin =>
                string.Equals(pin.Name, childColumn, StringComparison.OrdinalIgnoreCase));
            PinViewModel? parentPin = parentTable.OutputPins.FirstOrDefault(pin =>
                string.Equals(pin.Name, parentColumn, StringComparison.OrdinalIgnoreCase));
            if (childPin is null || parentPin is null)
                continue;

            NodeViewModel equalsNode = canvas.SpawnNode(
                NodeDefinitionRegistry.Get(NodeType.Equals),
                new Point(conditionX, conditionY + (i * 70d)));
            PinViewModel? equalsLeft = equalsNode.InputPins.FirstOrDefault(pin => pin.Name == "left");
            PinViewModel? equalsRight = equalsNode.InputPins.FirstOrDefault(pin => pin.Name == "right");
            if (equalsLeft is null || equalsRight is null)
                continue;

            canvas.ConnectPins(childPin, equalsLeft);
            canvas.ConnectPins(parentPin, equalsRight);
            comparisonNodes.Add(equalsNode);
        }

        if (comparisonNodes.Count == 0)
            return;

        PinViewModel? joinCondition = joinNode.InputPins.FirstOrDefault(pin => pin.Name == "condition");
        if (joinCondition is null)
            return;

        if (comparisonNodes.Count == 1)
        {
            PinViewModel? resultPin = comparisonNodes[0].OutputPins.FirstOrDefault(pin => pin.Name == "result");
            if (resultPin is not null)
                canvas.ConnectPins(resultPin, joinCondition);
            return;
        }

        NodeViewModel andNode = canvas.SpawnNode(
            NodeDefinitionRegistry.Get(NodeType.And),
            new Point(conditionX + 180, joinNode.Position.Y));
        PinViewModel? andConditions = andNode.InputPins.FirstOrDefault(pin => pin.Name == "conditions");
        PinViewModel? andResult = andNode.OutputPins.FirstOrDefault(pin => pin.Name == "result");
        if (andConditions is null || andResult is null)
            return;

        foreach (NodeViewModel comparisonNode in comparisonNodes)
        {
            PinViewModel? resultPin = comparisonNode.OutputPins.FirstOrDefault(pin => pin.Name == "result");
            if (resultPin is not null)
                canvas.ConnectPins(resultPin, andConditions);
        }

        canvas.ConnectPins(andResult, joinCondition);
    }

    private static void EnsureSuggestedProjectionForRelation(
        CanvasViewModel canvas,
        NodeViewModel childTable,
        NodeViewModel parentTable,
        ErRelationEdgeViewModel edge)
    {
        if (HasExplicitProjection(canvas))
            return;

        NodeViewModel resultOutput = EnsureResultOutputNode(canvas);
        NodeViewModel columnSetBuilder = EnsureProjectionColumnSetBuilder(canvas, resultOutput);
        MarkAutoProjection(resultOutput, edge);
        PinViewModel? columnsInput = columnSetBuilder.InputPins.FirstOrDefault(pin =>
            string.Equals(pin.Name, "columns", StringComparison.OrdinalIgnoreCase));
        if (columnsInput is null)
            return;

        foreach ((NodeViewModel tableNode, string columnName) in EnumerateSuggestedProjectionColumns(canvas, childTable, parentTable, edge))
        {
            PinViewModel? columnPin = tableNode.OutputPins.FirstOrDefault(pin =>
                string.Equals(pin.Name, columnName, StringComparison.OrdinalIgnoreCase));
            if (columnPin is null || HasConnection(canvas, columnPin, columnsInput))
                continue;

            canvas.ConnectPins(columnPin, columnsInput);
        }
    }

    private static void MarkAutoProjection(NodeViewModel resultOutput, ErRelationEdgeViewModel edge)
    {
        resultOutput.Parameters[AutoProjectionMarkerParameter] = "true";
        resultOutput.Parameters[AutoProjectionChildEntityParameter] = edge.ChildEntityId;
        resultOutput.Parameters[AutoProjectionParentEntityParameter] = edge.ParentEntityId;
        resultOutput.Parameters[AutoProjectionChildColumnsParameter] = string.Join("|", edge.ChildColumns);
        resultOutput.Parameters[AutoProjectionParentColumnsParameter] = string.Join("|", edge.ParentColumns);
        resultOutput.RaiseParameterChanged(AutoProjectionMarkerParameter);
    }

    private static bool HasExplicitProjection(CanvasViewModel canvas) =>
        canvas.Connections.Any(connection =>
            connection.ToPin?.Owner.Type == NodeType.ResultOutput
            && (string.Equals(connection.ToPin.Name, "columns", StringComparison.OrdinalIgnoreCase)
                || string.Equals(connection.ToPin.Name, "column", StringComparison.OrdinalIgnoreCase)));

    private static NodeViewModel EnsureResultOutputNode(CanvasViewModel canvas)
    {
        NodeViewModel? existing = canvas.Nodes.FirstOrDefault(node => node.Type == NodeType.ResultOutput);
        if (existing is not null)
            return existing;

        double x = canvas.Nodes.Count == 0 ? 960d : canvas.Nodes.Max(node => node.Position.X) + 420d;
        double y = canvas.Nodes.Count == 0 ? 160d : canvas.Nodes.Average(node => node.Position.Y);
        return canvas.SpawnNode(NodeDefinitionRegistry.Get(NodeType.ResultOutput), new Point(x, y));
    }

    private static NodeViewModel EnsureProjectionColumnSetBuilder(CanvasViewModel canvas, NodeViewModel resultOutput)
    {
        ConnectionViewModel? existingConnection = canvas.Connections.FirstOrDefault(connection =>
            connection.ToPin is not null
            && ReferenceEquals(connection.ToPin.Owner, resultOutput)
            && string.Equals(connection.ToPin.Name, "columns", StringComparison.OrdinalIgnoreCase)
            && connection.FromPin.Owner.Type == NodeType.ColumnSetBuilder);
        if (existingConnection is not null)
            return existingConnection.FromPin.Owner;

        NodeViewModel builder = canvas.SpawnNode(
            NodeDefinitionRegistry.Get(NodeType.ColumnSetBuilder),
            new Point(resultOutput.Position.X - 260d, resultOutput.Position.Y));
        PinViewModel? builderResult = builder.OutputPins.FirstOrDefault(pin =>
            string.Equals(pin.Name, "result", StringComparison.OrdinalIgnoreCase));
        PinViewModel? resultColumns = resultOutput.InputPins.FirstOrDefault(pin =>
            string.Equals(pin.Name, "columns", StringComparison.OrdinalIgnoreCase));
        if (builderResult is not null && resultColumns is not null && !HasConnection(canvas, builderResult, resultColumns))
            canvas.ConnectPins(builderResult, resultColumns);

        return builder;
    }

    private static IEnumerable<(NodeViewModel TableNode, string ColumnName)> EnumerateSuggestedProjectionColumns(
        CanvasViewModel canvas,
        NodeViewModel childTable,
        NodeViewModel parentTable,
        ErRelationEdgeViewModel edge)
    {
        TableMetadata? childMetadata = canvas.DatabaseMetadata?.FindTable(GetTableIdentifier(childTable));
        TableMetadata? parentMetadata = canvas.DatabaseMetadata?.FindTable(GetTableIdentifier(parentTable));
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (ColumnMetadata column in childMetadata?.PrimaryKeyColumns ?? [])
        {
            if (emitted.Add($"{GetTableIdentifier(childTable)}.{column.Name}"))
                yield return (childTable, column.Name);
        }

        foreach (string childColumn in edge.ChildColumns)
        {
            if (emitted.Add($"{GetTableIdentifier(childTable)}.{childColumn}"))
                yield return (childTable, childColumn);
        }

        foreach (string parentColumn in edge.ParentColumns)
        {
            if (emitted.Add($"{GetTableIdentifier(parentTable)}.{parentColumn}"))
                yield return (parentTable, parentColumn);
        }

        ColumnMetadata? descriptiveParentColumn = PickDescriptiveColumn(parentMetadata, edge.ParentColumns);
        if (descriptiveParentColumn is not null
            && emitted.Add($"{GetTableIdentifier(parentTable)}.{descriptiveParentColumn.Name}"))
        {
            yield return (parentTable, descriptiveParentColumn.Name);
        }
    }

    private static ColumnMetadata? PickDescriptiveColumn(
        TableMetadata? table,
        IReadOnlyList<string> excludedColumns)
    {
        if (table is null)
            return null;

        var excluded = new HashSet<string>(excludedColumns, StringComparer.OrdinalIgnoreCase);
        return table.Columns
            .Where(column =>
                !column.IsPrimaryKey
                && !column.IsForeignKey
                && !excluded.Contains(column.Name))
            .OrderByDescending(ScoreDescriptiveColumn)
            .ThenBy(column => column.OrdinalPosition)
            .FirstOrDefault(column => ScoreDescriptiveColumn(column) > 0);
    }

    private static int ScoreDescriptiveColumn(ColumnMetadata column)
    {
        string normalizedName = column.Name.Trim();
        string lowerName = normalizedName.ToLowerInvariant();
        int score = 0;

        if (IsTechnicalColumnName(lowerName))
            score -= 100;

        if (column.SemanticType == ColumnSemanticType.Text)
            score += 30;
        else if (column.SemanticType == ColumnSemanticType.Guid)
            score -= 20;
        else if (column.SemanticType == ColumnSemanticType.DateTime)
            score -= 25;
        else if (column.SemanticType == ColumnSemanticType.Boolean)
            score -= 10;

        if (lowerName is "name" or "full_name" or "display_name")
            score += 120;
        else if (lowerName is "title" or "label")
            score += 110;
        else if (lowerName is "description" or "summary")
            score += 90;
        else if (lowerName is "code" or "email" or "username")
            score += 80;
        else if (lowerName.EndsWith("_name", StringComparison.OrdinalIgnoreCase)
            || lowerName.EndsWith("_title", StringComparison.OrdinalIgnoreCase)
            || lowerName.EndsWith("_label", StringComparison.OrdinalIgnoreCase))
            score += 70;
        else if (lowerName.Contains("description", StringComparison.OrdinalIgnoreCase)
            || lowerName.Contains("summary", StringComparison.OrdinalIgnoreCase))
            score += 60;
        else if (lowerName.Contains("name", StringComparison.OrdinalIgnoreCase)
            || lowerName.Contains("title", StringComparison.OrdinalIgnoreCase)
            || lowerName.Contains("code", StringComparison.OrdinalIgnoreCase)
            || lowerName.Contains("email", StringComparison.OrdinalIgnoreCase))
            score += 45;

        return score;
    }

    private static bool IsTechnicalColumnName(string lowerName) =>
        lowerName is "created_at"
            or "updated_at"
            or "modified_at"
            or "deleted_at"
            or "timestamp"
            or "rowversion"
            or "version"
            or "is_deleted"
            or "deleted"
            or "is_active"
            or "active"
            or "enabled"
            or "is_enabled"
            or "sort_order"
            or "ordinal"
            or "sequence"
            or "position"
            or "created_by"
            or "updated_by"
        || lowerName.EndsWith("_at", StringComparison.OrdinalIgnoreCase)
        || lowerName.EndsWith("_ts", StringComparison.OrdinalIgnoreCase)
        || lowerName.EndsWith("_flag", StringComparison.OrdinalIgnoreCase)
        || lowerName.EndsWith("_version", StringComparison.OrdinalIgnoreCase);

    private static bool HasConnection(CanvasViewModel canvas, PinViewModel fromPin, PinViewModel toPin) =>
        canvas.Connections.Any(connection =>
            ReferenceEquals(connection.FromPin, fromPin)
            && ReferenceEquals(connection.ToPin, toPin));

    private static bool IsAutoProjectionResultOutput(NodeViewModel? node) =>
        node?.Type == NodeType.ResultOutput
        && string.Equals(
            node.Parameters.GetValueOrDefault(AutoProjectionMarkerParameter),
            "true",
            StringComparison.OrdinalIgnoreCase);

    private static NodeViewModel? FindProjectionColumnSetBuilder(CanvasViewModel canvas, NodeViewModel resultOutput)
    {
        ConnectionViewModel? connection = canvas.Connections.FirstOrDefault(candidate =>
            candidate.ToPin is not null
            && ReferenceEquals(candidate.ToPin.Owner, resultOutput)
            && string.Equals(candidate.ToPin.Name, "columns", StringComparison.OrdinalIgnoreCase)
            && candidate.FromPin.Owner.Type == NodeType.ColumnSetBuilder);
        return connection?.FromPin.Owner;
    }

    private static int RefineAutoProjection(CanvasViewModel canvas, NodeViewModel resultOutput)
    {
        if (!IsAutoProjectionResultOutput(resultOutput))
            return 0;

        NodeViewModel columnSetBuilder = FindProjectionColumnSetBuilder(canvas, resultOutput)
            ?? EnsureProjectionColumnSetBuilder(canvas, resultOutput);
        PinViewModel? columnsInput = columnSetBuilder.InputPins.FirstOrDefault(pin =>
            string.Equals(pin.Name, "columns", StringComparison.OrdinalIgnoreCase));
        if (columnsInput is null)
            return 0;

        int added = 0;
        foreach (NodeViewModel tableNode in canvas.Nodes
                     .Where(node => node.IsTableSource)
                     .OrderBy(node => node.Position.X)
                     .ThenBy(node => node.Position.Y))
        {
            if (HasProjectedWildcardForTable(canvas, columnSetBuilder, tableNode))
                continue;

            TableMetadata? tableMetadata = canvas.DatabaseMetadata?.FindTable(GetTableIdentifier(tableNode));
            HashSet<string> projectedColumns = GetProjectedColumnsForTable(canvas, columnSetBuilder, tableNode);
            ColumnMetadata? descriptiveColumn = PickDescriptiveColumn(tableMetadata, projectedColumns.ToArray());
            if (descriptiveColumn is null)
                continue;

            PinViewModel? columnPin = tableNode.OutputPins.FirstOrDefault(pin =>
                string.Equals(pin.Name, descriptiveColumn.Name, StringComparison.OrdinalIgnoreCase));
            if (columnPin is null || HasConnection(canvas, columnPin, columnsInput))
                continue;

            canvas.ConnectPins(columnPin, columnsInput);
            added++;
        }

        return added;
    }

    private static bool TryResetAutoProjection(CanvasViewModel canvas, NodeViewModel resultOutput)
    {
        if (!IsAutoProjectionResultOutput(resultOutput))
            return false;

        if (!TryReadStoredAutoProjection(resultOutput, out string? childEntityId, out string? parentEntityId, out IReadOnlyList<string>? childColumns, out IReadOnlyList<string>? parentColumns))
            return false;

        NodeViewModel? childTable = FindTableSourceNode(canvas, childEntityId!);
        NodeViewModel? parentTable = FindTableSourceNode(canvas, parentEntityId!);
        if (childTable is null || parentTable is null)
            return false;

        NodeViewModel columnSetBuilder = FindProjectionColumnSetBuilder(canvas, resultOutput)
            ?? EnsureProjectionColumnSetBuilder(canvas, resultOutput);
        PinViewModel? columnsInput = columnSetBuilder.InputPins.FirstOrDefault(pin =>
            string.Equals(pin.Name, "columns", StringComparison.OrdinalIgnoreCase));
        if (columnsInput is null)
            return false;

        foreach (ConnectionViewModel connection in canvas.Connections
                     .Where(connection =>
                         connection.ToPin is not null
                         && ReferenceEquals(connection.ToPin.Owner, columnSetBuilder)
                         && string.Equals(connection.ToPin.Name, "columns", StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            canvas.DeleteConnection(connection);
        }

        var edge = new ErRelationEdgeViewModel(
            constraintName: "stored-auto-projection",
            childEntityId: childEntityId!,
            parentEntityId: parentEntityId!,
            childColumns: childColumns!,
            parentColumns: parentColumns!,
            onDelete: ReferentialAction.NoAction,
            onUpdate: ReferentialAction.NoAction);
        foreach ((NodeViewModel tableNode, string columnName) in EnumerateSuggestedProjectionColumns(canvas, childTable, parentTable, edge))
        {
            PinViewModel? columnPin = tableNode.OutputPins.FirstOrDefault(pin =>
                string.Equals(pin.Name, columnName, StringComparison.OrdinalIgnoreCase));
            if (columnPin is null || HasConnection(canvas, columnPin, columnsInput))
                continue;

            canvas.ConnectPins(columnPin, columnsInput);
        }

        return true;
    }

    private static bool TryReadStoredAutoProjection(
        NodeViewModel resultOutput,
        out string? childEntityId,
        out string? parentEntityId,
        out IReadOnlyList<string>? childColumns,
        out IReadOnlyList<string>? parentColumns)
    {
        childEntityId = resultOutput.Parameters.GetValueOrDefault(AutoProjectionChildEntityParameter);
        parentEntityId = resultOutput.Parameters.GetValueOrDefault(AutoProjectionParentEntityParameter);
        childColumns = SplitStoredProjectionColumns(resultOutput.Parameters.GetValueOrDefault(AutoProjectionChildColumnsParameter));
        parentColumns = SplitStoredProjectionColumns(resultOutput.Parameters.GetValueOrDefault(AutoProjectionParentColumnsParameter));

        return !string.IsNullOrWhiteSpace(childEntityId)
            && !string.IsNullOrWhiteSpace(parentEntityId)
            && childColumns.Count > 0
            && parentColumns.Count > 0
            && childColumns.Count == parentColumns.Count;
    }

    private static IReadOnlyList<string> SplitStoredProjectionColumns(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static HashSet<string> GetProjectedColumnsForTable(
        CanvasViewModel canvas,
        NodeViewModel columnSetBuilder,
        NodeViewModel tableNode)
    {
        var projected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ConnectionViewModel connection in canvas.Connections.Where(connection =>
                     connection.ToPin is not null
                     && ReferenceEquals(connection.ToPin.Owner, columnSetBuilder)
                     && string.Equals(connection.ToPin.Name, "columns", StringComparison.OrdinalIgnoreCase)
                     && ReferenceEquals(connection.FromPin.Owner, tableNode)))
        {
            projected.Add(connection.FromPin.Name);
        }

        return projected;
    }

    private static bool HasProjectedWildcardForTable(
        CanvasViewModel canvas,
        NodeViewModel columnSetBuilder,
        NodeViewModel tableNode) =>
        canvas.Connections.Any(connection =>
            connection.ToPin is not null
            && ReferenceEquals(connection.ToPin.Owner, columnSetBuilder)
            && string.Equals(connection.ToPin.Name, "columns", StringComparison.OrdinalIgnoreCase)
            && ReferenceEquals(connection.FromPin.Owner, tableNode)
            && string.Equals(connection.FromPin.Name, "*", StringComparison.OrdinalIgnoreCase));

    private static bool HasWhereCondition(CanvasViewModel canvas, NodeViewModel resultOutput) =>
        canvas.Connections.Any(connection =>
            connection.ToPin is not null
            && ReferenceEquals(connection.ToPin.Owner, resultOutput)
            && string.Equals(connection.ToPin.Name, "where", StringComparison.OrdinalIgnoreCase));

    private static bool HasGroupByCondition(CanvasViewModel canvas, NodeViewModel resultOutput) =>
        canvas.Connections.Any(connection =>
            connection.ToPin is not null
            && ReferenceEquals(connection.ToPin.Owner, resultOutput)
            && string.Equals(connection.ToPin.Name, "group_by", StringComparison.OrdinalIgnoreCase));

    private static bool TryAddSuggestedFilter(CanvasViewModel canvas, NodeViewModel resultOutput)
    {
        if (!IsAutoProjectionResultOutput(resultOutput) || HasWhereCondition(canvas, resultOutput))
            return false;

        if (!TryReadStoredAutoProjection(
                resultOutput,
                out _,
                out string? parentEntityId,
                out _,
                out IReadOnlyList<string>? parentColumns))
        {
            return false;
        }

        NodeViewModel? parentTable = FindTableSourceNode(canvas, parentEntityId!);
        if (parentTable is null)
            return false;

        TableMetadata? parentMetadata = canvas.DatabaseMetadata?.FindTable(parentEntityId!);
        string[] excludedColumns = parentColumns?.ToArray() ?? [];
        ColumnMetadata? filterColumn = PickDescriptiveColumn(parentMetadata, excludedColumns)
            ?? parentMetadata?.PrimaryKeyColumns.FirstOrDefault();
        if (filterColumn is null)
            return false;

        PinViewModel? leftPin = parentTable.OutputPins.FirstOrDefault(pin =>
            string.Equals(pin.Name, filterColumn.Name, StringComparison.OrdinalIgnoreCase));
        PinViewModel? wherePin = resultOutput.InputPins.FirstOrDefault(pin =>
            string.Equals(pin.Name, "where", StringComparison.OrdinalIgnoreCase));
        if (leftPin is null || wherePin is null)
            return false;

        Point basePosition = new(resultOutput.Position.X - 280d, resultOutput.Position.Y + 120d);
        NodeViewModel equalsNode = canvas.SpawnNode(
            NodeDefinitionRegistry.Get(NodeType.Equals),
            basePosition);
        PinViewModel? equalsLeft = equalsNode.InputPins.FirstOrDefault(pin => pin.Name == "left");
        PinViewModel? equalsRight = equalsNode.InputPins.FirstOrDefault(pin => pin.Name == "right");
        PinViewModel? equalsResult = equalsNode.OutputPins.FirstOrDefault(pin => pin.Name == "result");
        if (equalsLeft is null || equalsRight is null || equalsResult is null)
            return false;

        NodeViewModel literalNode = CreateSuggestedFilterLiteralNode(canvas, filterColumn, new Point(basePosition.X - 220d, basePosition.Y + 8d));
        PinViewModel? literalResult = literalNode.OutputPins.FirstOrDefault(pin =>
            string.Equals(pin.Name, "result", StringComparison.OrdinalIgnoreCase));
        if (literalResult is null)
            return false;

        canvas.ConnectPins(leftPin, equalsLeft);
        canvas.ConnectPins(literalResult, equalsRight);
        canvas.ConnectPins(equalsResult, wherePin);
        return true;
    }

    private static NodeViewModel CreateSuggestedFilterLiteralNode(
        CanvasViewModel canvas,
        ColumnMetadata column,
        Point position)
    {
        NodeType nodeType;
        string value;

        switch (column.SemanticType)
        {
            case ColumnSemanticType.Numeric:
                nodeType = NodeType.ValueNumber;
                value = "0";
                break;
            case ColumnSemanticType.Boolean:
                nodeType = NodeType.ValueBoolean;
                value = "true";
                break;
            case ColumnSemanticType.DateTime:
                nodeType = NodeType.ValueDateTime;
                value = string.Empty;
                break;
            default:
                nodeType = NodeType.ValueString;
                value = string.Empty;
                break;
        }

        NodeViewModel node = canvas.SpawnNode(NodeDefinitionRegistry.Get(nodeType), position);
        node.Parameters["value"] = value;
        node.RaiseParameterChanged("value");
        return node;
    }

    private static bool TryApplySuggestedAggregation(CanvasViewModel canvas, NodeViewModel resultOutput)
    {
        if (!IsAutoProjectionResultOutput(resultOutput) || HasGroupByCondition(canvas, resultOutput))
            return false;

        if (!TryReadStoredAutoProjection(
                resultOutput,
                out _,
                out string? parentEntityId,
                out _,
                out IReadOnlyList<string>? parentColumns))
        {
            return false;
        }

        NodeViewModel? parentTable = FindTableSourceNode(canvas, parentEntityId!);
        if (parentTable is null)
            return false;

        TableMetadata? parentMetadata = canvas.DatabaseMetadata?.FindTable(parentEntityId!);
        ColumnMetadata? groupColumn = PickDescriptiveColumn(parentMetadata, parentColumns?.ToArray() ?? [])
            ?? parentMetadata?.PrimaryKeyColumns.FirstOrDefault();
        if (groupColumn is null)
            return false;

        PinViewModel? parentGroupPin = parentTable.OutputPins.FirstOrDefault(pin =>
            string.Equals(pin.Name, groupColumn.Name, StringComparison.OrdinalIgnoreCase));
        PinViewModel? outputColumnPin = resultOutput.InputPins.FirstOrDefault(pin =>
            string.Equals(pin.Name, "column", StringComparison.OrdinalIgnoreCase));
        PinViewModel? outputGroupByPin = resultOutput.InputPins.FirstOrDefault(pin =>
            string.Equals(pin.Name, "group_by", StringComparison.OrdinalIgnoreCase));
        if (parentGroupPin is null || outputColumnPin is null || outputGroupByPin is null)
            return false;

        RemoveConnectionsToResultOutputPin(canvas, resultOutput, "columns");
        RemoveConnectionsToResultOutputPin(canvas, resultOutput, "column");
        RemoveConnectionsToResultOutputPin(canvas, resultOutput, "group_by");
        RemoveConnectionsToResultOutputPin(canvas, resultOutput, "order_by");
        RemoveConnectionsToResultOutputPin(canvas, resultOutput, "order_by_desc");

        Point aggregatePosition = new(resultOutput.Position.X - 280d, resultOutput.Position.Y - 60d);
        NodeViewModel countNode = canvas.SpawnNode(
            NodeDefinitionRegistry.Get(NodeType.CountStar),
            aggregatePosition);
        NodeViewModel aliasNode = canvas.SpawnNode(
            NodeDefinitionRegistry.Get(NodeType.Alias),
            new Point(aggregatePosition.X + 180d, aggregatePosition.Y));
        aliasNode.Parameters["alias"] = "related_count";
        aliasNode.RaiseParameterChanged("alias");

        PinViewModel? countPin = countNode.OutputPins.FirstOrDefault(pin =>
            string.Equals(pin.Name, "count", StringComparison.OrdinalIgnoreCase));
        PinViewModel? aliasExpression = aliasNode.InputPins.FirstOrDefault(pin =>
            string.Equals(pin.Name, "expression", StringComparison.OrdinalIgnoreCase));
        PinViewModel? aliasResult = aliasNode.OutputPins.FirstOrDefault(pin =>
            string.Equals(pin.Name, "result", StringComparison.OrdinalIgnoreCase));
        PinViewModel? outputOrderByDesc = resultOutput.InputPins.FirstOrDefault(pin =>
            string.Equals(pin.Name, "order_by_desc", StringComparison.OrdinalIgnoreCase));
        if (countPin is null || aliasExpression is null || aliasResult is null)
            return false;

        canvas.ConnectPins(parentGroupPin, outputColumnPin);
        canvas.ConnectPins(parentGroupPin, outputGroupByPin);
        canvas.ConnectPins(countPin, aliasExpression);
        canvas.ConnectPins(aliasResult, outputColumnPin);
        if (outputOrderByDesc is not null)
            canvas.ConnectPins(aliasResult, outputOrderByDesc);

        return true;
    }

    private static void RemoveConnectionsToResultOutputPin(CanvasViewModel canvas, NodeViewModel resultOutput, string pinName)
    {
        foreach (ConnectionViewModel connection in canvas.Connections
                     .Where(connection =>
                         connection.ToPin is not null
                         && ReferenceEquals(connection.ToPin.Owner, resultOutput)
                         && string.Equals(connection.ToPin.Name, pinName, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            canvas.DeleteConnection(connection);
        }
    }

    private static bool HasEquivalentJoin(
        CanvasViewModel canvas,
        NodeViewModel childTable,
        NodeViewModel parentTable,
        ErRelationEdgeViewModel edge)
    {
        string expectedRightSource = GetTableIdentifier(parentTable);
        string simpleLeftExpr = $"{GetTableIdentifier(childTable)}.{edge.ChildColumn}";
        string simpleRightExpr = $"{GetTableIdentifier(parentTable)}.{edge.ParentColumn}";

        return canvas.Nodes.Any(node =>
            node.IsJoin
            && string.Equals(node.Parameters.GetValueOrDefault("right_source"), expectedRightSource, StringComparison.OrdinalIgnoreCase)
            && ((edge.ColumnPairCount <= 1
                    && string.Equals(node.Parameters.GetValueOrDefault("left_expr"), simpleLeftExpr, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(node.Parameters.GetValueOrDefault("right_expr"), simpleRightExpr, StringComparison.OrdinalIgnoreCase))
                || (edge.ColumnPairCount > 1
                    && HasJoinInputsFrom(node, canvas, childTable, parentTable))));
    }

    private static bool HasJoinInputsFrom(
        NodeViewModel joinNode,
        CanvasViewModel canvas,
        NodeViewModel childTable,
        NodeViewModel parentTable)
    {
        ConnectionViewModel? leftConnection = canvas.Connections.FirstOrDefault(connection =>
            ReferenceEquals(connection.ToPin?.Owner, joinNode)
            && string.Equals(connection.ToPin?.Name, "left", StringComparison.OrdinalIgnoreCase));
        ConnectionViewModel? rightConnection = canvas.Connections.FirstOrDefault(connection =>
            ReferenceEquals(connection.ToPin?.Owner, joinNode)
            && string.Equals(connection.ToPin?.Name, "right", StringComparison.OrdinalIgnoreCase));

        return ReferenceEquals(leftConnection?.FromPin.Owner, childTable)
            && ReferenceEquals(rightConnection?.FromPin.Owner, parentTable);
    }

    private static string GetTableIdentifier(NodeViewModel node) =>
        string.IsNullOrWhiteSpace(node.Subtitle) ? node.Title : node.Subtitle;

    private static bool TryResolveDdlPreviewTarget(CanvasViewModel ddlCanvas, out string? fullTableName)
    {
        fullTableName = null;

        NodeViewModel? selected = ddlCanvas.PropertyPanel.SelectedNode
            ?? ddlCanvas.Nodes.FirstOrDefault(node => node.IsSelected);
        if (selected is null)
            return false;

        if (selected.Type == NodeType.TableDefinition)
        {
            string schemaName = selected.Parameters.GetValueOrDefault("SchemaName")?.Trim() ?? string.Empty;
            string tableName = selected.Parameters.GetValueOrDefault("TableName")?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(tableName))
            {
                fullTableName = string.IsNullOrWhiteSpace(schemaName)
                    ? tableName
                    : $"{schemaName}.{tableName}";
                return true;
            }
        }

        if (selected.Type == NodeType.ViewDefinition)
        {
            string schemaName = selected.Parameters.GetValueOrDefault("SchemaName")?.Trim() ?? string.Empty;
            string viewName = selected.Parameters.GetValueOrDefault("ViewName")?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(viewName))
            {
                fullTableName = string.IsNullOrWhiteSpace(schemaName)
                    ? viewName
                    : $"{schemaName}.{viewName}";
                return true;
            }
        }

        if (selected.Type == NodeType.TableSource)
        {
            fullTableName = !string.IsNullOrWhiteSpace(selected.Subtitle)
                ? selected.Subtitle
                : selected.Title;
            return !string.IsNullOrWhiteSpace(fullTableName);
        }

        return false;
    }

    private static string? TryExtractFirstFromTable(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return null;

        var match = System.Text.RegularExpressions.Regex.Match(
            sql,
            @"\bfrom\s+([a-zA-Z0-9_\.\[\]`""]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!match.Success)
            return null;

        string raw = match.Groups[1].Value.Trim();
        int spaceIndex = raw.IndexOf(' ');
        if (spaceIndex > 0)
            raw = raw[..spaceIndex];

        return raw.Trim('"', '[', ']', '`');
    }

    private static string NormalizeEntityId(string entityId)
    {
        string trimmed = entityId.Trim();
        if (trimmed.Length == 0 || trimmed.Contains('.'))
            return trimmed;

        return $"public.{trimmed}";
    }

    private void BindPropertyPanelActions(CanvasViewModel? canvas)
    {
        if (canvas?.PropertyPanel is null)
            return;

        canvas.PropertyPanel.BindOpenSelectedJoinInErDiagram(OpenSelectedQueryJoinInErDiagramFromPanel);
        canvas.PropertyPanel.BindRefineAutoProjection(() => RefineAutoProjectionFromPanel(canvas));
        canvas.PropertyPanel.BindResetAutoProjection(() => ResetAutoProjectionFromPanel(canvas));
        canvas.PropertyPanel.BindAddSuggestedFilter(() => AddSuggestedFilterFromPanel(canvas));
        canvas.PropertyPanel.BindApplySuggestedAggregation(() => ApplySuggestedAggregationFromPanel(canvas));
    }

    private void OpenSelectedQueryJoinInErDiagramFromPanel()
    {
        if (TryOpenSelectedQueryJoinInErDiagram())
            return;

        Toasts.ShowWarning(
            "Selecione um JOIN valido no Query Canvas para abrir a relacao correspondente no ER.");
    }

    public bool TryRefineSelectedQueryAutoProjection()
    {
        CanvasViewModel? canvas = ActiveQueryCanvasDocument;
        NodeViewModel? selectedNode = canvas?.PropertyPanel.SelectedNode;
        if (canvas is null || selectedNode is null || !IsAutoProjectionResultOutput(selectedNode))
            return false;

        int added = RefineAutoProjection(canvas, selectedNode);
        if (added > 0)
        {
            canvas.PropertyPanel.ShowNode(selectedNode);
            canvas.NotifySuccess(
                "Sugestao de colunas refinada.",
                $"{added} coluna(s) descritiva(s) adicionada(s) ao output.");
            return true;
        }

        canvas.NotifyWarning(
            "Nao ha novas colunas descritivas para adicionar.",
            "A projection atual ja contem as melhores sugestoes disponiveis.");
        return true;
    }

    public bool TryResetSelectedQueryAutoProjection()
    {
        CanvasViewModel? canvas = ActiveQueryCanvasDocument;
        NodeViewModel? selectedNode = canvas?.PropertyPanel.SelectedNode;
        if (canvas is null || selectedNode is null || !IsAutoProjectionResultOutput(selectedNode))
            return false;

        if (!TryResetAutoProjection(canvas, selectedNode))
        {
            canvas.NotifyWarning(
                "Nao foi possivel resetar a sugestao do ER.",
                "O contexto original da projection automatica nao esta mais disponivel.");
            return true;
        }

        canvas.PropertyPanel.ShowNode(selectedNode);
        canvas.NotifySuccess(
            "Sugestao do ER restaurada.",
            "A projection voltou para o conjunto base gerado a partir da relacao selecionada.");
        return true;
    }

    private void RefineAutoProjectionFromPanel(CanvasViewModel canvas)
    {
        NodeViewModel? selectedNode = canvas.PropertyPanel.SelectedNode;
        if (!IsAutoProjectionResultOutput(selectedNode))
        {
            Toasts.ShowWarning(
                "Selecione um ResultOutput autogerado para refinar a sugestao de colunas.");
            return;
        }

        _ = TryRefineSelectedQueryAutoProjection();
    }

    private void ResetAutoProjectionFromPanel(CanvasViewModel canvas)
    {
        NodeViewModel? selectedNode = canvas.PropertyPanel.SelectedNode;
        if (!IsAutoProjectionResultOutput(selectedNode))
        {
            Toasts.ShowWarning(
                "Selecione um ResultOutput autogerado para resetar a sugestao do ER.");
            return;
        }

        _ = TryResetSelectedQueryAutoProjection();
    }

    public bool TryAddSuggestedFilterToSelectedAutoProjection()
    {
        CanvasViewModel? canvas = ActiveQueryCanvasDocument;
        NodeViewModel? selectedNode = canvas?.PropertyPanel.SelectedNode;
        if (canvas is null || selectedNode is null || !IsAutoProjectionResultOutput(selectedNode))
            return false;

        if (!TryAddSuggestedFilter(canvas, selectedNode))
        {
            canvas.NotifyWarning(
                "Nao foi possivel adicionar o filtro sugerido.",
                HasWhereCondition(canvas, selectedNode)
                    ? "O ResultOutput ja possui uma condicao WHERE conectada."
                    : "Nao foi possivel resolver uma coluna adequada para o filtro.");
            return true;
        }

        canvas.PropertyPanel.ShowNode(selectedNode);
        canvas.NotifySuccess(
            "Filtro sugerido adicionado.",
            "O Query Canvas recebeu uma condicao inicial editavel baseada na tabela relacionada.");
        return true;
    }

    private void AddSuggestedFilterFromPanel(CanvasViewModel canvas)
    {
        NodeViewModel? selectedNode = canvas.PropertyPanel.SelectedNode;
        if (!IsAutoProjectionResultOutput(selectedNode))
        {
            Toasts.ShowWarning(
                "Selecione um ResultOutput autogerado para adicionar o filtro sugerido.");
            return;
        }

        _ = TryAddSuggestedFilterToSelectedAutoProjection();
    }

    public bool TryApplySuggestedAggregationToSelectedAutoProjection()
    {
        CanvasViewModel? canvas = ActiveQueryCanvasDocument;
        NodeViewModel? selectedNode = canvas?.PropertyPanel.SelectedNode;
        if (canvas is null || selectedNode is null || !IsAutoProjectionResultOutput(selectedNode))
            return false;

        if (!TryApplySuggestedAggregation(canvas, selectedNode))
        {
            canvas.NotifyWarning(
                "Nao foi possivel aplicar a agregacao sugerida.",
                HasGroupByCondition(canvas, selectedNode)
                    ? "O ResultOutput ja possui agrupamento configurado."
                    : "Nao foi possivel resolver uma coluna adequada para o agrupamento.");
            return true;
        }

        canvas.PropertyPanel.ShowNode(selectedNode);
        canvas.NotifySuccess(
            "Agregacao sugerida aplicada.",
            "O output foi convertido para uma visao inicial de grupo + COUNT(*) da entidade relacionada.");
        return true;
    }

    private void ApplySuggestedAggregationFromPanel(CanvasViewModel canvas)
    {
        NodeViewModel? selectedNode = canvas.PropertyPanel.SelectedNode;
        if (!IsAutoProjectionResultOutput(selectedNode))
        {
            Toasts.ShowWarning(
                "Selecione um ResultOutput autogerado para aplicar a agregacao sugerida.");
            return;
        }

        _ = TryApplySuggestedAggregationToSelectedAutoProjection();
    }

    private static bool TryResolveJoinSelection(
        CanvasViewModel canvas,
        NodeViewModel joinNode,
        out ResolvedErRelation? relation)
    {
        relation = null;

        if (!TryResolveJoinInputConnection(canvas, joinNode, "left", out ConnectionViewModel? leftConnection)
            || !TryResolveJoinInputConnection(canvas, joinNode, "right", out ConnectionViewModel? rightConnection))
        {
            return false;
        }

        NodeViewModel childTable = leftConnection!.FromPin.Owner;
        NodeViewModel parentTable = rightConnection!.FromPin.Owner;
        if (!childTable.IsTableSource || !parentTable.IsTableSource)
            return false;

        var childColumns = new List<string>();
        var parentColumns = new List<string>();

        if (TryResolveJoinConditionPairs(canvas, joinNode, childTable, parentTable, childColumns, parentColumns))
        {
            relation = new ResolvedErRelation(
                GetTableIdentifier(childTable),
                GetTableIdentifier(parentTable),
                childColumns,
                parentColumns);
            return true;
        }

        childColumns.Add(leftConnection.FromPin.Name);
        parentColumns.Add(rightConnection.FromPin.Name);
        relation = new ResolvedErRelation(
            GetTableIdentifier(childTable),
            GetTableIdentifier(parentTable),
            childColumns,
            parentColumns);
        return true;
    }

    private static bool TryResolveJoinConditionPairs(
        CanvasViewModel canvas,
        NodeViewModel joinNode,
        NodeViewModel childTable,
        NodeViewModel parentTable,
        List<string> childColumns,
        List<string> parentColumns)
    {
        if (!TryResolveJoinInputConnection(canvas, joinNode, "condition", out ConnectionViewModel? conditionConnection))
        {
            return TryResolveJoinConditionPairsFromParameters(joinNode, childTable, parentTable, childColumns, parentColumns);
        }

        NodeViewModel conditionNode = conditionConnection!.FromPin.Owner;
        if (conditionNode.Type == NodeType.Equals)
            return TryCollectEqualsPair(canvas, conditionNode, childTable, parentTable, childColumns, parentColumns);

        if (conditionNode.Type != NodeType.And)
            return false;

        IReadOnlyList<ConnectionViewModel> comparisons = canvas.Connections
            .Where(connection =>
                ReferenceEquals(connection.ToPin?.Owner, conditionNode)
                && string.Equals(connection.ToPin?.Name, "conditions", StringComparison.OrdinalIgnoreCase))
            .ToList();

        bool addedAny = false;
        foreach (ConnectionViewModel comparison in comparisons)
        {
            if (comparison.FromPin.Owner.Type != NodeType.Equals)
                continue;

            addedAny |= TryCollectEqualsPair(canvas, comparison.FromPin.Owner, childTable, parentTable, childColumns, parentColumns);
        }

        return addedAny;
    }

    private static bool TryResolveJoinConditionPairsFromParameters(
        NodeViewModel joinNode,
        NodeViewModel childTable,
        NodeViewModel parentTable,
        List<string> childColumns,
        List<string> parentColumns)
    {
        string? leftExpr = joinNode.Parameters.GetValueOrDefault("left_expr");
        string? rightExpr = joinNode.Parameters.GetValueOrDefault("right_expr");

        string? leftSource;
        string? rightSource;
        string leftColumn;
        string rightColumn;

        if (!CanvasAutoJoinSemantics.TryParseQualifiedColumn(leftExpr ?? string.Empty, out leftSource, out leftColumn))
        {
            return false;
        }

        if (!CanvasAutoJoinSemantics.TryParseQualifiedColumn(rightExpr ?? string.Empty, out rightSource, out rightColumn))
        {
            return false;
        }

        if (!MatchesSource(childTable, leftSource) || !MatchesSource(parentTable, rightSource))
            return false;

        childColumns.Add(leftColumn);
        parentColumns.Add(rightColumn);
        return true;
    }

    private static bool TryCollectEqualsPair(
        CanvasViewModel canvas,
        NodeViewModel equalsNode,
        NodeViewModel childTable,
        NodeViewModel parentTable,
        List<string> childColumns,
        List<string> parentColumns)
    {
        if (!TryResolveJoinInputConnection(canvas, equalsNode, "left", out ConnectionViewModel? leftConnection)
            || !TryResolveJoinInputConnection(canvas, equalsNode, "right", out ConnectionViewModel? rightConnection))
        {
            return false;
        }

        if (ReferenceEquals(leftConnection!.FromPin.Owner, childTable)
            && ReferenceEquals(rightConnection!.FromPin.Owner, parentTable))
        {
            childColumns.Add(leftConnection.FromPin.Name);
            parentColumns.Add(rightConnection.FromPin.Name);
            return true;
        }

        if (ReferenceEquals(leftConnection.FromPin.Owner, parentTable)
            && ReferenceEquals(rightConnection!.FromPin.Owner, childTable))
        {
            childColumns.Add(rightConnection.FromPin.Name);
            parentColumns.Add(leftConnection.FromPin.Name);
            return true;
        }

        return false;
    }

    private static bool TryResolveJoinInputConnection(
        CanvasViewModel canvas,
        NodeViewModel node,
        string inputPinName,
        out ConnectionViewModel? connection)
    {
        connection = canvas.Connections.FirstOrDefault(candidate =>
            ReferenceEquals(candidate.ToPin?.Owner, node)
            && string.Equals(candidate.ToPin?.Name, inputPinName, StringComparison.OrdinalIgnoreCase));
        return connection is not null;
    }

    private static bool MatchesSource(NodeViewModel node, string? sourceRef) =>
        !string.IsNullOrWhiteSpace(sourceRef)
        && CanvasAutoJoinSemantics.MatchesSource(node.Subtitle, node.Title, node.Alias, sourceRef);

    private sealed record ResolvedErRelation(
        string ChildEntityId,
        string ParentEntityId,
        IReadOnlyList<string> ChildColumns,
        IReadOnlyList<string> ParentColumns);

    private string Localize(string key, string fallback)
    {
        string value = _localization[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    private ConnectionConfig? ResolveSqlEditorConnectionByProfileId(string? profileId)
    {
        ConnectionManagerViewModel? connectionManager = ResolveSqlEditorConnectionManager();
        if (connectionManager is null)
            return null;

        if (string.IsNullOrWhiteSpace(profileId))
            return ResolveSqlEditorActiveConnectionConfig();

        ConnectionProfile? selected = connectionManager.Profiles
            .FirstOrDefault(profile => string.Equals(profile.Id, profileId, StringComparison.Ordinal));

        return selected?.ToConnectionConfig() ?? ResolveSqlEditorActiveConnectionConfig();
    }

    private IReadOnlyList<SqlEditorConnectionProfileOption> ResolveSqlEditorConnectionProfiles()
    {
        ConnectionManagerViewModel? manager = ResolveSqlEditorConnectionManager();
        if (manager is null)
            return [];

        return manager.Profiles
            .Select(profile => new SqlEditorConnectionProfileOption
            {
                Id = profile.Id,
                DisplayName = profile.Name,
                Provider = profile.Provider,
            })
            .ToList();
    }

    private ConnectionManagerViewModel? ResolveSqlEditorConnectionManager() =>
        _sqlEditorConnectionManager;

    private ConnectionConfig? ResolveSqlEditorActiveConnectionConfig()
    {
        string? activeProfileId = _sqlEditorConnectionManager.ActiveProfileId;
        if (!string.IsNullOrWhiteSpace(activeProfileId))
        {
            ConnectionProfile? selected = _sqlEditorConnectionManager.Profiles
                .FirstOrDefault(profile => string.Equals(profile.Id, activeProfileId, StringComparison.Ordinal));
            if (selected is not null)
                return selected.ToConnectionConfig();
        }

        return null;
    }

    private DbMetadata? ResolveSqlEditorMetadata() =>
        _sqlEditorConnectionManager.ActiveMetadata
        ?? ResolveSharedMetadata();

    private ConnectionManagerViewModel? ResolveSharedConnectionManager() =>
        Canvas?.ConnectionManager ?? DdlCanvas?.ConnectionManager;

    private ConnectionConfig? ResolveSharedActiveConnectionConfig() =>
        Canvas?.ActiveConnectionConfig ?? DdlCanvas?.ActiveConnectionConfig;

    private DbMetadata? ResolveSharedMetadata() =>
        Canvas?.DatabaseMetadata ?? DdlCanvas?.DatabaseMetadata;

    private (CanvasViewModel? Canvas, ConnectionManagerViewModel? Manager) ResolveSharedConnectionContext()
    {
        if (Canvas is not null)
            return (Canvas, Canvas.ConnectionManager);

        if (DdlCanvas is not null)
            return (DdlCanvas, DdlCanvas.ConnectionManager);

        return (null, null);
    }
}
