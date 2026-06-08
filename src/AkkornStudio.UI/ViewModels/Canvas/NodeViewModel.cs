using System.Collections.ObjectModel;
using System.Data;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Material.Icons;
using AkkornStudio.Core;
using AkkornStudio.CanvasKit;
using AkkornStudio.Nodes;
using AkkornStudio.Nodes.Pins;
using AkkornStudio.UI.Serialization;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.Services;
using AkkornStudio.UI.Services.Theming;

namespace AkkornStudio.UI.ViewModels;

/// <summary>
/// Represents a Node in the visual query builder canvas.
/// Nodes are the fundamental building blocks that transform, filter, and compose queries.
/// </summary>
public sealed class NodeViewModel : ViewModelBase, ICanvasTableNode, ICanvasLayerNode
{
    private const string AutoProjectionMarkerParameter = "__akkorn_auto_projection";
    private static readonly IReadOnlyList<string> JoinTypeOptionValues = [
        "INNER",
        "LEFT",
        "RIGHT",
        "FULL",
        "CROSS",
    ];

    private static readonly Dictionary<NodeCategory, Color> _headerColors = BuildHeaderColors();
    private static readonly Dictionary<NodeCategory, Color> _headerLightColors = BuildHeaderLightColors();
    private static readonly Dictionary<NodeCategory, LinearGradientBrush> _headerGradients = BuildHeaderGradients();

    private static readonly SolidColorBrush SelectedBorderBrush = new(Color.Parse(UiColorConstants.C_3B82F6));
    private static readonly SolidColorBrush ErrorBorderBrush = new(Color.Parse(UiColorConstants.C_EF4444));
    private static readonly SolidColorBrush WarningBorderBrush = new(Color.Parse(UiColorConstants.C_FBBF24));
    private static readonly SolidColorBrush OrphanBorderBrush = new(Color.Parse(UiColorConstants.C_6B7280));
    private static readonly SolidColorBrush HighlightBorderBrush = new(Color.Parse(UiColorConstants.C_14B8A6));
    private static readonly SolidColorBrush DefaultBorderBrush = new(Color.Parse(UiColorConstants.C_252C3F));

    private static readonly BoxShadows SelectedShadow =
        BoxShadows.Parse($"0 0 0 2 {UiColorConstants.C_3B82F6}, 0 8 32 0 {UiColorConstants.C_60_3B82F6}");
    private static readonly BoxShadows ErrorShadow =
        BoxShadows.Parse($"0 0 0 2 {UiColorConstants.C_EF4444}, 0 4 16 0 {UiColorConstants.C_40_EF4444}");
    private static readonly BoxShadows WarningShadow =
        BoxShadows.Parse($"0 0 0 1 {UiColorConstants.C_FBBF24}, 0 4 12 0 {UiColorConstants.C_30_FBBF24}");
    private static readonly BoxShadows OrphanShadow =
        BoxShadows.Parse($"0 2 8 0 {UiColorConstants.C_20_6B7280}");
    private static readonly BoxShadows HighlightShadow =
        BoxShadows.Parse($"0 0 0 2 {UiColorConstants.C_14B8A6}, 0 0 22 0 {UiColorConstants.C_60_14B8A6}");
    private static readonly BoxShadows DefaultShadow =
        BoxShadows.Parse($"0 4 24 0 {UiColorConstants.C_00000040}, 0 1 4 0 {UiColorConstants.C_00000050}");

    private Point _position;
    private bool _isSelected,
        _isHighlighted,
        _isPrimaryFromSource,
        _isHovered,
        _isOrphan;
    private string? _alias;
    private string? _comparisonInlineSummary;
    private double _width = 220;
    private int _zOrder;
    private List<ValidationIssue> _validationIssues = [];
    private PinDataType? _comparisonExpectedScalarType;

    /// <summary>Unique identifier for this node instance.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>The type of this node (TableSource, Filter, Join, etc.).</summary>
    public NodeType Type { get; }

    /// <summary>The category this node belongs to (DataSource, Transform, etc.).</summary>
    public NodeCategory Category { get; }

    /// <summary>Display name of this node type.</summary>
    public string Title { get; }

    /// <summary>Longer description/subtitle for context.</summary>
    public string Subtitle { get; }

    string? ICanvasTableNode.FullName => Subtitle;
    string? ICanvasTableNode.Title => Title;

    /// <summary>Dictionary of configurable parameters specific to this node type.</summary>
    public Dictionary<string, string> Parameters { get; } = [];

    /// <summary>Dictionary of inline literal values for pins (used in serialization).</summary>
    public Dictionary<string, string> PinLiterals { get; } = [];

    /// <summary>Collection of input pins (data flows INTO these).</summary>
    public ObservableCollection<PinViewModel> InputPins { get; } = [];

    /// <summary>Collection of output pins (data flows FROM these).</summary>
    public ObservableCollection<PinViewModel> OutputPins { get; } = [];

    /// <summary>Ordered list of output columns for ResultOutput nodes.</summary>
    public ObservableCollection<OutputColumnEntry> OutputColumnOrder { get; } = [];

    /// <summary>Convenience: all pins (input + output).</summary>
    public IEnumerable<PinViewModel> AllPins => InputPins.Concat(OutputPins);

    public PinDataType? ComparisonExpectedScalarType
    {
        get => _comparisonExpectedScalarType;
        set
        {
            if (!Set(ref _comparisonExpectedScalarType, value))
                return;

            foreach (PinViewModel input in InputPins)
            {
                if (input.DataType != PinDataType.ColumnRef || input.ColumnRefMeta is not null)
                    continue;

                input.NotifyExpectedColumnScalarTypeChanged();
            }
        }
    }

    // ── Type predicates ──────────────────────────────────────────────────────

    /// <summary>True if this node is the final result output.</summary>
    public bool IsResultOutput => Type == NodeType.ResultOutput;

