using System.Data;
using System.Windows.Input;
using AkkornStudio.UI.Services.SqlEditor.Results;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace AkkornStudio.UI.ViewModels;

public sealed class SqlResultPageViewModel : ViewModelBase
{
    private const int DefaultPageSize = 100;

    private readonly Dictionary<Guid, Guid?> _sessionSourceEditorDocumentMap = [];
    private readonly Dictionary<Guid, HashSet<string>> _sessionCollapsedGroupKeys = [];
    private SqlResultSession? _session;
    private IReadOnlyList<SqlResultSession> _sessions = [];
    private Guid? _sourceSqlEditorDocumentId;
    private Action<Guid?>? _navigateBackToEditor;
    private SqlResultSessionService? _sessionService;
    private DataTable? _pagedRowsTable;
    private object? _selectedRowItem;
    private ObservableCollection<SqlResultSelectedRowFieldItemViewModel> _selectedRowFields = [];
    private string _selectedRowJson = "{}";
    private int _currentPage = 1;
    private int _totalPages = 1;
    private int _totalFilteredRows;
    private string _searchText = string.Empty;
    private string? _selectedSortColumn;
    private bool _sortAscending = true;
    private IReadOnlyList<string> _availableSortColumns = [];
    private ObservableCollection<SqlResultSortCriterionItemViewModel> _activeSortCriteria = [];
    private string? _selectedGroupColumn;
    private ObservableCollection<SqlResultGroupColumnItemViewModel> _activeGroupColumns = [];
    private ObservableCollection<SqlResultGroupBucketItemViewModel> _groupBuckets = [];
    private string? _selectedFilterColumn;
    private string _selectedFilterOperation = FilterOperationContains;
    private string _columnFilterValue = string.Empty;
    private IReadOnlyList<string> _availableFilterOperations = [];
    private ObservableCollection<SqlResultFilterCriterionItemViewModel> _activeFilterCriteria = [];
    private ObservableCollection<SqlResultColumnVisibilityItemViewModel> _columnVisibilityItems = [];
    private int _frozenColumnCount;

    private const string FilterOperationContains = "contains";
    private const string FilterOperationEquals = "equals";
    private const string FilterOperationNotEquals = "not_equals";
    private const string FilterOperationStartsWith = "starts_with";
    private const string FilterOperationEndsWith = "ends_with";
    private const string FilterOperationGreaterThan = "gt";
    private const string FilterOperationGreaterThanOrEqual = "gte";
    private const string FilterOperationLessThan = "lt";
    private const string FilterOperationLessThanOrEqual = "lte";

    private static readonly IReadOnlyList<string> TextFilterOperations =
    [
        FilterOperationContains,
        FilterOperationEquals,
        FilterOperationNotEquals,
        FilterOperationStartsWith,
        FilterOperationEndsWith,
    ];

    private static readonly IReadOnlyList<string> ComparableFilterOperations =
    [
        FilterOperationEquals,
        FilterOperationNotEquals,
        FilterOperationGreaterThan,
        FilterOperationGreaterThanOrEqual,
        FilterOperationLessThan,
        FilterOperationLessThanOrEqual,
    ];

    private static readonly JsonSerializerOptions RowJsonSerializerOptions = new()
    {
        WriteIndented = true,
    };

    public SqlResultPageViewModel()
    {
        BackToEditorCommand = new RelayCommand(
            () => _navigateBackToEditor?.Invoke(_sourceSqlEditorDocumentId),
            () => _navigateBackToEditor is not null);

        SelectSessionCommand = new RelayCommand<SqlResultSession>(SelectSession);
        TogglePinCommand = new RelayCommand(
            TogglePinForCurrentSession,
            () => Session is not null && _sessionService is not null);
        CloseSessionCommand = new RelayCommand(
            CloseCurrentSession,
            () => Session is not null && _sessionService is not null);
        NextPageCommand = new RelayCommand(
            MoveNextPage,
            () => HasNextPage);
        PreviousPageCommand = new RelayCommand(
            MovePreviousPage,
            () => HasPreviousPage);
        FirstPageCommand = new RelayCommand(
            MoveFirstPage,
            () => HasPreviousPage);
        LastPageCommand = new RelayCommand(
            MoveLastPage,
            () => HasNextPage);
        ToggleSortDirectionCommand = new RelayCommand(ToggleSortDirection);
        AddGroupColumnCommand = new RelayCommand(AddGroupColumn, () => CanAddGroupColumn);
        RemoveGroupColumnCommand = new RelayCommand<SqlResultGroupColumnItemViewModel>(RemoveGroupColumn);
        ClearGroupColumnsCommand = new RelayCommand(ClearGroupColumns, () => HasActiveGroupColumns);
        ToggleGroupBucketCollapsedCommand = new RelayCommand<SqlResultGroupBucketItemViewModel>(ToggleGroupBucketCollapsed);
        AddSortCriterionCommand = new RelayCommand(AddSortCriterion, () => CanAddSortCriterion);
        RemoveSortCriterionCommand = new RelayCommand<SqlResultSortCriterionItemViewModel>(RemoveSortCriterion);
        ClearSortCriteriaCommand = new RelayCommand(ClearSortCriteria, () => HasActiveSortCriteria);
        ApplyColumnFilterCommand = new RelayCommand(ApplyColumnFilter, () => CanAddColumnFilter);
        RemoveColumnFilterCommand = new RelayCommand<SqlResultFilterCriterionItemViewModel>(RemoveColumnFilter);
        ClearColumnFilterCommand = new RelayCommand(ClearColumnFilter, () => HasActiveColumnFilters);
        ShowAllColumnsCommand = new RelayCommand(ShowAllColumns);
        MoveColumnUpCommand = new RelayCommand<SqlResultColumnVisibilityItemViewModel>(MoveColumnUp, CanMoveColumnUp);
        MoveColumnDownCommand = new RelayCommand<SqlResultColumnVisibilityItemViewModel>(MoveColumnDown, CanMoveColumnDown);
        ToggleColumnFrozenCommand = new RelayCommand<SqlResultColumnVisibilityItemViewModel>(ToggleColumnFrozen);
        ClearSelectedRowCommand = new RelayCommand(ClearSelectedRow, () => HasSelectedRow);
    }

    public ICommand BackToEditorCommand { get; }
    public ICommand SelectSessionCommand { get; }
    public ICommand TogglePinCommand { get; }
    public ICommand CloseSessionCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand FirstPageCommand { get; }
    public ICommand LastPageCommand { get; }
    public ICommand ToggleSortDirectionCommand { get; }
    public ICommand AddGroupColumnCommand { get; }
    public ICommand RemoveGroupColumnCommand { get; }
    public ICommand ClearGroupColumnsCommand { get; }
    public ICommand ToggleGroupBucketCollapsedCommand { get; }
    public ICommand AddSortCriterionCommand { get; }
    public ICommand RemoveSortCriterionCommand { get; }
    public ICommand ClearSortCriteriaCommand { get; }
    public ICommand ApplyColumnFilterCommand { get; }
    public ICommand RemoveColumnFilterCommand { get; }
    public ICommand ClearColumnFilterCommand { get; }
    public ICommand ShowAllColumnsCommand { get; }
    public ICommand MoveColumnUpCommand { get; }
    public ICommand MoveColumnDownCommand { get; }
    public ICommand ToggleColumnFrozenCommand { get; }
    public ICommand ClearSelectedRowCommand { get; }

