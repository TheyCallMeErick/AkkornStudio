using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Text.Json;
using Avalonia;
using AkkornStudio.CanvasKit;
using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.Nodes;
using AkkornStudio.UI.Services.Export;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.Serialization;
using AkkornStudio.UI.Controls;
using AkkornStudio.UI.ViewModels.Canvas;
using AkkornStudio.UI.ViewModels.Canvas.Strategies;
using AkkornStudio.UI.Services.QueryPreview;
using AkkornStudio.UI.Services.Ddl;
using AkkornStudio.UI.ViewModels.UndoRedo;
using AkkornStudio.UI.ViewModels.UndoRedo.Commands;
using AkkornStudio.UI.ViewModels.Validation.Conventions;
using AkkornStudio.UI.Services.ConnectionManager;
using AkkornStudio.UI.Services.Settings;
using AkkornStudio.UI.Services.Validation;

namespace AkkornStudio.UI.ViewModels;

/// <summary>
/// Facade for all canvas operations.
/// Coordinates UI interactions by delegating to specialised managers:
///   - <see cref="NodeManager"/>       â€” node lifecycle (spawn, delete, demo)
///   - <see cref="PinManager"/>         â€” connections and type narrowing
///   - <see cref="SelectionManager"/>   â€” node selection and alignment
///   - <see cref="NodeLayoutManager"/>  â€” zoom, pan, snap, auto-layout
///   - <see cref="ValidationManager"/>  â€” graph validation and orphan detection
/// </summary>
public sealed class CanvasViewModel : ViewModelBase, IDisposable, ICanvasViewportState, ICanvasViewportSelectionState
{
    private const double SelectionFrameDefaultNodeHeight = 130d;

    public ObservableCollection<NodeViewModel> Nodes { get; } = [];
    public ObservableCollection<ConnectionViewModel> Connections { get; } = [];

    public bool IsEmpty => Nodes.Count == 0 && Connections.Count == 0;


    public SearchMenuViewModel SearchMenu { get; }
    public DataPreviewViewModel DataPreview { get; } = new();
    public ToastCenterViewModel Toasts { get; }
    public AppDiagnosticsViewModel Diagnostics { get; }
    public PropertyPanelViewModel PropertyPanel { get; }
    public LiveSqlBarViewModel LiveSql { get; set; }
    public AutoJoinOverlayViewModel AutoJoin { get; set; }
    public ManualJoinDialogViewModel ManualJoinDialog { get; }
    public UndoRedoStack UndoRedo { get; }
    public ConnectionManagerViewModel ConnectionManager { get; }
    public BenchmarkViewModel Benchmark { get; private set; } = null!;
    public ExplainPlanViewModel ExplainPlan { get; private set; } = null!;
    public SqlImporterViewModel SqlImporter { get; private set; } = null!;
    public FlowVersionOverlayViewModel FlowVersions { get; private set; } = null!;
    public FileVersionHistoryViewModel FileHistory { get; private set; } = null!;
    public SidebarViewModel Sidebar { get; private set; } = null!;

    private LiveDdlBarViewModel? _liveDdl;
    public LiveDdlBarViewModel? LiveDdl
    {
        get
        {
            if (_liveDdl is null && _domainStrategy is DdlDomainStrategy)
                _liveDdl = new LiveDdlBarViewModel(this);

            return _liveDdl;
        }
    }


    // Tracks per-node PropertyChanged handlers so they can be removed when a node is deleted.
    private readonly Dictionary<NodeViewModel, PropertyChangedEventHandler> _nodeValidationHandlers = new();

    private readonly INodeManager _nodeManager;
    private readonly IPinManager _pinManager;
    private readonly ISelectionManager _selectionManager;
    private readonly ILocalizationService _localizationService;
    private readonly NodeLayoutManager _layoutManager;
    private readonly ValidationManager _validationManager;
    private readonly ICanvasDomainStrategy _domainStrategy;
    private readonly IAliasConventionRegistry _aliasConventionRegistry;

    private SubCanvasEditingController _subCanvasController = null!;
    private CanvasAutoJoinController _autoJoinController = null!;

    // â”€â”€ Event handler storage for proper disposal â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // These fields store references to event handlers so they can be unsubscribed in Dispose()
    private PropertyChangedEventHandler? _liveSqlPropertyChangedHandler;
    private PropertyChangedEventHandler? _selfPropertyChangedHandler;
    private PropertyChangedEventHandler? _layoutManagerPropertyChangedHandler;
    private PropertyChangedEventHandler? _localizationPropertyChangedHandler;
    private PropertyChangedEventHandler? _explainPlanPropertyChangedHandler;
    private NotifyCollectionChangedEventHandler? _nodesCollectionChangedHandler;
    private NotifyCollectionChangedEventHandler? _connectionsCollectionChangedHandler;
    private Action? _propertyPanelWireStyleChangedHandler;
    private readonly HashSet<string> _pendingTableSourceReplacementConfirmations = [];
    private bool _isReplacingTableSourceNode;
    private string? _primaryFromSourceOverrideNodeId;
    private ProjectConventionSettings _projectConventionSettings = AppSettingsStore.LoadProjectConventionSettings() ?? new ProjectConventionSettings();

    // â”€â”€ Canvas state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private string _queryText = "";
    private bool _isDirty;
    private string? _filePath;
    private ConnectionViewModel? _selectedConnection;
    private ConnectionViewModel? _selectedBreakpointConnection;
    private int _selectedBreakpointIndex = -1;
    private CanvasWireCurveMode _wireCurveMode = CanvasWireCurveMode.Bezier;
    private DbMetadata? _databaseMetadata;
    private ConnectionConfig? _activeConnectionConfig;
    private readonly Dictionary<string, string> _previewParameterInputs = new(StringComparer.OrdinalIgnoreCase);
    // Backward-compatibility bridge used by legacy tests that inspect/corrupt
    // sub-editor sessions via reflection on CanvasViewModel.
    private object? _cteEditorSession;
    private object? _viewEditorSession;


    public void SetCanvasContext(CanvasContext context)
    {
        SearchMenu.CanvasContext = context;
        Sidebar.NodesList.CanvasContext = context;
    }