    /// <summary>True when this ResultOutput was auto-generated from the ER flow.</summary>
    public bool IsAutoProjectionResultOutput =>
        IsResultOutput
        && string.Equals(
            Parameters.GetValueOrDefault(AutoProjectionMarkerParameter),
            "true",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>True if this node is a ColumnList (multiple column selector).</summary>
    public bool IsColumnList => Type == NodeType.ColumnList;

    /// <summary>True when this node presents the table-definition structured layout.</summary>
    public bool UsesTableDefinitionLayout =>
        InputPins.Any(p => p.Name == "column") && InputPins.Any(p => p.Name == "constraint");

    /// <summary>True when this node can open the view subcanvas editor.</summary>
    public bool CanOpenViewSubcanvas =>
        Parameters.ContainsKey("ViewName") && InputPins.Any(p => p.Name == "query");

    /// <summary>True when this node can open the subquery subcanvas editor.</summary>
    public bool CanOpenSubquerySubcanvas =>
        Type is NodeType.Subquery or NodeType.SubqueryReference or NodeType.SubqueryDefinition;

    /// <summary>True if this is a logic gate (AND or OR).</summary>
    public bool IsLogicGate => Type is NodeType.And or NodeType.Or;

    /// <summary>True if this is a window function node.</summary>
    public bool IsWindowFunction => Type == NodeType.WindowFunction;

    /// <summary>True when this node is an explicit JOIN node.</summary>
    public bool IsJoin => Type == NodeType.Join;

    /// <summary>Available JOIN type options for inline node selector.</summary>
    public IReadOnlyList<string> JoinTypeOptions => JoinTypeOptionValues;

    /// <summary>Selected JOIN type in node UI.</summary>
    public string JoinTypeSelection
    {
        get
        {
            if (Parameters.TryGetValue("join_type", out string? jt) && !string.IsNullOrWhiteSpace(jt))
                return jt;
            return "INNER";
        }
        set
        {
            string normalized = string.IsNullOrWhiteSpace(value)
                ? "INNER"
                : value.Trim().ToUpperInvariant();
            if (!JoinTypeOptionValues.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                normalized = "INNER";

            Parameters["join_type"] = normalized;
            RaisePropertyChanged(nameof(JoinTypeSelection));
        }
    }

    /// <summary>True when a PARTITION slot can be removed safely.</summary>
    public bool CanRemoveWindowPartitionSlot =>
        IsWindowFunction && HasRemovableDynamicInputSlot("partition_");

    /// <summary>True when an ORDER slot can be removed safely.</summary>
    public bool CanRemoveWindowOrderSlot =>
        IsWindowFunction && HasRemovableDynamicInputSlot("order_");

    /// <summary>True if this is a value literal node (Number, String, DateTime, Boolean).</summary>
    public bool IsValueNode =>
        Type
            is NodeType.ValueNumber
                or NodeType.ValueString
                or NodeType.ValueDateTime
                or NodeType.ValueBoolean
                or NodeType.SystemDate
                or NodeType.SystemDateTime
                or NodeType.CurrentDate
                or NodeType.CurrentTime;

    /// <summary>True if this is a ScalarTypeDefinition node.</summary>
    public bool IsScalarTypeDefinition => Type == NodeType.ScalarTypeDefinition;

    /// <summary>True if this is a DDL ColumnDefinition node.</summary>
    public bool IsDdlColumnDefinition => Type == NodeType.ColumnDefinition;

    public string ColumnDefinitionDisplayName
    {
        get
        {
            if (Parameters.TryGetValue("ColumnName", out string? columnName)
                && !string.IsNullOrWhiteSpace(columnName))
                return columnName.Trim();

            return Title;
        }
    }

    public string ColumnDefinitionTypeLabel
    {
        get
        {
            if (Parameters.TryGetValue("ResolvedDataTypeDisplay", out string? resolved)
                && !string.IsNullOrWhiteSpace(resolved))
                return resolved.Trim();

            if (Parameters.TryGetValue("DataType", out string? configured)
                && !string.IsNullOrWhiteSpace(configured))
                return configured.Trim();

            return "INT";
        }
    }

    public string ColumnDefinitionNullabilityLabel
    {
        get
        {
            bool isNullable = Parameters.TryGetValue("IsNullable", out string? nullableText)
                && (string.Equals(nullableText, "true", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(nullableText, "1", StringComparison.OrdinalIgnoreCase));

            return isNullable ? "NULL" : "NOT NULL";
        }
    }

    /// <summary>
    /// Inline type label for the ScalarTypeDefinition node card,
    /// e.g. "VARCHAR(255)", "DECIMAL(18,2)", "TEXT".
    /// </summary>
    public string ScalarTypeInlineLabel
    {
        get
        {
            string typeKind = Parameters.TryGetValue("TypeKind", out string? k) && !string.IsNullOrWhiteSpace(k)
                ? k.Trim().ToUpperInvariant()
                : "VARCHAR";

            return typeKind switch
            {
                "VARCHAR" => Parameters.TryGetValue("Length", out string? l) && int.TryParse(l, out int len)
                    ? $"VARCHAR({Math.Max(1, len)})"
                    : "VARCHAR(255)",
                "DECIMAL" => Parameters.TryGetValue("Precision", out string? p) && int.TryParse(p, out int prec)
                    && Parameters.TryGetValue("Scale", out string? s) && int.TryParse(s, out int scale)
                    ? $"DECIMAL({Math.Max(1, prec)},{Math.Max(0, scale)})"
                    : "DECIMAL(18,2)",
                _ => typeKind,
            };
        }
    }

    /// <summary>True when the standard left/right pin columns should be shown.</summary>
    public bool ShowStandardPins =>
        !IsValueNode && !IsColumnList && !IsResultOutput && !UsesTableDefinitionLayout;

    /// <summary>
    /// True when the standard input band should be rendered.
    /// Hides the empty input area for source-like nodes that have no inputs.
    /// </summary>
    public bool ShowStandardInputBand => ShowStandardPins && InputPins.Count > 0;

    public string? ComparisonInlineSummary
    {
        get => _comparisonInlineSummary;
        private set
        {
            if (!Set(ref _comparisonInlineSummary, value))
                return;

            RaisePropertyChanged(nameof(HasComparisonInlineSummary));
        }
    }

    public bool HasComparisonInlineSummary => !string.IsNullOrWhiteSpace(ComparisonInlineSummary);

    public bool IsValueNumber => Type == NodeType.ValueNumber;
    public bool IsValueString => Type == NodeType.ValueString;
    public bool IsValueDateTime => Type == NodeType.ValueDateTime;
    public bool IsValueBoolean => Type == NodeType.ValueBoolean;

    /// <summary>Computed table projection rows for the table-definition visual template.</summary>
    public ObservableCollection<DdlTableColumnRowViewModel> TableDefinitionColumns { get; } = [];

    public bool HasTableDefinitionColumns => TableDefinitionColumns.Count > 0;

    public string TableDefinitionDisplayName
    {
        get
        {
            Parameters.TryGetValue("SchemaName", out string? schemaName);
            Parameters.TryGetValue("TableName", out string? tableName);

            string schema = string.IsNullOrWhiteSpace(schemaName) ? "public" : schemaName.Trim();
            string table = string.IsNullOrWhiteSpace(tableName) ? "<table>" : tableName.Trim();
            return $"{schema}.{table}";
        }
    }

    /// <summary>True if the "No inputs" placeholder should be displayed.</summary>
    public bool ShouldShowNoInputsPlaceholder =>
        InputPins.Count == 0 && !IsValueNode && !IsResultOutput && Type == NodeType.TableSource;

    /// <summary>The value of the literal, synchronized from Parameters["value"].</summary>
    public string ValueNodeText
    {
        get => Parameters.TryGetValue("value", out string? v) ? v : "";
        set
        {
            if (IsValueNode)
            {
                Parameters["value"] = value ?? "";
                RaisePropertyChanged(nameof(ValueNodeText));
            }
        }
    }

    // ── Visual properties ────────────────────────────────────────────────────

    /// <summary>Canvas position (top-left) of this node.</summary>
    public Point Position
    {
        get => _position;
        set => Set(ref _position, value);
    }

    /// <summary>True if this node is selected by the user.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            Set(ref _isSelected, value);
            RaisePropertyChanged(nameof(NodeBorderBrush));
            RaisePropertyChanged(nameof(NodeShadow));
        }
    }

    /// <summary>True when this node is highlighted by explain-plan table focus.</summary>
    public bool IsHighlighted
    {
        get => _isHighlighted;
        set
        {
            Set(ref _isHighlighted, value);
            RaisePropertyChanged(nameof(NodeBorderBrush));
            RaisePropertyChanged(nameof(NodeShadow));
        }
    }

    /// <summary>True when this node is the primary FROM source used by SQL compilation.</summary>
    public bool IsPrimaryFromSource
    {
        get => _isPrimaryFromSource;
        set => Set(ref _isPrimaryFromSource, value);
    }

    /// <summary>True if the user is hovering over this node.</summary>
    public bool IsHovered
    {
        get => _isHovered;
        set => Set(ref _isHovered, value);
    }

    /// <summary>True when this node does not contribute to the final query output.</summary>
    public bool IsOrphan
    {
        get => _isOrphan;
        set
        {
            Set(ref _isOrphan, value);
            RaisePropertyChanged(nameof(NodeBorderBrush));
            RaisePropertyChanged(nameof(NodeShadow));
            RaisePropertyChanged(nameof(NodeOpacity));
        }
    }

    /// <summary>Optional user-defined alias for this node.</summary>
    public string? Alias
    {
        get => _alias;
        set => Set(ref _alias, value);
    }

    /// <summary>Visual width of the node card.</summary>
    public double Width
    {
        get => _width;
        set => Set(ref _width, value);
    }

    /// <summary>Layer order inside canvas. Higher values render in front.</summary>
    public int ZOrder
    {
        get => _zOrder;
        set => Set(ref _zOrder, value);
    }

    /// <summary>Header color based on node category (for visual distinction).</summary>
    public Color HeaderColor =>
        _headerColors.TryGetValue(Category, out Color color)
            ? color
            : _headerColors[default];

    /// <summary>Lighter shade of HeaderColor for gradient.</summary>
    public Color HeaderColorLight =>
        _headerLightColors.TryGetValue(Category, out Color color)
            ? color
            : _headerLightColors[default];

    /// <summary>Gradient brush for the node header.</summary>
    public LinearGradientBrush HeaderGradient =>
        _headerGradients.TryGetValue(Category, out LinearGradientBrush? gradient)
            ? gradient
            : _headerGradients[default];

    // ── Validation state ─────────────────────────────────────────────────────

    /// <summary>List of validation issues (errors, warnings) for this node.</summary>
    public IReadOnlyList<ValidationIssue> ValidationIssues => _validationIssues;

    /// <summary>True if this node has any validation errors.</summary>
    public bool HasError => _validationIssues.Any(i => i.Severity == IssueSeverity.Error);

    /// <summary>True if this node has warnings (but no errors).</summary>
    public bool HasWarning =>
        !HasError && _validationIssues.Any(i => i.Severity == IssueSeverity.Warning);

    /// <summary>Formatted tooltip text showing all validation issues.</summary>
    public string? ValidationTooltip =>
        _validationIssues.Count > 0
            ? string.Join(
                "\n",
                _validationIssues.Select(i =>
                    $"{(i.Severity == IssueSeverity.Error ? "✕" : "⚠")} {i.Message}"
                    + (i.Suggestion is not null ? $"\n   → {i.Suggestion}" : "")
                )
            )
            : null;

    /// <summary>Set validation issues for this node (called by ValidationService).</summary>
    internal void SetValidation(IEnumerable<ValidationIssue> issues)
    {
        _validationIssues = [.. issues];
        RaisePropertyChanged(nameof(ValidationIssues));
        RaisePropertyChanged(nameof(HasError));
        RaisePropertyChanged(nameof(HasWarning));
        RaisePropertyChanged(nameof(ValidationTooltip));
        RaisePropertyChanged(nameof(NodeBorderBrush));
        RaisePropertyChanged(nameof(NodeShadow));
        RaisePropertyChanged(nameof(NodeOpacity));
    }

    // ── ResultOutput column ordering ─────────────────────────────────────────

    /// <summary>
    /// Rebuilds OutputColumnOrder from current canvas connections.
    /// Keeps user-defined order, appends new ones, removes deleted ones.
    /// </summary>
    internal void SyncOutputColumns(IEnumerable<ConnectionViewModel> allConnections)
    {
        if (!IsResultOutput)
            return;

        var incoming = allConnections.Where(c => c.ToPin?.Owner == this).ToList();
        var existingKeys = OutputColumnOrder.Select(e => e.Key).ToHashSet();
        var newKeys = incoming.Select(c => MakeKey(c.FromPin)).ToHashSet();

        // Remove orphaned entries
        foreach (OutputColumnEntry? entry in OutputColumnOrder.ToList())
            if (!newKeys.Contains(entry.Key))
                OutputColumnOrder.Remove(entry);

        // Append newly connected columns
        foreach (ConnectionViewModel? conn in incoming)
        {
            string key = MakeKey(conn.FromPin);
            if (!existingKeys.Contains(key))
            {
                string display =
                    conn.FromPin.Owner.Type == NodeType.TableSource
                        ? $"{conn.FromPin.Owner.Subtitle?.Split('.').Last() ?? conn.FromPin.Owner.Title}.{conn.FromPin.Name}"
                        : $"{conn.FromPin.Owner.Title} → {conn.FromPin.Name}";
                OutputColumnOrder.Add(
                    new OutputColumnEntry(
                        key,
                        display,
                        () => MoveColumnUp(key),
                        () => MoveColumnDown(key)
                    )
                );
            }
        }
    }

    internal void SyncCteSourceColumns(IEnumerable<ConnectionViewModel> allConnections)
    {
        if (Type != NodeType.CteSource)
            return;

        List<MaterializedSourceColumn> materializedColumns = BuildCteSourceColumns(allConnections);
        SyncMaterializedSourceOutputPins(materializedColumns, allConnections);
    }

    private List<MaterializedSourceColumn> BuildCteSourceColumns(IEnumerable<ConnectionViewModel> allConnections)
    {
        NodeViewModel? definition = ResolveCteDefinitionForMaterialization(allConnections);
        if (definition is null)
            return [];

        ConnectionViewModel? queryWire = allConnections.FirstOrDefault(c =>
            c.ToPin?.Owner == definition
            && c.ToPin.Name == "query"
            && c.FromPin.Owner.IsResultOutput);

        if (queryWire?.FromPin.Owner is NodeViewModel resultOutput)
            return BuildMaterializedColumnsFromResultOutput(resultOutput, allConnections);

        return BuildCteSourceColumnsFromPersistedDefinition(definition);
    }

    private NodeViewModel? ResolveCteDefinitionForMaterialization(IEnumerable<ConnectionViewModel> allConnections)
    {
        ConnectionViewModel? definitionWire = allConnections.FirstOrDefault(c =>
            c.ToPin?.Owner == this
            && c.ToPin.Name == "cte"
            && c.FromPin.Owner.Type == NodeType.CteDefinition);
        if (definitionWire?.FromPin.Owner is NodeViewModel connectedDefinition)
            return connectedDefinition;

        string? cteName = ResolveTextInput(this, "cte_name_text", allConnections);
        if (string.IsNullOrWhiteSpace(cteName)
            && Parameters.TryGetValue("cte_name", out string? configured)
            && !string.IsNullOrWhiteSpace(configured))
        {
            cteName = configured.Trim();
        }

        if (string.IsNullOrWhiteSpace(cteName))
            return null;

        return allConnections
            .SelectMany(connection => EnumerateConnectedOwners(connection))
            .FirstOrDefault(node =>
                node.Type == NodeType.CteDefinition
                && string.Equals(ResolveCteDefinitionName(node), cteName, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<NodeViewModel> EnumerateConnectedOwners(ConnectionViewModel connection)
    {
        yield return connection.FromPin.Owner;
        if (connection.ToPin is not null)
            yield return connection.ToPin.Owner;
    }

    private static string? ResolveCteDefinitionName(NodeViewModel definition)
    {
        if (definition.Parameters.TryGetValue("name", out string? name)
            && !string.IsNullOrWhiteSpace(name))
        {
            return name.Trim();
        }

        if (definition.Parameters.TryGetValue("cte_name", out string? legacyName)
            && !string.IsNullOrWhiteSpace(legacyName))
        {
            return legacyName.Trim();
        }

        return null;
    }

    private List<MaterializedSourceColumn> BuildMaterializedColumnsFromResultOutput(
        NodeViewModel resultOutput,
        IEnumerable<ConnectionViewModel> allConnections)
    {
        var materialized = new List<MaterializedSourceColumn>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (ConnectionViewModel connection in allConnections.Where(c =>
                     c.ToPin?.Owner == resultOutput
                     && (c.ToPin.Name == "column" || c.ToPin.Name == "columns")))
        {
            AddProjectionFromPin(materialized, seen, connection.FromPin, allConnections);
        }

        return materialized;
    }

    private void AddProjectionFromPin(
        ICollection<MaterializedSourceColumn> materialized,
        ISet<string> seen,
        PinViewModel sourcePin,
        IEnumerable<ConnectionViewModel> allConnections)
    {
        if (IsProjectionContainerPin(sourcePin))
        {
            foreach (ConnectionViewModel nested in allConnections.Where(c =>
                         c.ToPin?.Owner == sourcePin.Owner
                         && IsProjectionContainerInputPin(sourcePin.Owner, c.ToPin.Name)))
            {
                AddProjectionFromPin(materialized, seen, nested.FromPin, allConnections);
            }

            return;
        }

        if (IsWildcardProjectionPin(sourcePin))
        {
            if (sourcePin.ColumnSetMeta?.Columns is { Count: > 0 } wildcardColumns)
            {
                foreach (ColumnRefMeta column in wildcardColumns)
                {
                    string wildcardColumnName = column.ColumnName;
                    if (string.IsNullOrWhiteSpace(wildcardColumnName) || !seen.Add(wildcardColumnName))
                        continue;

                    materialized.Add(new MaterializedSourceColumn(wildcardColumnName, column.ScalarType));
                }
            }

            foreach (PinViewModel expandedPin in sourcePin.Owner.OutputPins.Where(p =>
                         p.Direction == PinDirection.Output
                         && p.EffectiveDataType == PinDataType.ColumnRef))
            {
                AddProjectionFromPin(materialized, seen, expandedPin, allConnections);
            }

            return;
        }

        string columnName = ResolveProjectedColumnName(sourcePin, allConnections);
        if (string.IsNullOrWhiteSpace(columnName) || !seen.Add(columnName))
            return;

        PinDataType scalarType = ResolveProjectedScalarType(sourcePin);
        materialized.Add(new MaterializedSourceColumn(columnName, scalarType));
    }

    private void SyncMaterializedSourceOutputPins(
        IReadOnlyList<MaterializedSourceColumn> columns,
        IEnumerable<ConnectionViewModel> allConnections)
    {
        HashSet<string> connectedPinNames = allConnections
            .Where(c => c.FromPin.Owner == this)
            .Select(c => c.FromPin.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        HashSet<string> desiredColumnNames = columns
            .Select(column => column.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (PinViewModel pin in OutputPins
                     .Where(p => p.Name is not "result" and not "*"
                         && !desiredColumnNames.Contains(p.Name)
                         && !connectedPinNames.Contains(p.Name))
                     .ToList())
        {
            OutputPins.Remove(pin);
        }

        foreach (MaterializedSourceColumn column in columns)
        {
            if (OutputPins.Any(p => string.Equals(p.Name, column.Name, StringComparison.OrdinalIgnoreCase)))
                continue;

            PinViewModel pin = CreatePinViewModel(
                new PinDescriptor(
                    column.Name,
                    PinDirection.Output,
                    PinDataType.ColumnRef,
                    ColumnRefMeta: new ColumnRefMeta(
                        ColumnName: column.Name,
                        TableAlias: ResolveMaterializedSourceAlias(allConnections),
                        ScalarType: column.ScalarType,
                        IsNullable: true)));

            OutputPins.Add(pin);
        }

        PinViewModel? wildcardPin = OutputPins.FirstOrDefault(p => p.Name == "*");
        if (wildcardPin is null || (!connectedPinNames.Contains("*") && wildcardPin.ColumnSetMeta?.Columns.Count != columns.Count))
        {
            if (wildcardPin is not null && !connectedPinNames.Contains("*"))
                OutputPins.Remove(wildcardPin);

            if (wildcardPin is null || !connectedPinNames.Contains("*"))
            {
                ColumnSetMeta? columnSetMeta = columns.Count == 0
                    ? null
                    : new ColumnSetMeta(columns
                        .Select(column => new ColumnRefMeta(
                            column.Name,
                            ResolveMaterializedSourceAlias(allConnections),
                            column.ScalarType,
                            true))
                        .ToList());

                OutputPins.Add(CreatePinViewModel(
                    new PinDescriptor(
                        "*",
                        PinDirection.Output,
                        PinDataType.ColumnSet,
                        IsRequired: false,
                        Description: "All columns from this CTE source",
                        ColumnSetMeta: columnSetMeta)));
            }
        }

        foreach (PinViewModel pin in OutputPins.Where(p => p.Direction == PinDirection.Output))
            pin.IsConnected = connectedPinNames.Contains(pin.Name);
    }

    private string ResolveProjectedColumnName(PinViewModel sourcePin, IEnumerable<ConnectionViewModel> allConnections)
    {
        if (sourcePin.Owner.Type == NodeType.Alias)
        {
            string? aliasFromInput = ResolveTextInput(sourcePin.Owner, "alias_text", allConnections);
            if (!string.IsNullOrWhiteSpace(aliasFromInput))
                return aliasFromInput;

            if (sourcePin.Owner.Parameters.TryGetValue("alias", out string? alias)
                && !string.IsNullOrWhiteSpace(alias))
            {
                return alias.Trim();
            }
        }

        return sourcePin.Name;
    }

    private static PinDataType ResolveProjectedScalarType(PinViewModel pin)
    {
        if (pin.ColumnRefMeta is not null)
            return pin.ColumnRefMeta.ScalarType;

        if (pin.ExpectedColumnScalarType is not null)
            return pin.ExpectedColumnScalarType.Value;

        return pin.EffectiveDataType switch
        {
            PinDataType.Text or PinDataType.Integer or PinDataType.Decimal or PinDataType.Number
                or PinDataType.Boolean or PinDataType.DateTime or PinDataType.Json => pin.EffectiveDataType,
            _ => PinDataType.Expression,
        };
    }

    internal PinDataType? ResolveExpectedScalarTypeForPin(PinViewModel pin)
    {
        if (pin.Direction != PinDirection.Input
            || pin.DataType != PinDataType.ColumnRef
            || pin.ColumnRefMeta is not null)
        {
            return null;
        }

        return Type is NodeType.Equals
            or NodeType.NotEquals
            or NodeType.GreaterThan
            or NodeType.GreaterOrEqual
            or NodeType.LessThan
            or NodeType.LessOrEqual
            or NodeType.Between
            or NodeType.NotBetween
            ? ComparisonExpectedScalarType
            : null;
    }

    private string? ResolveMaterializedSourceAlias(IEnumerable<ConnectionViewModel> allConnections)
    {
        string? aliasFromInput = ResolveTextInput(this, "alias_text", allConnections);
        if (!string.IsNullOrWhiteSpace(aliasFromInput))
            return aliasFromInput;

        if (Parameters.TryGetValue("alias", out string? alias)
            && !string.IsNullOrWhiteSpace(alias))
        {
            return alias.Trim();
        }

        ConnectionViewModel? definitionWire = allConnections.FirstOrDefault(c =>
            c.ToPin?.Owner == this
            && c.ToPin.Name == "cte"
            && c.FromPin.Owner.Type == NodeType.CteDefinition);

        NodeViewModel? definition = definitionWire?.FromPin.Owner;
        string? definitionName = ResolveTextInput(definition, "name_text", allConnections);
        if (!string.IsNullOrWhiteSpace(definitionName))
            return definitionName;

        if (definition is not null)
        {
            if (definition.Parameters.TryGetValue("name", out string? name)
                && !string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }

            if (definition.Parameters.TryGetValue("cte_name", out string? legacyName)
                && !string.IsNullOrWhiteSpace(legacyName))
            {
                return legacyName.Trim();
            }
        }

        return Title;
    }

    private static string? ResolveTextInput(
        NodeViewModel? node,
        string pinName,
        IEnumerable<ConnectionViewModel> allConnections)
    {
        if (node is null)
            return null;

        ConnectionViewModel? wire = allConnections.FirstOrDefault(c =>
            c.ToPin?.Owner == node
            && c.ToPin.Name == pinName);

        if (wire is null)
        {
            if (node.PinLiterals.TryGetValue(pinName, out string? literal)
                && !string.IsNullOrWhiteSpace(literal))
            {
                return literal.Trim().Trim('\'', '"').Trim();
            }

            return null;
        }

        if (wire.FromPin.Owner.Parameters.TryGetValue("value", out string? value)
            && !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        return null;
    }

    internal void SyncComparisonInlineSummary(IEnumerable<ConnectionViewModel> allConnections)
    {
        ComparisonInlineSummary = BuildComparisonInlineSummary(allConnections);
    }

    private string? BuildComparisonInlineSummary(IEnumerable<ConnectionViewModel> allConnections)
    {
        if (!SupportsBinaryComparisonInlineSummary())
            return null;

        string left = ResolveComparisonOperandDisplay("left", allConnections);
        string right = ResolveComparisonOperandDisplay("right", allConnections);
        return $"{left} {ResolveComparisonOperatorSymbol()} {right}";
    }

    private bool SupportsBinaryComparisonInlineSummary() =>
        Type is NodeType.Equals
            or NodeType.NotEquals
            or NodeType.GreaterThan
            or NodeType.GreaterOrEqual
            or NodeType.LessThan
            or NodeType.LessOrEqual;

    private string ResolveComparisonOperandDisplay(
        string pinName,
        IEnumerable<ConnectionViewModel> allConnections)
    {
        ConnectionViewModel? wire = allConnections.FirstOrDefault(c =>
            c.ToPin?.Owner == this
            && string.Equals(c.ToPin.Name, pinName, StringComparison.OrdinalIgnoreCase));

        if (wire is not null)
            return DescribeComparisonSourcePin(wire.FromPin);

        if (PinLiterals.TryGetValue(pinName, out string? literal)
            && !string.IsNullOrWhiteSpace(literal))
        {
            return FormatComparisonLiteral(literal);
        }

        return $"<{pinName}>";
    }

    private static string DescribeComparisonSourcePin(PinViewModel fromPin)
    {
        if (fromPin.ColumnRefMeta is not null)
        {
            string qualifier = string.IsNullOrWhiteSpace(fromPin.ColumnRefMeta.TableAlias)
                ? fromPin.Owner.Title
                : fromPin.ColumnRefMeta.TableAlias.Trim();
            return string.IsNullOrWhiteSpace(qualifier)
                ? fromPin.ColumnRefMeta.ColumnName
                : $"{qualifier}.{fromPin.ColumnRefMeta.ColumnName}";
        }

        if (fromPin.Owner.IsValueNode
            && fromPin.Owner.Parameters.TryGetValue("value", out string? value)
            && !string.IsNullOrWhiteSpace(value))
        {
            return FormatComparisonLiteral(value);
        }

        return $"{fromPin.Owner.Title}.{fromPin.Name}";
    }

    private string ResolveComparisonOperatorSymbol() =>
        Type switch
        {
            NodeType.Equals => "=",
            NodeType.NotEquals => "<>",
            NodeType.GreaterThan => ">",
            NodeType.GreaterOrEqual => ">=",
            NodeType.LessThan => "<",
            NodeType.LessOrEqual => "<=",
            _ => "=",
        };

    private static string FormatComparisonLiteral(string rawLiteral)
    {
        string normalized = rawLiteral
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        if (string.IsNullOrWhiteSpace(normalized))
            return "''";

        const int maxPreviewLength = 48;
        if (normalized.Length <= maxPreviewLength)
            return normalized;

        return normalized[..(maxPreviewLength - 3)] + "...";
    }

    private static bool IsProjectionInputPinName(string pinName) =>
        string.Equals(pinName, "columns", StringComparison.OrdinalIgnoreCase)
        || string.Equals(pinName, "metadata", StringComparison.OrdinalIgnoreCase);

    private static bool IsWildcardProjectionPin(PinViewModel pin) =>
        pin.EffectiveDataType == PinDataType.ColumnSet
        && string.Equals(pin.Name, "*", StringComparison.OrdinalIgnoreCase);

    private readonly record struct MaterializedSourceColumn(string Name, PinDataType ScalarType);

    private List<MaterializedSourceColumn> BuildCteSourceColumnsFromPersistedDefinition(NodeViewModel definition)
    {
        if (!definition.Parameters.TryGetValue(CanvasSerializer.CteSubgraphParameterKey, out string? payload)
            || string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        SavedCteSubgraph? subgraph;
        try
        {
            subgraph = JsonSerializer.Deserialize<SavedCteSubgraph>(payload);
        }
        catch
        {
            return [];
        }

        if (subgraph is null || subgraph.Nodes.Count == 0)
            return [];

        var tempCanvas = new CanvasViewModel();
        var tempSavedCanvas = new SavedCanvas(
            Version: CanvasSerializer.CurrentCanvasSchemaVersion,
            DatabaseProvider: null,
            ConnectionName: null,
            Zoom: 1,
            PanX: 0,
            PanY: 0,
            Nodes: subgraph.Nodes,
            Connections: subgraph.Connections,
            SelectBindings: [],
            WhereBindings: [],
            AppVersion: CanvasSerializer.AppVersion,
            CreatedAt: DateTime.UtcNow.ToString("o"),
            Description: "temporary cte materialization");

        CanvasLoadResult loadResult = CanvasSerializer.Deserialize(
            JsonSerializer.Serialize(tempSavedCanvas),
            tempCanvas);
        if (!loadResult.Success)
            return [];

        NodeViewModel? resultOutput = ResolvePersistedCteResultOutput(tempCanvas.Nodes, subgraph.ResultOutputNodeId);
        if (resultOutput is null)
            return [];

        return BuildMaterializedColumnsFromResultOutput(resultOutput, tempCanvas.Connections);
    }

    private static bool IsProjectionContainerPin(PinViewModel pin) =>
        pin.Name == "result"
        && pin.Owner.Type is NodeType.ColumnList or NodeType.ColumnSetBuilder or NodeType.ColumnSetMerge;

    private static bool IsProjectionContainerInputPin(NodeViewModel owner, string pinName)
    {
        if (owner.Type == NodeType.ColumnSetMerge)
            return string.Equals(pinName, "sets", StringComparison.OrdinalIgnoreCase);

        return IsProjectionInputPinName(pinName);
    }

    private static NodeViewModel? ResolvePersistedCteResultOutput(
        IEnumerable<NodeViewModel> nodes,
        string? preferredNodeId)
    {
        if (!string.IsNullOrWhiteSpace(preferredNodeId))
        {
            NodeViewModel? preferred = nodes.FirstOrDefault(n =>
                string.Equals(n.Id, preferredNodeId, StringComparison.OrdinalIgnoreCase)
                && n.IsResultOutput);
            if (preferred is not null)
                return preferred;
        }

        return nodes.FirstOrDefault(n => n.IsResultOutput);
    }

    private static string MakeKey(PinViewModel pin) => $"{pin.Owner.Id}::{pin.Name}";

    private void MoveColumnUp(string key)
    {
        int idx = IndexOf(key);
        if (idx > 0)
            OutputColumnOrder.Move(idx, idx - 1);
    }

    private void MoveColumnDown(string key)
    {
        int idx = IndexOf(key);
        if (idx >= 0 && idx < OutputColumnOrder.Count - 1)
            OutputColumnOrder.Move(idx, idx + 1);
    }

    private int IndexOf(string key)
    {
        for (int i = 0; i < OutputColumnOrder.Count; i++)
            if (OutputColumnOrder[i].Key == key)
                return i;
        return -1;
    }

    /// <summary>Returns ordered (nodeId, pinName) pairs for SQL compilation.</summary>
    public IReadOnlyList<(string NodeId, string PinName)> GetOrderedColumns() =>
        OutputColumnOrder
            .Select(e => e.Key.Split("::", 2))
            .Where(p => p.Length == 2)
            .Select(p => (p[0], p[1]))
            .ToList();

    // ── Border/shadow styling based on state ─────────────────────────────────

    /// <summary>Border brush color based on selection/validation state.</summary>
    public SolidColorBrush NodeBorderBrush =>
        IsHighlighted ? HighlightBorderBrush
        : IsSelected ? SelectedBorderBrush
        : HasError ? ErrorBorderBrush
        : HasWarning ? WarningBorderBrush
        : IsOrphan ? OrphanBorderBrush
        : DefaultBorderBrush;

    /// <summary>Shadow effects based on state.</summary>
    public BoxShadows NodeShadow =>
        IsHighlighted ? HighlightShadow
        : IsSelected ? SelectedShadow
        : HasError ? ErrorShadow
        : HasWarning ? WarningShadow
        : IsOrphan ? OrphanShadow
        : DefaultShadow;

    /// <summary>Opacity reduced for orphan nodes (visual signal).</summary>
    public double NodeOpacity => IsOrphan ? 0.45 : 1.0;

    /// <summary>Icon resource for this node's category.</summary>
    public string CategoryIcon => NodeIconCatalog.GetForCategory(Category);

    // ── Inline data preview (TableSource nodes only) ──────────────────────────

    private bool _showInlinePreview;
    private bool _isPreviewLoading;
    private DataTable? _inlinePreviewData;
    private string? _inlinePreviewError;

    /// <summary>True if this node supports inline data preview (TableSource only).</summary>
    public bool IsTableSource => Type == NodeType.TableSource;

    /// <summary>Whether the inline preview section is currently expanded.</summary>
    public bool ShowInlinePreview
    {
        get => _showInlinePreview;
        private set => Set(ref _showInlinePreview, value);
    }

    /// <summary>True while a sample query is in flight.</summary>
    public bool IsPreviewLoading
    {
        get => _isPreviewLoading;
        private set
        {
            Set(ref _isPreviewLoading, value);
            RaisePropertyChanged(nameof(HasInlinePreviewData));
            RaisePropertyChanged(nameof(HasInlinePreviewError));
        }
    }

    /// <summary>DataTable populated with up to 5 sample rows from DemoCatalog.</summary>
    public DataTable? InlinePreviewData
    {
        get => _inlinePreviewData;
        private set
        {
            Set(ref _inlinePreviewData, value);
            RaisePropertyChanged(nameof(HasInlinePreviewData));
        }
    }

    /// <summary>Error message when sample query fails.</summary>
    public string? InlinePreviewError
    {
        get => _inlinePreviewError;
        private set
        {
            Set(ref _inlinePreviewError, value);
            RaisePropertyChanged(nameof(HasInlinePreviewError));
        }
    }

    public bool HasInlinePreviewData  => _inlinePreviewData is { Rows.Count: > 0 } && !_isPreviewLoading;
    public bool HasInlinePreviewError => !string.IsNullOrEmpty(_inlinePreviewError) && !_isPreviewLoading;

    /// <summary>Toggle preview panel; auto-loads on first open.</summary>
    public async Task ToggleInlinePreviewAsync(ConnectionConfig? connectionConfig = null, CancellationToken cancellationToken = default)
    {
        ShowInlinePreview = !ShowInlinePreview;
        if (ShowInlinePreview && _inlinePreviewData is null && string.IsNullOrEmpty(_inlinePreviewError))
            await LoadInlinePreviewAsync(connectionConfig, cancellationToken);
    }

    private async Task LoadInlinePreviewAsync(ConnectionConfig? connectionConfig, CancellationToken cancellationToken)
    {
        IsPreviewLoading = true;
        InlinePreviewError = null;

        try
        {
            if (connectionConfig is not null)
            {
                DataTable? liveData = await TryLoadLiveInlinePreviewAsync(connectionConfig, cancellationToken);
                if (liveData is not null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => InlinePreviewData = liveData);
                    return;
                }
            }

            await Task.Delay(450, cancellationToken);   // fallback simulated preview

            // Safely handle null Subtitle and Title
            string tableName = Subtitle ?? Title ?? "Unknown";
            string tableShort = !string.IsNullOrEmpty(tableName)
                ? tableName.Split('.').Last().ToLowerInvariant()
                : "unknown";

            // Resolve schema from DemoCatalog or fall back to first entry if available
            var entry = InlinePreviewCatalog.FirstOrDefault(e =>
                e.TableName.Contains(tableShort, StringComparison.OrdinalIgnoreCase));

            if (entry is null && InlinePreviewCatalog.Length > 0)
                entry = InlinePreviewCatalog[0];

            if (entry is null)
            {
                InlinePreviewError = L("node.preview.noCatalog", "No catalog available");
                IsPreviewLoading = false;
                return;
            }

            var dt   = new DataTable();
            var rng  = new Random((Subtitle ?? Title ?? "seed").GetHashCode() ^ 0xA1B2);

            foreach (var col in entry.Columns)
                dt.Columns.Add(col.Name);

            for (int row = 1; row <= 5; row++)
            {
                var r = dt.NewRow();
                foreach (var col in entry.Columns)
                    r[col.Name] = col.Generator(row, rng);
                dt.Rows.Add(r);
            }

            await Dispatcher.UIThread.InvokeAsync(() => InlinePreviewData = dt);
        }
        catch (OperationCanceledException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                InlinePreviewError = L("node.preview.cancelled", "Preview cancelled"));
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => InlinePreviewError = ex.Message);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsPreviewLoading = false);
        }
    }

    private async Task<DataTable?> TryLoadLiveInlinePreviewAsync(ConnectionConfig connectionConfig, CancellationToken cancellationToken)
    {
        string tableReference = ResolveInlinePreviewTableReference(connectionConfig.Provider);
        if (string.IsNullOrWhiteSpace(tableReference))
            return null;

        string sql = $"SELECT * FROM {tableReference}";
        var queryExecutor = new QueryExecutorService();
        DataTable data = await queryExecutor.ExecuteQueryAsync(connectionConfig, sql, maxRows: 5, ct: cancellationToken);
        return data;
    }

    private string ResolveInlinePreviewTableReference(DatabaseProvider provider)
    {
        string source = !string.IsNullOrWhiteSpace(Subtitle) ? Subtitle : Title;
        string normalized = source.Trim();
        if (normalized.Length == 0)
            return normalized;

        if (!normalized.Contains('.'))
            return QuoteIdentifier(provider, normalized);

        string[] parts = normalized.Split('.', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return normalized;

        return $"{QuoteIdentifier(provider, parts[0])}.{QuoteIdentifier(provider, parts[1])}";
    }

    private static string QuoteIdentifier(DatabaseProvider provider, string identifier)
    {
        string trimmed = identifier.Trim();
        return provider switch
        {
            DatabaseProvider.MySql => $"`{trimmed.Replace("`", "``")}`",
            DatabaseProvider.SqlServer => $"[{trimmed.Replace("]", "]]")}]",
            _ => $"\"{trimmed.Replace("\"", "\"\"")}\"",
        };
    }

