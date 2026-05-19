using System.Collections.ObjectModel;
using Avalonia.Media;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.ViewModels.ErDiagram;

public enum ErEntityDetailsTab
{
    Columns,
    CreateStatement,
    Dependencies,
}

public enum ErNodeDensity
{
    Compact,
    Normal,
    Detailed,
}

public enum ErVisualState
{
    Normal,
    ConnectedHighlight,
    Warning,
    Error,
    Changed,
}

/// <summary>
/// Represents an entity node in the ER canvas.
/// </summary>
public sealed class ErEntityNodeViewModel : ViewModelBase
{
    private const double HeaderHeight = 56d;
    private const double TabsHeight = 28d;
    private const double ColumnsHeaderHeight = 22d;
    private const double ColumnRowHeight = 24d;
    private const int NormalDensityColumnLimit = 10;

    private string _id;
    private string _schema;
    private string _name;
    private double _x;
    private double _y;
    private bool _isSelected;
    private bool _isHovered;
    private bool _isDimmed;
    private bool _isHidden;
    private bool _isHoverFocusPending;
    private ErVisualState _visualState = ErVisualState.Normal;
    private ErEntityDetailsTab _selectedTab = ErEntityDetailsTab.Columns;
    private ErNodeDensity _density = ErNodeDensity.Normal;

    public ErEntityNodeViewModel(
        string schema,
        string name,
        bool isView,
        long? estimatedRowCount,
        IEnumerable<ErColumnRowViewModel>? columns = null,
        IEnumerable<string>? dependencies = null,
        string? createStatementSql = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Entity name cannot be empty.", nameof(name));

        _schema = schema?.Trim() ?? string.Empty;
        _name = name.Trim();
        _id = BuildCanonicalId(_schema, _name);

        IsView = isView;
        EstimatedRowCount = estimatedRowCount;
        Columns = columns is null
            ? []
            : new ObservableCollection<ErColumnRowViewModel>(columns);
        Dependencies = dependencies is null
            ? []
            : new ObservableCollection<string>(dependencies);
        CreateStatementSql = string.IsNullOrWhiteSpace(createStatementSql)
            ? "-- Definition unavailable"
            : createStatementSql.Trim();
    }

    public string Id
    {
        get => _id;
        private set => Set(ref _id, value);
    }

    public string Schema
    {
        get => _schema;
        private set => Set(ref _schema, value);
    }

    public string Name
    {
        get => _name;
        private set => Set(ref _name, value);
    }

    public string DisplayName => Name;

    public string DisplaySchema => string.IsNullOrWhiteSpace(Schema) ? "public" : Schema;

    public string ObjectTypeLabel => IsView ? "VIEW" : "TABLE";

    public bool IsView { get; }

    public long? EstimatedRowCount { get; }

    public ObservableCollection<ErColumnRowViewModel> Columns { get; }

    public ObservableCollection<string> Dependencies { get; }

    public string CreateStatementSql { get; }

    public int ColumnCount => Columns.Count;

    public int PrimaryKeyCount => Columns.Count(column => column.IsPrimaryKey);

    public int ForeignKeyCount => Columns.Count(column => column.IsForeignKey);

    public string MetaSummary =>
        $"{ColumnCount} columns | {PrimaryKeyCount} PK | {ForeignKeyCount} FK";

    public string SelectionSummary => MetaSummary;

    public bool HasDependencies => Dependencies.Count > 0;

    public ErNodeDensity Density
    {
        get => _density;
        set
        {
            if (!Set(ref _density, value))
                return;

            RaisePropertyChanged(nameof(IsCompactDensity));
            RaisePropertyChanged(nameof(IsNormalDensity));
            RaisePropertyChanged(nameof(IsDetailedDensity));
            RaisePropertyChanged(nameof(VisibleColumns));
            RaisePropertyChanged(nameof(VisibleColumnCount));
            RaisePropertyChanged(nameof(HiddenColumnCount));
            RaisePropertyChanged(nameof(HasHiddenColumns));
            RaisePropertyChanged(nameof(ShowTabs));
            RaisePropertyChanged(nameof(ShowDetailsArea));
            RaisePropertyChanged(nameof(NodeHeight));
        }
    }

