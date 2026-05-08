using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia;
using AkkornStudio.Metadata;
using AkkornStudio.Nodes;
using AkkornStudio.UI.Services.Theming;

namespace AkkornStudio.UI.ViewModels;

/// <summary>
/// ViewModel for the Schema browser tab in the sidebar.
/// Displays database schema: Tables, Views, Procedures, and Triggers.
/// </summary>
public sealed class SchemaViewModel : ViewModelBase
{
    private sealed record SchemaCatalogEntry(
        SchemaObjectViewModel Item,
        bool IsView,
        string SchemaName,
        string TableName,
        IReadOnlyList<string> ColumnNames,
        bool HasPrimaryKeyColumn,
        bool HasForeignKeyColumn,
        bool HasIndexedColumn,
        string SearchText);

    private string _filterQuery = string.Empty;
    private string? _selectedSchema;
    private bool _isLoading;
    private bool _hasConnection;
    private DbMetadata? _metadata;
    private int _visibleObjectCount;
    private IReadOnlyList<SchemaCatalogEntry> _catalogEntries = [];

    /// <summary>
    /// The name of the currently connected database.
    /// </summary>
    public string DatabaseName { get; private set; } = "No Connection";

    /// <summary>
    /// The search/filter query to narrow down tables and columns.
    /// </summary>
    public string FilterQuery
    {
        get => _filterQuery;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (!Set(ref _filterQuery, normalized))
                return;

            ApplyFilterToCategories();
            RaisePropertyChanged(nameof(HasFilter));
        }
    }

    /// <summary>
    /// The currently selected schema filter.
    /// </summary>
    public string? SelectedSchema
    {
        get => _selectedSchema;
        set
        {
            if (!Set(ref _selectedSchema, value))
                return;

            IsLoading = true;
            try
            {
                RebuildCatalogEntries();
                ApplyFilterToCategories();
            }
            finally
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>
    /// True when schema is being loaded from the database.
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (!Set(ref _isLoading, value))
                return;

            RaisePropertyChanged(nameof(ShowLoadingState));
            RaisePropertyChanged(nameof(ShowFilterEmptyState));
            RaisePropertyChanged(nameof(ShowNoTablesState));
        }
    }

    /// <summary>
    /// True when there is an active database connection.
    /// </summary>
    public bool HasConnection
    {
        get => _hasConnection;
        set
        {
            if (Set(ref _hasConnection, value))
            {
                RaisePropertyChanged(nameof(ShowNoConnectionState));
                RaisePropertyChanged(nameof(ShowLoadingState));
                RaisePropertyChanged(nameof(ShowFilterEmptyState));
                RaisePropertyChanged(nameof(ShowNoTablesState));
            }
        }
    }

    public bool HasFilter => !string.IsNullOrWhiteSpace(FilterQuery);
    public bool ShowNoConnectionState => !HasConnection;
    public bool ShowLoadingState => HasConnection && IsLoading;
    public bool ShowFilterEmptyState => HasConnection && !IsLoading && HasFilter && _visibleObjectCount == 0;
    public bool ShowNoTablesState => HasConnection && !IsLoading && !HasFilter && _visibleObjectCount == 0;

    /// <summary>
    /// The database metadata to display.
    /// </summary>
    public DbMetadata? Metadata
    {
        get => _metadata;
        set
        {
            if (Set(ref _metadata, value))
            {
                DatabaseName = value?.DatabaseName ?? "No Connection";
                if (value is null)
                {
                    _selectedSchema = null;
                    RaisePropertyChanged(nameof(SelectedSchema));
                }
                else if (!string.IsNullOrWhiteSpace(_selectedSchema)
                    && !value.Schemas.Any(schema => string.Equals(schema.Name, _selectedSchema, StringComparison.OrdinalIgnoreCase)))
                {
                    _selectedSchema = value.Schemas.FirstOrDefault()?.Name;
                    RaisePropertyChanged(nameof(SelectedSchema));
                }

                HasConnection = value is not null;
                IsLoading = true;
                try
                {
                    RebuildCatalogEntries();
                    ApplyFilterToCategories();
                }
                finally
                {
                    IsLoading = false;
                }
            }
        }
    }

    /// <summary>
    /// Categorized schema items (Tables, Views, Procedures, Triggers).
    /// </summary>
    public ObservableCollection<SchemaCategoryViewModel> Categories { get; } = new();

    private readonly Action<string, IEnumerable<(string name, PinDataType type)>, TableMetadata, Point>? _onAddTableNode;

    public SchemaViewModel(
        Action<string, IEnumerable<(string name, PinDataType type)>, TableMetadata, Point>? onAddTableNode = null)
    {
        _onAddTableNode = onAddTableNode;
    }

    private void RebuildCatalogEntries()
    {
        _catalogEntries = [];

        if (Metadata is null || Metadata.Schemas.Count == 0)
            return;

        var catalog = new List<SchemaCatalogEntry>();

        IEnumerable<SchemaMetadata> schemas = Metadata.Schemas;
        if (!string.IsNullOrWhiteSpace(SelectedSchema))
        {
            schemas = schemas.Where(schema =>
                string.Equals(schema.Name, SelectedSchema, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var schema in schemas)
        {
            foreach (var table in schema.Tables)
            {
                var fullName = $"{table.Schema ?? "public"}.{table.Name}";

                ICommand? addNodeCmd = null;
                if (_onAddTableNode is not null)
                {
                    var columns = table.Columns.Select(c =>
                    {
                        var pinType = MapSqlTypeToPinDataType(c.DataType);
                        return (c.Name, pinType);
                    });

                    addNodeCmd = new RelayCommand(() =>
                        _onAddTableNode(fullName, columns, table, new Point(200, 200))
                    );
                }

                var item = new SchemaObjectViewModel(
                    table.Name,
                    table.Kind != TableKind.Table ? "Eye" : "Table",
                    fullName,
                    null,
                    null,
                    table,
                    addNodeCmd);

                foreach (var column in table.Columns)
                {
                    string colIcon;
                    string badgeColor;

                    if (column.IsPrimaryKey)
                    {
                        colIcon = "KeyPlus";
                        badgeColor = UiColorConstants.C_FCD34D; // Yellow
                    }
                    else if (column.IsForeignKey)
                    {
                        colIcon = "LinkVariant";
                        badgeColor = UiColorConstants.C_F87171; // Red
                    }
                    else if (column.IsIndexed)
                    {
                        colIcon = "DatabaseSearch";
                        badgeColor = UiColorConstants.C_60A5FA; // Blue
                    }
                    else
                    {
                        colIcon = "CircleOutline";
                        badgeColor = UiColorConstants.C_9CA3AF; // Gray
                    }

                    var colItem = new SchemaObjectViewModel(
                        column.Name,
                        colIcon,
                        column.DataType ?? "unknown",
                        column.DataType ?? "unknown",
                        badgeColor,
                        column);

                    item.Children.Add(colItem);
                }

                string searchText = string.Join(' ',
                    table.Name,
                    table.Schema ?? string.Empty,
                    string.Join(' ', table.Columns.Select(c => c.Name)));

                catalog.Add(new SchemaCatalogEntry(
                    item,
                    table.Kind != TableKind.Table,
                    schema.Name,
                    table.Name,
                    table.Columns.Select(column => column.Name).ToArray(),
                    table.Columns.Any(column => column.IsPrimaryKey),
                    table.Columns.Any(column => column.IsForeignKey),
                    table.Columns.Any(column => column.IsIndexed),
                    searchText));
            }
        }

        _catalogEntries = catalog;
    }

    private void ApplyFilterToCategories()
    {
        Categories.Clear();
        _visibleObjectCount = 0;

        if (_catalogEntries.Count == 0)
        {
            RaisePropertyChanged(nameof(ShowNoConnectionState));
            RaisePropertyChanged(nameof(ShowLoadingState));
            RaisePropertyChanged(nameof(ShowFilterEmptyState));
            RaisePropertyChanged(nameof(ShowNoTablesState));
            return;
        }

        var tablesCategory = new SchemaCategoryViewModel("Tables", "Table", UiColorConstants.C_60A5FA);
        var viewsCategory = new SchemaCategoryViewModel("Views", "Eye", UiColorConstants.C_34D399);

        IEnumerable<SchemaCatalogEntry> filteredEntries = _catalogEntries;
        if (!string.IsNullOrWhiteSpace(FilterQuery))
        {
            SchemaFilterInstruction instruction = SchemaFilterInstruction.Parse(FilterQuery);
            filteredEntries = filteredEntries.Where(entry => instruction.Matches(entry));
        }

        foreach (SchemaCatalogEntry entry in filteredEntries)
        {
            if (entry.IsView)
                viewsCategory.Items.Add(entry.Item);
            else
                tablesCategory.Items.Add(entry.Item);
        }

        _visibleObjectCount = tablesCategory.Items.Count + viewsCategory.Items.Count;

        if (tablesCategory.Items.Count > 0)
            Categories.Add(tablesCategory);

        if (viewsCategory.Items.Count > 0)
            Categories.Add(viewsCategory);

        RaisePropertyChanged(nameof(ShowNoConnectionState));
        RaisePropertyChanged(nameof(ShowLoadingState));
        RaisePropertyChanged(nameof(ShowFilterEmptyState));
        RaisePropertyChanged(nameof(ShowNoTablesState));
    }

    private sealed class SchemaFilterInstruction
    {
        private readonly List<string> _generalTerms = [];
        private readonly List<string> _schemaTerms = [];
        private readonly List<string> _tableTerms = [];
        private readonly List<string> _columnTerms = [];

        private bool _tablesOnly;
        private bool _viewsOnly;
        private bool _columnsOnly;
        private bool _requirePrimaryKey;
        private bool _requireForeignKey;
        private bool _requireIndex;

        public static SchemaFilterInstruction Parse(string rawQuery)
        {
            var instruction = new SchemaFilterInstruction();
            string[] tokens = rawQuery
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (string token in tokens)
                instruction.ApplyToken(token);

            return instruction;
        }

        public bool Matches(SchemaCatalogEntry entry)
        {
            if (_tablesOnly && entry.IsView)
                return false;

            if (_viewsOnly && !entry.IsView)
                return false;

            if (_requirePrimaryKey && !entry.HasPrimaryKeyColumn)
                return false;

            if (_requireForeignKey && !entry.HasForeignKeyColumn)
                return false;

            if (_requireIndex && !entry.HasIndexedColumn)
                return false;

            if (!ContainsAllTerms(entry.SchemaName, _schemaTerms))
                return false;

            if (!ContainsAllTerms(entry.TableName, _tableTerms))
                return false;

            if (!ColumnsContainAllTerms(entry.ColumnNames, _columnTerms))
                return false;

            return _columnsOnly
                ? ColumnsContainAllTerms(entry.ColumnNames, _generalTerms)
                : ContainsAllTerms(entry.SearchText, _generalTerms);
        }

        private void ApplyToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return;

            if (TryApplyKeyValueToken(token))
                return;

            if (token.StartsWith('@'))
            {
                _tablesOnly = true;
                AddTrimmedValue(_tableTerms, token[1..]);
                return;
            }

            if (token.StartsWith('#'))
            {
                _viewsOnly = true;
                AddTrimmedValue(_tableTerms, token[1..]);
                return;
            }

            if (token.StartsWith('!'))
            {
                _columnsOnly = true;
                AddTrimmedValue(_columnTerms, token[1..]);
                return;
            }

            _generalTerms.Add(token);
        }

        private bool TryApplyKeyValueToken(string token)
        {
            int separatorIndex = token.IndexOf(':');
            if (separatorIndex <= 0)
            {
                if (token.Equals("pk", StringComparison.OrdinalIgnoreCase))
                {
                    _requirePrimaryKey = true;
                    return true;
                }

                if (token.Equals("fk", StringComparison.OrdinalIgnoreCase))
                {
                    _requireForeignKey = true;
                    return true;
                }

                if (token.Equals("idx", StringComparison.OrdinalIgnoreCase)
                    || token.Equals("index", StringComparison.OrdinalIgnoreCase))
                {
                    _requireIndex = true;
                    return true;
                }

                return false;
            }

            string prefix = token[..separatorIndex];
            string value = token[(separatorIndex + 1)..];

            switch (prefix.ToLowerInvariant())
            {
                case "schema":
                case "s":
                    AddTrimmedValue(_schemaTerms, value);
                    return true;
                case "table":
                case "t":
                    _tablesOnly = true;
                    AddTrimmedValue(_tableTerms, value);
                    return true;
                case "view":
                case "v":
                    _viewsOnly = true;
                    AddTrimmedValue(_tableTerms, value);
                    return true;
                case "col":
                case "column":
                case "c":
                    _columnsOnly = true;
                    AddTrimmedValue(_columnTerms, value);
                    return true;
                case "pk":
                    _requirePrimaryKey = true;
                    AddTrimmedValue(_columnTerms, value);
                    return true;
                case "fk":
                    _requireForeignKey = true;
                    AddTrimmedValue(_columnTerms, value);
                    return true;
                case "idx":
                case "index":
                    _requireIndex = true;
                    AddTrimmedValue(_columnTerms, value);
                    return true;
                default:
                    return false;
            }
        }

        private static bool ContainsAllTerms(string source, IReadOnlyList<string> terms)
        {
            if (terms.Count == 0)
                return true;

            return terms.All(term =>
                source.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        private static bool ColumnsContainAllTerms(IEnumerable<string> columns, IReadOnlyList<string> terms)
        {
            if (terms.Count == 0)
                return true;

            string[] normalizedColumns = columns
                .Where(column => !string.IsNullOrWhiteSpace(column))
                .ToArray();

            return terms.All(term =>
                normalizedColumns.Any(column =>
                    column.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        private static void AddTrimmedValue(List<string> target, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            target.Add(value.Trim());
        }
    }

    public static PinDataType MapSqlTypeToPinDataType(string? rawType)
    {
        string normalized = (rawType ?? string.Empty).Trim().ToLowerInvariant();

        return normalized switch
        {
            "int" or "integer" or "bigint" or "smallint" or "tinyint" => PinDataType.Integer,
            "decimal" or "numeric" or "float" or "double" or "real" or "money" => PinDataType.Decimal,
            "varchar" or "nvarchar" or "text" or "char" or "nchar" or "string" => PinDataType.Text,
            "bool" or "boolean" or "bit" => PinDataType.Boolean,
            "datetime" or "timestamp" or "date" or "time" => PinDataType.DateTime,
            "json" or "jsonb" => PinDataType.Json,
            _ => PinDataType.Expression,
        };
    }
}