    // ── Static inline catalog (Northwind sample data) ────────────────────────

    private sealed record ColDef(string Name, Func<int, Random, object> Generator);

    private static string L(string key, string fallback)
    {
        string value = LocalizationService.Instance[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }
    private sealed record TableDef(string TableName, ColDef[] Columns);

    private static readonly string[] _statuses = ["ACTIVE", "SHIPPED", "PENDING", "CANCELLED"];
    private static readonly string[] _cities   = ["New York", "São Paulo", "London", "Berlin", "Tokyo"];
    private static readonly string[] _names    = ["Alice", "Bob", "Carol", "Dave", "Eve", "Frank"];
    private static readonly string[] _emails   = ["alice@ex.com", "bob@ex.com", "carol@ex.com", "dave@ex.com"];
    private static readonly string[] _products = ["Widget A", "Gadget B", "Gizmo C", "Doohickey D"];

    private static readonly TableDef[] InlinePreviewCatalog =
    [
        new("orders",
        [
            new("id",          (row, _)   => (object)row),
            new("customer_id", (row, rng) => rng.Next(1, 20)),
            new("status",      (_,   rng) => _statuses[rng.Next(_statuses.Length)]),
            new("total",       (_,   rng) => Math.Round(rng.NextDouble() * 4000 + 50, 2)),
            new("created_at",  (_,   rng) => DateTime.UtcNow.AddDays(-rng.Next(0, 365)).ToString("yyyy-MM-dd")),
        ]),
        new("customers",
        [
            new("id",         (row, _)   => (object)row),
            new("name",       (_,   rng) => _names[rng.Next(_names.Length)]),
            new("email",      (_,   rng) => _emails[rng.Next(_emails.Length)]),
            new("city",       (_,   rng) => _cities[rng.Next(_cities.Length)]),
            new("created_at", (_,   rng) => DateTime.UtcNow.AddDays(-rng.Next(0, 730)).ToString("yyyy-MM-dd")),
        ]),
        new("products",
        [
            new("id",       (row, _)   => (object)row),
            new("name",     (_,   rng) => _products[rng.Next(_products.Length)]),
            new("price",    (_,   rng) => Math.Round(rng.NextDouble() * 200 + 5, 2)),
            new("stock",    (_,   rng) => rng.Next(0, 500)),
        ]),
        new("default",
        [
            new("id",    (row, _)   => (object)row),
            new("name",  (_,   rng) => _names[rng.Next(_names.Length)]),
            new("value", (_,   rng) => rng.Next(1, 1000)),
        ]),
    ];

    private static Dictionary<NodeCategory, LinearGradientBrush> BuildHeaderGradients()
    {
        var map = new Dictionary<NodeCategory, LinearGradientBrush>();
        foreach (NodeCategory category in Enum.GetValues<NodeCategory>())
        {
            Color start = _headerColors.TryGetValue(category, out Color startColor)
                ? startColor
                : _headerColors[default];

            Color end = _headerLightColors.TryGetValue(category, out Color endColor)
                ? endColor
                : _headerLightColors[default];

            map[category] = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                [
                    new GradientStop(start, 0.0),
                    new GradientStop(end, 1.0),
                ],
            };
        }

        return map;
    }