    public string QueryText
    {
        get => _queryText;
        set => Set(ref _queryText, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        set => Set(ref _isDirty, value);
    }

    public string? CurrentFilePath
    {
        get => _filePath;
        set
        {
            Set(ref _filePath, value);
            RaisePropertyChanged(nameof(WindowTitle));
        }
    }

    public CanvasWireCurveMode WireCurveMode
    {
        get => _wireCurveMode;
        set => Set(ref _wireCurveMode, value);
    }

    /// <summary>
    /// The currently connected database metadata (schemas, tables, columns).
    /// Updated when a new database connection is established.
    /// </summary>
    public DbMetadata? DatabaseMetadata
    {
        get => _databaseMetadata;
        set => Set(ref _databaseMetadata, value);
    }

    /// <summary>
    /// The active database connection configuration.
    /// Used to execute queries for preview results.
    /// </summary>
    public ConnectionConfig? ActiveConnectionConfig
    {
        get => _activeConnectionConfig;
        set
        {
            if (!Set(ref _activeConnectionConfig, value))
                return;

            if (LiveSql is not null)
                LiveSql.Provider = value?.Provider ?? DatabaseProvider.Postgres;
        }
    }

    public IReadOnlyDictionary<string, string> PreviewParameterInputs => _previewParameterInputs;

    public string WindowTitle =>
        (
            CurrentFilePath is not null
                ? Path.GetFileNameWithoutExtension(CurrentFilePath)
                : L("main.window.untitled", "Untitled")
        )
        + (IsDirty ? " â€¢" : "")
        + $" - {L("app.windowTitle", "AkkornStudio")}";

    public bool IsCanvasEmpty => Nodes.Count == 0;
    public ConnectionViewModel? SelectedConnection
    {
        get => _selectedConnection;
        private set => Set(ref _selectedConnection, value);
    }
    public bool HasSelectedConnection => SelectedConnection is not null;
    public ConnectionViewModel? SelectedBreakpointConnection
    {
        get => _selectedBreakpointConnection;
        private set => Set(ref _selectedBreakpointConnection, value);
    }
    public int SelectedBreakpointIndex
    {
        get => _selectedBreakpointIndex;
        private set => Set(ref _selectedBreakpointIndex, value);
    }
    public bool HasSelectedBreakpoint => SelectedBreakpointConnection is not null && SelectedBreakpointIndex >= 0;

    /// <summary>
    /// True when canvas should be disabled (e.g., during database connection).
    /// </summary>
    public bool IsCanvasDisabled => ConnectionManager.IsConnecting;
    public bool IsInCteEditor => _subCanvasController.IsInCteEditor;
    public bool IsInViewEditor => _subCanvasController.IsInViewEditor;
    public bool IsCanvasDimmedBySubcanvas => _subCanvasController.IsCanvasDimmedBySubcanvas;
    public string CteEditorBreadcrumb => _subCanvasController.CteEditorBreadcrumb;
    public string EditorExitLabel => _subCanvasController.EditorExitLabel;
    public string EditorExitA11y => _subCanvasController.EditorExitA11y;


    public double Zoom
    {
        get => _layoutManager.Zoom;
        set => _layoutManager.Zoom = value;
    }

    public Point PanOffset
    {
        get => _layoutManager.PanOffset;
        set => _layoutManager.PanOffset = value;
    }

    public bool SnapToGrid
    {
        get => _layoutManager.SnapToGrid;
        set => _layoutManager.SnapToGrid = value;
    }

    public const int GridSize = NodeLayoutManager.GridSize;
    public string ZoomPercent => _layoutManager.ZoomPercent;
    public string SnapToGridLabel => _layoutManager.SnapToGridLabel;


    public bool HasErrors => _validationManager.HasErrors;
    public int ErrorCount => _validationManager.ErrorCount;
    public int WarningCount => _validationManager.WarningCount;
    public bool HasOrphanNodes => _validationManager.HasOrphanNodes;
    public int OrphanCount => _validationManager.OrphanCount;
    public bool HasNamingViolations => _validationManager.HasNamingViolations;
    public int NamingConformance => _validationManager.NamingConformance;


    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
    public RelayCommand DeleteSelectedCommand { get; }
    public RelayCommand CleanupOrphansCommand { get; }
    public RelayCommand AutoFixNamingCommand { get; }
    public RelayCommand AutoLayoutCommand { get; }
    public RelayCommand BringSelectionToFrontCommand { get; }
    public RelayCommand SendSelectionToBackCommand { get; }
    public RelayCommand BringSelectionForwardCommand { get; }
    public RelayCommand SendSelectionBackwardCommand { get; }
    public RelayCommand NormalizeLayersCommand { get; }
    public RelayCommand EnterCteEditorCommand { get; }
    public RelayCommand ExitCteEditorCommand { get; }
    public RelayCommand DiscardAndExitSubEditorCommand { get; }
    public RelayCommand RunSelectedAutoJoinCommand { get; }


    public RelayCommand SelectAllCommand => _selectionManager.SelectAllCommand;
    public RelayCommand DeselectAllCommand => _selectionManager.DeselectAllCommand;
    public RelayCommand AlignLeftCommand => _selectionManager.AlignLeftCommand;
    public RelayCommand AlignRightCommand => _selectionManager.AlignRightCommand;
    public RelayCommand AlignTopCommand => _selectionManager.AlignTopCommand;
    public RelayCommand AlignBottomCommand => _selectionManager.AlignBottomCommand;
    public RelayCommand AlignCenterHCommand => _selectionManager.AlignCenterHCommand;
    public RelayCommand AlignCenterVCommand => _selectionManager.AlignCenterVCommand;
    public RelayCommand DistributeHCommand => _selectionManager.DistributeHCommand;
    public RelayCommand DistributeVCommand => _selectionManager.DistributeVCommand;

    public RelayCommand ZoomInCommand => _layoutManager.ZoomInCommand;
    public RelayCommand ZoomOutCommand => _layoutManager.ZoomOutCommand;
    public RelayCommand ResetZoomCommand => _layoutManager.ResetZoomCommand;
    public RelayCommand FitToScreenCommand => _layoutManager.FitToScreenCommand;
    public RelayCommand ToggleSnapCommand => _layoutManager.ToggleSnapCommand;

    public bool HasTwoSelectedTableSources => _autoJoinController.HasTwoSelectedTableSources;


    public CanvasViewModel()
        : this(null, null, null, null, null)
    {
    }

    public CanvasViewModel(
        INodeManager? nodeManager,
        IPinManager? pinManager,
        ISelectionManager? selectionManager,
        ILocalizationService? localizationService,
        ICanvasDomainStrategy? domainStrategy = null,
        ToastCenterViewModel? toastCenter = null,
        IAliasConventionRegistry? aliasConventionRegistry = null,
        IConnectionManagerViewModelFactory? connectionManagerFactory = null)
    {
        Toasts = toastCenter ?? new ToastCenterViewModel();
        ConnectionManager = connectionManagerFactory?.Create() ?? new ConnectionManagerViewModel();
        UndoRedo = new UndoRedoStack(this);
        SearchMenu = new SearchMenuViewModel();
        PropertyPanel = new PropertyPanelViewModel(
            UndoRedo,
            () => Connections,
            () => DatabaseMetadata,
            OnPropertyPanelParametersCommitted,
            TrySetPrimaryFromSourceNode);
        ApplyProjectConventionSettings(_projectConventionSettings);
        PropertyPanel.SelectedWireCurveMode = WireCurveMode;
        _localizationService = localizationService ?? LocalizationService.Instance;
        _domainStrategy = domainStrategy ?? new QueryDomainStrategy();
        _aliasConventionRegistry = aliasConventionRegistry
            ?? Validation.Conventions.AliasConventionRegistry.CreateDefault();

        // Initialise managers
        _nodeManager = nodeManager ?? new NodeManager(Nodes, Connections, UndoRedo, PropertyPanel, SearchMenu);
        _selectionManager = selectionManager ?? new SelectionManager(Nodes, PropertyPanel, UndoRedo);
        _layoutManager = new NodeLayoutManager(this, UndoRedo);
        _pinManager = pinManager ?? new PinManager(
            Nodes,
            Connections,
            UndoRedo,
            () => WireCurveMode.ToRoutingMode());
        _validationManager = new ValidationManager(this);

        // Forward layout manager changes through this ViewModel so bindings observing
        // CanvasViewModel receive updates for delegated properties (Zoom/Pan/Snap labels).
        _layoutManagerPropertyChangedHandler = (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(NodeLayoutManager.Zoom):
                    RaisePropertyChanged(nameof(Zoom));
                    RaisePropertyChanged(nameof(ZoomPercent));
                    break;

                case nameof(NodeLayoutManager.PanOffset):
                    RaisePropertyChanged(nameof(PanOffset));
                    break;

                case nameof(NodeLayoutManager.SnapToGrid):
                    RaisePropertyChanged(nameof(SnapToGrid));
                    RaisePropertyChanged(nameof(SnapToGridLabel));
                    break;

                case nameof(NodeLayoutManager.ZoomPercent):
                    RaisePropertyChanged(nameof(ZoomPercent));
                    break;

                case nameof(NodeLayoutManager.SnapToGridLabel):
                    RaisePropertyChanged(nameof(SnapToGridLabel));
                    break;
            }
        };
        _layoutManager.PropertyChanged += _layoutManagerPropertyChangedHandler;

        _localizationPropertyChangedHandler = (_, e) =>
        {
            if (e.PropertyName is "" or "Item[]" or nameof(ILocalizationService.CurrentCulture))
            {
                RaisePropertyChanged(nameof(CteEditorBreadcrumb));
                RaisePropertyChanged(nameof(EditorExitLabel));
                RaisePropertyChanged(nameof(EditorExitA11y));
            }
        };
        _localizationService.PropertyChanged += _localizationPropertyChangedHandler;

        LiveSql = new LiveSqlBarViewModel(this);

        // Create child VMs required by the controllers before building commands
        Diagnostics = new AppDiagnosticsViewModel(this);
        AutoJoin = new AutoJoinOverlayViewModel();
        ManualJoinDialog = new ManualJoinDialogViewModel(_localizationService);

        // Initialise focused controllers (SRP extractions)
        _subCanvasController = new SubCanvasEditingController(
            this,
            _domainStrategy,
            _selectionManager,
            Diagnostics,
            _localizationService,
            notifyStateChanged: NotifySubEditorStateChanged,
            getCteSession: () => _cteEditorSession,
            setCteSession: session => _cteEditorSession = session,
            getViewSession: () => _viewEditorSession,
            setViewSession: session => _viewEditorSession = session
        );
        _autoJoinController = new CanvasAutoJoinController(
            Nodes,
            Connections,
            AutoJoin,
            ManualJoinDialog,
            Toasts,
            _localizationService,
            applicationService: null,
            suggestionService: null,
            notifier: null,
            spawnNode: (def, pos) => _nodeManager.SpawnNode(def, pos),
            connectPins: (from, to) =>
            {
                _pinManager.ConnectPins(from, to);
                IsDirty = true;
            },
            notifyRunSelectedAutoJoinCanExecute: () => RunSelectedAutoJoinCommand?.NotifyCanExecuteChanged()
        );

        // Build commands
        UndoCommand = new RelayCommand(UndoRedo.Undo, () => UndoRedo.CanUndo);
        RedoCommand = new RelayCommand(UndoRedo.Redo, () => UndoRedo.CanRedo);
        DeleteSelectedCommand = new RelayCommand(DeleteSelected);
        CleanupOrphansCommand = new RelayCommand(CleanupOrphans, () => HasOrphanNodes);
        AutoFixNamingCommand = new RelayCommand(AutoFixNaming, () => HasNamingViolations);
        AutoLayoutCommand = new RelayCommand(
            () => _layoutManager.RunAutoLayout(),
            () => Nodes.Count > 0
        );
        BringSelectionToFrontCommand = new RelayCommand(
            () => BringSelectionToFront(),
            () => Nodes.Any(n => n.IsSelected)
        );
        SendSelectionToBackCommand = new RelayCommand(
            () => SendSelectionToBack(),
            () => Nodes.Any(n => n.IsSelected)
        );
        BringSelectionForwardCommand = new RelayCommand(
            () => BringSelectionForward(),
            () => Nodes.Any(n => n.IsSelected)
        );
        SendSelectionBackwardCommand = new RelayCommand(
            () => SendSelectionBackward(),
            () => Nodes.Any(n => n.IsSelected)
        );
        NormalizeLayersCommand = new RelayCommand(
            () => NormalizeLayers(),
            () => Nodes.Count > 0
        );
        EnterCteEditorCommand = new RelayCommand(
            () =>
                _ = _subCanvasController.RunSubEditorActionSafeAsync(
                    _subCanvasController.EnterSelectedCteEditorAsync
                ),
            _subCanvasController.CanEnterSelectedCteEditor
        );
        ExitCteEditorCommand = new RelayCommand(
            () =>
                _ = _subCanvasController.RunSubEditorActionSafeAsync(
                    () => _subCanvasController.ExitCteEditorAsync()
                ),
            () => IsInCteEditor
        );
        DiscardAndExitSubEditorCommand = new RelayCommand(
            () =>
                _ = _subCanvasController.RunSubEditorActionSafeAsync(
                    () => _subCanvasController.ExitCteEditorAsync(forceDiscard: true)
                ),
            () => IsInCteEditor
        );
        RunSelectedAutoJoinCommand = new RelayCommand(
            _autoJoinController.RunSelectedAutoJoin,
            () => HasTwoSelectedTableSources
        );

        // Link commands into validation manager for CanExecute refresh
        _validationManager.CleanupOrphansCommand = CleanupOrphansCommand;
        _validationManager.AutoFixNamingCommand = AutoFixNamingCommand;

        Benchmark = new BenchmarkViewModel(this);
        ExplainPlan = new ExplainPlanViewModel(this);
        SqlImporter = new SqlImporterViewModel(this);
        FlowVersions = new FlowVersionOverlayViewModel(this);
        FileHistory = new FileVersionHistoryViewModel(this);

        _explainPlanPropertyChangedHandler = (_, e) =>
        {
            if (e.PropertyName == nameof(ExplainPlanViewModel.HighlightedTableName))
                ApplyExplainNodeHighlight(ExplainPlan.HighlightedTableName);
        };
        ExplainPlan.PropertyChanged += _explainPlanPropertyChangedHandler;

        // Initialize Sidebar with its three tabs
        var nodesList = new NodesListViewModel(
            spawnNode: (definition, position) =>
            {
                Point resolvedPosition = double.IsNaN(position.X) || double.IsNaN(position.Y)
                    ? _layoutManager.ViewportCenterCanvas()
                    : position;
                _nodeManager.SpawnNode(definition, resolvedPosition);
            }
        );

        var schemaVM = new SchemaViewModel(
            onAddTableNode: (tableName, columns, table, position) =>
            {
                _ = _domainStrategy.TryHandleSchemaTableInsert(
                    table,
                    position,
                    isDdlModeActiveResolver: () => _domainStrategy is DdlDomainStrategy,
                    importDdlTableAction: TryImportSchemaTableToDdlCanvas,
                    () => _nodeManager.SpawnTableNode(tableName, columns, position)
                );
            }
        );
        Schema = schemaVM;

        Sidebar = new SidebarViewModel(nodesList, ConnectionManager, Diagnostics);

        // Subscribe to LiveSql property changes with stored handler
        _liveSqlPropertyChangedHandler = (_, e) =>
        {
            if (e.PropertyName == nameof(LiveSqlBarViewModel.RawSql))
                PropertyPanel.UpdateSqlTrace(LiveSql.RawSql);
        };
        LiveSql.PropertyChanged += _liveSqlPropertyChangedHandler;
        DataPreview.ErrorNotified += OnDataPreviewErrorNotified;
        _propertyPanelWireStyleChangedHandler = () =>
        {
            if (SelectedConnection is not null)
            {
                _ = SetSelectedConnectionRoutingMode(PropertyPanel.SelectedWireCurveMode.ToRoutingMode());
                return;
            }

            WireCurveMode = PropertyPanel.SelectedWireCurveMode;
        };
        PropertyPanel.WireStyleChanged += _propertyPanelWireStyleChangedHandler;

        SearchMenu.LoadTables([]);

        // Enable automatic table loading when database is connected
        ConnectionManager.SearchMenu = SearchMenu;
        ConnectionManager.Canvas = this;

        // Update schema tab when database metadata changes
        // Store handler for proper unsubscribe in Dispose()
        _selfPropertyChangedHandler = (_, e) =>
        {
            if (e.PropertyName == nameof(DatabaseMetadata))
                schemaVM.Metadata = DatabaseMetadata;
        };
        this.PropertyChanged += _selfPropertyChangedHandler;

        // Store collection changed handlers for proper unsubscribe
        _nodesCollectionChangedHandler = (_, e) => HandleNodesCollectionChanged(e);
        Nodes.CollectionChanged += _nodesCollectionChangedHandler;

        _connectionsCollectionChangedHandler = (_, e) => HandleConnectionsCollectionChanged(e);
        Connections.CollectionChanged += _connectionsCollectionChangedHandler;

        NotifyLayerCommandsCanExecuteChanged();
        IsDirty = false;
    }