    public SqlResultSession? Session
    {
        get => _session;
        private set
        {
            if (!Set(ref _session, value))
                return;

            RaisePropertyChanged(nameof(HasSession));
            RaisePropertyChanged(nameof(SqlText));
            RaisePropertyChanged(nameof(ConnectionId));
            RaisePropertyChanged(nameof(DatabaseName));
            RaisePropertyChanged(nameof(ExecutedAtText));
            RaisePropertyChanged(nameof(DurationText));
            RaisePropertyChanged(nameof(StatusText));
            RaisePropertyChanged(nameof(RowCountText));
            RaisePropertyChanged(nameof(ColumnCountText));
            RaisePropertyChanged(nameof(IsCurrentSessionPinned));
            RaisePropertyChanged(nameof(TogglePinText));
            RaisePropertyChanged(nameof(SelectedSession));
            RebuildRowsProjection();
            NotifyCommands();
        }
    }

    public IReadOnlyList<SqlResultSession> Sessions
    {
        get => _sessions;
        private set
        {
            _sessions = value;
            RaisePropertyChanged(nameof(Sessions));
            RaisePropertyChanged(nameof(HasSessions));
        }
    }

    public bool HasSession => Session is not null;
    public bool HasSessions => Sessions.Count > 0;

    public SqlResultSession? SelectedSession
    {
        get => Session;
        set => SelectSession(value);
    }

    public string SqlText => Session?.SqlText ?? string.Empty;
    public string ConnectionId => Session?.ConnectionId ?? string.Empty;
    public string DatabaseName => Session?.DatabaseName ?? "-";
    public string ExecutedAtText => Session?.ExecutedAt.LocalDateTime.ToString("dd/MM/yyyy HH:mm:ss") ?? "-";
    public string DurationText => Session is null ? "-" : $"{Session.ExecutionTime.TotalMilliseconds:0} ms";
    public string StatusText => Session?.Status == SqlResultSessionStatus.Success ? "Sucesso" : "Erro";
    public DataView? RowsView => _pagedRowsTable?.DefaultView;
    public object? SelectedRowItem
    {
        get => _selectedRowItem;
        set
        {
            if (!Set(ref _selectedRowItem, value))
                return;

            UpdateSelectedRowState(value);
        }
    }

