using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using Material.Icons;
using AkkornStudio.Metadata;
using AkkornStudio.Nodes;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.ViewModels.Canvas;
using AkkornStudio.UI.ViewModels.UndoRedo;
using AkkornStudio.UI.ViewModels.UndoRedo.Commands;
using AkkornStudio.UI.ViewModels.Validation.Conventions;

namespace AkkornStudio.UI.ViewModels;

// ─── Property panel ──────────────────────────────────────────────────────────

/// <summary>
/// Bound to the right-side panel. Shows details and editable parameters for
/// the currently selected node, or a multi-selection summary.
/// </summary>
public sealed class PropertyPanelViewModel : ViewModelBase
{
    private NodeViewModel? _selectedNode;
    private bool _isVisible;
    private string _panelTitle = LocalizationService.Instance["property.panel.title"];
    private string _lastRawSql = string.Empty;
    private string? _sqlTraceFragment;
    private string? _sqlTraceContext;
    private PropertyPanelTab _activeTab = PropertyPanelTab.Properties;
    private string _selectedNamingConvention = "snake_case";
    private bool _enforceAliasNaming = true;
    private bool _warnOnReservedKeywords = true;
    private string _maxAliasLength = "64";
    private CanvasWireCurveMode _selectedWireCurveMode = CanvasWireCurveMode.Bezier;
    private ConnectionViewModel? _selectedWire;

    private readonly UndoRedoStack _undo;
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private readonly Func<IEnumerable<ConnectionViewModel>> _connectionsResolver;
    private readonly Func<IEnumerable<NodeViewModel>>? _nodesResolver;
    private readonly Func<DbMetadata?> _metadataResolver;
    private readonly Action<NodeViewModel, IReadOnlyList<(string Name, string? Value)>>? _parametersCommitted;
    private readonly Func<NodeViewModel, bool>? _setPrimaryFromSource;
    private readonly Action<ICanvasCommand> _executeCommand;
    private Action? _openSelectedJoinInErDiagram;
    private Action? _refineAutoProjection;
    private Action? _resetAutoProjection;
    private Action? _addSuggestedFilter;
    private Action? _applySuggestedAggregation;
    private readonly Dictionary<ParameterRowViewModel, PropertyChangedEventHandler> _parameterRowPropertyHandlers = [];
    private const string AutoProjectionMarkerParameter = "__akkorn_auto_projection";
    private static readonly HashSet<string> SupportedConventions =
    [
        "snake_case",
        "camelCase",
        "PascalCase",
        "SCREAMING_SNAKE_CASE",
    ];

    public event Action? NamingSettingsChanged;
    public event Action? WireStyleChanged;

    public PropertyPanelViewModel(
        UndoRedoStack undo,
        Func<IEnumerable<ConnectionViewModel>>? connectionsResolver = null,
        Func<DbMetadata?>? metadataResolver = null,
        Action<NodeViewModel, IReadOnlyList<(string Name, string? Value)>>? parametersCommitted = null,
        Func<NodeViewModel, bool>? setPrimaryFromSource = null,
        Func<IEnumerable<NodeViewModel>>? nodesResolver = null,
        Action<ICanvasCommand>? executeCommand = null)
    {
        _undo = undo;
        _connectionsResolver = connectionsResolver ?? (() => []);
        _nodesResolver = nodesResolver;
        _metadataResolver = metadataResolver ?? (() => null);
        _parametersCommitted = parametersCommitted;
        _setPrimaryFromSource = setPrimaryFromSource;
        _executeCommand = executeCommand ?? _undo.Execute;
        Parameters.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null)
            {
                foreach (ParameterRowViewModel row in e.OldItems)
                    DetachParameterRowHandler(row);
            }