    public bool IsCompactDensity => Density == ErNodeDensity.Compact;

    public bool IsNormalDensity => Density == ErNodeDensity.Normal;

    public bool IsDetailedDensity => Density == ErNodeDensity.Detailed;

    public IReadOnlyList<ErColumnRowViewModel> VisibleColumns =>
        Density switch
        {
            ErNodeDensity.Compact => [],
            ErNodeDensity.Normal => [.. PrioritizedColumns().Take(NormalDensityColumnLimit)],
            _ => [.. PrioritizedColumns()],
        };

    public int VisibleColumnCount => VisibleColumns.Count;

    public int HiddenColumnCount => Math.Max(0, ColumnCount - VisibleColumnCount);

    public bool HasHiddenColumns => HiddenColumnCount > 0;

    public bool ShowTabs => !IsCompactDensity;

    public bool ShowDetailsArea => !IsCompactDensity;

    public double DetailsPanelHeight =>
        IsCompactDensity
            ? 0
            : Math.Max(72d, VisibleColumnCount * ColumnRowHeight);

    public double NodeHeight =>
        HeaderHeight
        + (ShowTabs ? TabsHeight : 0)
        + (SelectedTab == ErEntityDetailsTab.Columns && ShowDetailsArea ? ColumnsHeaderHeight : 0)
        + DetailsPanelHeight;

    public ErEntityDetailsTab SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (!Set(ref _selectedTab, value))
                return;