    public SchemaViewModel Schema { get; }

    private void HandleNodesCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        IsDirty = true;
        RaisePropertyChanged(nameof(IsCanvasEmpty));

        if (e.NewItems is not null)
        {
            foreach (NodeViewModel node in e.NewItems)
            {
                AttachNodeTracking(node);
                node.SyncComparisonInlineSummary(Connections);
            }
        }

        if (e.OldItems is not null)
        {
            foreach (NodeViewModel node in e.OldItems)
                DetachNodeTracking(node);
        }

        _validationManager?.ScheduleValidation();
        RaisePropertyChanged(nameof(HasTwoSelectedTableSources));
        RunSelectedAutoJoinCommand.NotifyCanExecuteChanged();
        ApplyExplainNodeHighlight(ExplainPlan.HighlightedTableName);
        RefreshPrimaryFromSourceMarkers();
    }

    private void AttachNodeTracking(NodeViewModel node)
    {
        _domainStrategy.OnNodeAdded(node, Connections);

        PropertyChangedEventHandler handler = OnTrackedNodePropertyChanged;
        node.PropertyChanged += handler;
        _nodeValidationHandlers[node] = handler;
    }

    private void DetachNodeTracking(NodeViewModel node)
    {
        if (!_nodeValidationHandlers.TryGetValue(node, out PropertyChangedEventHandler? handler))
            return;

        node.PropertyChanged -= handler;
        _nodeValidationHandlers.Remove(node);
    }

    private void OnTrackedNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _validationManager?.ScheduleValidation();

        if (sender is NodeViewModel changedNode
            && !string.IsNullOrWhiteSpace(e.PropertyName)
            && e.PropertyName.StartsWith("Param_", StringComparison.Ordinal))
        {
            string changedParameter = e.PropertyName["Param_".Length..];
            PropertyPanel.SynchronizeSelectedNodeParameter(changedNode, changedParameter);
        }

        if (e.PropertyName != nameof(NodeViewModel.IsSelected))
            return;

        NotifyLayerCommandsCanExecuteChanged();
        NotifyCteEditorCommandsCanExecuteChanged();
        RaisePropertyChanged(nameof(HasTwoSelectedTableSources));
        RunSelectedAutoJoinCommand.NotifyCanExecuteChanged();
    }

    private void HandleConnectionsCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (IsTransientConnectionDragChange(e))
            return;

        if (SelectedConnection is not null && !Connections.Contains(SelectedConnection))
            ClearConnectionSelection();

        IsDirty = true;
        _validationManager?.ScheduleValidation();
        NotifyLayerCommandsCanExecuteChanged();
        NotifyCteEditorCommandsCanExecuteChanged();

        // Single pass â€” check each node type once instead of three Where iterations.
        foreach (NodeViewModel node in Nodes)
        {
            if (node.IsResultOutput)
                node.SyncOutputColumns(Connections);
            else if (node.Type == NodeType.CteSource)
                node.SyncCteSourceColumns(Connections);
            else if (node.IsColumnList)
                node.SyncColumnListPins(Connections);
            else if (node.IsLogicGate)
                node.SyncLogicGatePins(Connections);
            else if (node.IsWindowFunction)
                node.SyncWindowFunctionPins(Connections);
            else if (node.Type is NodeType.Subquery or NodeType.SubqueryReference or NodeType.SubqueryDefinition)
                node.SyncSubqueryInputPins(Connections);

            node.SyncComparisonInlineSummary(Connections);
        }

        if (e.NewItems is not null)
        {
            foreach (ConnectionViewModel connection in e.NewItems.Cast<object>().OfType<ConnectionViewModel>())
                _domainStrategy.OnConnectionEstablished(connection, Connections, Nodes);
        }

        if (e.OldItems is not null)
        {
            foreach (ConnectionViewModel connection in e.OldItems.Cast<object>().OfType<ConnectionViewModel>())
                _domainStrategy.OnConnectionRemoved(connection, Connections, Nodes);

            HandleSubqueryInputDisconnectWarnings(e.OldItems.Cast<object>().OfType<ConnectionViewModel>());
        }

        PropertyPanel.RefreshConnectionOverrides();
        RefreshPrimaryFromSourceMarkers();
    }

    private void RefreshPrimaryFromSourceMarkers()
    {
        NodeViewModel? primarySourceNode = ResolvePrimaryFromSourceNode();

        foreach (NodeViewModel node in Nodes)
            node.IsPrimaryFromSource = ReferenceEquals(node, primarySourceNode);

        PropertyPanel.NotifySelectedNodePrimaryFromSourceChanged();
    }

    private NodeViewModel? ResolvePrimaryFromSourceNode()
    {
        List<NodeViewModel> tableNodes = Nodes.Where(n => n.Type == NodeType.TableSource).ToList();
        List<NodeViewModel> cteSourceNodes = Nodes.Where(n => n.Type == NodeType.CteSource).ToList();
        List<NodeViewModel> subqueryNodes = Nodes
            .Where(n => n.Type is NodeType.Subquery or NodeType.SubqueryReference)
            .ToList();

        List<NodeViewModel> sourceCandidates =
        [
            .. tableNodes,
            .. cteSourceNodes,
            .. subqueryNodes,
        ];

        if (!string.IsNullOrWhiteSpace(_primaryFromSourceOverrideNodeId))
        {
            NodeViewModel? overridden = sourceCandidates.FirstOrDefault(node =>
                string.Equals(node.Id, _primaryFromSourceOverrideNodeId, StringComparison.OrdinalIgnoreCase));
            if (overridden is not null)
                return overridden;

            _primaryFromSourceOverrideNodeId = null;
        }

        NodeViewModel? resultOutputNode = ResolvePrimaryResultOutputNode();
        if (resultOutputNode is not null)
        {
            HashSet<string> upstreamNodeIds = CollectUpstreamNodeIds(resultOutputNode);
            NodeViewModel? upstreamSource = tableNodes.FirstOrDefault(n => upstreamNodeIds.Contains(n.Id))
                ?? cteSourceNodes.FirstOrDefault(n => upstreamNodeIds.Contains(n.Id))
                ?? subqueryNodes.FirstOrDefault(n => upstreamNodeIds.Contains(n.Id));

            if (upstreamSource is not null)
                return upstreamSource;
        }

        return tableNodes.FirstOrDefault()
            ?? cteSourceNodes.FirstOrDefault()
            ?? subqueryNodes.FirstOrDefault();
    }

    public bool CanSetPrimaryFromSource(NodeViewModel? node)
    {
        return node is not null
            && node.Type is NodeType.TableSource or NodeType.CteSource or NodeType.Subquery or NodeType.SubqueryReference;
    }

    public bool TrySetPrimaryFromSourceNode(NodeViewModel? node)
    {
        if (!CanSetPrimaryFromSource(node))
            return false;

        _primaryFromSourceOverrideNodeId = node!.Id;
        RefreshPrimaryFromSourceMarkers();
        _validationManager?.ScheduleValidation();
        return true;
    }

    private HashSet<string> CollectUpstreamNodeIds(NodeViewModel sinkNode)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { sinkNode.Id };
        var queue = new Queue<string>();
        queue.Enqueue(sinkNode.Id);

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            foreach (ConnectionViewModel conn in Connections.Where(c => c.ToPin?.Owner.Id == current))
            {
                string fromOwnerId = conn.FromPin.Owner.Id;
                if (visited.Add(fromOwnerId))
                    queue.Enqueue(fromOwnerId);
            }
        }

        return visited;
    }

    private NodeViewModel? ResolvePrimaryResultOutputNode()
    {
        List<NodeViewModel> outputs = Nodes
            .Where(n => n.Type == NodeType.ResultOutput)
            .ToList();
        if (outputs.Count == 0)
            return null;
        if (outputs.Count == 1)
            return outputs[0];

        NodeViewModel? externalSink = outputs.FirstOrDefault(output =>
            !Connections.Any(c =>
                c.FromPin.Owner == output
                && c.ToPin is not null
                && c.ToPin.Owner.Type == NodeType.CteDefinition
                && c.ToPin.Name.Equals("query", StringComparison.OrdinalIgnoreCase)));

        return externalSink ?? outputs[0];
    }

    private static bool IsTransientConnectionDragChange(NotifyCollectionChangedEventArgs e)
    {
        static bool HasStructuralEndpoint(NotifyCollectionChangedEventArgs args)
        {
            bool NewHasStructured = args.NewItems is not null
                && args.NewItems.Cast<object>().OfType<ConnectionViewModel>().Any(c => c.ToPin is not null);
            bool OldHasStructured = args.OldItems is not null
                && args.OldItems.Cast<object>().OfType<ConnectionViewModel>().Any(c => c.ToPin is not null);
            return NewHasStructured || OldHasStructured;
        }

        return !HasStructuralEndpoint(e);
    }

    private void NotifyLayerCommandsCanExecuteChanged()
    {
        BringSelectionToFrontCommand.NotifyCanExecuteChanged();
        SendSelectionToBackCommand.NotifyCanExecuteChanged();
        BringSelectionForwardCommand.NotifyCanExecuteChanged();
        SendSelectionBackwardCommand.NotifyCanExecuteChanged();
        NormalizeLayersCommand.NotifyCanExecuteChanged();
    }

    private void NotifyCteEditorCommandsCanExecuteChanged()
    {
        EnterCteEditorCommand.NotifyCanExecuteChanged();
        ExitCteEditorCommand.NotifyCanExecuteChanged();
        DiscardAndExitSubEditorCommand.NotifyCanExecuteChanged();
    }

    // â”€â”€ Sub-editor operations (delegated to SubCanvasEditingController) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public Task<bool> EnterSelectedCteEditorAsync() =>
        _subCanvasController.EnterSelectedCteEditorAsync();

    public Task<bool> EnterCteEditorAsync(NodeViewModel cteNode) =>
        _subCanvasController.EnterCteEditorAsync(cteNode);

    public Task<bool> EnterSubqueryEditorAsync(NodeViewModel subqueryNode) =>
        _subCanvasController.EnterSubqueryEditorAsync(subqueryNode);

    public Task<bool> ExitCteEditorAsync(bool forceDiscard = false) =>
        _subCanvasController.ExitCteEditorAsync(forceDiscard);

    public Task<bool> EnterViewEditorAsync(NodeViewModel viewNode) =>
        _subCanvasController.EnterViewEditorAsync(viewNode);

    public string SerializeForPersistence() =>
        _subCanvasController.SerializeForPersistence();

    private void NotifySubEditorStateChanged()
    {
        RaisePropertyChanged(nameof(IsInCteEditor));
        RaisePropertyChanged(nameof(IsInViewEditor));
        RaisePropertyChanged(nameof(IsCanvasDimmedBySubcanvas));
        RaisePropertyChanged(nameof(CteEditorBreadcrumb));
        RaisePropertyChanged(nameof(EditorExitLabel));
        RaisePropertyChanged(nameof(EditorExitA11y));
        NotifyCteEditorCommandsCanExecuteChanged();
    }

    private void HandleSubqueryInputDisconnectWarnings(IEnumerable<ConnectionViewModel> removedConnections)
    {
        foreach (ConnectionViewModel removed in removedConnections)
        {
            PinViewModel? toPin = removed.ToPin;
            if (toPin is null)
                continue;

            NodeViewModel owner = toPin.Owner;
            if (owner.Type is not (NodeType.Subquery or NodeType.SubqueryReference or NodeType.SubqueryDefinition))
                continue;
            if (!toPin.Name.StartsWith("input_", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!owner.Parameters.TryGetValue(CanvasSerializer.SubquerySubgraphParameterKey, out string? payload)
                || string.IsNullOrWhiteSpace(payload))
            {
                continue;
            }

            SavedSubquerySubgraph? subgraph;
            try
            {
                subgraph = JsonSerializer.Deserialize<SavedSubquerySubgraph>(payload);
            }
            catch
            {
                continue;
            }

            if (subgraph?.InputBindings is null || string.IsNullOrWhiteSpace(subgraph.BridgeNodeId))
                continue;

            SavedSubqueryInputBinding? binding = subgraph.InputBindings.FirstOrDefault(x =>
                string.Equals(x.InputPinName, toPin.Name, StringComparison.OrdinalIgnoreCase));
            if (binding is null)
                continue;

            bool usedInsideSubquery = subgraph.Connections.Any(connection =>
                string.Equals(connection.FromNodeId, subgraph.BridgeNodeId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(connection.FromPinName, binding.BridgePinName, StringComparison.OrdinalIgnoreCase));

            if (!usedInsideSubquery)
                continue;

            Toasts.ShowWarning(
                L("toast.subqueryInputDisconnected", "Um input usado pela subconsulta foi desconectado."),
                string.Format(
                    L(
                        "toast.subqueryInputDisconnectedDetails",
                        "Node: {0} • Input: {1}. Abra a subconsulta e reconecte a origem."),
                    owner.Title,
                    binding.SourceLabel ?? binding.BridgePinName));
        }
    }

    // â”€â”€ Node operations (delegated to NodeManager) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public NodeViewModel SpawnNode(NodeDefinition def, Point pos) =>
        _nodeManager.SpawnNode(def, pos);

    public NodeViewModel SpawnTableNode(
        string table,
        IEnumerable<(string n, PinDataType t)> cols,
        Point pos
    ) => _nodeManager.SpawnTableNode(table, cols, pos);

    public bool TryInsertSchemaTableNode(string tableFullName, Point position)
    {
        if (!TryResolveTableColumns(tableFullName, out TableMetadata? table, out List<(string Name, PinDataType Type)> columns))
        {
            Toasts.ShowWarning(
                L("toast.schemaTableDragUnavailable", "Não foi possível localizar a tabela no metadata atual."),
                tableFullName);
            return false;
        }

        bool handled = _domainStrategy.TryHandleSchemaTableInsert(
            table!,
            position,
            null,
            null,
            () => _nodeManager.SpawnTableNode(tableFullName, columns.Select(c => (c.Name, c.Type)), position));

        if (!handled)
            return false;

        IsDirty = true;
        return true;
    }

    public void DeleteSelected() => _nodeManager.DeleteSelected();

    public void CleanupOrphans() => _nodeManager.CleanupOrphans();

    /// <summary>
    /// Initializes canvas with demo nodes. Used for unit tests and initial UI setup.
    /// </summary>
    public void InitializeDemoNodes() => _nodeManager.SpawnDemoNodes(UndoRedo);

    // â”€â”€ Snippets â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Saves the currently selected nodes (and their internal connections) as a
    /// named snippet in the persistent snippet store.
    /// </summary>
    public void SaveSelectionAsSnippet(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;
        List<NodeViewModel> selected = _selectionManager.SelectedNodes();
        if (selected.Count == 0)
            return;

        (List<SavedNode> nodes, List<SavedConnection> conns) =
            CanvasSerializer.SerialiseSubgraph(selected, Connections);

        var snippet = new SavedSnippet(
            Id: Guid.NewGuid().ToString(),
            Name: name.Trim(),
            Tags: null,
            Description: null,
            CreatedAt: DateTime.UtcNow.ToString("o"),
            Nodes: nodes,
            Connections: conns
        );

        SnippetStore.Add(snippet);
        SearchMenu.LoadSnippets();
    }

    /// <summary>
    /// Inserts a saved snippet into the canvas, centered at <paramref name="canvasPos"/>,
    /// with fresh node IDs to avoid conflicts with the existing graph.
    /// </summary>
    public void InsertSnippet(SavedSnippet snippet, Point canvasPos) =>
        CanvasSerializer.InsertSubgraph(snippet.Nodes, snippet.Connections, this, canvasPos);

    /// <summary>
    /// Loads a query template by clearing the canvas and invoking the template's Build action.
    /// Resets undo history and dirty flag so the user starts with a clean slate.
    /// </summary>
    public void LoadTemplate(QueryTemplate template)
    {
        var stateBeforeTemplate = new RestoreCanvasStateCommand(this, "Load Template");
        _subCanvasController.AbortActiveEditorSession();

        Connections.Clear();
        Nodes.Clear();
        CurrentFilePath = null;
        QueryText = "";
        Zoom = 1.0;
        PanOffset = new Point(0, 0);
        template.Build(this);
        IsDirty = false;

        stateBeforeTemplate.CaptureAfterState(this);
        UndoRedo.Execute(stateBeforeTemplate);
    }

    /// <summary>
    /// Resets the canvas to a clean state and sets the database metadata.
    /// Called when a user connects to a new database.
    /// </summary>
    public void SetDatabaseAndResetCanvas(DbMetadata? metadata)
    {
        SetDatabaseAndResetCanvas(metadata, null);
    }

    /// <summary>
    /// Updates the database metadata and connection configuration without clearing the canvas.
    /// </summary>
    public void SetDatabaseContext(DbMetadata? metadata, ConnectionConfig? config)
    {
        DatabaseMetadata = metadata;
        ActiveConnectionConfig = config;
    }

    /// <summary>
    /// Resets the canvas to a clean state and sets the database metadata and connection config.
    /// Called when a user connects to a new database.
    /// </summary>
    public void SetDatabaseAndResetCanvas(DbMetadata? metadata, ConnectionConfig? config)
    {
        // Clear the current canvas
        Connections.Clear();
        Nodes.Clear();
        CurrentFilePath = null;
        QueryText = "";
        Zoom = 1.0;
        PanOffset = new Point(0, 0);
        IsDirty = false;
        UndoRedo.Clear();

        // Set the new database metadata and connection config
        DatabaseMetadata = metadata;
        ActiveConnectionConfig = config;

        // Keep the canvas empty when there is no metadata (first-run/disconnected state).
    }

    // â”€â”€ Naming & layout â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Converts all alias violations to snake_case (undoable).</summary>
    public void AutoFixNaming()
    {
        NamingConventionPolicy policy = BuildNamingConventionPolicy();
        var cmd = new AutoFixNamingCommand(Nodes, policy, _aliasConventionRegistry);
        if (!cmd.HasChanges)
            return;
        UndoRedo.Execute(cmd);
    }

    public void ApplyProjectConventionSettings(ProjectConventionSettings settings)
    {
        settings ??= new ProjectConventionSettings();
        _projectConventionSettings = new ProjectConventionSettings
        {
            NamingConvention = settings.NamingConvention,
            EnforceAliasNaming = settings.EnforceAliasNaming,
            WarnOnReservedKeywords = settings.WarnOnReservedKeywords,
            MaxAliasLength = Math.Max(0, settings.MaxAliasLength),
            DefaultWireCurveMode = settings.DefaultWireCurveMode,
        };

        // Keep the property panel in sync with project-level settings so
        // validation and auto-fix entry points read the same convention state.
        PropertyPanel.SelectedNamingConvention = _projectConventionSettings.NamingConvention;
        PropertyPanel.EnforceAliasNaming = _projectConventionSettings.EnforceAliasNaming;
        PropertyPanel.WarnOnReservedKeywords = _projectConventionSettings.WarnOnReservedKeywords;
        PropertyPanel.MaxAliasLength = _projectConventionSettings.MaxAliasLength.ToString();

        if (Enum.TryParse(_projectConventionSettings.DefaultWireCurveMode, ignoreCase: true, out CanvasWireCurveMode defaultWireCurveMode))
        {
            WireCurveMode = defaultWireCurveMode;
            if (SelectedConnection is null)
                PropertyPanel.SelectedWireCurveMode = defaultWireCurveMode;
        }

        _validationManager?.ScheduleValidation();
    }

    public NamingConventionPolicy BuildNamingConventionPolicy()
    {
        string conventionName = _projectConventionSettings.NamingConvention switch
        {
            "snake_case" => "snake_case",
            "camelCase" => "camelCase",
            "PascalCase" => "PascalCase",
            "SCREAMING_SNAKE_CASE" => "SCREAMING_SNAKE_CASE",
            _ => "snake_case",
        };

        return new NamingConventionPolicy
        {
            EnforceSnakeCase = _projectConventionSettings.EnforceAliasNaming
                && string.Equals(conventionName, "snake_case", StringComparison.OrdinalIgnoreCase),
            MaxLength = Math.Max(0, _projectConventionSettings.MaxAliasLength),
            NoLeadingDigit = true,
            NoSpaces = true,
            ConventionName = _projectConventionSettings.EnforceAliasNaming ? conventionName : null,
        };
    }

    public IAliasConventionRegistry AliasConventions => _aliasConventionRegistry;

    /// <summary>Arranges nodes into logical columns (undoable). Pass a scope to layout only selected nodes.</summary>
    public void RunAutoLayout(IReadOnlyList<NodeViewModel>? scope = null)
    {
        if (Nodes.Count == 0)
            return;
        UndoRedo.Execute(new AutoLayoutCommand(this, scope));
    }

    // â”€â”€ Pin & connection operations (delegated to PinManager) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public void ConnectPins(PinViewModel from, PinViewModel to)
    {
        _pinManager.ConnectPins(from, to);
        IsDirty = true;
    }

    public void DeleteConnection(ConnectionViewModel conn)
    {
        if (ReferenceEquals(SelectedBreakpointConnection, conn))
            ClearSelectedWireBreakpoint();

        if (ReferenceEquals(SelectedConnection, conn))
            ClearConnectionSelection();

        _pinManager.DeleteConnection(conn);
        IsDirty = true;
    }

    public bool InsertWireBreakpoint(ConnectionViewModel wire, int insertIndex, Point position)
    {
        if (wire.RoutingMode != CanvasWireRoutingMode.Orthogonal)
            return false;

        List<WireBreakpoint> before = [.. wire.Breakpoints];
        int index = Math.Clamp(insertIndex, 0, before.Count);
        var after = new List<WireBreakpoint>(before.Count + 1);
        after.AddRange(before.Take(index));
        after.Add(new WireBreakpoint(position));
        after.AddRange(before.Skip(index));

        UndoRedo.Execute(new SetWireBreakpointsCommand(
            wire,
            before,
            after,
            "Insert wire breakpoint"));

        if (ReferenceEquals(SelectedBreakpointConnection, wire) && SelectedBreakpointIndex >= index)
        {
            SelectedBreakpointIndex++;
            RaisePropertyChanged(nameof(HasSelectedBreakpoint));
        }

        return true;
    }

    public bool UpdateWireBreakpoint(ConnectionViewModel wire, int index, Point position)
    {
        List<WireBreakpoint> before = [.. wire.Breakpoints];
        if (index < 0 || index >= before.Count)
            return false;

        var after = new List<WireBreakpoint>(before);
        after[index] = new WireBreakpoint(position);
        UndoRedo.Execute(new SetWireBreakpointsCommand(
            wire,
            before,
            after,
            "Move wire breakpoint"));
        return true;
    }

    public bool CommitWireBreakpointDrag(
        ConnectionViewModel wire,
        int index,
        Point initialPosition,
        Point finalPosition)
    {
        List<WireBreakpoint> after = [.. wire.Breakpoints];
        if (index < 0 || index >= after.Count)
            return false;

        var before = new List<WireBreakpoint>(after);
        before[index] = new WireBreakpoint(initialPosition);
        after[index] = new WireBreakpoint(finalPosition);

        UndoRedo.Execute(new SetWireBreakpointsCommand(
            wire,
            before,
            after,
            "Move wire breakpoint"));
        return true;
    }

    public bool RemoveWireBreakpoint(ConnectionViewModel wire, int index)
    {
        List<WireBreakpoint> before = [.. wire.Breakpoints];
        if (index < 0 || index >= before.Count)
            return false;

        var after = new List<WireBreakpoint>(before);
        after.RemoveAt(index);
        UndoRedo.Execute(new SetWireBreakpointsCommand(
            wire,
            before,
            after,
            "Remove wire breakpoint"));

        if (ReferenceEquals(SelectedBreakpointConnection, wire))
        {
            if (SelectedBreakpointIndex == index)
            {
                ClearSelectedWireBreakpoint();
            }
            else if (SelectedBreakpointIndex > index)
            {
                SelectedBreakpointIndex--;
                RaisePropertyChanged(nameof(HasSelectedBreakpoint));
            }
        }

        return true;
    }

    /// <summary>
    /// Propagates a parameter change to the active domain strategy so that
    /// dependent nodes (e.g., table previews) can re-sync when a connected
    /// type-defining node's parameter changes.
    /// </summary>
    internal void NotifyNodeParameterChanged(NodeViewModel node, string paramName)
    {
        _domainStrategy.OnParameterChanged(node, paramName, Connections, Nodes);
        RefreshComparisonInlineSummaries();
    }

    private void RefreshComparisonInlineSummaries()
    {
        foreach (NodeViewModel node in Nodes)
            node.SyncComparisonInlineSummary(Connections);
    }

    // â”€â”€ Selection (delegated to SelectionManager) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public void SelectAll() => _selectionManager.SelectAll();

    public void DeselectAll()
    {
        _selectionManager.DeselectAll();
        ClearConnectionSelection();
    }

    public void ClearSelection() => DeselectAll();

    public bool TrySelectInRegion(Rect region)
    {
        ClearConnectionSelection();
        bool anySelected = CanvasSelectionViewportMath.TrySelectNodesInRegion(
            Nodes,
            region,
            SelectionFrameDefaultNodeHeight);

        List<NodeViewModel> selected = [.. Nodes.Where(node => node.IsSelected)];
        if (selected.Count == 1)
            PropertyPanel.ShowNode(selected[0]);
        else if (selected.Count > 1)
            PropertyPanel.ShowMultiSelection(selected);
        else
            PropertyPanel.Clear();

        return anySelected;
    }

    public bool TryGetSelectionFrame(double padding, out Rect frame)
    {
        return CanvasSelectionViewportMath.TryGetSelectionFrame(
            Nodes.Where(node => node.IsSelected),
            SelectionFrameDefaultNodeHeight,
            padding,
            out frame);
    }

    public void SelectNode(NodeViewModel node, bool add = false) =>
        SelectNodeCore(node, add);

    public void SelectConnection(ConnectionViewModel connection)
    {
        if (!Connections.Contains(connection))
            return;

        ClearSelectedWireBreakpoint();
        _selectionManager.DeselectAll();

        foreach (ConnectionViewModel candidate in Connections)
            candidate.IsSelected = ReferenceEquals(candidate, connection);

        SelectedConnection = connection;
        PropertyPanel.SelectedWireCurveMode = connection.RoutingMode.ToCurveMode();
        PropertyPanel.ShowWire(connection);
        RaisePropertyChanged(nameof(HasSelectedConnection));
    }

    public void ClearConnectionSelection()
    {
        ClearSelectedWireBreakpoint();

        bool changed = false;
        foreach (ConnectionViewModel connection in Connections)
        {
            if (!connection.IsSelected)
                continue;

            connection.IsSelected = false;
            changed = true;
        }

        if (SelectedConnection is not null)
        {
            SelectedConnection = null;
            changed = true;
        }

        PropertyPanel.ClearSelectedWire();
        PropertyPanel.SelectedWireCurveMode = WireCurveMode;

        if (changed)
            RaisePropertyChanged(nameof(HasSelectedConnection));
    }

    public bool SelectWireBreakpoint(ConnectionViewModel wire, int index)
    {
        if (!Connections.Contains(wire))
            return false;

        if (index < 0 || index >= wire.Breakpoints.Count)
            return false;

        if (!ReferenceEquals(SelectedConnection, wire))
            SelectConnection(wire);

        bool changed = false;
        if (!ReferenceEquals(SelectedBreakpointConnection, wire))
        {
            SelectedBreakpointConnection = wire;
            changed = true;
        }

        if (SelectedBreakpointIndex != index)
        {
            SelectedBreakpointIndex = index;
            changed = true;
        }

        if (changed)
            RaisePropertyChanged(nameof(HasSelectedBreakpoint));

        return changed;
    }

    public void ClearSelectedWireBreakpoint()
    {
        bool changed = false;

        if (SelectedBreakpointConnection is not null)
        {
            SelectedBreakpointConnection = null;
            changed = true;
        }

        if (SelectedBreakpointIndex != -1)
        {
            SelectedBreakpointIndex = -1;
            changed = true;
        }

        if (changed)
            RaisePropertyChanged(nameof(HasSelectedBreakpoint));
    }

    public bool DeleteSelectedConnection()
    {
        if (SelectedConnection is null)
            return false;

        DeleteConnection(SelectedConnection);
        return true;
    }

    public bool DeleteSelectedWireBreakpoint()
    {
        ConnectionViewModel? selectedWire = SelectedBreakpointConnection;
        if (selectedWire is null || SelectedBreakpointIndex < 0)
            return false;

        return RemoveWireBreakpoint(selectedWire, SelectedBreakpointIndex);
    }

    public bool SetSelectedConnectionRoutingMode(CanvasWireRoutingMode mode)
    {
        if (SelectedConnection is null)
            return false;

        return SetConnectionRoutingMode(SelectedConnection, mode);
    }

    public bool SetConnectionRoutingMode(ConnectionViewModel wire, CanvasWireRoutingMode mode)
    {
        if (!Connections.Contains(wire))
            return false;

        CanvasWireRoutingMode before = wire.RoutingMode;
        if (before == mode)
            return false;

        UndoRedo.Execute(new SetWireRoutingModeCommand(
            wire,
            before,
            mode,
            "Change wire routing mode"));

        if (mode != CanvasWireRoutingMode.Orthogonal && ReferenceEquals(SelectedBreakpointConnection, wire))
            ClearSelectedWireBreakpoint();

        if (ReferenceEquals(SelectedConnection, wire))
            PropertyPanel.SelectedWireCurveMode = mode.ToCurveMode();

        return true;
    }

    public bool FocusNodeById(string? nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            return false;

        NodeViewModel? node = Nodes.FirstOrDefault(n =>
            n.Id.Equals(nodeId, StringComparison.Ordinal)
        );
        if (node is null)
            return false;

        SelectNodeCore(node, add: false);
        _layoutManager.FocusOnNode(node);
        return true;
    }

    private void SelectNodeCore(NodeViewModel node, bool add)
    {
        ClearConnectionSelection();

        _selectionManager.SelectNode(node, add);
    }

    public bool BringSelectionToFront() =>
        ApplyLayerOrder(
            L("main.layerOrder.bringToFront", "Bring to front"),
            NodeLayerOrdering.BringToFront,
            requireSelection: true
        );

    public bool SendSelectionToBack() =>
        ApplyLayerOrder(
            L("main.layerOrder.sendToBack", "Send to back"),
            NodeLayerOrdering.SendToBack,
            requireSelection: true
        );

    public bool BringSelectionForward() =>
        ApplyLayerOrder(
            L("main.layerOrder.bringForward", "Bring forward"),
            NodeLayerOrdering.BringForward,
            requireSelection: true
        );

    public bool SendSelectionBackward() =>
        ApplyLayerOrder(
            L("main.layerOrder.sendBackward", "Send backward"),
            NodeLayerOrdering.SendBackward,
            requireSelection: true
        );

    public bool NormalizeLayers() =>
        ApplyLayerOrder(
            L("main.layerOrder.normalizeLayers", "Normalize layers"),
            NodeLayerOrdering.OrderByZ,
            requireSelection: false
        );

    private bool ApplyLayerOrder(
        string action,
        Func<List<NodeViewModel>, List<NodeViewModel>> reorder,
        bool requireSelection
    )
    {
        List<NodeViewModel> all = Nodes.ToList();
        if (all.Count == 0)
            return false;
        if (requireSelection && all.All(n => !n.IsSelected))
            return false;

        Dictionary<NodeViewModel, int> from = all.ToDictionary(n => n, n => n.ZOrder);
        List<NodeViewModel> ordered = reorder(all);
        Dictionary<NodeViewModel, int> to = NodeLayerOrdering.BuildNormalizedMap(ordered);

        if (from.All(kv => to.TryGetValue(kv.Key, out int z) && z == kv.Value))
            return false;

        UndoRedo.Execute(new ReorderNodesCommand(action, from, to));
        return true;
    }

    // â”€â”€ Coordinate transforms â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public void ZoomToward(Point screen, double factor)
    {
        double old = Zoom;
        Zoom = Math.Clamp(old * factor, 0.15, 4.0);
        PanOffset = new Point(
            screen.X - (screen.X - PanOffset.X) * (Zoom / old),
            screen.Y - (screen.Y - PanOffset.Y) * (Zoom / old)
        );
    }

    /// <summary>
    /// Informs the layout manager of the current viewport size so that
    /// <c>FitToScreen</c> can compute an accurate zoom level and pan offset.
    /// Called by <c>InfiniteCanvas.ArrangeOverride</c> after each layout pass.
    /// </summary>
    public void SetViewportSize(double width, double height) =>
        _layoutManager.SetViewportSize(width, height);

    public Point ScreenToCanvas(Point s) =>
        new((s.X - PanOffset.X) / Zoom, (s.Y - PanOffset.Y) / Zoom);

    public Point CanvasToScreen(Point c) =>
        new(c.X * Zoom + PanOffset.X, c.Y * Zoom + PanOffset.Y);

    // â”€â”€ Query & export â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public void UpdateQueryText(string sql)
    {
        QueryText = sql;
        DataPreview.QueryText = sql;
    }

    public void RememberPreviewParameterInputs(IReadOnlyDictionary<string, string> values)
    {
        foreach ((string key, string value) in values)
            _previewParameterInputs[key] = value;
    }

    public void ReplacePreviewParameterInputs(IReadOnlyDictionary<string, string>? values)
    {
        _previewParameterInputs.Clear();
        if (values is null)
            return;

        foreach ((string key, string value) in values)
            _previewParameterInputs[key] = value;
    }

    /// <summary>
    /// Finds the first export node of <paramref name="exportType"/> and triggers export.
    /// Returns the generated file path, or null if none found or export failed.
    /// </summary>
    public async Task<string?> TriggerExportAsync(NodeType exportType, string? overridePath = null)
    {
        NodeViewModel? node = Nodes.FirstOrDefault(n => n.Type == exportType);
        if (node is null)
            return null;
        return await ExportNodeHandler.RunExportAsync(this, node, overridePath);
    }

    public void ScheduleValidation() => _validationManager.ScheduleValidation();

    public static double Snap(double v) => NodeLayoutManager.Snap(v);

    /// <summary>Forwards to <see cref="NodeManager.DemoCatalog"/> â€” kept for backwards compatibility.</summary>
    public static IReadOnlyList<(
        string FullName,
        IReadOnlyList<(string Name, PinDataType Type)> Cols
    )> DemoCatalog => NodeManager.DemoCatalog;

    // â”€â”€ Auto-join (delegated to CanvasAutoJoinController) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public void TriggerAutoJoinAnalysis(string newTableFullName) =>
        _autoJoinController.TriggerAutoJoinAnalysis(newTableFullName);

    public void AnalyzeAllCanvasJoins() =>
        _autoJoinController.AnalyzeAllCanvasJoins();

    public void NotifySuccess(string message, string? details = null) =>
        Toasts.ShowSuccess(message, details);

    public void NotifyWarning(string message, string? details = null) =>
        Toasts.ShowWarning(message, details);

    public void NotifyWarning(string message, string? details, Action onDetails) =>
        Toasts.ShowWarning(message, details, onDetails);

    public void NotifyError(string message, string? details = null) =>
        Toasts.ShowError(message, details);

    private void OnDataPreviewErrorNotified(string message, string? details) =>
        NotifyError(message, details);

    private void TryImportSchemaTableToDdlCanvas(TableMetadata table, Point position)
    {
        if (DatabaseMetadata is null)
            return;

        var importer = new DdlSchemaImporter();
        _ = importer.ImportTable(DatabaseMetadata, table.FullName, this, position);
    }

    private string L(string key, string fallback)
    {
        string value = _localizationService[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    private void ApplyExplainNodeHighlight(string? highlightedTableName) =>
        CanvasTableHighlightEngine.ApplyHighlight(Nodes, highlightedTableName);

    private void OnPropertyPanelParametersCommitted(
        NodeViewModel node,
        IReadOnlyList<(string Name, string? Value)> committedChanges)
    {
        if (_isReplacingTableSourceNode || node.Type != NodeType.TableSource || !IsPlaceholderTableSource(node))
            return;

        if (!committedChanges.Any(change => IsTableSelectionParameter(change.Name)))
            return;

        if (!TryResolveSelectedTableFullName(node, out string? selectedTableFullName) || string.IsNullOrWhiteSpace(selectedTableFullName))
            return;

        TryPromotePlaceholderTableSource(node, selectedTableFullName!);
    }

    private void TryPromotePlaceholderTableSource(NodeViewModel placeholderNode, string selectedTableFullName)
    {
        if (!Nodes.Contains(placeholderNode))
            return;

        if (!TryResolveTableColumns(selectedTableFullName, out _, out List<(string Name, PinDataType Type)> columns))
            return;

        bool hasOutputConnections = Connections.Any(connection => ReferenceEquals(connection.FromPin.Owner, placeholderNode));
        if (!hasOutputConnections)
        {
            ReplacePlaceholderTableSource(placeholderNode, selectedTableFullName, columns);
            return;
        }

        string confirmationKey = $"{placeholderNode.Id}|{selectedTableFullName}";
        if (!_pendingTableSourceReplacementConfirmations.Add(confirmationKey))
            return;

        int disconnectedConnections = Connections.Count(connection => ReferenceEquals(connection.FromPin.Owner, placeholderNode));
        Toasts.ShowWarning(
            L("toast.tableSourceReplaceRequiresConfirmation", "A troca da tabela vai desconectar saídas existentes."),
            string.Format(
                L(
                    "toast.tableSourceReplaceRequiresConfirmationDetails",
                    "Tabela selecionada: {0}. {1} conexão(ões) de saída serão removidas. Abra os detalhes do aviso para confirmar."),
                selectedTableFullName,
                disconnectedConnections),
            onDetails: () =>
            {
                _pendingTableSourceReplacementConfirmations.Remove(confirmationKey);
                ReplacePlaceholderTableSource(placeholderNode, selectedTableFullName, columns);
            });
    }

    private void ReplacePlaceholderTableSource(
        NodeViewModel placeholderNode,
        string selectedTableFullName,
        IReadOnlyList<(string Name, PinDataType Type)> columns)
    {
        if (!Nodes.Contains(placeholderNode))
            return;

        _isReplacingTableSourceNode = true;
        try
        {
            Point originalPosition = placeholderNode.Position;
            string? originalAlias = placeholderNode.Alias;
            int originalZOrder = placeholderNode.ZOrder;

            List<ConnectionViewModel> attachedConnections = Connections
                .Where(connection =>
                    ReferenceEquals(connection.FromPin.Owner, placeholderNode)
                    || ReferenceEquals(connection.ToPin?.Owner, placeholderNode))
                .ToList();

            using UndoRedoStack.UndoRedoTransaction tx = UndoRedo.BeginTransaction(
                L("undo.tableSourceReplace", "Replace placeholder table source"));

            if (attachedConnections.Count > 0)
                UndoRedo.Execute(new DeleteSelectionCommand([], attachedConnections));

            UndoRedo.Execute(new DeleteSelectionCommand([placeholderNode], []));

            NodeViewModel tableNode = _nodeManager.SpawnTableNode(
                selectedTableFullName,
                columns.Select(column => (column.Name, column.Type)),
                originalPosition);

            tableNode.Alias = originalAlias;
            tableNode.ZOrder = originalZOrder;

            DeselectAll();
            SelectNode(tableNode);
            PropertyPanel.ShowNode(tableNode);

            tx.Commit();
            IsDirty = true;
            Toasts.ShowSuccess(
                L("toast.tableSourceReplaced", "Tabela aplicada ao node com sucesso."),
                selectedTableFullName);
        }
        finally
        {
            _isReplacingTableSourceNode = false;
        }
    }

    private bool TryResolveSelectedTableFullName(NodeViewModel node, out string? selectedTableFullName)
    {
        selectedTableFullName = null;
        string[] preferenceOrder =
        [
            "table_full_name",
            "table",
            "source_table",
            "from_table",
        ];

        foreach (string key in preferenceOrder)
        {
            if (node.Parameters.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
            {
                selectedTableFullName = NormalizeTableFullName(value!);
                return true;
            }
        }

        return false;
    }

    private bool TryResolveTableColumns(
        string tableFullName,
        out TableMetadata? tableMetadata,
        out List<(string Name, PinDataType Type)> columns)
    {
        columns = [];
        tableMetadata = null;
        string normalized = NormalizeTableFullName(tableFullName);

        if (DatabaseMetadata is not null)
        {
            tableMetadata = DatabaseMetadata.Schemas
                .SelectMany(schema => schema.Tables)
                .FirstOrDefault(table =>
                    string.Equals($"{table.Schema}.{table.Name}", normalized, StringComparison.OrdinalIgnoreCase));

            if (tableMetadata is not null)
            {
                columns = tableMetadata.Columns
                    .Select(column => (column.Name, SchemaViewModel.MapSqlTypeToPinDataType(column.DataType)))
                    .ToList();
                return columns.Count > 0;
            }
        }

        var demo = NodeManager.DemoCatalog
            .FirstOrDefault(entry => string.Equals(entry.FullName, normalized, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(demo.FullName))
        {
            columns = demo.Cols.Select(column => (column.Name, column.Type)).ToList();
            return columns.Count > 0;
        }

        return false;
    }

    private static bool IsPlaceholderTableSource(NodeViewModel node)
        => node.Type == NodeType.TableSource
           && node.OutputPins.Count == 1
           && string.Equals(node.OutputPins[0].Name, "*", StringComparison.OrdinalIgnoreCase);

    private static bool IsTableSelectionParameter(string parameterName)
        => string.Equals(parameterName, "table_full_name", StringComparison.OrdinalIgnoreCase)
           || string.Equals(parameterName, "table", StringComparison.OrdinalIgnoreCase)
           || string.Equals(parameterName, "source_table", StringComparison.OrdinalIgnoreCase)
           || string.Equals(parameterName, "from_table", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeTableFullName(string raw)
    {
        string trimmed = raw.Trim();
        if (trimmed.Length == 0)
            return trimmed;

        if (trimmed.Contains('.'))
            return trimmed;

        return $"public.{trimmed}";
    }

    /// <summary>
    /// Disposes all resources and unsubscribes from all event handlers.
    /// Called when the CanvasViewModel is being replaced (e.g., Ctrl+N to create new canvas).
    /// Prevents memory leaks by releasing references to event handlers and collection handlers.
    /// </summary>
    public void Dispose()
    {
        // Dispose AutoJoin overlay (which clears its cards and handlers)
        AutoJoin?.Dispose();

        // Unsubscribe from LiveSql PropertyChanged
        if (_liveSqlPropertyChangedHandler is not null)
            LiveSql.PropertyChanged -= _liveSqlPropertyChangedHandler;

        DataPreview.ErrorNotified -= OnDataPreviewErrorNotified;
        if (_propertyPanelWireStyleChangedHandler is not null)
            PropertyPanel.WireStyleChanged -= _propertyPanelWireStyleChangedHandler;

        // Unsubscribe from self PropertyChanged
        if (_selfPropertyChangedHandler is not null)
            this.PropertyChanged -= _selfPropertyChangedHandler;

        // Unsubscribe from layout manager PropertyChanged forwarding
        if (_layoutManagerPropertyChangedHandler is not null)
            _layoutManager.PropertyChanged -= _layoutManagerPropertyChangedHandler;

        if (_localizationPropertyChangedHandler is not null)
            _localizationService.PropertyChanged -= _localizationPropertyChangedHandler;
        if (_explainPlanPropertyChangedHandler is not null)
            ExplainPlan.PropertyChanged -= _explainPlanPropertyChangedHandler;

        _autoJoinController?.Dispose();

        // Unsubscribe from collection changed handlers
        if (_nodesCollectionChangedHandler is not null)
            Nodes.CollectionChanged -= _nodesCollectionChangedHandler;

        if (_connectionsCollectionChangedHandler is not null)
            Connections.CollectionChanged -= _connectionsCollectionChangedHandler;

        // Unsubscribe from all node validation handlers
        foreach (var handler in _nodeValidationHandlers.Values)
            foreach (var node in Nodes)
                node.PropertyChanged -= handler;
        _nodeValidationHandlers.Clear();
    }

    /// <summary>
    /// Replaces the entire canvas graph with new nodes and connections.
    /// </summary>
    public void ReplaceGraph(IEnumerable<NodeViewModel> nodes, IEnumerable<ConnectionViewModel> connections)
    {
        Nodes.Clear();
        Connections.Clear();

        foreach (NodeViewModel node in nodes)
            Nodes.Add(node);

        foreach (ConnectionViewModel connection in connections)
            Connections.Add(connection);

        RefreshPrimaryFromSourceMarkers();
    }

    /// <summary>
    /// Gets or sets the database provider for DDL generation and compilation.
    /// </summary>
    public DatabaseProvider Provider
    {
        get => LiveDdl?.Provider ?? DatabaseProvider.SqlServer;
        set
        {
            if (LiveDdl is not null)
                LiveDdl.Provider = value;
        }
    }
}