    private static Dictionary<NodeCategory, Color> BuildHeaderColors() => new()
    {
        [NodeCategory.DataSource] = Color.Parse(UiColorConstants.C_0F766E),
        [NodeCategory.StringTransform] = Color.Parse(UiColorConstants.C_4338CA),
        [NodeCategory.MathTransform] = Color.Parse(UiColorConstants.C_B45309),
        [NodeCategory.TypeCast] = Color.Parse(UiColorConstants.C_7E22CE),
        [NodeCategory.Comparison] = Color.Parse(UiColorConstants.C_BE123C),
        [NodeCategory.LogicGate] = Color.Parse(UiColorConstants.C_C2410C),
        [NodeCategory.Json] = Color.Parse(UiColorConstants.C_6D28D9),
        [NodeCategory.Aggregate] = Color.Parse(UiColorConstants.C_15803D),
        [NodeCategory.Conditional] = Color.Parse(UiColorConstants.C_0E7490),
        [NodeCategory.ResultModifier] = Color.Parse(UiColorConstants.C_EC4899),
        [NodeCategory.Output] = Color.Parse(UiColorConstants.C_10B981),
        [NodeCategory.Literal] = Color.Parse(UiColorConstants.C_4A5568),
        [NodeCategory.Ddl] = Color.Parse(UiColorConstants.C_1D4ED8),
    };