            if (e.NewItems is not null)
            {
                foreach (ParameterRowViewModel row in e.NewItems)
                    EnsureParameterRowHandler(row);
            }
        };
        _loc.PropertyChanged += (_, _) =>
        {
            RaisePropertyChanged(nameof(NodeAliasLabel));
            RaisePropertyChanged(nameof(PanelTitle));
        };

        SelectPropertiesTabCommand = new RelayCommand(() => ActiveTab = PropertyPanelTab.Properties);
        SelectProjectSettingsTabCommand = new RelayCommand(() =>
            ActiveTab = PropertyPanelTab.ProjectSettings
        );
        SetAsPrimaryFromSourceCommand = new RelayCommand(
            SetAsPrimaryFromSource,
            () => CanSetAsPrimaryFromSource);
        OpenSelectedJoinInErDiagramCommand = new RelayCommand(
            OpenSelectedJoinInErDiagram,
            () => CanOpenSelectedJoinInErDiagram);
        RefineAutoProjectionCommand = new RelayCommand(
            RefineAutoProjection,
            () => CanRefineAutoProjection);
        ResetAutoProjectionCommand = new RelayCommand(
            ResetAutoProjection,
            () => CanResetAutoProjection);
        AddSuggestedFilterCommand = new RelayCommand(
            AddSuggestedFilter,
            () => CanAddSuggestedFilter);
        ApplySuggestedAggregationCommand = new RelayCommand(
            ApplySuggestedAggregation,
            () => CanApplySuggestedAggregation);
    }

    public RelayCommand SelectPropertiesTabCommand { get; }
    public RelayCommand SelectProjectSettingsTabCommand { get; }
    public RelayCommand SetAsPrimaryFromSourceCommand { get; }
    public RelayCommand OpenSelectedJoinInErDiagramCommand { get; }
    public RelayCommand RefineAutoProjectionCommand { get; }
    public RelayCommand ResetAutoProjectionCommand { get; }
    public RelayCommand AddSuggestedFilterCommand { get; }
    public RelayCommand ApplySuggestedAggregationCommand { get; }

    // ── Sub-collections ───────────────────────────────────────────────────────
    public ObservableCollection<ParameterRowViewModel> Parameters { get; } = [];
    public ObservableCollection<PinInfoRowViewModel> InputPins { get; } = [];
    public ObservableCollection<PinInfoRowViewModel> OutputPins { get; } = [];

    // ── State ─────────────────────────────────────────────────────────────────

    public NodeViewModel? SelectedNode
    {
        get => _selectedNode;
        private set => Set(ref _selectedNode, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => Set(ref _isVisible, value);
    }

    public string PanelTitle
    {
        get => _panelTitle;
        private set => Set(ref _panelTitle, value);
    }

    public PropertyPanelTab ActiveTab
    {
        get => _activeTab;
        set
        {
            if (Set(ref _activeTab, value))
            {
                RaisePropertyChanged(nameof(ShowPropertiesTab));
                RaisePropertyChanged(nameof(ShowProjectSettingsTab));
            }
        }
    }

    public bool ShowPropertiesTab => ActiveTab == PropertyPanelTab.Properties;
    public bool ShowProjectSettingsTab => ActiveTab == PropertyPanelTab.ProjectSettings;

    public IReadOnlyList<string> NamingConventionOptions { get; } =
    [
        "snake_case",
        "camelCase",
        "PascalCase",
        "SCREAMING_SNAKE_CASE",
    ];

    public string SelectedNamingConvention
    {
        get => _selectedNamingConvention;
        set
        {
            if (!Set(ref _selectedNamingConvention, value))
                return;

            NamingSettingsChanged?.Invoke();
        }
    }

    public bool EnforceAliasNaming
    {
        get => _enforceAliasNaming;
        set
        {
            if (!Set(ref _enforceAliasNaming, value))
                return;

            NamingSettingsChanged?.Invoke();
        }
    }

    public bool WarnOnReservedKeywords
    {
        get => _warnOnReservedKeywords;
        set => Set(ref _warnOnReservedKeywords, value);
    }

    public string MaxAliasLength
    {
        get => _maxAliasLength;
        set
        {
            if (!Set(ref _maxAliasLength, value))
                return;

            NamingSettingsChanged?.Invoke();
        }
    }

    public IReadOnlyList<CanvasWireCurveMode> WireCurveModeOptions { get; } =
    [
        CanvasWireCurveMode.Bezier,
        CanvasWireCurveMode.Straight,
        CanvasWireCurveMode.Orthogonal,
    ];

    public CanvasWireCurveMode SelectedWireCurveMode
    {
        get => _selectedWireCurveMode;
        set
        {
            if (!Set(ref _selectedWireCurveMode, value))
                return;

            WireStyleChanged?.Invoke();
        }
    }

    public bool HasSelectedWire => _selectedWire is not null;

    public string SelectedWireLabel
    {
        get
        {
            if (_selectedWire is null)
                return string.Empty;

            string fromNode = _selectedWire.FromPin.Owner.Title;
            string fromPin = _selectedWire.FromPin.Name;
            string toNode = _selectedWire.ToPin?.Owner.Title ?? "?";
            string toPin = _selectedWire.ToPin?.Name ?? "?";
            return $"{fromNode}.{fromPin} -> {toNode}.{toPin}";
        }
    }

    public NamingConventionPolicy BuildNamingConventionPolicy()
    {
        int parsedMaxLength = int.TryParse(MaxAliasLength, out int maxLength) && maxLength >= 0
            ? maxLength
            : 64;

        string? conventionName = EnforceAliasNaming
            ? ResolveConventionOrDefault(SelectedNamingConvention)
            : null;

        bool enforceSnakeCase = EnforceAliasNaming
            && string.Equals(conventionName, "snake_case", StringComparison.OrdinalIgnoreCase);

        return new NamingConventionPolicy
        {
            EnforceSnakeCase = enforceSnakeCase,
            MaxLength = parsedMaxLength,
            NoLeadingDigit = true,
            NoSpaces = true,
            ConventionName = conventionName,
        };
    }

    private static string ResolveConventionOrDefault(string value) =>
        SupportedConventions.Contains(value)
            ? value
            : "snake_case";

    // ── SQL Trace ─────────────────────────────────────────────────────────────

    public string? SqlTraceFragment
    {
        get => _sqlTraceFragment;
        private set => Set(ref _sqlTraceFragment, value);
    }

    public string? SqlTraceContext
    {
        get => _sqlTraceContext;
        private set => Set(ref _sqlTraceContext, value);
    }

    public bool HasSqlTrace => SqlTraceFragment is not null;

    /// <summary>
    /// Called by CanvasViewModel whenever the live SQL output changes.
    /// Stores the latest SQL and recomputes the trace for the selected node.
    /// </summary>
    public void UpdateSqlTrace(string rawSql)
    {
        _lastRawSql = rawSql ?? string.Empty;
        RecomputeTrace();
    }

    private void RecomputeTrace()
    {
        if (SelectedNode is null || string.IsNullOrWhiteSpace(_lastRawSql))
        {
            SqlTraceFragment = null;
            SqlTraceContext = null;
            RaisePropertyChanged(nameof(HasSqlTrace));
            return;
        }
        (SqlTraceContext, SqlTraceFragment) = ExtractTrace(SelectedNode, _lastRawSql);
        RaisePropertyChanged(nameof(HasSqlTrace));
    }

    // ── Computed from SelectedNode ────────────────────────────────────────────

    public bool HasNode => SelectedNode is not null;
    public bool HasParams => Parameters.Count > 0;
    public bool HasInputs => InputPins.Count > 0;
    public bool HasOutputs => OutputPins.Count > 0;
    public bool ShowSelectNodeHint => !HasNode && !HasSelectedWire;

    public string NodeTitle => SelectedNode?.Title ?? string.Empty;
    public string NodeCategory => SelectedNode?.Category.ToString() ?? string.Empty;
    public string NodeTypeLabel => SelectedNode?.Type.ToString() ?? string.Empty;
    public string NodeTypeSubtitle => SelectedNode?.Subtitle ?? string.Empty;
    public string NodeAlias
    {
        get => SelectedNode?.Alias ?? string.Empty;
        set
        {
            if (SelectedNode is not null)
                SelectedNode.Alias = string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    public string NodeAliasLabel =>
        IsSourceAliasNode(SelectedNode?.Type)
            ? _loc["property.sourceAlias"]
            : _loc["property.outputAlias"];

    /// <summary>
    /// True when alias editing is meaningful for the selected node type.
    /// Hides the alias input for structural/predicate/output nodes where alias has no effect.
    /// </summary>
    public bool ShowAliasEditor => SelectedNode is not null && SupportsAliasEditor(SelectedNode.Type);

    public bool IsSelectedNodePrimaryFromSource => SelectedNode?.IsPrimaryFromSource == true;

    public bool IsSelectedNodeSourceType => SelectedNode is not null && IsSourceAliasNode(SelectedNode.Type);

    public bool IsSelectedNodeJoinType => SelectedNode?.IsJoin == true;

    public bool CanSetAsPrimaryFromSource =>
        IsSelectedNodeSourceType
        && !IsSelectedNodePrimaryFromSource
        && _setPrimaryFromSource is not null;

    public bool CanOpenSelectedJoinInErDiagram =>
        IsSelectedNodeJoinType && _openSelectedJoinInErDiagram is not null;

    public bool IsSelectedNodeAutoProjectionResultOutput =>
        SelectedNode?.Type == NodeType.ResultOutput
        && string.Equals(
            SelectedNode.Parameters.GetValueOrDefault(AutoProjectionMarkerParameter),
            "true",
            StringComparison.OrdinalIgnoreCase);

    public bool CanRefineAutoProjection =>
        IsSelectedNodeAutoProjectionResultOutput && _refineAutoProjection is not null;

    public bool CanResetAutoProjection =>
        IsSelectedNodeAutoProjectionResultOutput && _resetAutoProjection is not null;

    public bool CanAddSuggestedFilter =>
        IsSelectedNodeAutoProjectionResultOutput && _addSuggestedFilter is not null;

    public bool CanApplySuggestedAggregation =>
        IsSelectedNodeAutoProjectionResultOutput && _applySuggestedAggregation is not null;

    public Avalonia.Media.LinearGradientBrush? HeaderGradient => SelectedNode?.HeaderGradient;

    public string CategoryIcon => SelectedNode?.CategoryIcon ?? string.Empty;
    public MaterialIconKind CategoryIconKind =>
        SelectedNode?.CategoryIconKind ?? MaterialIconKind.Help;

    public void BindOpenSelectedJoinInErDiagram(Action? openSelectedJoinInErDiagram)
    {
        _openSelectedJoinInErDiagram = openSelectedJoinInErDiagram;
        OpenSelectedJoinInErDiagramCommand.NotifyCanExecuteChanged();
    }

    public void BindRefineAutoProjection(Action? refineAutoProjection)
    {
        _refineAutoProjection = refineAutoProjection;
        RefineAutoProjectionCommand.NotifyCanExecuteChanged();
    }

    public void BindResetAutoProjection(Action? resetAutoProjection)
    {
        _resetAutoProjection = resetAutoProjection;
        ResetAutoProjectionCommand.NotifyCanExecuteChanged();
    }

    public void BindAddSuggestedFilter(Action? addSuggestedFilter)
    {
        _addSuggestedFilter = addSuggestedFilter;
        AddSuggestedFilterCommand.NotifyCanExecuteChanged();
    }

    public void BindApplySuggestedAggregation(Action? applySuggestedAggregation)
    {
        _applySuggestedAggregation = applySuggestedAggregation;
        ApplySuggestedAggregationCommand.NotifyCanExecuteChanged();
    }

    // ── Selection management ──────────────────────────────────────────────────

    public void ShowNode(NodeViewModel node)
    {
        _selectedWire = null;
        // Commit any dirty parameters before switching
        CommitDirty();

        SelectedNode = node;
        PanelTitle = node.Title;
        IsVisible = true;

        RebuildRows(node);
        RecomputeTrace();
        RaisePropertyChanged(nameof(HasNode));
        RaisePropertyChanged(nameof(HasParams));
        RaisePropertyChanged(nameof(HasInputs));
        RaisePropertyChanged(nameof(HasOutputs));
        RaisePropertyChanged(nameof(NodeTitle));
        RaisePropertyChanged(nameof(NodeCategory));
        RaisePropertyChanged(nameof(NodeTypeLabel));
        RaisePropertyChanged(nameof(NodeTypeSubtitle));
        RaisePropertyChanged(nameof(NodeAlias));
        RaisePropertyChanged(nameof(NodeAliasLabel));
        RaisePropertyChanged(nameof(ShowAliasEditor));
        RaisePropertyChanged(nameof(IsSelectedNodePrimaryFromSource));
        RaisePropertyChanged(nameof(IsSelectedNodeSourceType));
        RaisePropertyChanged(nameof(IsSelectedNodeJoinType));
        RaisePropertyChanged(nameof(CanSetAsPrimaryFromSource));
        RaisePropertyChanged(nameof(CanOpenSelectedJoinInErDiagram));
        RaisePropertyChanged(nameof(IsSelectedNodeAutoProjectionResultOutput));
        RaisePropertyChanged(nameof(CanRefineAutoProjection));
        RaisePropertyChanged(nameof(CanResetAutoProjection));
        RaisePropertyChanged(nameof(CanAddSuggestedFilter));
        RaisePropertyChanged(nameof(CanApplySuggestedAggregation));
        RaisePropertyChanged(nameof(HeaderGradient));
        RaisePropertyChanged(nameof(CategoryIcon));
        RaisePropertyChanged(nameof(CategoryIconKind));
        RaisePropertyChanged(nameof(ShowPropertiesTab));
        RaisePropertyChanged(nameof(ShowProjectSettingsTab));
        RaisePropertyChanged(nameof(HasSelectedWire));
        RaisePropertyChanged(nameof(SelectedWireLabel));
        RaisePropertyChanged(nameof(ShowSelectNodeHint));
        SetAsPrimaryFromSourceCommand.NotifyCanExecuteChanged();
        OpenSelectedJoinInErDiagramCommand.NotifyCanExecuteChanged();
        RefineAutoProjectionCommand.NotifyCanExecuteChanged();
        ResetAutoProjectionCommand.NotifyCanExecuteChanged();
        AddSuggestedFilterCommand.NotifyCanExecuteChanged();
        ApplySuggestedAggregationCommand.NotifyCanExecuteChanged();
    }

    public void ShowMultiSelection(IReadOnlyList<NodeViewModel> nodes)
    {
        _selectedWire = null;
        CommitDirty();
        ClearParameterRowHandlers();
        SelectedNode = null;
        PanelTitle = string.Format(
            L("property.panel.multiSelected", "{0} nodes selected"),
            nodes.Count
        );
        Parameters.Clear();
        InputPins.Clear();
        OutputPins.Clear();
        SqlTraceFragment = null;
        SqlTraceContext = null;
        RaisePropertyChanged(nameof(HasSqlTrace));
        IsVisible = true;
        RaisePropertyChanged(nameof(HasNode));
        RaisePropertyChanged(nameof(HasParams));
        RaisePropertyChanged(nameof(HasInputs));
        RaisePropertyChanged(nameof(HasOutputs));
        RaisePropertyChanged(nameof(NodeTypeLabel));
        RaisePropertyChanged(nameof(NodeTypeSubtitle));
        RaisePropertyChanged(nameof(NodeAliasLabel));
        RaisePropertyChanged(nameof(ShowAliasEditor));
        RaisePropertyChanged(nameof(IsSelectedNodePrimaryFromSource));
        RaisePropertyChanged(nameof(IsSelectedNodeSourceType));
        RaisePropertyChanged(nameof(IsSelectedNodeJoinType));
        RaisePropertyChanged(nameof(CanSetAsPrimaryFromSource));
        RaisePropertyChanged(nameof(CanOpenSelectedJoinInErDiagram));
        RaisePropertyChanged(nameof(IsSelectedNodeAutoProjectionResultOutput));
        RaisePropertyChanged(nameof(CanRefineAutoProjection));
        RaisePropertyChanged(nameof(CanResetAutoProjection));
        RaisePropertyChanged(nameof(CanAddSuggestedFilter));
        RaisePropertyChanged(nameof(CanApplySuggestedAggregation));
        RaisePropertyChanged(nameof(ShowPropertiesTab));
        RaisePropertyChanged(nameof(ShowProjectSettingsTab));
        RaisePropertyChanged(nameof(HasSelectedWire));
        RaisePropertyChanged(nameof(SelectedWireLabel));
        RaisePropertyChanged(nameof(ShowSelectNodeHint));
        SetAsPrimaryFromSourceCommand.NotifyCanExecuteChanged();
        OpenSelectedJoinInErDiagramCommand.NotifyCanExecuteChanged();
        RefineAutoProjectionCommand.NotifyCanExecuteChanged();
        ResetAutoProjectionCommand.NotifyCanExecuteChanged();
        AddSuggestedFilterCommand.NotifyCanExecuteChanged();
        ApplySuggestedAggregationCommand.NotifyCanExecuteChanged();
    }

    public void Clear()
    {
        _selectedWire = null;
        CommitDirty();
        ClearParameterRowHandlers();
        SelectedNode = null;
        PanelTitle = L("property.panel.title", "Properties");
        Parameters.Clear();
        InputPins.Clear();
        OutputPins.Clear();
        SqlTraceFragment = null;
        SqlTraceContext = null;
        RaisePropertyChanged(nameof(HasSqlTrace));
        IsVisible = false;
        RaisePropertyChanged(nameof(HasNode));
        RaisePropertyChanged(nameof(HasParams));
        RaisePropertyChanged(nameof(HasInputs));
        RaisePropertyChanged(nameof(HasOutputs));
        RaisePropertyChanged(nameof(NodeTypeLabel));
        RaisePropertyChanged(nameof(NodeTypeSubtitle));
        RaisePropertyChanged(nameof(NodeAliasLabel));
        RaisePropertyChanged(nameof(ShowAliasEditor));
        RaisePropertyChanged(nameof(IsSelectedNodePrimaryFromSource));
        RaisePropertyChanged(nameof(IsSelectedNodeSourceType));
        RaisePropertyChanged(nameof(IsSelectedNodeJoinType));
        RaisePropertyChanged(nameof(CanSetAsPrimaryFromSource));
        RaisePropertyChanged(nameof(CanOpenSelectedJoinInErDiagram));
        RaisePropertyChanged(nameof(IsSelectedNodeAutoProjectionResultOutput));
        RaisePropertyChanged(nameof(CanRefineAutoProjection));
        RaisePropertyChanged(nameof(CanResetAutoProjection));
        RaisePropertyChanged(nameof(CanAddSuggestedFilter));
        RaisePropertyChanged(nameof(CanApplySuggestedAggregation));
        RaisePropertyChanged(nameof(ShowPropertiesTab));
        RaisePropertyChanged(nameof(ShowProjectSettingsTab));
        RaisePropertyChanged(nameof(HasSelectedWire));
        RaisePropertyChanged(nameof(SelectedWireLabel));
        RaisePropertyChanged(nameof(ShowSelectNodeHint));
        SetAsPrimaryFromSourceCommand.NotifyCanExecuteChanged();
        OpenSelectedJoinInErDiagramCommand.NotifyCanExecuteChanged();
        RefineAutoProjectionCommand.NotifyCanExecuteChanged();
        ResetAutoProjectionCommand.NotifyCanExecuteChanged();
        AddSuggestedFilterCommand.NotifyCanExecuteChanged();
        ApplySuggestedAggregationCommand.NotifyCanExecuteChanged();
    }

    public void ShowWire(ConnectionViewModel wire)
    {
        CommitDirty();
        ClearParameterRowHandlers();
        SelectedNode = null;
        Parameters.Clear();
        InputPins.Clear();
        OutputPins.Clear();
        SqlTraceFragment = null;
        SqlTraceContext = null;
        RaisePropertyChanged(nameof(HasSqlTrace));
        RaisePropertyChanged(nameof(HasNode));
        RaisePropertyChanged(nameof(HasParams));
        RaisePropertyChanged(nameof(HasInputs));
        RaisePropertyChanged(nameof(HasOutputs));
        RaisePropertyChanged(nameof(ShowAliasEditor));
        RaisePropertyChanged(nameof(IsSelectedNodePrimaryFromSource));
        RaisePropertyChanged(nameof(IsSelectedNodeSourceType));
        RaisePropertyChanged(nameof(CanSetAsPrimaryFromSource));
        RaisePropertyChanged(nameof(IsSelectedNodeAutoProjectionResultOutput));
        RaisePropertyChanged(nameof(CanRefineAutoProjection));
        RaisePropertyChanged(nameof(CanResetAutoProjection));
        RaisePropertyChanged(nameof(CanAddSuggestedFilter));
        RaisePropertyChanged(nameof(CanApplySuggestedAggregation));
        RaisePropertyChanged(nameof(ShowSelectNodeHint));

        _selectedWire = wire;
        PanelTitle = L("property.panel.title", "Properties");
        IsVisible = true;
        RaisePropertyChanged(nameof(HasSelectedWire));
        RaisePropertyChanged(nameof(SelectedWireLabel));
        RaisePropertyChanged(nameof(ShowSelectNodeHint));
        SetAsPrimaryFromSourceCommand.NotifyCanExecuteChanged();
        RefineAutoProjectionCommand.NotifyCanExecuteChanged();
        ResetAutoProjectionCommand.NotifyCanExecuteChanged();
        AddSuggestedFilterCommand.NotifyCanExecuteChanged();
        ApplySuggestedAggregationCommand.NotifyCanExecuteChanged();
    }

    public void ClearSelectedWire()
    {
        if (_selectedWire is null)
            return;

        _selectedWire = null;
        RaisePropertyChanged(nameof(HasSelectedWire));
        RaisePropertyChanged(nameof(SelectedWireLabel));
        RaisePropertyChanged(nameof(ShowSelectNodeHint));
        RaisePropertyChanged(nameof(CanSetAsPrimaryFromSource));
        SetAsPrimaryFromSourceCommand.NotifyCanExecuteChanged();
    }

    public void NotifySelectedNodePrimaryFromSourceChanged()
    {
        RaisePropertyChanged(nameof(IsSelectedNodePrimaryFromSource));
        RaisePropertyChanged(nameof(CanSetAsPrimaryFromSource));
        SetAsPrimaryFromSourceCommand.NotifyCanExecuteChanged();
    }

    private void SetAsPrimaryFromSource()
    {
        if (SelectedNode is null || _setPrimaryFromSource is null)
            return;

        if (!_setPrimaryFromSource(SelectedNode))
            return;

        RaisePropertyChanged(nameof(IsSelectedNodePrimaryFromSource));
        RaisePropertyChanged(nameof(CanSetAsPrimaryFromSource));
        SetAsPrimaryFromSourceCommand.NotifyCanExecuteChanged();
    }

    private static bool IsSourceAliasNode(NodeType? type) =>
        type is NodeType.TableSource or NodeType.Subquery or NodeType.SubqueryReference or NodeType.CteSource;

    private string L(string key, string fallback)
    {
        string value = _loc[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    private static bool SupportsAliasEditor(NodeType type) => type switch
    {
        // Source aliases
        NodeType.TableSource or NodeType.Subquery or NodeType.SubqueryReference or NodeType.CteSource => true,

        // Explicit alias / scalar transforms / computed expressions
        NodeType.Alias
            or NodeType.Upper or NodeType.Lower or NodeType.Trim or NodeType.Substring
            or NodeType.RegexMatch or NodeType.RegexReplace or NodeType.RegexExtract
            or NodeType.Concat or NodeType.StringLength or NodeType.Replace
            or NodeType.Round or NodeType.Abs or NodeType.Ceil or NodeType.Floor
            or NodeType.Add or NodeType.Subtract or NodeType.Multiply or NodeType.Divide
            or NodeType.Modulo or NodeType.DateAdd or NodeType.DateDiff or NodeType.DatePart
            or NodeType.DateFormat
            or NodeType.CountStar or NodeType.CountDistinct or NodeType.Sum or NodeType.Avg
            or NodeType.Min or NodeType.Max or NodeType.StringAgg or NodeType.WindowFunction
            or NodeType.Cast or NodeType.ColumnRefCast or NodeType.ScalarFromColumn
            or NodeType.JsonExtract or NodeType.JsonValue or NodeType.JsonArrayLength
            or NodeType.Case or NodeType.NullFill or NodeType.EmptyFill or NodeType.ValueMap
            or NodeType.ValueNumber or NodeType.ValueString or NodeType.ValueDateTime
            or NodeType.ValueBoolean or NodeType.SystemDate or NodeType.SystemDateTime
            or NodeType.CurrentDate or NodeType.CurrentTime => true,

        _ => false,
    };

    // ── Parameter building ────────────────────────────────────────────────────

    private void RebuildRows(NodeViewModel node)
    {
        ClearParameterRowHandlers();
        Parameters.Clear();
        InputPins.Clear();
        OutputPins.Clear();

        // Get the static definition for this node type
        NodeDefinition? def = null;
        try
        {
            def = NodeDefinitionRegistry.Get(node.Type);
        }
        catch
        { /* TableSource and custom nodes have no registry entry */
        }

        if (def is not null)
        {
            foreach (NodeParameter param in def.Parameters)
            {
                node.Parameters.TryGetValue(param.Name, out string? currentVal);
                var row = new ParameterRowViewModel(param, currentVal);
                ApplyConnectionOverride(node, row, currentVal ?? param.DefaultValue);
                ApplyTextSuggestions(node, row);
                Parameters.Add(row);
            }
        }

        AttachParameterRowHandlers();

        foreach (PinViewModel pin in node.InputPins)
            InputPins.Add(new PinInfoRowViewModel(pin));

        foreach (PinViewModel pin in node.OutputPins)
            OutputPins.Add(new PinInfoRowViewModel(pin));
    }

    public void RefreshConnectionOverrides()
    {
        if (SelectedNode is null)
            return;

        foreach (ParameterRowViewModel row in Parameters)
        {
            SelectedNode.Parameters.TryGetValue(row.Name, out string? currentVal);
            ApplyConnectionOverride(SelectedNode, row, currentVal ?? row.DefaultValue);
            ApplyTextSuggestions(SelectedNode, row);
        }
    }

    public void SynchronizeSelectedNodeParameter(NodeViewModel node, string paramName)
    {
        if (SelectedNode is null || !ReferenceEquals(SelectedNode, node))
            return;

        if (_nodesResolver is not null
            && !_nodesResolver().Any(existing => ReferenceEquals(existing, node)))
        {
            // The selected node was detached from canvas; drop panel state safely.
            SelectedNode = null;
            Clear();
            return;
        }

        ParameterRowViewModel? row = Parameters.FirstOrDefault(parameter =>
            string.Equals(parameter.Name, paramName, StringComparison.OrdinalIgnoreCase));
        if (row is null)
            return;

        node.Parameters.TryGetValue(row.Name, out string? currentVal);
        ApplyConnectionOverride(node, row, currentVal ?? row.DefaultValue);
        ApplyTextSuggestions(node, row);
    }

    private void ApplyConnectionOverride(NodeViewModel node, ParameterRowViewModel row, string? fallbackValue)
    {
        ConnectionViewModel? drivingConnection = _connectionsResolver()
            .FirstOrDefault(c =>
                c.ToPin is not null
                && ReferenceEquals(c.ToPin.Owner, node)
                && IsPinDrivingParameter(node.Type, c.ToPin.Name, row.Name));

        if (drivingConnection is null)
        {
            row.ClearConnectionOverride(fallbackValue);
            return;
        }

        row.SetConnectionOverrideValue(ResolveDrivenValue(drivingConnection.FromPin));
    }

    private static string ResolveDrivenValue(PinViewModel sourcePin)
    {
        NodeViewModel sourceNode = sourcePin.Owner;

        if (sourceNode.IsValueNode && sourceNode.Parameters.TryGetValue("value", out string? literal))
            return literal ?? string.Empty;

        if (sourceNode.Type == NodeType.ScalarTypeDefinition)
            return BuildScalarTypePreview(sourceNode);

        if (sourceNode.Type == NodeType.EnumTypeDefinition)
            return "ENUM";

        if (sourceNode.IsTableSource)
            return $"{sourceNode.Title}.{sourcePin.Name}";

        return $"{sourceNode.Title}.{sourcePin.Name}";
    }

    private static bool IsPinDrivingParameter(NodeType targetNodeType, string pinName, string parameterName)
    {
        if (string.Equals(pinName, parameterName, StringComparison.OrdinalIgnoreCase))
            return true;

        return targetNodeType switch
        {
            NodeType.ColumnDefinition =>
                string.Equals(pinName, "type_def", StringComparison.OrdinalIgnoreCase)
                && string.Equals(parameterName, "DataType", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static string BuildScalarTypePreview(NodeViewModel sourceNode)
    {
        string kind = ReadParam(sourceNode, "TypeKind", "VARCHAR").Trim().ToUpperInvariant();
        return kind switch
        {
            "VARCHAR" => $"VARCHAR({ReadPositiveInt(sourceNode, "Length", 255)})",
            "DECIMAL" =>
                $"DECIMAL({ReadPositiveInt(sourceNode, "Precision", 18)},{ReadNonNegativeInt(sourceNode, "Scale", 2)})",
            "TEXT" => "TEXT",
            "INT" => "INT",
            "BIGINT" => "BIGINT",
            "BOOLEAN" => "BOOLEAN",
            "DATE" => "DATE",
            "DATETIME" => "DATETIME",
            "JSON" => "JSON",
            "UUID" => "UUID",
            _ => kind,
        };
    }

    private static string ReadParam(NodeViewModel node, string name, string fallback)
        => node.Parameters.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;

    private static int ReadPositiveInt(NodeViewModel node, string name, int fallback)
    {
        if (
            node.Parameters.TryGetValue(name, out string? raw)
            && int.TryParse(raw, out int parsed)
            && parsed > 0
        )
            return parsed;

        return fallback;
    }

    private static int ReadNonNegativeInt(NodeViewModel node, string name, int fallback)
    {
        if (
            node.Parameters.TryGetValue(name, out string? raw)
            && int.TryParse(raw, out int parsed)
            && parsed >= 0
        )
            return parsed;

        return fallback;
    }

    private void ApplyTextSuggestions(NodeViewModel node, ParameterRowViewModel row)
    {
        if (!row.IsText)
            return;

        IEnumerable<string> suggestions = ResolveSuggestions(node, row.Name);
        row.SetSuggestedValues(suggestions);
    }

    private IEnumerable<string> ResolveSuggestions(NodeViewModel node, string parameterName)
    {
        DbMetadata? metadata = _metadataResolver();
        if (metadata is null)
            return ResolveFallbackSuggestions(parameterName);

        bool tableOnly = IsTableOnlyParameter(node, parameterName);
        bool viewOnly = IsViewOnlyParameter(node, parameterName);
        string? selectedSchema = ResolveSelectedSchema();

        IEnumerable<TableMetadata> objects = metadata.Schemas.SelectMany(schema => schema.Tables);
        if (tableOnly)
            objects = objects.Where(t => t.Kind == TableKind.Table);
        else if (viewOnly)
            objects = objects.Where(t => t.Kind != TableKind.Table);

        if (IsSchemaParameter(node, parameterName))
        {
            return objects
                .Select(t => string.IsNullOrWhiteSpace(t.Schema) ? "public" : t.Schema)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static schema => schema, StringComparer.OrdinalIgnoreCase);
        }

        if (IsObjectNameOnlyParameter(node, parameterName))
        {
            IEnumerable<TableMetadata> filtered = FilterBySchema(objects, selectedSchema);
            return filtered
                .Select(t => t.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase);
        }

        if (IsQualifiedObjectParameter(node, parameterName))
        {
            IEnumerable<TableMetadata> filtered = FilterBySchema(objects, selectedSchema);
            return filtered
                .Select(t => QualifyName(t.Schema, t.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static fullName => fullName, StringComparer.OrdinalIgnoreCase);
        }

        return [];
    }

    private IEnumerable<string> ResolveFallbackSuggestions(string parameterName)
    {
        if (IsSchemaParameter(SelectedNode, parameterName))
            return ["public"];

        return [];
    }

    private static IEnumerable<TableMetadata> FilterBySchema(
        IEnumerable<TableMetadata> objects,
        string? selectedSchema)
    {
        if (string.IsNullOrWhiteSpace(selectedSchema))
            return objects;

        return objects.Where(t => string.Equals(t.Schema, selectedSchema, StringComparison.OrdinalIgnoreCase));
    }

    private string? ResolveSelectedSchema()
    {
        ParameterRowViewModel? schemaRow = Parameters.FirstOrDefault(static row =>
            string.Equals(row.Name, "SchemaName", StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.Name, "Schema", StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.Name, "NewSchema", StringComparison.OrdinalIgnoreCase));

        if (schemaRow is not null && !string.IsNullOrWhiteSpace(schemaRow.Value))
            return schemaRow.Value!.Trim();

        if (SelectedNode is not null)
        {
            if (SelectedNode.Parameters.TryGetValue("SchemaName", out string? schemaName)
                && !string.IsNullOrWhiteSpace(schemaName))
            {
                return schemaName.Trim();
            }

            if (SelectedNode.Parameters.TryGetValue("Schema", out string? schema)
                && !string.IsNullOrWhiteSpace(schema))
            {
                return schema.Trim();
            }

            if (SelectedNode.Parameters.TryGetValue("NewSchema", out string? newSchema)
                && !string.IsNullOrWhiteSpace(newSchema))
            {
                return newSchema.Trim();
            }
        }

        return null;
    }

    private static bool IsSchemaParameter(NodeViewModel? node, string parameterName) =>
        string.Equals(parameterName, "SchemaName", StringComparison.OrdinalIgnoreCase)
        || string.Equals(parameterName, "Schema", StringComparison.OrdinalIgnoreCase)
        || (node?.Type == NodeType.RenameTableOp
            && string.Equals(parameterName, "NewSchema", StringComparison.OrdinalIgnoreCase));

    private static bool IsObjectNameOnlyParameter(NodeViewModel? node, string parameterName) =>
        string.Equals(parameterName, "TableName", StringComparison.OrdinalIgnoreCase)
        || string.Equals(parameterName, "ViewName", StringComparison.OrdinalIgnoreCase)
        || (node?.Type == NodeType.RenameTableOp
            && string.Equals(parameterName, "NewName", StringComparison.OrdinalIgnoreCase));

    private static bool IsQualifiedObjectParameter(NodeViewModel? node, string parameterName) =>
        string.Equals(parameterName, "table_full_name", StringComparison.OrdinalIgnoreCase)
        || string.Equals(parameterName, "table", StringComparison.OrdinalIgnoreCase)
        || string.Equals(parameterName, "source_table", StringComparison.OrdinalIgnoreCase)
        || string.Equals(parameterName, "from_table", StringComparison.OrdinalIgnoreCase);

    private static bool IsTableOnlyParameter(NodeViewModel node, string parameterName)
    {
        if (node.Type is NodeType.TableReference or NodeType.TableDefinition)
            return true;

        return string.Equals(parameterName, "TableName", StringComparison.OrdinalIgnoreCase)
            || string.Equals(parameterName, "table_full_name", StringComparison.OrdinalIgnoreCase)
            || string.Equals(parameterName, "table", StringComparison.OrdinalIgnoreCase)
            || string.Equals(parameterName, "source_table", StringComparison.OrdinalIgnoreCase)
            || string.Equals(parameterName, "from_table", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsViewOnlyParameter(NodeViewModel node, string parameterName)
    {
        if (node.Type == NodeType.ViewReference)
            return true;

        return string.Equals(parameterName, "ViewName", StringComparison.OrdinalIgnoreCase);
    }

    private static string QualifyName(string schema, string name) =>
        string.IsNullOrWhiteSpace(schema) ? name : $"{schema}.{name}";

    private void AttachParameterRowHandlers()
    {
        foreach (ParameterRowViewModel row in Parameters)
            EnsureParameterRowHandler(row);
    }

    private void EnsureParameterRowHandler(ParameterRowViewModel row)
    {
        if (_parameterRowPropertyHandlers.ContainsKey(row))
            return;

        PropertyChangedEventHandler handler = (_, e) =>
        {
            if (e.PropertyName != nameof(ParameterRowViewModel.Value))
                return;

            NodeViewModel? selectedNode = SelectedNode;
            if (selectedNode is null)
                return;

            if (!IsSchemaParameter(selectedNode, row.Name))
                return;

            foreach (ParameterRowViewModel targetRow in Parameters.Where(parameter =>
                         IsObjectNameOnlyParameter(selectedNode, parameter.Name)
                         || IsQualifiedObjectParameter(selectedNode, parameter.Name)))
            {
                ApplyTextSuggestions(selectedNode, targetRow);
            }
        };

        row.PropertyChanged += handler;
        _parameterRowPropertyHandlers[row] = handler;
    }

    private void DetachParameterRowHandler(ParameterRowViewModel row)
    {
        if (_parameterRowPropertyHandlers.TryGetValue(row, out PropertyChangedEventHandler? handler))
        {
            row.PropertyChanged -= handler;
            _parameterRowPropertyHandlers.Remove(row);
        }
    }

    private void ClearParameterRowHandlers()
    {
        foreach ((ParameterRowViewModel row, PropertyChangedEventHandler handler) in _parameterRowPropertyHandlers)
            row.PropertyChanged -= handler;

        _parameterRowPropertyHandlers.Clear();
    }

    // ── Commit / apply ────────────────────────────────────────────────────────

    /// <summary>
    /// Writes all dirty parameter rows to the node via undo-able commands.
    /// Called automatically on selection change and on explicit Apply.
    /// </summary>
    public void CommitDirty()
    {
        if (SelectedNode is null)
            return;

        List<ParameterRowViewModel> dirtyRows = Parameters.Where(r => r.IsDirty).ToList();
        var committedChanges = new List<(string Name, string? Value)>(dirtyRows.Count);
        using UndoRedoStack.UndoRedoTransaction transaction = _undo.BeginTransaction("Edit Parameters");

        try
        {
            foreach (ParameterRowViewModel row in dirtyRows)
            {
                SelectedNode.Parameters.TryGetValue(row.Name, out string? old);
                _executeCommand(new EditParameterCommand(SelectedNode, row.Name, old, row.Value));
                committedChanges.Add((row.Name, row.Value));
            }
            transaction.Commit();
        }
        catch
        {
            foreach (ParameterRowViewModel row in Parameters)
                ApplyTextSuggestions(SelectedNode, row);
            throw;
        }

        foreach (ParameterRowViewModel row in dirtyRows)
            row.MarkClean();

        foreach (ParameterRowViewModel row in Parameters)
            ApplyTextSuggestions(SelectedNode, row);

        if (committedChanges.Count > 0)
            _parametersCommitted?.Invoke(SelectedNode, committedChanges);
    }

    private void OpenSelectedJoinInErDiagram()
    {
        if (!CanOpenSelectedJoinInErDiagram)
            return;

        _openSelectedJoinInErDiagram?.Invoke();
    }

    private void RefineAutoProjection()
    {
        if (!CanRefineAutoProjection)
            return;

        _refineAutoProjection?.Invoke();
    }

    private void ResetAutoProjection()
    {
        if (!CanResetAutoProjection)
            return;

        _resetAutoProjection?.Invoke();
    }

    private void AddSuggestedFilter()
    {
        if (!CanAddSuggestedFilter)
            return;

        _addSuggestedFilter?.Invoke();
    }

    private void ApplySuggestedAggregation()
    {
        if (!CanApplySuggestedAggregation)
            return;

        _applySuggestedAggregation?.Invoke();
    }

    // ── SQL Trace extraction ──────────────────────────────────────────────────

    private static (string? context, string? fragment) ExtractTrace(NodeViewModel node, string sql)
    {
        switch (node.Type)
        {
            case NodeType.TableSource:
            {
                string name = node.Title.Trim();
                Match m = Regex.Match(sql,
                    $@"(?:FROM|JOIN)\s+({Regex.Escape(name)}(?:\s+\w+)?)",
                    RegexOptions.IgnoreCase);
                if (m.Success)
                    return ("Source table in FROM / JOIN clause", m.Value.Trim());
                return ("Source table", $"Table: {name}");
            }
            case NodeType.RowSetJoin:
                return ("RowSet join", "rowset LEFT/INNER/RIGHT/FULL JOIN rowset ON condition");
            case NodeType.RowSetFilter:
                return ("RowSet filter", "WHERE condition(s) over input rowset");
            case NodeType.RowSetAggregate:
                return ("RowSet aggregate", "GROUP BY + aggregate metrics over input rowset");
            case NodeType.CteSource:
                return ("CTE source", "FROM CTERef AS alias");
            case NodeType.CteDefinition:
                return ("CTE definition", "WITH CTERef AS (SELECT ... FROM source_table)");
            case NodeType.CompileWhere:
            {
                Match m = Regex.Match(sql,
                    @"WHERE\s+(.+?)(?=\s+(?:GROUP\s+BY|ORDER\s+BY|LIMIT|HAVING|$))",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (m.Success)
                {
                    string cond = m.Groups[1].Value.Trim();
                    if (cond.Length > 80) cond = cond[..77] + "...";
                    return ("Filters applied in WHERE clause", $"WHERE {cond}");
                }
                break;
            }
            case NodeType.Top:
            {
                Match m = Regex.Match(sql, @"LIMIT\s+\d+|TOP\s+\d+", RegexOptions.IgnoreCase);
                if (m.Success)
                    return ("Row count limit", m.Value.Trim());
                break;
            }
            case NodeType.ResultOutput:
            {
                Match m = Regex.Match(sql,
                    @"SELECT\s+(.+?)\s+FROM",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (m.Success)
                {
                    string cols = m.Groups[1].Value.Trim();
                    if (cols.Length > 80) cols = cols[..77] + "...";
                    return ("Final SELECT output columns", $"SELECT {cols}");
                }
                break;
            }
            case NodeType.ColumnList:
            case NodeType.ColumnSetBuilder:
            case NodeType.ColumnSetMerge:
            {
                Match m = Regex.Match(sql,
                    @"SELECT\s+(.+?)\s+FROM",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (m.Success)
                {
                    string cols = m.Groups[1].Value.Trim();
                    if (cols.Length > 80) cols = cols[..77] + "...";
                    return ("Column selection list", $"SELECT {cols}");
                }
                break;
            }
            case NodeType.CountStar:
                return ("Aggregate function", "COUNT(*)");
            case NodeType.ColumnRefCast:
                return ("Column cast", "CAST(column AS type)");
            case NodeType.ScalarFromColumn:
                return ("Scalar extraction", "Use a column reference as scalar expression");
            case NodeType.CountDistinct:
                return ("Aggregate function", "COUNT(DISTINCT ...)");
            case NodeType.Sum:
                return ("Aggregate function", "SUM(...)");
            case NodeType.Avg:
                return ("Aggregate function", "AVG(...)");
            case NodeType.Min:
                return ("Aggregate function", "MIN(...)");
            case NodeType.Max:
                return ("Aggregate function", "MAX(...)");
            case NodeType.StringAgg:
                return ("Aggregate function", "STRING_AGG(..., ', ')");
            case NodeType.WindowFunction:
                return ("Window function", "ROW_NUMBER/RANK/DENSE_RANK/NTILE/LAG/LEAD/FIRST_VALUE/LAST_VALUE OVER (...) ");
            case NodeType.And:
                return ("Logic gate", "... AND ...");
            case NodeType.Or:
                return ("Logic gate", "... OR ...");
            case NodeType.Not:
                return ("Logic gate", "NOT (...)");
            case NodeType.Equals:
                return ("Comparison predicate", "col = value");
            case NodeType.NotEquals:
                return ("Comparison predicate", "col <> value");
            case NodeType.GreaterThan:
                return ("Comparison predicate", "col > value");
            case NodeType.GreaterOrEqual:
                return ("Comparison predicate", "col >= value");
            case NodeType.LessThan:
                return ("Comparison predicate", "col < value");
            case NodeType.LessOrEqual:
                return ("Comparison predicate", "col <= value");
            case NodeType.Between:
                return ("Comparison predicate", "col BETWEEN a AND b");
            case NodeType.NotBetween:
                return ("Comparison predicate", "col NOT BETWEEN a AND b");
            case NodeType.IsNull:
                return ("Comparison predicate", "col IS NULL");
            case NodeType.IsNotNull:
                return ("Comparison predicate", "col IS NOT NULL");
            case NodeType.Like:
                return ("Comparison predicate", "col LIKE pattern");
            case NodeType.NotLike:
                return ("Comparison predicate", "col NOT LIKE pattern");
            case NodeType.Upper:
                return ("String transform", "UPPER(col)");
            case NodeType.Lower:
                return ("String transform", "LOWER(col)");
            case NodeType.Trim:
                return ("String transform", "TRIM(col)");
            case NodeType.Concat:
                return ("String transform", "CONCAT(...)");
            case NodeType.Substring:
                return ("String transform", "SUBSTRING(col, start, length)");
            case NodeType.StringLength:
                return ("String transform", "LENGTH(col)");
            case NodeType.Replace:
                return ("String transform", "REPLACE(col, search, replace)");
            case NodeType.RegexMatch:
                return ("String transform", "col REGEXP pattern");
            case NodeType.RegexExtract or NodeType.RegexReplace:
                return ("String transform", "REGEXP_REPLACE(col, ...)");
            case NodeType.Round:
                return ("Math transform", "ROUND(col, decimals)");
            case NodeType.Abs:
                return ("Math transform", "ABS(col)");
            case NodeType.Ceil:
                return ("Math transform", "CEIL(col)");
            case NodeType.Floor:
                return ("Math transform", "FLOOR(col)");
            case NodeType.Add:
                return ("Math transform", "col + value");
            case NodeType.Subtract:
                return ("Math transform", "col - value");
            case NodeType.Multiply:
                return ("Math transform", "col * value");
            case NodeType.Divide:
                return ("Math transform", "col / value");
            case NodeType.Modulo:
                return ("Math transform", "col % value");
            case NodeType.DateAdd:
                return ("Date arithmetic", "DATEADD(unit, amount, date)");
            case NodeType.DateDiff:
                return ("Date arithmetic", "DATEDIFF(unit, start, end)");
            case NodeType.DatePart:
                return ("Date arithmetic", "DATEPART(unit, date)");
            case NodeType.DateFormat:
                return ("Date arithmetic", "FORMAT(date, pattern)");
            case NodeType.Cast:
                return ("Type cast", "CAST(col AS type)");
            case NodeType.Alias:
            {
                string alias = string.IsNullOrWhiteSpace(node.Alias) ? node.Title : node.Alias;
                return ("Column alias", $"col AS {alias}");
            }
            case NodeType.JsonExtract or NodeType.JsonValue:
                return ("JSON extract", "JSON_EXTRACT(col, path)");
            case NodeType.JsonArrayLength:
                return ("JSON function", "JSON_ARRAY_LENGTH(col)");
            case NodeType.Case:
                return ("Conditional", "CASE WHEN ... THEN ... END");
            case NodeType.NullFill:
                return ("Conditional", "COALESCE(col, fallback)");
            case NodeType.EmptyFill:
                return ("Conditional", "COALESCE(NULLIF(TRIM(col), ''), fallback)");
            case NodeType.ValueMap:
                return ("Conditional", "CASE WHEN col = src THEN dst ELSE col END");
            case NodeType.ValueNumber or NodeType.ValueString
                or NodeType.ValueDateTime or NodeType.ValueBoolean:
                return ("Literal value", $"Value: {node.Title}");
            case NodeType.SystemDate or NodeType.SystemDateTime:
                return ("System date/time", "CURRENT_TIMESTAMP / NOW() / GETDATE()");
            case NodeType.CurrentDate:
                return ("System date", "CURRENT_DATE / CURDATE() / CAST(GETDATE() AS DATE)");
            case NodeType.CurrentTime:
                return ("System time", "CURRENT_TIME / CURTIME() / CAST(GETDATE() AS TIME)");
        }
        return (null, null);
    }
}