            RaisePropertyChanged(nameof(IsColumnsTabSelected));
            RaisePropertyChanged(nameof(IsCreateTabSelected));
            RaisePropertyChanged(nameof(IsDependenciesTabSelected));
            RaisePropertyChanged(nameof(NodeHeight));
        }
    }

    public bool IsColumnsTabSelected
    {
        get => SelectedTab == ErEntityDetailsTab.Columns;
        set
        {
            if (value)
                SelectedTab = ErEntityDetailsTab.Columns;
        }
    }

    public bool IsCreateTabSelected
    {
        get => SelectedTab == ErEntityDetailsTab.CreateStatement;
        set
        {
            if (value)
                SelectedTab = ErEntityDetailsTab.CreateStatement;
        }
    }

    public bool IsDependenciesTabSelected
    {
        get => SelectedTab == ErEntityDetailsTab.Dependencies;
        set
        {
            if (value)
                SelectedTab = ErEntityDetailsTab.Dependencies;
        }
    }

    public double X
    {
        get => _x;
        set => Set(ref _x, value);
    }

    public double Y
    {
        get => _y;
        set => Set(ref _y, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!Set(ref _isSelected, value))
                return;

            RaiseVisualPropertiesChanged();
        }
    }

    public bool IsHovered
    {
        get => _isHovered;
        set
        {
            if (!Set(ref _isHovered, value))
                return;

            RaiseVisualPropertiesChanged();
        }
    }

    public bool IsDimmed
    {
        get => _isDimmed;
        set
        {
            if (!Set(ref _isDimmed, value))
                return;

            RaiseVisualPropertiesChanged();
        }
    }

    public bool IsHidden
    {
        get => _isHidden;
        set => Set(ref _isHidden, value);
    }

    public bool IsHoverFocusPending
    {
        get => _isHoverFocusPending;
        set => Set(ref _isHoverFocusPending, value);
    }

    public ErVisualState VisualState
    {
        get => _visualState;
        set
        {
            if (!Set(ref _visualState, value))
                return;

            RaiseVisualPropertiesChanged();
        }
    }

    public IBrush NodeBackground => new SolidColorBrush(Color.Parse("#0F1626"));

    public IBrush HeaderBackground => new SolidColorBrush(Color.Parse("#121B2D"));

    public IBrush NodeBorderBrush =>
        IsSelected
            ? new SolidColorBrush(Color.Parse("#3B82F6"))
            : VisualState switch
            {
                ErVisualState.Warning => new SolidColorBrush(Color.Parse("#F59E0B")),
                ErVisualState.Error => new SolidColorBrush(Color.Parse("#EF4444")),
                ErVisualState.Changed => new SolidColorBrush(Color.Parse("#C47A3A")),
                ErVisualState.ConnectedHighlight => new SolidColorBrush(Color.Parse("#7FA36A")),
                _ => new SolidColorBrush(Color.Parse("#273247")),
            };

    public double NodeBorderThickness => IsSelected ? 2d : IsHovered ? 1.5d : 1d;

    public double NodeOpacity => IsDimmed ? 0.34d : 1d;

    public IBrush HeaderTitleBrush => new SolidColorBrush(Color.Parse("#E8EAED"));

    public IBrush HeaderSchemaBrush => new SolidColorBrush(Color.Parse("#9CA3AF"));

    public IBrush HeaderMetaBrush => new SolidColorBrush(Color.Parse("#64748B"));

    public string ObjectKindIconKind => IsView ? "EyeOutline" : "TableLarge";

    public IBrush HeaderAccentBrush =>
        IsView
            ? new SolidColorBrush(Color.Parse("#D9A441"))
            : new SolidColorBrush(Color.Parse("#3B82F6"));

    public IBrush ObjectTypeBadgeBackground =>
        IsView
            ? new SolidColorBrush(Color.Parse("#183246"))
            : new SolidColorBrush(Color.Parse("#1F2A3F"));

    public IBrush ObjectTypeBadgeForeground =>
        IsView
            ? new SolidColorBrush(Color.Parse("#8FC5FF"))
            : new SolidColorBrush(Color.Parse("#AFB9D0"));

    public void HighlightColumns(IReadOnlySet<string> columnNames)
    {
        foreach (ErColumnRowViewModel column in Columns)
            column.IsRelationEndpointHighlighted = columnNames.Contains(column.ColumnName);
    }

    public bool TryGetVisibleColumnIndex(string columnName, out int visibleIndex)
    {
        IReadOnlyList<ErColumnRowViewModel> visible = VisibleColumns;
        for (int i = 0; i < visible.Count; i++)
        {
            if (!string.Equals(visible[i].ColumnName, columnName, StringComparison.OrdinalIgnoreCase))
                continue;

            visibleIndex = i;
            return true;
        }

        visibleIndex = -1;
        return false;
    }

    public void Rename(string newSchema, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("Entity name cannot be empty.", nameof(newName));

        Schema = newSchema?.Trim() ?? string.Empty;
        Name = newName.Trim();
        Id = BuildCanonicalId(Schema, Name);
        RaisePropertyChanged(nameof(DisplayName));
        RaisePropertyChanged(nameof(DisplaySchema));
    }

    private void RaiseVisualPropertiesChanged()
    {
        RaisePropertyChanged(nameof(NodeBorderBrush));
        RaisePropertyChanged(nameof(NodeBorderThickness));
        RaisePropertyChanged(nameof(NodeOpacity));
    }

    private static string BuildCanonicalId(string schema, string name) =>
        string.IsNullOrWhiteSpace(schema) ? name : $"{schema}.{name}";

    private IEnumerable<ErColumnRowViewModel> PrioritizedColumns()
    {
        return Columns
            .Select((column, index) => new { column, index })
            .OrderBy(static pair =>
                pair.column.IsPrimaryKey ? 0 :
                pair.column.IsForeignKey ? 1 :
                pair.column.IsUnique ? 2 : 3)
            .ThenBy(static pair => pair.index)
            .Select(static pair => pair.column);
    }
}