    private static Dictionary<NodeCategory, Color> BuildHeaderLightColors() => new()
    {
        [NodeCategory.DataSource] = Color.Parse(UiColorConstants.C_14B8A6),
        [NodeCategory.StringTransform] = Color.Parse(UiColorConstants.C_818CF8),
        [NodeCategory.MathTransform] = Color.Parse(UiColorConstants.C_FBBF24),
        [NodeCategory.TypeCast] = Color.Parse(UiColorConstants.C_C084FC),
        [NodeCategory.Comparison] = Color.Parse(UiColorConstants.C_FB7185),
        [NodeCategory.LogicGate] = Color.Parse(UiColorConstants.C_FB923C),
        [NodeCategory.Json] = Color.Parse(UiColorConstants.C_A78BFA),
        [NodeCategory.Aggregate] = Color.Parse(UiColorConstants.C_4ADE80),
        [NodeCategory.Conditional] = Color.Parse(UiColorConstants.C_22D3EE),
        [NodeCategory.ResultModifier] = Color.Parse(UiColorConstants.C_FB7185),
        [NodeCategory.Output] = Color.Parse(UiColorConstants.C_34D399),
        [NodeCategory.Literal] = Color.Parse(UiColorConstants.C_9CA3AF),
        [NodeCategory.Ddl] = Color.Parse(UiColorConstants.C_60A5FA),
    };