    public ObservableCollection<SqlResultSelectedRowFieldItemViewModel> SelectedRowFields
    {
        get => _selectedRowFields;
        private set
        {
            _selectedRowFields = value;
            RaisePropertyChanged(nameof(SelectedRowFields));
            RaisePropertyChanged(nameof(HasSelectedRow));
            (ClearSelectedRowCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    public string SelectedRowJson
    {
        get => _selectedRowJson;
        private set => Set(ref _selectedRowJson, value);
    }

    public bool HasSelectedRow => SelectedRowFields.Count > 0;
    public string SelectedRowSummary => Session?.ViewState.SelectedRowIndex is int idx && idx >= 0
        ? $"Row {idx + 1}"
        : "-";
    public string RowCountText => _totalFilteredRows.ToString();
    public string ColumnCountText => Session?.ResultSet.Data?.Columns.Count.ToString() ?? "0";
    public bool IsCurrentSessionPinned => Session?.IsPinned == true;
    public string TogglePinText => IsCurrentSessionPinned ? "Desfixar" : "Fixar";
    public bool HasPreviousPage => CurrentPage > 1;
    public bool HasNextPage => CurrentPage < TotalPages;
    public int FrozenColumnCount
    {
        get => _frozenColumnCount;
        private set => Set(ref _frozenColumnCount, Math.Max(0, value));
    }

    public int CurrentPage
    {
        get => _currentPage;
        private set
        {
            int bounded = Math.Max(1, value);
            if (_currentPage == bounded)
                return;

            _currentPage = bounded;
            if (Session is not null)
                Session.ViewState.CurrentPage = bounded;

            RaisePropertyChanged(nameof(CurrentPage));
            RaisePropertyChanged(nameof(PageSummaryText));
            RaisePropertyChanged(nameof(HasPreviousPage));
            RaisePropertyChanged(nameof(HasNextPage));
            NotifyCommands();
        }
    }

    public int TotalPages
    {
        get => _totalPages;
        private set
        {
            int bounded = Math.Max(1, value);
            if (_totalPages == bounded)
                return;

            _totalPages = bounded;
            RaisePropertyChanged(nameof(TotalPages));
            RaisePropertyChanged(nameof(PageSummaryText));
            RaisePropertyChanged(nameof(HasPreviousPage));
            RaisePropertyChanged(nameof(HasNextPage));
            NotifyCommands();
        }
    }

    public string PageSummaryText => $"{CurrentPage}/{TotalPages}";

    public string SearchText
    {
        get => _searchText;
        set
        {
            string normalized = value ?? string.Empty;
            if (!Set(ref _searchText, normalized))
                return;

            CurrentPage = 1;
            RebuildRowsProjection();
        }
    }

    public IReadOnlyList<string> AvailableSortColumns
    {
        get => _availableSortColumns;
        private set
        {
            _availableSortColumns = value;
            RaisePropertyChanged(nameof(AvailableSortColumns));
        }
    }

    public ObservableCollection<SqlResultColumnVisibilityItemViewModel> ColumnVisibilityItems
    {
        get => _columnVisibilityItems;
        private set
        {
            _columnVisibilityItems = value;
            RaisePropertyChanged(nameof(ColumnVisibilityItems));
        }
    }

    public ObservableCollection<SqlResultSortCriterionItemViewModel> ActiveSortCriteria
    {
        get => _activeSortCriteria;
        private set
        {
            _activeSortCriteria = value;
            RaisePropertyChanged(nameof(ActiveSortCriteria));
            RaisePropertyChanged(nameof(HasActiveSortCriteria));
            (ClearSortCriteriaCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    public ObservableCollection<SqlResultGroupColumnItemViewModel> ActiveGroupColumns
    {
        get => _activeGroupColumns;
        private set
        {
            _activeGroupColumns = value;
            RaisePropertyChanged(nameof(ActiveGroupColumns));
            RaisePropertyChanged(nameof(HasActiveGroupColumns));
            (ClearGroupColumnsCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    public ObservableCollection<SqlResultGroupBucketItemViewModel> GroupBuckets
    {
        get => _groupBuckets;
        private set
        {
            _groupBuckets = value;
            RaisePropertyChanged(nameof(GroupBuckets));
            RaisePropertyChanged(nameof(HasGroupBuckets));
        }
    }

    public ObservableCollection<SqlResultFilterCriterionItemViewModel> ActiveFilterCriteria
    {
        get => _activeFilterCriteria;
        private set
        {
            _activeFilterCriteria = value;
            RaisePropertyChanged(nameof(ActiveFilterCriteria));
            RaisePropertyChanged(nameof(HasActiveColumnFilters));
            (ClearColumnFilterCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    public string? SelectedSortColumn
    {
        get => _selectedSortColumn;
        set
        {
            if (!Set(ref _selectedSortColumn, value))
                return;

            (AddSortCriterionCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    public bool SortAscending
    {
        get => _sortAscending;
        private set
        {
            if (!Set(ref _sortAscending, value))
                return;

            RaisePropertyChanged(nameof(SortDirectionText));
        }
    }

    public string SortDirectionText => SortAscending ? "Asc" : "Desc";
    public bool HasActiveSortCriteria => ActiveSortCriteria.Count > 0;
    public bool CanAddSortCriterion => !string.IsNullOrWhiteSpace(SelectedSortColumn);

    public string? SelectedGroupColumn
    {
        get => _selectedGroupColumn;
        set
        {
            if (!Set(ref _selectedGroupColumn, value))
                return;

            (AddGroupColumnCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    public bool CanAddGroupColumn => !string.IsNullOrWhiteSpace(SelectedGroupColumn);
    public bool HasActiveGroupColumns => ActiveGroupColumns.Count > 0;
    public bool HasGroupBuckets => GroupBuckets.Count > 0;

    public IReadOnlyList<string> AvailableFilterOperations
    {
        get => _availableFilterOperations;
        private set
        {
            _availableFilterOperations = value;
            RaisePropertyChanged(nameof(AvailableFilterOperations));
        }
    }

    public string? SelectedFilterColumn
    {
        get => _selectedFilterColumn;
        set
        {
            if (!Set(ref _selectedFilterColumn, value))
                return;

            UpdateAvailableFilterOperations();
            (ApplyColumnFilterCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    public string SelectedFilterOperation
    {
        get => _selectedFilterOperation;
        set
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? FilterOperationContains : value;
            if (!Set(ref _selectedFilterOperation, normalized))
                return;

            (ApplyColumnFilterCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    public string ColumnFilterValue
    {
        get => _columnFilterValue;
        set
        {
            string normalized = value ?? string.Empty;
            if (!Set(ref _columnFilterValue, normalized))
                return;

            (ApplyColumnFilterCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    public bool CanAddColumnFilter =>
        !string.IsNullOrWhiteSpace(SelectedFilterColumn)
        && !string.IsNullOrWhiteSpace(SelectedFilterOperation)
        && !string.IsNullOrWhiteSpace(ColumnFilterValue);

    public bool HasActiveColumnFilters => ActiveFilterCriteria.Count > 0;

    public void ConfigureBackNavigation(Action<Guid?> navigateBackToEditor)
    {
        _navigateBackToEditor = navigateBackToEditor ?? throw new ArgumentNullException(nameof(navigateBackToEditor));
        (BackToEditorCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    public void ConfigureSessionService(SqlResultSessionService sessionService)
    {
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        RefreshSessions();
        NotifyCommands();
    }

    public void SetSession(SqlResultSession session, Guid? sourceSqlEditorDocumentId)
    {
        ArgumentNullException.ThrowIfNull(session);
        _sessionSourceEditorDocumentMap[session.Id] = sourceSqlEditorDocumentId;
        if (!_sessionCollapsedGroupKeys.ContainsKey(session.Id))
            _sessionCollapsedGroupKeys[session.Id] = [];
        _sourceSqlEditorDocumentId = sourceSqlEditorDocumentId;
        ApplySessionState(session);
        Session = session;
        RefreshSessions();
    }

    private void SelectSession(SqlResultSession? session)
    {
        if (session is null)
            return;

        ApplySessionState(session);
        Session = session;
        _sourceSqlEditorDocumentId = _sessionSourceEditorDocumentMap.GetValueOrDefault(session.Id);
    }

    private void ApplySessionState(SqlResultSession session)
    {
        _searchText = string.Empty;
        RaisePropertyChanged(nameof(SearchText));

        ActiveGroupColumns = new ObservableCollection<SqlResultGroupColumnItemViewModel>(
            session.ViewState.GroupedColumns.Select(column => new SqlResultGroupColumnItemViewModel(column)));
        _selectedGroupColumn = session.ViewState.GroupedColumns.FirstOrDefault();
        RaisePropertyChanged(nameof(SelectedGroupColumn));
        (AddGroupColumnCommand as RelayCommand)?.NotifyCanExecuteChanged();

        ActiveSortCriteria = new ObservableCollection<SqlResultSortCriterionItemViewModel>(
            session.ViewState.Sorts.Select(sort => new SqlResultSortCriterionItemViewModel(sort.ColumnName, !sort.Descending)));

        SqlResultSortCriterionItemViewModel? firstSort = ActiveSortCriteria.FirstOrDefault();
        _selectedSortColumn = firstSort?.ColumnName;
        RaisePropertyChanged(nameof(SelectedSortColumn));
        (AddSortCriterionCommand as RelayCommand)?.NotifyCanExecuteChanged();

        _sortAscending = firstSort?.Ascending != false;
        RaisePropertyChanged(nameof(SortAscending));
        RaisePropertyChanged(nameof(SortDirectionText));

        ActiveFilterCriteria = new ObservableCollection<SqlResultFilterCriterionItemViewModel>(
            session.ViewState.Filters.Select(filter => new SqlResultFilterCriterionItemViewModel(
                filter.ColumnName,
                filter.Operation,
                filter.Value)));

        SqlColumnFilter? firstFilter = session.ViewState.Filters.FirstOrDefault();
        _selectedFilterColumn = firstFilter?.ColumnName;
        RaisePropertyChanged(nameof(SelectedFilterColumn));
        UpdateAvailableFilterOperations();

        _selectedFilterOperation = firstFilter?.Operation ?? AvailableFilterOperations.FirstOrDefault() ?? FilterOperationContains;
        RaisePropertyChanged(nameof(SelectedFilterOperation));

        _columnFilterValue = firstFilter?.Value ?? string.Empty;
        RaisePropertyChanged(nameof(ColumnFilterValue));
        RaisePropertyChanged(nameof(CanAddColumnFilter));
        RaisePropertyChanged(nameof(HasActiveColumnFilters));
        (ApplyColumnFilterCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ClearColumnFilterCommand as RelayCommand)?.NotifyCanExecuteChanged();

        CurrentPage = Math.Max(1, session.ViewState.CurrentPage);
    }

    private void TogglePinForCurrentSession()
    {
        if (Session is null || _sessionService is null)
            return;

        _sessionService.SetPinned(Session.Id, !Session.IsPinned);
        Session = _sessionService.Get(Session.Id);
        RefreshSessions();
    }

    private void CloseCurrentSession()
    {
        if (Session is null || _sessionService is null)
            return;

        Guid closingId = Session.Id;
        _sessionService.Remove(closingId);
        _sessionSourceEditorDocumentMap.Remove(closingId);
        _sessionCollapsedGroupKeys.Remove(closingId);
        RefreshSessions();

        SqlResultSession? next = Sessions.FirstOrDefault();
        if (next is null)
        {
            Session = null;
            _pagedRowsTable = null;
            RaisePropertyChanged(nameof(RowsView));
            return;
        }

        SelectSession(next);
    }

    private void MoveFirstPage()
    {
        if (!HasPreviousPage)
            return;

        CurrentPage = 1;
        RebuildRowsProjection();
    }

    private void MovePreviousPage()
    {
        if (!HasPreviousPage)
            return;

        CurrentPage--;
        RebuildRowsProjection();
    }

    private void MoveNextPage()
    {
        if (!HasNextPage)
            return;

        CurrentPage++;
        RebuildRowsProjection();
    }

    private void MoveLastPage()
    {
        if (!HasNextPage)
            return;

        CurrentPage = TotalPages;
        RebuildRowsProjection();
    }

    private void ToggleSortDirection()
    {
        SortAscending = !SortAscending;
    }

    private void AddGroupColumn()
    {
        if (Session is null || string.IsNullOrWhiteSpace(SelectedGroupColumn))
            return;

        if (!Session.ViewState.GroupedColumns.Contains(SelectedGroupColumn, StringComparer.Ordinal))
            Session.ViewState.GroupedColumns.Add(SelectedGroupColumn);

        ActiveGroupColumns = new ObservableCollection<SqlResultGroupColumnItemViewModel>(
            Session.ViewState.GroupedColumns.Select(column => new SqlResultGroupColumnItemViewModel(column)));

        CurrentPage = 1;
        RebuildRowsProjection();
    }

    private void RemoveGroupColumn(SqlResultGroupColumnItemViewModel? groupColumn)
    {
        if (Session is null || groupColumn is null)
            return;

        Session.ViewState.GroupedColumns = Session.ViewState.GroupedColumns
            .Where(column => !string.Equals(column, groupColumn.ColumnName, StringComparison.Ordinal))
            .ToList();

        ActiveGroupColumns = new ObservableCollection<SqlResultGroupColumnItemViewModel>(
            Session.ViewState.GroupedColumns.Select(column => new SqlResultGroupColumnItemViewModel(column)));

        CurrentPage = 1;
        RebuildRowsProjection();
    }

    private void ClearGroupColumns()
    {
        if (Session is null || Session.ViewState.GroupedColumns.Count == 0)
            return;

        Session.ViewState.GroupedColumns.Clear();
        ActiveGroupColumns = [];
        _sessionCollapsedGroupKeys[Session.Id] = [];

        CurrentPage = 1;
        RebuildRowsProjection();
    }

    private void ToggleGroupBucketCollapsed(SqlResultGroupBucketItemViewModel? bucket)
    {
        if (Session is null || bucket is null)
            return;

        HashSet<string> collapsed = GetCollapsedGroupKeysForSession(Session.Id);
        if (collapsed.Contains(bucket.GroupKey))
            collapsed.Remove(bucket.GroupKey);
        else
            collapsed.Add(bucket.GroupKey);

        CurrentPage = 1;
        RebuildRowsProjection();
    }

    private void ClearSelectedRow()
    {
        if (Session is null)
            return;

        Session.ViewState.SelectedRowIndex = null;
        SelectedRowFields = [];
        SelectedRowJson = "{}";
        _selectedRowItem = null;
        RaisePropertyChanged(nameof(SelectedRowItem));
        RaisePropertyChanged(nameof(SelectedRowSummary));
    }

    private void RestoreSelectedRowFromSessionState()
    {
        if (Session is null || _pagedRowsTable is null)
            return;

        int? selectedRowIndex = Session.ViewState.SelectedRowIndex;
        if (!selectedRowIndex.HasValue
            || selectedRowIndex.Value < 0
            || selectedRowIndex.Value >= _pagedRowsTable.Rows.Count)
        {
            if (selectedRowIndex.HasValue)
                Session.ViewState.SelectedRowIndex = null;
            SelectedRowFields = [];
            SelectedRowJson = "{}";
            _selectedRowItem = null;
            RaisePropertyChanged(nameof(SelectedRowItem));
            RaisePropertyChanged(nameof(SelectedRowSummary));
            return;
        }

        SelectedRowItem = _pagedRowsTable.DefaultView[selectedRowIndex.Value];
    }

    private void UpdateSelectedRowState(object? selectedItem)
    {
        if (Session is null || selectedItem is not DataRowView rowView)
        {
            if (Session is not null)
                Session.ViewState.SelectedRowIndex = null;
            SelectedRowFields = [];
            SelectedRowJson = "{}";
            RaisePropertyChanged(nameof(SelectedRowSummary));
            return;
        }

        int rowIndex = _pagedRowsTable?.Rows.IndexOf(rowView.Row) ?? -1;
        Session.ViewState.SelectedRowIndex = rowIndex >= 0 ? rowIndex : null;

        var fieldItems = new List<SqlResultSelectedRowFieldItemViewModel>(rowView.Row.Table.Columns.Count);
        var jsonObject = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (DataColumn column in rowView.Row.Table.Columns)
        {
            object? rawValue = rowView.Row[column];
            object? normalized = rawValue == DBNull.Value ? null : rawValue;
            string displayValue = normalized?.ToString() ?? "<null>";
            fieldItems.Add(new SqlResultSelectedRowFieldItemViewModel(column.ColumnName, displayValue));
            jsonObject[column.ColumnName] = normalized;
        }

        SelectedRowFields = new ObservableCollection<SqlResultSelectedRowFieldItemViewModel>(fieldItems);
        SelectedRowJson = JsonSerializer.Serialize(jsonObject, RowJsonSerializerOptions);
        RaisePropertyChanged(nameof(SelectedRowSummary));
    }

    private void AddSortCriterion()
    {
        if (Session is null || string.IsNullOrWhiteSpace(SelectedSortColumn))
            return;

        Session.ViewState.Sorts = Session.ViewState.Sorts
            .Where(sort => !string.Equals(sort.ColumnName, SelectedSortColumn, StringComparison.Ordinal))
            .ToList();
        Session.ViewState.Sorts.Add(new SqlColumnSort(SelectedSortColumn, Descending: !SortAscending));

        ActiveSortCriteria = new ObservableCollection<SqlResultSortCriterionItemViewModel>(
            Session.ViewState.Sorts.Select(sort => new SqlResultSortCriterionItemViewModel(sort.ColumnName, !sort.Descending)));

        CurrentPage = 1;
        RebuildRowsProjection();
    }

    private void RemoveSortCriterion(SqlResultSortCriterionItemViewModel? criterion)
    {
        if (Session is null || criterion is null)
            return;

        Session.ViewState.Sorts = Session.ViewState.Sorts
            .Where(sort => !string.Equals(sort.ColumnName, criterion.ColumnName, StringComparison.Ordinal))
            .ToList();

        ActiveSortCriteria = new ObservableCollection<SqlResultSortCriterionItemViewModel>(
            Session.ViewState.Sorts.Select(sort => new SqlResultSortCriterionItemViewModel(sort.ColumnName, !sort.Descending)));

        CurrentPage = 1;
        RebuildRowsProjection();
    }

    private void ClearSortCriteria()
    {
        if (Session is null || Session.ViewState.Sorts.Count == 0)
            return;

        Session.ViewState.Sorts.Clear();
        ActiveSortCriteria = [];

        CurrentPage = 1;
        RebuildRowsProjection();
    }

    private void ApplyColumnFilter()
    {
        if (Session is null || !CanAddColumnFilter || SelectedFilterColumn is null)
            return;

        string value = ColumnFilterValue.Trim();
        string operation = SelectedFilterOperation.Trim();

        Session.ViewState.Filters = Session.ViewState.Filters
            .Where(filter => !(string.Equals(filter.ColumnName, SelectedFilterColumn, StringComparison.Ordinal)
                               && string.Equals(filter.Operation, operation, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        Session.ViewState.Filters.Add(new SqlColumnFilter(SelectedFilterColumn, operation, value));

        ActiveFilterCriteria = new ObservableCollection<SqlResultFilterCriterionItemViewModel>(
            Session.ViewState.Filters.Select(filter => new SqlResultFilterCriterionItemViewModel(
                filter.ColumnName,
                filter.Operation,
                filter.Value)));

        CurrentPage = 1;
        RebuildRowsProjection();
    }

    private void RemoveColumnFilter(SqlResultFilterCriterionItemViewModel? criterion)
    {
        if (Session is null || criterion is null)
            return;

        Session.ViewState.Filters = Session.ViewState.Filters
            .Where(filter => !(string.Equals(filter.ColumnName, criterion.ColumnName, StringComparison.Ordinal)
                               && string.Equals(filter.Operation, criterion.Operation, StringComparison.OrdinalIgnoreCase)
                               && string.Equals(filter.Value ?? string.Empty, criterion.Value ?? string.Empty, StringComparison.Ordinal)))
            .ToList();

        ActiveFilterCriteria = new ObservableCollection<SqlResultFilterCriterionItemViewModel>(
            Session.ViewState.Filters.Select(filter => new SqlResultFilterCriterionItemViewModel(
                filter.ColumnName,
                filter.Operation,
                filter.Value)));

        CurrentPage = 1;
        RebuildRowsProjection();
    }

    private void ClearColumnFilter()
    {
        if (Session is null)
            return;

        Session.ViewState.Filters.Clear();
        ActiveFilterCriteria = [];

        _columnFilterValue = string.Empty;
        RaisePropertyChanged(nameof(ColumnFilterValue));
        RaisePropertyChanged(nameof(CanAddColumnFilter));
        (ApplyColumnFilterCommand as RelayCommand)?.NotifyCanExecuteChanged();

        CurrentPage = 1;
        RebuildRowsProjection();
    }

    private void UpdateAvailableFilterOperations()
    {
        DataTable? table = Session?.ResultSet.Data;
        IReadOnlyList<string> operations = ResolveFilterOperationsForColumn(table, SelectedFilterColumn);
        AvailableFilterOperations = operations;

        if (operations.Count == 0)
        {
            SelectedFilterOperation = FilterOperationContains;
            return;
        }

        if (!operations.Contains(SelectedFilterOperation, StringComparer.Ordinal))
            SelectedFilterOperation = operations[0];
    }

    private static IReadOnlyList<string> ResolveFilterOperationsForColumn(DataTable? table, string? columnName)
    {
        if (table is null || string.IsNullOrWhiteSpace(columnName) || !table.Columns.Contains(columnName))
            return TextFilterOperations;

        Type columnType = Nullable.GetUnderlyingType(table.Columns[columnName]!.DataType) ?? table.Columns[columnName]!.DataType;
        if (columnType == typeof(string) || columnType == typeof(Guid))
            return TextFilterOperations;

        if (columnType == typeof(bool))
            return [FilterOperationEquals, FilterOperationNotEquals];

        if (IsNumericType(columnType) || columnType == typeof(DateTime) || columnType == typeof(DateTimeOffset) || columnType == typeof(TimeSpan))
            return ComparableFilterOperations;

        return TextFilterOperations;
    }

    private static bool IsNumericType(Type type)
    {
        TypeCode code = Type.GetTypeCode(type);
        return code is TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16
            or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64
            or TypeCode.Single or TypeCode.Double or TypeCode.Decimal;
    }

    private void ShowAllColumns()
    {
        SqlResultSession? activeSession = Session;
        DataTable? table = activeSession?.ResultSet.Data;
        if (activeSession is null || table is null)
            return;

        activeSession.ViewState.VisibleColumns.Clear();
        foreach (DataColumn column in table.Columns)
            activeSession.ViewState.VisibleColumns.Add(column.ColumnName);

        foreach (SqlResultColumnVisibilityItemViewModel item in ColumnVisibilityItems)
            item.SetFromState(true);

        CurrentPage = 1;
        RebuildRowsProjection();
    }

    private void OnColumnVisibilityChanged(SqlResultColumnVisibilityItemViewModel item)
    {
        SqlResultSession? activeSession = Session;
        DataTable? table = activeSession?.ResultSet.Data;
        if (activeSession is null || table is null)
            return;

        if (!item.IsVisible && activeSession.ViewState.VisibleColumns.Count <= 1)
        {
            item.SetFromState(true);
            return;
        }

        if (item.IsVisible)
            activeSession.ViewState.VisibleColumns.Add(item.ColumnName);
        else
            activeSession.ViewState.VisibleColumns.Remove(item.ColumnName);

        CurrentPage = 1;
        RebuildRowsProjection();
    }

    private void MoveColumnUp(SqlResultColumnVisibilityItemViewModel? item)
    {
        if (!CanMoveColumnUp(item))
            return;

        SqlResultSession activeSession = Session!;
        int index = activeSession.ViewState.ColumnOrder.FindIndex(column =>
            string.Equals(column, item!.ColumnName, StringComparison.Ordinal));
        if (index <= 0)
            return;

        (activeSession.ViewState.ColumnOrder[index - 1], activeSession.ViewState.ColumnOrder[index]) =
            (activeSession.ViewState.ColumnOrder[index], activeSession.ViewState.ColumnOrder[index - 1]);

        RebuildRowsProjection();
    }

    private void MoveColumnDown(SqlResultColumnVisibilityItemViewModel? item)
    {
        if (!CanMoveColumnDown(item))
            return;

        SqlResultSession activeSession = Session!;
        int index = activeSession.ViewState.ColumnOrder.FindIndex(column =>
            string.Equals(column, item!.ColumnName, StringComparison.Ordinal));
        if (index < 0 || index >= activeSession.ViewState.ColumnOrder.Count - 1)
            return;

        (activeSession.ViewState.ColumnOrder[index], activeSession.ViewState.ColumnOrder[index + 1]) =
            (activeSession.ViewState.ColumnOrder[index + 1], activeSession.ViewState.ColumnOrder[index]);

        RebuildRowsProjection();
    }

    private void ToggleColumnFrozen(SqlResultColumnVisibilityItemViewModel? item)
    {
        if (Session is null || item is null)
            return;

        if (Session.ViewState.FrozenColumns.Contains(item.ColumnName))
            Session.ViewState.FrozenColumns.Remove(item.ColumnName);
        else
            Session.ViewState.FrozenColumns.Add(item.ColumnName);

        item.SetFrozenFromState(Session.ViewState.FrozenColumns.Contains(item.ColumnName));
        RebuildRowsProjection();
    }

    private bool CanMoveColumnUp(SqlResultColumnVisibilityItemViewModel? item)
    {
        if (Session is null || item is null)
            return false;

        int index = Session.ViewState.ColumnOrder.FindIndex(column =>
            string.Equals(column, item.ColumnName, StringComparison.Ordinal));
        return index > 0;
    }

    private bool CanMoveColumnDown(SqlResultColumnVisibilityItemViewModel? item)
    {
        if (Session is null || item is null)
            return false;

        int index = Session.ViewState.ColumnOrder.FindIndex(column =>
            string.Equals(column, item.ColumnName, StringComparison.Ordinal));
        return index >= 0 && index < Session.ViewState.ColumnOrder.Count - 1;
    }

    private void RefreshSessions()
    {
        if (_sessionService is null)
        {
            Sessions = Session is null ? [] : [Session];
            return;
        }

        Sessions = _sessionService.Sessions;
    }

    private HashSet<string> GetCollapsedGroupKeysForSession(Guid sessionId)
    {
        if (_sessionCollapsedGroupKeys.TryGetValue(sessionId, out HashSet<string>? existing))
            return existing;

        var created = new HashSet<string>(StringComparer.Ordinal);
        _sessionCollapsedGroupKeys[sessionId] = created;
        return created;
    }

    private static string BuildGroupKey(DataRow row, IReadOnlyList<string> groupedColumns)
    {
        var parts = new List<string>(groupedColumns.Count);
        for (int i = 0; i < groupedColumns.Count; i++)
        {
            object? value = row[groupedColumns[i]];
            parts.Add(value is null || value == DBNull.Value ? "<null>" : value.ToString() ?? string.Empty);
        }

        return string.Join(" | ", parts);
    }

    private static IEnumerable<DataRow> ApplyGroupingAndSortCriteria(
        IEnumerable<DataRow> rows,
        DataTable table,
        IReadOnlyList<string>? groupedColumns,
        IReadOnlyList<SqlColumnSort>? sorts)
    {
        var criteria = new List<(string ColumnName, bool Descending)>();

        if (groupedColumns is not null)
        {
            foreach (string groupedColumn in groupedColumns)
            {
                if (string.IsNullOrWhiteSpace(groupedColumn) || !table.Columns.Contains(groupedColumn))
                    continue;

                if (criteria.Any(existing => string.Equals(existing.ColumnName, groupedColumn, StringComparison.Ordinal)))
                    continue;

                criteria.Add((groupedColumn, false));
            }
        }

        if (sorts is not null)
        {
            foreach (SqlColumnSort sort in sorts)
            {
                if (string.IsNullOrWhiteSpace(sort.ColumnName) || !table.Columns.Contains(sort.ColumnName))
                    continue;

                if (criteria.Any(existing => string.Equals(existing.ColumnName, sort.ColumnName, StringComparison.Ordinal)))
                    continue;

                criteria.Add((sort.ColumnName, sort.Descending));
            }
        }

        if (criteria.Count == 0)
            return rows;

        IOrderedEnumerable<DataRow>? orderedRows = null;
        foreach ((string columnName, bool descending) in criteria)
        {
            if (orderedRows is null)
            {
                orderedRows = descending
                    ? rows.OrderByDescending(row => row[columnName], ValueComparer.Instance)
                    : rows.OrderBy(row => row[columnName], ValueComparer.Instance);
                continue;
            }

            orderedRows = descending
                ? orderedRows.ThenByDescending(row => row[columnName], ValueComparer.Instance)
                : orderedRows.ThenBy(row => row[columnName], ValueComparer.Instance);
        }

        return orderedRows ?? rows;
    }

    private static IEnumerable<DataRow> ApplyFilterCriteria(
        IEnumerable<DataRow> rows,
        DataTable table,
        IReadOnlyList<SqlColumnFilter>? filters)
    {
        if (filters is null || filters.Count == 0)
            return rows;

        return rows.Where(row => filters.All(filter => MatchesFilter(row, table, filter)));
    }

    private static bool MatchesFilter(DataRow row, DataTable table, SqlColumnFilter filter)
    {
        if (string.IsNullOrWhiteSpace(filter.ColumnName) || !table.Columns.Contains(filter.ColumnName))
            return true;

        object? value = row[filter.ColumnName];
        if (value is null || value == DBNull.Value)
            return false;

        string operation = (filter.Operation ?? FilterOperationContains).Trim().ToLowerInvariant();
        string input = filter.Value?.Trim() ?? string.Empty;
        string valueText = value.ToString() ?? string.Empty;

        return operation switch
        {
            FilterOperationContains => valueText.Contains(input, StringComparison.OrdinalIgnoreCase),
            FilterOperationStartsWith => valueText.StartsWith(input, StringComparison.OrdinalIgnoreCase),
            FilterOperationEndsWith => valueText.EndsWith(input, StringComparison.OrdinalIgnoreCase),
            FilterOperationEquals => CompareForFilter(value, input, out int eqCmp) && eqCmp == 0,
            FilterOperationNotEquals => CompareForFilter(value, input, out int neCmp) && neCmp != 0,
            FilterOperationGreaterThan => CompareForFilter(value, input, out int gtCmp) && gtCmp > 0,
            FilterOperationGreaterThanOrEqual => CompareForFilter(value, input, out int gteCmp) && gteCmp >= 0,
            FilterOperationLessThan => CompareForFilter(value, input, out int ltCmp) && ltCmp < 0,
            FilterOperationLessThanOrEqual => CompareForFilter(value, input, out int lteCmp) && lteCmp <= 0,
            _ => valueText.Contains(input, StringComparison.OrdinalIgnoreCase),
        };
    }

    private static bool CompareForFilter(object currentValue, string filterInput, out int comparison)
    {
        comparison = 0;

        object normalizedValue = currentValue;
        Type targetType = Nullable.GetUnderlyingType(normalizedValue.GetType()) ?? normalizedValue.GetType();
        if (targetType == typeof(DateTimeOffset))
        {
            if (!DateTimeOffset.TryParse(filterInput, out DateTimeOffset parsedOffset))
                return false;

            comparison = ((DateTimeOffset)normalizedValue).CompareTo(parsedOffset);
            return true;
        }

        if (targetType == typeof(DateTime))
        {
            if (!DateTime.TryParse(filterInput, out DateTime parsedDate))
                return false;

            comparison = ((DateTime)normalizedValue).CompareTo(parsedDate);
            return true;
        }

        if (targetType == typeof(TimeSpan))
        {
            if (!TimeSpan.TryParse(filterInput, out TimeSpan parsedSpan))
                return false;

            comparison = ((TimeSpan)normalizedValue).CompareTo(parsedSpan);
            return true;
        }

        if (targetType == typeof(bool))
        {
            if (!bool.TryParse(filterInput, out bool parsedBool))
                return false;

            comparison = ((bool)normalizedValue).CompareTo(parsedBool);
            return true;
        }

        if (IsNumericType(targetType))
        {
            if (!decimal.TryParse(Convert.ToString(normalizedValue), out decimal left))
                return false;
            if (!decimal.TryParse(filterInput, out decimal right))
                return false;

            comparison = left.CompareTo(right);
            return true;
        }

        string leftText = normalizedValue.ToString() ?? string.Empty;
        comparison = string.Compare(leftText, filterInput, StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private void RebuildRowsProjection()
    {
        DataTable? table = Session?.ResultSet.Data;
        if (table is null)
        {
            _pagedRowsTable = null;
            _totalFilteredRows = 0;
            TotalPages = 1;
            CurrentPage = 1;
            AvailableSortColumns = [];
            ActiveGroupColumns = [];
            ActiveSortCriteria = [];
            AvailableFilterOperations = TextFilterOperations;
            ActiveFilterCriteria = [];
            GroupBuckets = [];
            ColumnVisibilityItems = [];
            FrozenColumnCount = 0;
            SelectedRowFields = [];
            SelectedRowJson = "{}";
            _selectedRowItem = null;
            RaisePropertyChanged(nameof(SelectedRowItem));
            RaisePropertyChanged(nameof(SelectedRowSummary));
            RaisePropertyChanged(nameof(RowsView));
            RaisePropertyChanged(nameof(RowCountText));
            return;
        }

        SqlResultViewState? viewState = Session?.ViewState;
        List<string> allColumns = table.Columns.Cast<DataColumn>().Select(column => column.ColumnName).ToList();
        AvailableSortColumns = allColumns;
        UpdateAvailableFilterOperations();

        EnsureViewStateColumns(viewState, allColumns);
        List<string> orderedColumns = ResolveOrderedColumns(viewState, allColumns);
        HashSet<string> visibleSet = viewState?.VisibleColumns.Count > 0
            ? viewState.VisibleColumns
            : allColumns.ToHashSet(StringComparer.Ordinal);
        HashSet<string> frozenSet = viewState?.FrozenColumns.Count > 0
            ? viewState.FrozenColumns
            : [];

        List<string> visibleOrderedColumns = orderedColumns
            .Where(column => visibleSet.Contains(column))
            .ToList();
        if (visibleOrderedColumns.Count == 0 && orderedColumns.Count > 0)
        {
            visibleOrderedColumns.Add(orderedColumns[0]);
            viewState?.VisibleColumns.Add(orderedColumns[0]);
        }

        List<string> frozenVisibleColumns = visibleOrderedColumns
            .Where(column => frozenSet.Contains(column))
            .ToList();
        List<string> nonFrozenVisibleColumns = visibleOrderedColumns
            .Where(column => !frozenSet.Contains(column))
            .ToList();
        visibleOrderedColumns = [.. frozenVisibleColumns, .. nonFrozenVisibleColumns];
        FrozenColumnCount = frozenVisibleColumns.Count;

        ColumnVisibilityItems = new ObservableCollection<SqlResultColumnVisibilityItemViewModel>(
            orderedColumns.Select(column => new SqlResultColumnVisibilityItemViewModel(
                column,
                visibleSet.Contains(column),
                frozenSet.Contains(column),
                OnColumnVisibilityChanged)));

        IEnumerable<DataRow> rows = table.Rows.Cast<DataRow>();
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string needle = SearchText.Trim();
            rows = rows.Where(row => RowMatchesSearch(row, needle, visibleOrderedColumns));
        }

        rows = ApplyFilterCriteria(rows, table, viewState?.Filters);

        rows = ApplyGroupingAndSortCriteria(rows, table, viewState?.GroupedColumns, viewState?.Sorts);

        List<DataRow> groupedRows = rows.ToList();
        List<string> groupedColumns = viewState?.GroupedColumns
            .Where(column => table.Columns.Contains(column))
            .ToList()
            ?? [];

        if (Session is not null && groupedColumns.Count > 0)
        {
            HashSet<string> collapsedGroupKeys = GetCollapsedGroupKeysForSession(Session.Id);
            Dictionary<string, int> bucketCounts = groupedRows
                .GroupBy(row => BuildGroupKey(row, groupedColumns), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

            GroupBuckets = new ObservableCollection<SqlResultGroupBucketItemViewModel>(
                bucketCounts
                    .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .Select(entry => new SqlResultGroupBucketItemViewModel(
                        entry.Key,
                        entry.Value,
                        collapsedGroupKeys.Contains(entry.Key))));

            if (collapsedGroupKeys.Count > 0)
            {
                groupedRows = groupedRows
                    .Where(row => !collapsedGroupKeys.Contains(BuildGroupKey(row, groupedColumns)))
                    .ToList();
            }
        }
        else
        {
            GroupBuckets = [];
        }

        List<DataRow> filteredRows = groupedRows;
        _totalFilteredRows = filteredRows.Count;
        int computedTotalPages = Math.Max(1, (int)Math.Ceiling(_totalFilteredRows / (double)DefaultPageSize));
        TotalPages = computedTotalPages;
        CurrentPage = Math.Min(CurrentPage, TotalPages);

        int skip = (CurrentPage - 1) * DefaultPageSize;
        List<DataRow> pageRows = filteredRows.Skip(skip).Take(DefaultPageSize).ToList();

        DataTable projected = BuildProjectedTable(table, visibleOrderedColumns);
        foreach (DataRow row in pageRows)
            projected.Rows.Add(BuildProjectedRow(projected, row, visibleOrderedColumns));

        _pagedRowsTable = projected;
        RaisePropertyChanged(nameof(RowsView));
        RaisePropertyChanged(nameof(RowCountText));
        RestoreSelectedRowFromSessionState();
    }

    private static void EnsureViewStateColumns(SqlResultViewState? viewState, IReadOnlyList<string> allColumns)
    {
        if (viewState is null)
            return;

        foreach (string column in allColumns)
        {
            if (!viewState.ColumnOrder.Contains(column))
                viewState.ColumnOrder.Add(column);
        }

        if (viewState.VisibleColumns.Count == 0)
        {
            foreach (string column in allColumns)
                viewState.VisibleColumns.Add(column);
        }

        viewState.ColumnOrder = viewState.ColumnOrder
            .Where(allColumns.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        viewState.VisibleColumns = viewState.VisibleColumns
            .Where(allColumns.Contains)
            .ToHashSet(StringComparer.Ordinal);
        viewState.FrozenColumns = viewState.FrozenColumns
            .Where(allColumns.Contains)
            .ToHashSet(StringComparer.Ordinal);
        viewState.GroupedColumns = viewState.GroupedColumns
            .Where(allColumns.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static List<string> ResolveOrderedColumns(SqlResultViewState? viewState, IReadOnlyList<string> allColumns)
    {
        if (viewState is null)
            return allColumns.ToList();

        if (viewState.ColumnOrder.Count == 0)
            return allColumns.ToList();

        return viewState.ColumnOrder.Where(allColumns.Contains).ToList();
    }

    private static DataTable BuildProjectedTable(DataTable sourceTable, IReadOnlyList<string> visibleOrderedColumns)
    {
        var projected = new DataTable(sourceTable.TableName);
        foreach (string columnName in visibleOrderedColumns)
        {
            DataColumn sourceColumn = sourceTable.Columns[columnName]!;
            var cloned = new DataColumn(sourceColumn.ColumnName, sourceColumn.DataType)
            {
                AllowDBNull = sourceColumn.AllowDBNull,
                Caption = sourceColumn.Caption,
                MaxLength = sourceColumn.MaxLength,
                ReadOnly = sourceColumn.ReadOnly,
            };
            projected.Columns.Add(cloned);
        }

        return projected;
    }

    private static object[] BuildProjectedRow(DataTable projectedTable, DataRow sourceRow, IReadOnlyList<string> visibleOrderedColumns)
    {
        object[] values = new object[projectedTable.Columns.Count];
        for (int i = 0; i < visibleOrderedColumns.Count; i++)
            values[i] = sourceRow[visibleOrderedColumns[i]];

        return values;
    }

    private static bool RowMatchesSearch(DataRow row, string needle, IReadOnlyList<string> visibleOrderedColumns)
    {
        for (int i = 0; i < visibleOrderedColumns.Count; i++)
        {
            object? value = row[visibleOrderedColumns[i]];
            if (value is null || value == DBNull.Value)
                continue;

            if (value.ToString()?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true)
                return true;
        }

        return false;
    }

    private void NotifyCommands()
    {
        (TogglePinCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (CloseSessionCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (NextPageCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (PreviousPageCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (FirstPageCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (LastPageCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ClearColumnFilterCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ApplyColumnFilterCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (AddGroupColumnCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ClearGroupColumnsCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (AddSortCriterionCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ClearSortCriteriaCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (MoveColumnUpCommand as RelayCommand<SqlResultColumnVisibilityItemViewModel>)?.NotifyCanExecuteChanged();
        (MoveColumnDownCommand as RelayCommand<SqlResultColumnVisibilityItemViewModel>)?.NotifyCanExecuteChanged();
        (ClearSelectedRowCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    public sealed class SqlResultSortCriterionItemViewModel
    {
        public SqlResultSortCriterionItemViewModel(string columnName, bool ascending)
        {
            ColumnName = columnName;
            Ascending = ascending;
        }

        public string ColumnName { get; }
        public bool Ascending { get; }
        public string DirectionText => Ascending ? "Asc" : "Desc";
    }

    public sealed class SqlResultGroupColumnItemViewModel
    {
        public SqlResultGroupColumnItemViewModel(string columnName)
        {
            ColumnName = columnName;
        }

        public string ColumnName { get; }
    }

    public sealed class SqlResultGroupBucketItemViewModel
    {
        public SqlResultGroupBucketItemViewModel(string groupKey, int rowCount, bool isCollapsed)
        {
            GroupKey = groupKey;
            RowCount = rowCount;
            IsCollapsed = isCollapsed;
        }

        public string GroupKey { get; }
        public int RowCount { get; }
        public bool IsCollapsed { get; }
        public string CollapseActionText => IsCollapsed ? "Expand" : "Collapse";
    }

    public sealed class SqlResultFilterCriterionItemViewModel
    {
        public SqlResultFilterCriterionItemViewModel(string columnName, string operation, string? value)
        {
            ColumnName = columnName;
            Operation = operation;
            Value = value ?? string.Empty;
        }

        public string ColumnName { get; }
        public string Operation { get; }
        public string Value { get; }
        public string OperationText => Operation switch
        {
            FilterOperationContains => "contains",
            FilterOperationEquals => "=",
            FilterOperationNotEquals => "!=",
            FilterOperationStartsWith => "starts",
            FilterOperationEndsWith => "ends",
            FilterOperationGreaterThan => ">",
            FilterOperationGreaterThanOrEqual => ">=",
            FilterOperationLessThan => "<",
            FilterOperationLessThanOrEqual => "<=",
            _ => Operation,
        };
    }

    public sealed class SqlResultSelectedRowFieldItemViewModel
    {
        public SqlResultSelectedRowFieldItemViewModel(string columnName, string value)
        {
            ColumnName = columnName;
            Value = value;
        }

        public string ColumnName { get; }
        public string Value { get; }
    }

    public sealed class SqlResultColumnVisibilityItemViewModel : ViewModelBase
    {
        private readonly Action<SqlResultColumnVisibilityItemViewModel> _onVisibilityChanged;
        private bool _isVisible;
        private bool _isFrozen;

        public SqlResultColumnVisibilityItemViewModel(
            string columnName,
            bool isVisible,
            bool isFrozen,
            Action<SqlResultColumnVisibilityItemViewModel> onVisibilityChanged)
        {
            ColumnName = columnName;
            _isVisible = isVisible;
            _isFrozen = isFrozen;
            _onVisibilityChanged = onVisibilityChanged;
        }

        public string ColumnName { get; }

        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (!Set(ref _isVisible, value))
                    return;

                _onVisibilityChanged(this);
            }
        }

        public bool IsFrozen
        {
            get => _isFrozen;
            private set
            {
                if (!Set(ref _isFrozen, value))
                    return;
                RaisePropertyChanged(nameof(FrozenActionText));
            }
        }

        public string FrozenActionText => IsFrozen ? "Unfreeze" : "Freeze";

        public void SetFromState(bool isVisible)
        {
            if (_isVisible == isVisible)
                return;

            _isVisible = isVisible;
            RaisePropertyChanged(nameof(IsVisible));
        }

        public void SetFrozenFromState(bool isFrozen)
        {
            IsFrozen = isFrozen;
        }
    }

    private sealed class ValueComparer : IComparer<object?>
    {
        public static readonly ValueComparer Instance = new();

        public int Compare(object? x, object? y)
        {
            bool xNull = x is null || x == DBNull.Value;
            bool yNull = y is null || y == DBNull.Value;
            if (xNull && yNull)
                return 0;
            if (xNull)
                return 1;
            if (yNull)
                return -1;

            object xValue = x!;
            object yValue = y!;

            if (xValue is IComparable xComparable && yValue.GetType() == xValue.GetType())
                return xComparable.CompareTo(yValue);

            string xText = xValue.ToString() ?? string.Empty;
            string yText = yValue.ToString() ?? string.Empty;
            return string.CompareOrdinal(xText, yText);
        }
    }
}