    /// <summary>Material icon kind for category visualization.</summary>
    public MaterialIconKind CategoryIconKind => NodeIconCatalog.GetKindForCategory(Category);

    // ── Constructors ─────────────────────────────────────────────────────────

    /// <summary>
    /// Create a new node from a NodeDefinition.
    /// Initializes all standard pins from the definition.
    /// For ColumnList, adds the special "columns" input pin.
    /// </summary>
    public NodeViewModel(NodeDefinition def, Point pos)
    {
        Type = def.Type;
        Category = def.Category;
        Title = def.DisplayName;
        Subtitle = def.Description.Length > 40 ? def.Description[..37] + "…" : def.Description;
        Position = pos;

        // Initialize parameters with defaults
        foreach (NodeParameter p in def.Parameters)
            if (p.DefaultValue is not null)
                Parameters[p.Name] = p.DefaultValue;

        // Create pins from definition
        foreach (PinDescriptor pin in def.Pins)
        {
            PinViewModel vm = CreatePinViewModel(pin);
            if (pin.Direction == PinDirection.Input)
                InputPins.Add(vm);
            else
                OutputPins.Add(vm);
        }

        if (def.Type == NodeType.WindowFunction)
            EnsureWindowFunctionMinimumPins();

        if (def.Type is NodeType.Subquery or NodeType.SubqueryReference or NodeType.SubqueryDefinition)
            EnsureSubqueryInputMinimumPins();

        // ColumnList: add the "columns" input pin ready for connection
        if (def.Type == NodeType.ColumnList)
            InputPins.Add(
                CreatePinViewModel(
                    new PinDescriptor(
                        "columns",
                        PinDirection.Input,
                        PinDataType.ColumnRef,
                        IsRequired: false,
                        AllowMultiple: true,
                        Description: "Connect columns or expressions to include in the list"
                    ))
            );

        InputPins.CollectionChanged += (_, _) =>
        {
            RaisePropertyChanged(nameof(ShowStandardInputBand));
            RaisePropertyChanged(nameof(ShouldShowNoInputsPlaceholder));
            RaisePropertyChanged(nameof(UsesTableDefinitionLayout));
            RaisePropertyChanged(nameof(CanOpenViewSubcanvas));
            RaisePropertyChanged(nameof(CanOpenSubquerySubcanvas));
        };
    }

    /// <summary>
    /// Create a TableSource node from database metadata.
    /// Each column becomes an output pin.
    /// </summary>
    public NodeViewModel(string tableName, IEnumerable<(string n, PinDataType t)> cols, Point pos)
    {
        Type = NodeType.TableSource;
        Category = NodeCategory.DataSource;
        Title = tableName.Split('.').Last();
        Subtitle = tableName;
        Position = pos;
        foreach ((string n, PinDataType t) in cols)
        {
            PinViewModel pin = CreatePinViewModel(
                new PinDescriptor(
                    n,
                    PinDirection.Output,
                    PinDataType.ColumnRef,
                    ColumnRefMeta: new ColumnRefMeta(
                        ColumnName: n,
                        TableAlias: Title,
                        ScalarType: t,
                        IsNullable: true
                    )));

            OutputPins.Add(pin);
        }

        // Structural projection pin for SELECT * from this source.
        OutputPins.Add(
            CreatePinViewModel(
                new PinDescriptor(
                    "*",
                    PinDirection.Output,
                    PinDataType.ColumnSet,
                    IsRequired: false,
                    Description: "All columns from this source"
                ))
        );

        InputPins.CollectionChanged += (_, _) =>
        {
            RaisePropertyChanged(nameof(ShowStandardInputBand));
            RaisePropertyChanged(nameof(ShouldShowNoInputsPlaceholder));
            RaisePropertyChanged(nameof(UsesTableDefinitionLayout));
            RaisePropertyChanged(nameof(CanOpenViewSubcanvas));
        };
    }

    // ── Pin management ───────────────────────────────────────────────────────

    private PinViewModel CreatePinViewModel(PinDescriptor descriptor)
    {
        var owner = new PinModelOwner(Id, Type);
        var pinId = new PinId($"{Id}:{descriptor.Name}:{descriptor.Direction}");
        PinModel model = PinModelFactory.Create(
            pinId,
            descriptor,
            owner,
            descriptor.DataType,
            expectedColumnScalarType: null);
        return new PinViewModel(model, this);
    }

    /// <summary>Find a pin by name and optional direction.</summary>
    public PinViewModel? FindPin(string name, PinDirection? dir = null) =>
        AllPins.FirstOrDefault(p => p.Name == name && (dir is null || p.Direction == dir));

    /// <summary>Notify when a parameter changes (for binding).</summary>
    public void RaiseParameterChanged(string p)
    {
        RaisePropertyChanged($"Param_{p}");

        if (IsResultOutput && string.Equals(p, AutoProjectionMarkerParameter, StringComparison.OrdinalIgnoreCase))
            RaisePropertyChanged(nameof(IsAutoProjectionResultOutput));

        if (string.Equals(p, "ViewName", StringComparison.OrdinalIgnoreCase))
            RaisePropertyChanged(nameof(CanOpenViewSubcanvas));

        if (string.Equals(p, "SchemaName", StringComparison.OrdinalIgnoreCase)
            || string.Equals(p, "TableName", StringComparison.OrdinalIgnoreCase))
        {
            RaisePropertyChanged(nameof(TableDefinitionDisplayName));
        }

        if (Type == NodeType.Join
            && p.Equals("join_type", StringComparison.OrdinalIgnoreCase))
        {
            RaisePropertyChanged(nameof(JoinTypeSelection));
        }

        if (Type == NodeType.ScalarTypeDefinition
            && p is "TypeKind" or "Length" or "Precision" or "Scale")
        {
            RaisePropertyChanged(nameof(ScalarTypeInlineLabel));
        }

        if (Type == NodeType.ColumnDefinition
            && p is "ColumnName" or "DataType" or "IsNullable" or "ResolvedDataTypeDisplay")
        {
            RaisePropertyChanged(nameof(ColumnDefinitionDisplayName));
            RaisePropertyChanged(nameof(ColumnDefinitionTypeLabel));
            RaisePropertyChanged(nameof(ColumnDefinitionNullabilityLabel));
        }
    }

    // ── Dynamic pin synchronization ──────────────────────────────────────────

    /// <summary>
    /// Sync ColumnList pins: ensure "columns" input pin exists.
    /// </summary>
    internal void SyncColumnListPins(IEnumerable<ConnectionViewModel> _)
    {
        if (!IsColumnList)
            return;

        // Ensure the single input pin exists
        if (InputPins.FirstOrDefault(p => p.Name == "columns") is null)
        {
            InputPins.Add(
                CreatePinViewModel(
                    new PinDescriptor(
                        "columns",
                        PinDirection.Input,
                        PinDataType.ColumnRef,
                        IsRequired: false,
                        AllowMultiple: true,
                        Description: "Connect columns or expressions to include in the list"
                    ))
            );
        }
    }

    internal void ReplaceTableDefinitionColumns(IEnumerable<DdlTableColumnRowViewModel> rows)
    {
        TableDefinitionColumns.Clear();
        foreach (DdlTableColumnRowViewModel row in rows)
            TableDefinitionColumns.Add(row);

        RaisePropertyChanged(nameof(HasTableDefinitionColumns));
    }

    /// <summary>
    /// Sync AND/OR gate pins: single variadic "conditions" pin, no dynamic slots.
    /// </summary>
    internal void SyncLogicGatePins(IEnumerable<ConnectionViewModel> _)
    {
        if (!IsLogicGate)
            return;

        if (InputPins.Any(p => p.Name == "conditions"))
            return;

        InputPins.Insert(
            0,
            CreatePinViewModel(
                new PinDescriptor(
                    "conditions",
                    PinDirection.Input,
                    PinDataType.Boolean,
                    IsRequired: false,
                    AllowMultiple: true,
                    Description: "Connect one or more boolean conditions"
                ))
        );
    }

    /// <summary>
    /// Sync WindowFunction pins: manages variadic PARTITION/ORDER inputs.
    /// Keeps connected pins, removes disconnected slots, and leaves one empty slot.
    /// </summary>
    internal void SyncWindowFunctionPins(IEnumerable<ConnectionViewModel> c)
    {
        if (!IsWindowFunction)
            return;

        SyncWindowDynamicInputPins(
            "partition_",
            PinDataType.ColumnRef,
            "Connect a partition expression",
            c
        );
        SyncWindowDynamicInputPins(
            "order_",
            PinDataType.ColumnRef,
            "Connect an order expression",
            c
        );
        ReorderWindowFunctionInputPins();
        RaiseWindowSlotStateChanged();
    }

    internal void SyncSubqueryInputPins(IEnumerable<ConnectionViewModel> allConnections)
    {
        if (Type is not (NodeType.Subquery or NodeType.SubqueryReference or NodeType.SubqueryDefinition))
            return;

        SyncWindowDynamicInputPins(
            "input_",
            PinDataType.ColumnRef,
            "Connect a parent-scope expression to use inside subquery editor",
            allConnections);
    }

    /// <summary>Adds an explicit PARTITION BY input slot to WindowFunction.</summary>
    public void AddWindowPartitionSlot()
    {
        if (!IsWindowFunction)
            return;
        AddDynamicInputSlot("partition_", PinDataType.ColumnRef, "Connect a partition expression");
        ReorderWindowFunctionInputPins();
        RaiseWindowSlotStateChanged();
    }

    /// <summary>Removes the last unconnected PARTITION BY slot from WindowFunction.</summary>
    public void RemoveWindowPartitionSlot()
    {
        if (!IsWindowFunction)
            return;
        RemoveLastEmptyDynamicInputSlot("partition_");
        ReorderWindowFunctionInputPins();
        RaiseWindowSlotStateChanged();
    }

    /// <summary>Adds an explicit ORDER BY input slot to WindowFunction.</summary>
    public void AddWindowOrderSlot()
    {
        if (!IsWindowFunction)
            return;
        AddDynamicInputSlot("order_", PinDataType.ColumnRef, "Connect an order expression");
        ReorderWindowFunctionInputPins();
        RaiseWindowSlotStateChanged();
    }

    /// <summary>Removes the last unconnected ORDER BY slot from WindowFunction.</summary>
    public void RemoveWindowOrderSlot()
    {
        if (!IsWindowFunction)
            return;
        RemoveLastEmptyDynamicInputSlot("order_");
        ReorderWindowFunctionInputPins();
        RaiseWindowSlotStateChanged();
    }

    /// <summary>
    /// Shared implementation for dynamic pin management.
    /// Keeps connected pins, removes disconnected, always leaves one empty slot.
    /// </summary>
    private void SyncDynamicInputPins(
        string prefix,
        PinDataType slotType,
        IEnumerable<ConnectionViewModel> allConnections
    )
    {
        var connectedPinNames = allConnections
            .Where(c => c.ToPin?.Owner == this && (c.ToPin?.Name.StartsWith(prefix) ?? false))
            .Select(c => c.ToPin!.Name)
            .ToHashSet();

        // Remove disconnected pins
        foreach (
            PinViewModel? p in InputPins
                .Where(p => p.Name.StartsWith(prefix) && !connectedPinNames.Contains(p.Name))
                .ToList()
        )
            InputPins.Remove(p);

        // Update connection status
        foreach (PinViewModel? p in InputPins.Where(p => p.Name.StartsWith(prefix)))
            p.IsConnected = connectedPinNames.Contains(p.Name);

        // Find next available slot number
        var used = InputPins
            .Where(p => p.Name.StartsWith(prefix) && int.TryParse(p.Name[prefix.Length..], out _))
            .Select(p => int.Parse(p.Name[prefix.Length..]))
            .ToHashSet();
        int next = 1;
        while (used.Contains(next))
            next++;

        // Add empty slot for next connection
        InputPins.Add(
            CreatePinViewModel(
                new PinDescriptor(
                    $"{prefix}{next}",
                    PinDirection.Input,
                    slotType,
                    IsRequired: false,
                    Description: slotType == PinDataType.Boolean
                        ? "Connect a boolean condition"
                        : "Connect a column or expression"
                ))
        );
    }

    private void AddDynamicInputSlot(string prefix, PinDataType slotType, string description)
    {
        var used = InputPins
            .Where(p => p.Name.StartsWith(prefix) && int.TryParse(p.Name[prefix.Length..], out _))
            .Select(p => int.Parse(p.Name[prefix.Length..]))
            .ToHashSet();

        int next = 1;
        while (used.Contains(next))
            next++;

        InputPins.Add(
            CreatePinViewModel(
                new PinDescriptor(
                    $"{prefix}{next}",
                    PinDirection.Input,
                    slotType,
                    IsRequired: false,
                    Description: description
                ))
        );
    }

    private void RemoveLastEmptyDynamicInputSlot(string prefix)
    {
        var candidates = InputPins
            .Where(p => p.Name.StartsWith(prefix) && int.TryParse(p.Name[prefix.Length..], out _))
            .OrderByDescending(p => int.Parse(p.Name[prefix.Length..]))
            .ToList();

        if (candidates.Count <= 1)
            return; // keep at least one slot

        PinViewModel? removable = candidates.FirstOrDefault(p => !p.IsConnected);
        if (removable is not null)
            InputPins.Remove(removable);
    }

    private bool HasRemovableDynamicInputSlot(string prefix)
    {
        var candidates = InputPins
            .Where(p => p.Name.StartsWith(prefix) && int.TryParse(p.Name[prefix.Length..], out _))
            .ToList();

        return candidates.Count > 1 && candidates.Any(p => !p.IsConnected);
    }

    private void RaiseWindowSlotStateChanged()
    {
        RaisePropertyChanged(nameof(CanRemoveWindowPartitionSlot));
        RaisePropertyChanged(nameof(CanRemoveWindowOrderSlot));
    }

    private void SyncWindowDynamicInputPins(
        string prefix,
        PinDataType slotType,
        string description,
        IEnumerable<ConnectionViewModel> allConnections
    )
    {
        var connectedPinNames = allConnections
            .Where(c => c.ToPin?.Owner == this && (c.ToPin?.Name.StartsWith(prefix) ?? false))
            .Select(c => c.ToPin!.Name)
            .ToHashSet();

        var slots = InputPins
            .Where(p => p.Name.StartsWith(prefix) && int.TryParse(p.Name[prefix.Length..], out _))
            .OrderBy(p => int.Parse(p.Name[prefix.Length..]))
            .ToList();

        if (slots.Count == 0)
        {
            AddDynamicInputSlot(prefix, slotType, description);
            slots = InputPins
                .Where(p => p.Name.StartsWith(prefix) && int.TryParse(p.Name[prefix.Length..], out _))
                .OrderBy(p => int.Parse(p.Name[prefix.Length..]))
                .ToList();
        }

        foreach (PinViewModel slot in slots)
            slot.IsConnected = connectedPinNames.Contains(slot.Name);

        if (!slots.Any(s => !s.IsConnected))
            AddDynamicInputSlot(prefix, slotType, description);
    }

    private void EnsureWindowFunctionMinimumPins()
    {
        // Keep the node in minimum usable state by default: one partition slot and one order slot.
        while (InputPins.Any(p => p.Name.StartsWith("partition_") && p.Name != "partition_1"))
        {
            PinViewModel? extra = InputPins.FirstOrDefault(
                p => p.Name.StartsWith("partition_") && p.Name != "partition_1"
            );
            if (extra is null)
                break;
            InputPins.Remove(extra);
        }

        while (InputPins.Any(p => p.Name.StartsWith("order_") && p.Name != "order_1"))
        {
            PinViewModel? extra = InputPins.FirstOrDefault(
                p => p.Name.StartsWith("order_") && p.Name != "order_1"
            );
            if (extra is null)
                break;
            InputPins.Remove(extra);
        }

        if (!InputPins.Any(p => p.Name == "partition_1"))
        {
            InputPins.Add(
                CreatePinViewModel(
                    new PinDescriptor(
                        "partition_1",
                        PinDirection.Input,
                        PinDataType.ColumnRef,
                        IsRequired: false,
                        Description: "Connect a partition expression"
                    ))
            );
        }

        if (!InputPins.Any(p => p.Name == "order_1"))
        {
            InputPins.Add(
                CreatePinViewModel(
                    new PinDescriptor(
                        "order_1",
                        PinDirection.Input,
                        PinDataType.ColumnRef,
                        IsRequired: false,
                        Description: "Connect an order expression"
                    ))
            );
        }

        ReorderWindowFunctionInputPins();
    }

    private void EnsureSubqueryInputMinimumPins()
    {
        if (InputPins.Any(pin => pin.Name.StartsWith("input_", StringComparison.Ordinal)))
            return;

        InputPins.Add(
            CreatePinViewModel(
                new PinDescriptor(
                    "input_1",
                    PinDirection.Input,
                    PinDataType.ColumnRef,
                    IsRequired: false,
                    Description: "Connect a parent-scope expression to use inside subquery editor"
                )));
    }

    private void ReorderWindowFunctionInputPins()
    {
        if (!IsWindowFunction)
            return;

        static int PinOrder(string name, string prefix)
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
                return int.MaxValue;

            return int.TryParse(name[prefix.Length..], out int n) ? n : int.MaxValue;
        }

        var fixedInputs = InputPins
            .Where(p => p.Name is not null && !p.Name.StartsWith("partition_") && !p.Name.StartsWith("order_"))
            .ToList();

        var partitionInputs = InputPins
            .Where(p => p.Name.StartsWith("partition_"))
            .OrderBy(p => PinOrder(p.Name, "partition_"))
            .ToList();

        var orderInputs = InputPins
            .Where(p => p.Name.StartsWith("order_"))
            .OrderBy(p => PinOrder(p.Name, "order_"))
            .ToList();

        InputPins.Clear();
        foreach (PinViewModel pin in fixedInputs)
            InputPins.Add(pin);
        foreach (PinViewModel pin in partitionInputs)
            InputPins.Add(pin);
        foreach (PinViewModel pin in orderInputs)
            InputPins.Add(pin);
    }
}
