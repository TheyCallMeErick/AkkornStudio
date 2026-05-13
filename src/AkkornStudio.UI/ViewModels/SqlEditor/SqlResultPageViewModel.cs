using System.Data;
using System.Windows.Input;
using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.UI.Services.SqlEditor;
using AkkornStudio.UI.Services.SqlEditor.Results;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace AkkornStudio.UI.ViewModels;

public sealed class SqlResultPageViewModel : ViewModelBase
{
    private const int DefaultPageSize = 100;
    private const int DefaultRefreshMaxRows = 1000;

    private readonly Dictionary<Guid, Guid?> _sessionSourceEditorDocumentMap = [];
    private readonly Dictionary<Guid, HashSet<string>> _sessionCollapsedGroupKeys = [];
    private SqlResultSession? _session;
    private IReadOnlyList<SqlResultSession> _sessions = [];
    private Guid? _sourceSqlEditorDocumentId;
    private Action<Guid?>? _navigateBackToEditor;
    private SqlResultSessionService? _sessionService;
    private DataTable? _pagedRowsTable;
    private IReadOnlyList<DataRow> _filteredRows = [];
    private IReadOnlyList<DataRow> _pagedSourceRows = [];
    private object? _selectedRowItem;
    private bool _isResultDetailVisible;
    private ObservableCollection<SqlResultSelectedRowFieldItemViewModel> _selectedRowFields = [];
    private string _selectedRowJson = "{}";
    private string _generatedWhereClause = string.Empty;
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
    private ObservableCollection<SqlResultColumnProfileItemViewModel> _columnProfiles = [];
    private int _frozenColumnCount;
    private readonly MutationGuardService _mutationGuardService;
    private readonly SqlMutationDiffService _mutationDiffService;
    private readonly SqlEditorMutationExecutionOrchestrator _mutationExecutionOrchestrator;
    private readonly SqlResultTransactionExecutionService _transactionExecutionService;
    private readonly Func<string?, ConnectionConfig?, int, CancellationToken, Task<SqlEditorResultSet>> _executeSqlAsync;
    private readonly Func<ConnectionConfig, IReadOnlyList<string>, bool, CancellationToken, Task<SqlResultTransactionExecutionResult>> _executeTransactionalPendingChangesAsync;
    private SqlResultChangeSet? _pendingChangeSetPreview;
    private string _pendingChangeSetSummaryText = string.Empty;
    private string _pendingDiffPreviewText = string.Empty;
    private string _generatedPendingSqlText = string.Empty;
    private bool _isPreparingPendingPreview;
    private bool _isPendingExecutionConfirmationVisible;
    private bool _isExecutingPendingChanges;
    private bool _isRefreshingSession;
    private string _pendingExecutionStatusText = string.Empty;
    private bool _hasPendingExecutionError;
    private bool _useTransactionalExecution;
    private bool _isBuildingColumnProfiles;
    private string _columnProfileStatusText = string.Empty;
    private string _sessionAnnotationText = string.Empty;
    private Action<Guid?, string>? _appendSqlToEditor;
    private Func<string?, ConnectionConfig?>? _connectionConfigBySessionResolver;
    private Func<DbMetadata?>? _metadataResolver;
    private readonly SqlResultColumnProfilingService _columnProfilingService;
    private readonly ISqlResultSnippetStore _snippetStore;
    private ObservableCollection<SqlSavedQuerySnippet> _savedSnippets = [];
    private SqlSavedQuerySnippet? _selectedSnippet;
    private string _snippetNameInput = string.Empty;
    private string _snippetDescriptionInput = string.Empty;
    private string _snippetTagsInput = string.Empty;
    private IReadOnlyList<SqlQuickTemplateOption> _availableSqlTemplates = [];
    private SqlQuickTemplateOption? _selectedSqlTemplate;
    private IReadOnlyList<SqlTableQuickActionOption> _availableTableQuickActions = [];

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
        : this(
            new MutationGuardService(),
            new SqlMutationDiffService(new SqlEditorExecutionService()),
            null,
            null,
            null,
            null)
    {
    }

    internal SqlResultPageViewModel(
        MutationGuardService mutationGuardService,
        SqlMutationDiffService mutationDiffService,
        SqlEditorMutationExecutionOrchestrator? mutationExecutionOrchestrator,
        Func<string?, ConnectionConfig?, int, CancellationToken, Task<SqlEditorResultSet>>? executeSqlAsync,
        Func<ConnectionConfig, IReadOnlyList<string>, bool, CancellationToken, Task<SqlResultTransactionExecutionResult>>? executeTransactionalPendingChangesAsync = null,
        ISqlResultSnippetStore? snippetStore = null)
    {
        _mutationGuardService = mutationGuardService ?? throw new ArgumentNullException(nameof(mutationGuardService));
        _mutationDiffService = mutationDiffService ?? throw new ArgumentNullException(nameof(mutationDiffService));
        _mutationExecutionOrchestrator = mutationExecutionOrchestrator
            ?? new SqlEditorMutationExecutionOrchestrator(
                new SqlEditorExecutionService(),
                _mutationGuardService,
                _mutationDiffService);
        _transactionExecutionService = new SqlResultTransactionExecutionService();
        _columnProfilingService = new SqlResultColumnProfilingService();
        _snippetStore = snippetStore ?? new FileSqlResultSnippetStore();
        _executeSqlAsync = executeSqlAsync ?? new SqlEditorExecutionService().ExecuteAsync;
        _executeTransactionalPendingChangesAsync = executeTransactionalPendingChangesAsync
            ?? _transactionExecutionService.ExecuteAsync;

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
        CloseSessionTabCommand = new RelayCommand<SqlResultSession>(CloseSessionTab);
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
        BuildColumnProfilesCommand = new RelayCommand(
            () => _ = BuildColumnProfilesAsync(),
            () => Session?.ResultSet.Data is not null && !_isBuildingColumnProfiles);
        MoveColumnUpCommand = new RelayCommand<SqlResultColumnVisibilityItemViewModel>(MoveColumnUp, CanMoveColumnUp);
        MoveColumnDownCommand = new RelayCommand<SqlResultColumnVisibilityItemViewModel>(MoveColumnDown, CanMoveColumnDown);
        ToggleColumnFrozenCommand = new RelayCommand<SqlResultColumnVisibilityItemViewModel>(ToggleColumnFrozen);
        ClearSelectedRowCommand = new RelayCommand(ClearSelectedRow, () => HasSelectedRow);
        ShowSelectedRowDetailsCommand = new RelayCommand(ShowSelectedRowDetails, () => CanShowSelectedRowDetails);
        HideSelectedRowDetailsCommand = new RelayCommand(HideSelectedRowDetails, () => IsResultDetailVisible);
        CopySelectedCellCommand = new RelayCommand(CopySelectedCell, () => HasSelectedCell);
        CopySelectedRowAsJsonCommand = new RelayCommand(CopySelectedRowAsJson, () => HasSelectedRow);
        CopySelectedRowAsCsvCommand = new RelayCommand(CopySelectedRowAsCsv, () => HasSelectedRow);
        CopySelectedRowAsMarkdownCommand = new RelayCommand(CopySelectedRowAsMarkdown, () => HasSelectedRow);
        CopyVisibleRowsAsJsonCommand = new RelayCommand(CopyVisibleRowsAsJson, () => HasVisibleRows);
        CopyVisibleRowsAsCsvCommand = new RelayCommand(CopyVisibleRowsAsCsv, () => HasVisibleRows);
        CopyVisibleRowsAsMarkdownCommand = new RelayCommand(CopyVisibleRowsAsMarkdown, () => HasVisibleRows);
        ExportVisibleRowsAsJsonCommand = new RelayCommand(ExportVisibleRowsAsJson, () => HasVisibleRows);
        ExportVisibleRowsAsCsvCommand = new RelayCommand(ExportVisibleRowsAsCsv, () => HasVisibleRows);
        CopySelectedColumnAsSqlInCommand = new RelayCommand(CopySelectedColumnAsSqlIn, () => HasSelectedCell);
        GenerateWhereClauseCommand = new RelayCommand(GenerateWhereClause, () => CanGenerateWhereClause);
        FilterBySelectedCellValueCommand = new RelayCommand(FilterBySelectedCellValue, () => HasSelectedCell);
        HideSelectedCellColumnCommand = new RelayCommand(HideSelectedCellColumn, () => HasSelectedCell);
        SortSelectedColumnAscendingCommand = new RelayCommand(() => SortSelectedColumn(descending: false), () => HasSelectedCell);
        SortSelectedColumnDescendingCommand = new RelayCommand(() => SortSelectedColumn(descending: true), () => HasSelectedCell);
        CancelPendingEditsCommand = new RelayCommand(CancelPendingEdits, () => CanCancelPendingEdits);
        PreparePendingChangesPreviewCommand = new RelayCommand(
            () => _ = PreparePendingChangesPreviewAsync(),
            () => CanPreparePendingChangesPreview);
        ClearPendingChangesPreviewCommand = new RelayCommand(
            ClearPendingChangesPreview,
            () => HasPendingChangeSetPreview || HasGeneratedPendingSqlText);
        GeneratePendingSqlCommand = new RelayCommand(
            GeneratePendingSqlFromCurrentEdits,
            () => CanGeneratePendingSql);
        CopyGeneratedPendingSqlCommand = new RelayCommand(
            CopyGeneratedPendingSql,
            () => CanCopyGeneratedPendingSql);
        SendGeneratedPendingSqlToEditorCommand = new RelayCommand(
            SendGeneratedPendingSqlToEditor,
            () => CanSendGeneratedPendingSqlToEditor);
        RefreshSessionCommand = new RelayCommand(
            () => _ = RefreshCurrentSessionAsync(),
            () => CanRefreshSession);
        RequestExecutePendingChangesCommand = new RelayCommand(
            RequestExecutePendingChanges,
            () => CanRequestExecutePendingChanges);
        ConfirmExecutePendingChangesCommand = new RelayCommand(
            () => _ = ConfirmExecutePendingChangesAsync(),
            () => CanConfirmExecutePendingChanges);
        ConfirmExecutePendingChangesRollbackCommand = new RelayCommand(
            () => _ = ConfirmExecutePendingChangesWithRollbackAsync(),
            () => CanConfirmExecutePendingChangesWithRollback);
        CancelExecutePendingChangesCommand = new RelayCommand(
            CancelExecutePendingChanges,
            () => CanCancelExecutePendingChanges);
        SaveSessionAnnotationCommand = new RelayCommand(
            SaveSessionAnnotation,
            () => CanSaveSessionAnnotation);
        ClearSessionAnnotationCommand = new RelayCommand(
            ClearSessionAnnotation,
            () => CanClearSessionAnnotation);
        SaveCurrentSqlAsSnippetCommand = new RelayCommand(
            SaveCurrentSqlAsSnippet,
            () => CanSaveCurrentSqlAsSnippet);
        ToggleCurrentSqlFavoriteCommand = new RelayCommand(
            ToggleCurrentSqlFavorite,
            () => CanToggleCurrentSqlFavorite);
        OpenSelectedSnippetInEditorCommand = new RelayCommand(
            OpenSelectedSnippetInEditor,
            () => CanOpenSelectedSnippetInEditor);
        OpenSnippetInEditorCommand = new RelayCommand<SqlSavedQuerySnippet>(
            OpenSnippetInEditor,
            CanOpenSnippetInEditor);
        DeleteSelectedSnippetCommand = new RelayCommand(
            DeleteSelectedSnippet,
            () => CanDeleteSelectedSnippet);
        DeleteSnippetCommand = new RelayCommand<SqlSavedQuerySnippet>(DeleteSnippet);
        SendSelectedSqlTemplateToEditorCommand = new RelayCommand(
            SendSelectedSqlTemplateToEditor,
            () => CanSendSelectedSqlTemplateToEditor);
        SendTableQuickActionToEditorCommand = new RelayCommand<SqlTableQuickActionOption>(
            SendTableQuickActionToEditor,
            CanSendTableQuickActionToEditor);
        NavigateSelectedForeignKeyCommand = new RelayCommand(
            NavigateSelectedForeignKey,
            () => CanNavigateSelectedForeignKey);

        ReloadSavedSnippets();
    }

    public ICommand BackToEditorCommand { get; }
    public ICommand SelectSessionCommand { get; }
    public ICommand TogglePinCommand { get; }
    public ICommand CloseSessionCommand { get; }
    public ICommand CloseSessionTabCommand { get; }
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
    public ICommand BuildColumnProfilesCommand { get; }
    public ICommand MoveColumnUpCommand { get; }
    public ICommand MoveColumnDownCommand { get; }
    public ICommand ToggleColumnFrozenCommand { get; }
    public ICommand ClearSelectedRowCommand { get; }
    public ICommand ShowSelectedRowDetailsCommand { get; }
    public ICommand HideSelectedRowDetailsCommand { get; }
    public ICommand CopySelectedCellCommand { get; }
    public ICommand CopySelectedRowAsJsonCommand { get; }
    public ICommand CopySelectedRowAsCsvCommand { get; }
    public ICommand CopySelectedRowAsMarkdownCommand { get; }
    public ICommand CopyVisibleRowsAsJsonCommand { get; }
    public ICommand CopyVisibleRowsAsCsvCommand { get; }
    public ICommand CopyVisibleRowsAsMarkdownCommand { get; }
    public ICommand ExportVisibleRowsAsJsonCommand { get; }
    public ICommand ExportVisibleRowsAsCsvCommand { get; }
    public ICommand CopySelectedColumnAsSqlInCommand { get; }
    public ICommand GenerateWhereClauseCommand { get; }
    public ICommand FilterBySelectedCellValueCommand { get; }
    public ICommand HideSelectedCellColumnCommand { get; }
    public ICommand SortSelectedColumnAscendingCommand { get; }
    public ICommand SortSelectedColumnDescendingCommand { get; }
    public ICommand CancelPendingEditsCommand { get; }
    public ICommand PreparePendingChangesPreviewCommand { get; }
    public ICommand ClearPendingChangesPreviewCommand { get; }
    public ICommand GeneratePendingSqlCommand { get; }
    public ICommand CopyGeneratedPendingSqlCommand { get; }
    public ICommand SendGeneratedPendingSqlToEditorCommand { get; }
    public ICommand RefreshSessionCommand { get; }
    public ICommand RequestExecutePendingChangesCommand { get; }
    public ICommand ConfirmExecutePendingChangesCommand { get; }
    public ICommand ConfirmExecutePendingChangesRollbackCommand { get; }
    public ICommand CancelExecutePendingChangesCommand { get; }
    public ICommand SaveSessionAnnotationCommand { get; }
    public ICommand ClearSessionAnnotationCommand { get; }
    public ICommand SaveCurrentSqlAsSnippetCommand { get; }
    public ICommand ToggleCurrentSqlFavoriteCommand { get; }
    public ICommand OpenSelectedSnippetInEditorCommand { get; }
    public ICommand OpenSnippetInEditorCommand { get; }
    public ICommand DeleteSelectedSnippetCommand { get; }
    public ICommand DeleteSnippetCommand { get; }
    public ICommand SendSelectedSqlTemplateToEditorCommand { get; }
    public ICommand SendTableQuickActionToEditorCommand { get; }
    public ICommand NavigateSelectedForeignKeyCommand { get; }
    public event Action<string>? ClipboardCopyRequested;
    public event Action<SqlResultExportRequest>? ExportRequested;

    public SqlResultSession? Session
    {
        get => _session;
        private set
        {
            if (!Set(ref _session, value))
                return;

            ResetPendingChangeArtifacts();
            ResetColumnProfiles();
            IsResultDetailVisible = false;
            ApplySessionTransactionModePreference(value);
            UpdatePendingExecutionStatus(string.Empty, hasError: false);
            RaisePropertyChanged(nameof(HasSession));
            RaisePropertyChanged(nameof(SqlText));
            RaisePropertyChanged(nameof(ConnectionId));
            RaisePropertyChanged(nameof(DatabaseName));
            RaisePropertyChanged(nameof(ResultBreadcrumbText));
            RaisePropertyChanged(nameof(ExecutedAtText));
            RaisePropertyChanged(nameof(DurationText));
            RaisePropertyChanged(nameof(StatusText));
            RaisePropertyChanged(nameof(RowCountText));
            RaisePropertyChanged(nameof(ColumnCountText));
            RaisePropertyChanged(nameof(IsCurrentSessionPinned));
            RaisePropertyChanged(nameof(TogglePinText));
            RaisePropertyChanged(nameof(SelectedSession));
            RaisePropertyChanged(nameof(EditabilityStatusText));
            RaisePropertyChanged(nameof(IsEditableSession));
            RaisePropertyChanged(nameof(HasPendingEdits));
            RaisePropertyChanged(nameof(PendingEditsCount));
            RaisePropertyChanged(nameof(CanCancelPendingEdits));
            RaisePropertyChanged(nameof(CanPreparePendingChangesPreview));
            RaisePropertyChanged(nameof(CanGeneratePendingSql));
            RaisePropertyChanged(nameof(HasColumnProfiles));
            RaisePropertyChanged(nameof(IsBuildingColumnProfiles));
            RaisePropertyChanged(nameof(ColumnProfileStatusText));
            RaisePropertyChanged(nameof(HasColumnProfileStatusText));
            RaisePropertyChanged(nameof(HasPendingPreviewPanel));
            RaisePropertyChanged(nameof(CanRefreshSession));
            RaisePropertyChanged(nameof(UseTransactionalExecution));
            RaisePropertyChanged(nameof(IsTransactionModeAvailable));
            RaisePropertyChanged(nameof(TransactionModeStatusText));
            RaisePropertyChanged(nameof(HasTransactionModeStatusText));
            RaisePropertyChanged(nameof(HasTransactionRollbackOption));
            RaisePropertyChanged(nameof(ConfirmExecutePendingChangesText));
            RaisePropertyChanged(nameof(IsProductionLikeConnectionContext));
            RaisePropertyChanged(nameof(CanRequestExecutePendingChanges));
            RaisePropertyChanged(nameof(CanConfirmExecutePendingChanges));
            RaisePropertyChanged(nameof(CanConfirmExecutePendingChangesWithRollback));
            RaisePropertyChanged(nameof(CanCancelExecutePendingChanges));
            RaisePropertyChanged(nameof(SessionAnnotationText));
            RaisePropertyChanged(nameof(HasSessionAnnotation));
            RaisePropertyChanged(nameof(CanSaveSessionAnnotation));
            RaisePropertyChanged(nameof(CanClearSessionAnnotation));
            RaisePropertyChanged(nameof(IsCurrentSqlFavorite));
            RaisePropertyChanged(nameof(ToggleCurrentSqlFavoriteText));
            RaisePropertyChanged(nameof(CanSaveCurrentSqlAsSnippet));
            RaisePropertyChanged(nameof(CanToggleCurrentSqlFavorite));
            RaisePropertyChanged(nameof(HasVisibleRows));
            RaisePropertyChanged(nameof(HasSelectedCell));
            RaisePropertyChanged(nameof(SelectedCellSummary));
            RaisePropertyChanged(nameof(CanGenerateWhereClause));
            RaisePropertyChanged(nameof(CanNavigateSelectedForeignKey));
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
    public string ResultBreadcrumbText
    {
        get
        {
            if (Session is null)
                return "Home > Editor SQL > Resultado";

            string connection = string.IsNullOrWhiteSpace(Session.ConnectionId) ? "-" : Session.ConnectionId;
            string database = string.IsNullOrWhiteSpace(Session.DatabaseName) ? "-" : Session.DatabaseName!;
            string schema = string.IsNullOrWhiteSpace(Session.SchemaName) ? "-" : Session.SchemaName!;
            return $"{connection} > {database} > {schema} > Resultado";
        }
    }
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
            RaisePropertyChanged(nameof(CanShowSelectedRowDetails));
            (ClearSelectedRowCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (ShowSelectedRowDetailsCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    public string SelectedRowJson
    {
        get => _selectedRowJson;
        private set => Set(ref _selectedRowJson, value);
    }

    public bool HasSelectedRow => SelectedRowFields.Count > 0;
    public bool IsResultDetailVisible
    {
        get => _isResultDetailVisible;
        private set
        {
            if (!Set(ref _isResultDetailVisible, value))
                return;

            RaisePropertyChanged(nameof(CanShowSelectedRowDetails));
            (HideSelectedRowDetailsCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }
    public bool CanShowSelectedRowDetails => HasSelectedRow && !IsResultDetailVisible;
    public string SelectedRowSummary => Session?.ViewState.SelectedRowIndex is int idx && idx >= 0
        ? $"Row {idx + 1}"
        : "-";
    public bool HasVisibleRows => _pagedRowsTable?.Rows.Count > 0;
    public bool HasSelectedCell => Session?.ViewState.SelectedCell is not null;
    public string SelectedCellSummary
    {
        get
        {
            CellSelection? selection = Session?.ViewState.SelectedCell;
            if (selection is null)
                return "-";

            return $"R{selection.RowIndex + 1} · {selection.ColumnName}";
        }
    }
    public string GeneratedWhereClause
    {
        get => _generatedWhereClause;
        private set => Set(ref _generatedWhereClause, value ?? string.Empty);
    }
    public bool CanGenerateWhereClause =>
        HasSelectedRow
        && Session?.InlineEditEligibility.IsEligible == true
        && Session.InlineEditEligibility.PrimaryKeyColumns.Count > 0;
    public bool IsEditableSession => Session?.InlineEditEligibility.IsEligible == true;
    public bool HasPendingEdits => Session?.ViewState.PendingEdits.Count > 0;
    public int PendingEditsCount => Session?.ViewState.PendingEdits.Count ?? 0;
    public bool CanCancelPendingEdits => HasPendingEdits;
    public bool CanPreparePendingChangesPreview => HasPendingEdits && !_isPreparingPendingPreview;
    public bool CanGeneratePendingSql => HasPendingEdits;
    public bool HasPendingChangeSetPreview => _pendingChangeSetPreview is not null;
    public string PendingChangeSetSummaryText
    {
        get => _pendingChangeSetSummaryText;
        private set => Set(ref _pendingChangeSetSummaryText, value ?? string.Empty);
    }
    public string PendingDiffPreviewText
    {
        get => _pendingDiffPreviewText;
        private set => Set(ref _pendingDiffPreviewText, value ?? string.Empty);
    }
    public string GeneratedPendingSqlText
    {
        get => _generatedPendingSqlText;
        private set => Set(ref _generatedPendingSqlText, value ?? string.Empty);
    }
    public bool HasGeneratedPendingSqlText => !string.IsNullOrWhiteSpace(GeneratedPendingSqlText);
    public bool CanCopyGeneratedPendingSql => HasGeneratedPendingSqlText;
    public bool CanSendGeneratedPendingSqlToEditor => HasGeneratedPendingSqlText && _appendSqlToEditor is not null;
    public bool UseTransactionalExecution
    {
        get => _useTransactionalExecution;
        set
        {
            bool normalized = value && IsTransactionModeAvailable;
            if (!Set(ref _useTransactionalExecution, normalized))
                return;

            if (Session is not null)
                Session.ViewState.UseTransactionalExecution = normalized;

            RaisePropertyChanged(nameof(CanRequestExecutePendingChanges));
            RaisePropertyChanged(nameof(CanConfirmExecutePendingChangesWithRollback));
            RaisePropertyChanged(nameof(HasTransactionRollbackOption));
            RaisePropertyChanged(nameof(ConfirmExecutePendingChangesText));
            NotifyCommands();
        }
    }
    public bool IsTransactionModeAvailable => Session is not null && SupportsTransactionProvider(Session.Provider);
    public string TransactionModeStatusText =>
        Session is null || IsTransactionModeAvailable
            ? string.Empty
            : $"Transactional execution is unavailable for provider '{Session.Provider}'.";
    public bool HasTransactionModeStatusText => Session is not null && !IsTransactionModeAvailable;
    public bool HasTransactionRollbackOption => UseTransactionalExecution && IsTransactionModeAvailable;
    public string ConfirmExecutePendingChangesText => HasTransactionRollbackOption ? "Confirm Commit" : "Confirm Execute";
    public bool IsPendingExecutionConfirmationVisible => _isPendingExecutionConfirmationVisible;
    public bool IsExecutingPendingChanges => _isExecutingPendingChanges;
    public bool HasPendingExecutionError => _hasPendingExecutionError;
    public string PendingExecutionStatusText
    {
        get => _pendingExecutionStatusText;
        private set => Set(ref _pendingExecutionStatusText, value ?? string.Empty);
    }
    public bool HasPendingExecutionStatusText => !string.IsNullOrWhiteSpace(PendingExecutionStatusText);
    public bool HasPendingPreviewPanel =>
        HasPendingChangeSetPreview
        || HasPendingExecutionStatusText
        || IsPendingExecutionConfirmationVisible;
    public bool IsProductionLikeConnectionContext => DetectProductionLikeConnectionContext();
    public bool CanRequestExecutePendingChanges =>
        HasPendingEdits
        && !_isExecutingPendingChanges
        && !IsProductionLikeConnectionContext
        && (!UseTransactionalExecution || IsTransactionModeAvailable);
    public bool CanConfirmExecutePendingChanges =>
        _isPendingExecutionConfirmationVisible
        && !_isExecutingPendingChanges;
    public bool CanConfirmExecutePendingChangesWithRollback =>
        _isPendingExecutionConfirmationVisible
        && !_isExecutingPendingChanges
        && HasTransactionRollbackOption;
    public bool CanCancelExecutePendingChanges =>
        _isPendingExecutionConfirmationVisible
        && !_isExecutingPendingChanges;
    public bool CanRefreshSession => Session is not null && !_isRefreshingSession && !_isExecutingPendingChanges;
    public string SessionAnnotationText
    {
        get => _sessionAnnotationText;
        set
        {
            string normalized = value ?? string.Empty;
            if (!Set(ref _sessionAnnotationText, normalized))
                return;

            RaisePropertyChanged(nameof(CanSaveSessionAnnotation));
            (SaveSessionAnnotationCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }
    public bool HasSessionAnnotation => !string.IsNullOrWhiteSpace(Session?.Annotation);
    public bool CanSaveSessionAnnotation =>
        Session is not null
        && _sessionService is not null
        && !string.Equals(SessionAnnotationText.Trim(), Session.Annotation ?? string.Empty, StringComparison.Ordinal);
    public bool CanClearSessionAnnotation =>
        Session is not null
        && _sessionService is not null
        && !string.IsNullOrWhiteSpace(Session?.Annotation);
    public ObservableCollection<SqlSavedQuerySnippet> SavedSnippets
    {
        get => _savedSnippets;
        private set
        {
            _savedSnippets = value;
            RaisePropertyChanged(nameof(SavedSnippets));
            RaisePropertyChanged(nameof(HasSavedSnippets));
            RaisePropertyChanged(nameof(FavoriteSnippetCountText));
            RaisePropertyChanged(nameof(IsCurrentSqlFavorite));
            RaisePropertyChanged(nameof(CanToggleCurrentSqlFavorite));
            NotifyCommands();
        }
    }
    public bool HasSavedSnippets => SavedSnippets.Count > 0;
    public SqlSavedQuerySnippet? SelectedSnippet
    {
        get => _selectedSnippet;
        set
        {
            if (!Set(ref _selectedSnippet, value))
                return;

            RaisePropertyChanged(nameof(CanOpenSelectedSnippetInEditor));
            RaisePropertyChanged(nameof(CanDeleteSelectedSnippet));
            NotifyCommands();
        }
    }
    public string SnippetNameInput
    {
        get => _snippetNameInput;
        set
        {
            string normalized = value ?? string.Empty;
            if (!Set(ref _snippetNameInput, normalized))
                return;

            RaisePropertyChanged(nameof(CanSaveCurrentSqlAsSnippet));
            NotifyCommands();
        }
    }
    public string SnippetDescriptionInput
    {
        get => _snippetDescriptionInput;
        set => Set(ref _snippetDescriptionInput, value ?? string.Empty);
    }
    public string SnippetTagsInput
    {
        get => _snippetTagsInput;
        set => Set(ref _snippetTagsInput, value ?? string.Empty);
    }
    public bool IsCurrentSqlFavorite => TryFindSnippetBySql(SqlText, out SqlSavedQuerySnippet? snippet) && snippet?.IsFavorite == true;
    public string ToggleCurrentSqlFavoriteText => IsCurrentSqlFavorite ? "Unfavorite SQL" : "Favorite SQL";
    public string FavoriteSnippetCountText => $"Favorites: {SavedSnippets.Count(item => item.IsFavorite)}";
    public bool CanSaveCurrentSqlAsSnippet => Session is not null && !string.IsNullOrWhiteSpace(SqlText);
    public bool CanToggleCurrentSqlFavorite => Session is not null && !string.IsNullOrWhiteSpace(SqlText);
    public bool CanOpenSelectedSnippetInEditor => SelectedSnippet is not null && _appendSqlToEditor is not null;
    public bool CanDeleteSelectedSnippet => SelectedSnippet is not null;
    public IReadOnlyList<SqlQuickTemplateOption> AvailableSqlTemplates
    {
        get => _availableSqlTemplates;
        private set
        {
            _availableSqlTemplates = value;
            RaisePropertyChanged(nameof(AvailableSqlTemplates));
            RaisePropertyChanged(nameof(HasAvailableSqlTemplates));
            RaisePropertyChanged(nameof(CanSendSelectedSqlTemplateToEditor));
            NotifyCommands();
        }
    }
    public bool HasAvailableSqlTemplates => AvailableSqlTemplates.Count > 0;
    public SqlQuickTemplateOption? SelectedSqlTemplate
    {
        get => _selectedSqlTemplate;
        set
        {
            if (!Set(ref _selectedSqlTemplate, value))
                return;

            RaisePropertyChanged(nameof(CanSendSelectedSqlTemplateToEditor));
            NotifyCommands();
        }
    }
    public bool CanSendSelectedSqlTemplateToEditor =>
        _appendSqlToEditor is not null
        && Session is not null
        && SelectedSqlTemplate is not null;
    public IReadOnlyList<SqlTableQuickActionOption> AvailableTableQuickActions
    {
        get => _availableTableQuickActions;
        private set
        {
            _availableTableQuickActions = value;
            RaisePropertyChanged(nameof(AvailableTableQuickActions));
            RaisePropertyChanged(nameof(HasAvailableTableQuickActions));
            NotifyCommands();
        }
    }
    public bool HasAvailableTableQuickActions => AvailableTableQuickActions.Count > 0;
    public bool CanNavigateSelectedForeignKey =>
        _appendSqlToEditor is not null
        && TryResolveForeignKeyNavigationContext(out _, out _);
    public string EditabilityStatusText
    {
        get
        {
            var eligibility = Session?.InlineEditEligibility;
            if (eligibility?.IsEligible != true)
                return "Read-only (safe inline edit unavailable)";

            string tableName = string.IsNullOrWhiteSpace(eligibility.TableFullName)
                ? "table"
                : eligibility.TableFullName;
            string keyColumns = eligibility.PrimaryKeyColumns.Count == 0
                ? "-"
                : string.Join(", ", eligibility.PrimaryKeyColumns);
            return $"Editable ({tableName}; PK: {keyColumns})";
        }
    }
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

    public ObservableCollection<SqlResultColumnProfileItemViewModel> ColumnProfiles
    {
        get => _columnProfiles;
        private set
        {
            _columnProfiles = value;
            RaisePropertyChanged(nameof(ColumnProfiles));
            RaisePropertyChanged(nameof(HasColumnProfiles));
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
    public bool HasColumnProfiles => ColumnProfiles.Count > 0;
    public bool IsBuildingColumnProfiles => _isBuildingColumnProfiles;
    public string ColumnProfileStatusText
    {
        get => _columnProfileStatusText;
        private set => Set(ref _columnProfileStatusText, value ?? string.Empty);
    }
    public bool HasColumnProfileStatusText => !string.IsNullOrWhiteSpace(ColumnProfileStatusText);

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

    public void ConfigureSqlAppendToEditor(Action<Guid?, string> appendSqlToEditor)
    {
        _appendSqlToEditor = appendSqlToEditor ?? throw new ArgumentNullException(nameof(appendSqlToEditor));
        RaisePropertyChanged(nameof(CanNavigateSelectedForeignKey));
        NotifyCommands();
    }

    public void ConfigureConnectionResolver(Func<string?, ConnectionConfig?> connectionConfigBySessionResolver)
    {
        _connectionConfigBySessionResolver = connectionConfigBySessionResolver ?? throw new ArgumentNullException(nameof(connectionConfigBySessionResolver));
        RaisePropertyChanged(nameof(IsProductionLikeConnectionContext));
        RaisePropertyChanged(nameof(CanRefreshSession));
        RaisePropertyChanged(nameof(CanRequestExecutePendingChanges));
        RaisePropertyChanged(nameof(CanNavigateSelectedForeignKey));
        NotifyCommands();
    }

    public void ConfigureMetadataResolver(Func<DbMetadata?> metadataResolver)
    {
        _metadataResolver = metadataResolver ?? throw new ArgumentNullException(nameof(metadataResolver));
        RaisePropertyChanged(nameof(CanNavigateSelectedForeignKey));
        NotifyCommands();
    }

    public bool TryBuildReportExportContext(out SqlEditorReportExportContext? context)
    {
        if (Session is null)
        {
            context = null;
            return false;
        }

        SqlEditorResultSet result = Session.ResultSet;
        var columns = new List<string>();
        var resultRows = new List<IReadOnlyDictionary<string, object?>>();
        if (result.Data is not null)
        {
            foreach (DataColumn column in result.Data.Columns)
                columns.Add(column.ColumnName);

            foreach (DataRow row in result.Data.Rows)
            {
                var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (DataColumn column in result.Data.Columns)
                    values[column.ColumnName] = NormalizeReportCellValue(row[column]);

                resultRows.Add(values);
            }
        }

        string status = result.Success ? "success" : "error";
        if (result.Success && !string.IsNullOrWhiteSpace(result.ErrorMessage))
            status = "warning";

        long? executionMs = (long)Math.Round(result.ExecutionTime.TotalMilliseconds);
        long? rowCount = result.RowsAffected;
        if (!rowCount.HasValue && result.Data is not null)
            rowCount = result.Data.Rows.Count;

        ConnectionConfig? connection = _connectionConfigBySessionResolver?.Invoke(Session.ConnectionId);
        context = new SqlEditorReportExportContext(
            Sql: result.StatementSql,
            SchemaColumns: columns,
            SchemaDetails: BuildReportSchemaDetails(columns, resultRows),
            ResultRows: resultRows,
            ExecutionResult: new SqlEditorReportExecutionResult(
                RowCount: rowCount,
                ExecutionTimeMs: executionMs,
                Status: status,
                ErrorMessage: result.ErrorMessage),
            Connection: connection,
            ActiveFilePath: null,
            TabTitle: BuildReportTabTitle(result.StatementSql));

        return true;
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
        GeneratedWhereClause = string.Empty;
        RaisePropertyChanged(nameof(SearchText));
        SnippetNameInput = BuildDefaultSnippetName(session);
        SnippetDescriptionInput = string.Empty;
        SnippetTagsInput = string.Empty;
        AvailableSqlTemplates = BuildSqlQuickTemplates(session);
        SelectedSqlTemplate = AvailableSqlTemplates.FirstOrDefault();
        AvailableTableQuickActions = BuildTableQuickActions(session);
        SessionAnnotationText = session.Annotation ?? string.Empty;
        RaisePropertyChanged(nameof(HasSessionAnnotation));
        RaisePropertyChanged(nameof(CanClearSessionAnnotation));

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
        CloseSessionCore(Session);
    }

    private void CloseSessionTab(SqlResultSession? session)
    {
        CloseSessionCore(session);
    }

    private void CloseSessionCore(SqlResultSession? session)
    {
        if (session is null || _sessionService is null)
            return;

        bool wasActive = Session?.Id == session.Id;
        Guid closingId = session.Id;
        _sessionService.Remove(closingId);
        _sessionSourceEditorDocumentMap.Remove(closingId);
        _sessionCollapsedGroupKeys.Remove(closingId);
        RefreshSessions();

        if (!wasActive)
            return;

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

    private void SaveSessionAnnotation()
    {
        if (Session is null || _sessionService is null)
            return;

        string? normalized = string.IsNullOrWhiteSpace(SessionAnnotationText)
            ? null
            : SessionAnnotationText.Trim();

        if (!_sessionService.SetAnnotation(Session.Id, normalized))
            return;

        Session.Annotation = normalized;
        SessionAnnotationText = normalized ?? string.Empty;
        RaisePropertyChanged(nameof(HasSessionAnnotation));
        RaisePropertyChanged(nameof(CanSaveSessionAnnotation));
        RaisePropertyChanged(nameof(CanClearSessionAnnotation));
        NotifyCommands();
    }

    private void ClearSessionAnnotation()
    {
        if (Session is null || _sessionService is null)
            return;

        if (!_sessionService.SetAnnotation(Session.Id, null))
            return;

        Session.Annotation = null;
        SessionAnnotationText = string.Empty;
        RaisePropertyChanged(nameof(HasSessionAnnotation));
        RaisePropertyChanged(nameof(CanSaveSessionAnnotation));
        RaisePropertyChanged(nameof(CanClearSessionAnnotation));
        NotifyCommands();
    }

    private void SaveCurrentSqlAsSnippet()
    {
        if (Session is null || string.IsNullOrWhiteSpace(SqlText))
            return;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (TryFindSnippetBySql(SqlText, out SqlSavedQuerySnippet? existing) && existing is not null)
        {
            var updated = existing with
            {
                Name = string.IsNullOrWhiteSpace(SnippetNameInput) ? existing.Name : SnippetNameInput.Trim(),
                Description = string.IsNullOrWhiteSpace(SnippetDescriptionInput)
                    ? existing.Description
                    : SnippetDescriptionInput.Trim(),
                Tags = string.IsNullOrWhiteSpace(SnippetTagsInput) ? existing.Tags : SnippetTagsInput.Trim(),
                ConnectionId = Session.ConnectionId,
                DatabaseName = Session.DatabaseName,
                UpdatedAtUtc = now,
            };
            _snippetStore.Upsert(updated);
            ReloadSavedSnippets(selectSnippetId: updated.Id);
            return;
        }

        string name = string.IsNullOrWhiteSpace(SnippetNameInput)
            ? BuildDefaultSnippetName(Session)
            : SnippetNameInput.Trim();
        string description = string.IsNullOrWhiteSpace(SnippetDescriptionInput)
            ? $"Saved from SQL result session at {Session.ExecutedAt.LocalDateTime:dd/MM/yyyy HH:mm:ss}."
            : SnippetDescriptionInput.Trim();
        string tags = string.IsNullOrWhiteSpace(SnippetTagsInput) ? "sql result saved" : SnippetTagsInput.Trim();

        var snippet = new SqlSavedQuerySnippet(
            Id: Guid.NewGuid().ToString("N"),
            Name: name,
            Description: description,
            Tags: tags,
            SqlText: SqlText.Trim(),
            ConnectionId: Session.ConnectionId,
            DatabaseName: Session.DatabaseName,
            CreatedAtUtc: now,
            UpdatedAtUtc: now,
            IsFavorite: false);
        _snippetStore.Upsert(snippet);
        ReloadSavedSnippets(selectSnippetId: snippet.Id);
    }

    private void ToggleCurrentSqlFavorite()
    {
        if (Session is null || string.IsNullOrWhiteSpace(SqlText))
            return;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (TryFindSnippetBySql(SqlText, out SqlSavedQuerySnippet? existing) && existing is not null)
        {
            _snippetStore.Upsert(existing with
            {
                IsFavorite = !existing.IsFavorite,
                UpdatedAtUtc = now,
            });
            ReloadSavedSnippets(selectSnippetId: existing.Id);
            return;
        }

        var snippet = new SqlSavedQuerySnippet(
            Id: Guid.NewGuid().ToString("N"),
            Name: BuildDefaultSnippetName(Session),
            Description: $"Favorited from SQL result session at {Session.ExecutedAt.LocalDateTime:dd/MM/yyyy HH:mm:ss}.",
            Tags: "sql favorite",
            SqlText: SqlText.Trim(),
            ConnectionId: Session.ConnectionId,
            DatabaseName: Session.DatabaseName,
            CreatedAtUtc: now,
            UpdatedAtUtc: now,
            IsFavorite: true);
        _snippetStore.Upsert(snippet);
        ReloadSavedSnippets(selectSnippetId: snippet.Id);
    }

    private void OpenSelectedSnippetInEditor()
    {
        OpenSnippetInEditor(SelectedSnippet);
    }

    private void OpenSnippetInEditor(SqlSavedQuerySnippet? snippet)
    {
        if (!CanOpenSnippetInEditor(snippet))
            return;

        _appendSqlToEditor?.Invoke(_sourceSqlEditorDocumentId, snippet!.SqlText);
    }

    private bool CanOpenSnippetInEditor(SqlSavedQuerySnippet? snippet)
    {
        return snippet is not null
            && _appendSqlToEditor is not null
            && !string.IsNullOrWhiteSpace(snippet.SqlText);
    }

    private void DeleteSelectedSnippet()
    {
        DeleteSnippet(SelectedSnippet);
    }

    private void DeleteSnippet(SqlSavedQuerySnippet? snippet)
    {
        if (snippet is null)
            return;

        if (!_snippetStore.Delete(snippet.Id))
            return;

        ReloadSavedSnippets();
    }

    private void ReloadSavedSnippets(string? selectSnippetId = null)
    {
        IReadOnlyList<SqlSavedQuerySnippet> snippets = _snippetStore.Load();
        SavedSnippets = new ObservableCollection<SqlSavedQuerySnippet>(snippets);
        if (!string.IsNullOrWhiteSpace(selectSnippetId))
        {
            SelectedSnippet = SavedSnippets.FirstOrDefault(item =>
                string.Equals(item.Id, selectSnippetId, StringComparison.OrdinalIgnoreCase));
        }
        else if (SelectedSnippet is not null)
        {
            string existingId = SelectedSnippet.Id;
            SelectedSnippet = SavedSnippets.FirstOrDefault(item =>
                string.Equals(item.Id, existingId, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            SelectedSnippet = SavedSnippets.FirstOrDefault();
        }

        RaisePropertyChanged(nameof(IsCurrentSqlFavorite));
        RaisePropertyChanged(nameof(ToggleCurrentSqlFavoriteText));
    }

    private bool TryFindSnippetBySql(string? sqlText, out SqlSavedQuerySnippet? snippet)
    {
        snippet = null;
        if (string.IsNullOrWhiteSpace(sqlText))
            return false;

        string normalizedSql = NormalizeSqlForSnippetIdentity(sqlText);
        snippet = SavedSnippets.FirstOrDefault(item =>
            string.Equals(NormalizeSqlForSnippetIdentity(item.SqlText), normalizedSql, StringComparison.Ordinal));
        return snippet is not null;
    }

    private static string NormalizeSqlForSnippetIdentity(string sqlText)
    {
        return (sqlText ?? string.Empty).Trim();
    }

    private static string BuildDefaultSnippetName(SqlResultSession session)
    {
        string database = string.IsNullOrWhiteSpace(session.DatabaseName) ? "sql" : session.DatabaseName!;
        return $"{database}-snippet-{session.ExecutedAt.LocalDateTime:yyyyMMdd-HHmmss}";
    }

    private void SendSelectedSqlTemplateToEditor()
    {
        if (!CanSendSelectedSqlTemplateToEditor || Session is null || SelectedSqlTemplate is null)
            return;

        string sql = BuildSqlFromTemplate(Session, SelectedSqlTemplate.Key);
        if (string.IsNullOrWhiteSpace(sql))
            return;

        _appendSqlToEditor?.Invoke(_sourceSqlEditorDocumentId, sql);
    }

    private static IReadOnlyList<SqlQuickTemplateOption> BuildSqlQuickTemplates(SqlResultSession session)
    {
        string table = ResolveTemplateTableName(session);
        if (string.IsNullOrWhiteSpace(table))
            return [];

        return
        [
            new SqlQuickTemplateOption("select_top_100", "SELECT TOP/LIMIT 100", $"Preview first 100 rows from {table}."),
            new SqlQuickTemplateOption("find_by_pk", "Find by PK", $"Find a row by primary key in {table}."),
            new SqlQuickTemplateOption("count_by_column", "Count by Column", $"Aggregate counts by one column in {table}."),
            new SqlQuickTemplateOption("find_duplicates", "Find Duplicates", $"Detect duplicate values in {table}."),
            new SqlQuickTemplateOption("find_nulls", "Find Nulls", $"Find rows with null values in {table}."),
            new SqlQuickTemplateOption("recent_records", "Recent Records", $"List latest records in {table}."),
            new SqlQuickTemplateOption("recently_modified", "Recently Modified", $"List recently modified rows in {table}."),
        ];
    }

    private static string BuildSqlFromTemplate(SqlResultSession session, string templateKey)
    {
        string table = ResolveTemplateTableName(session);
        if (string.IsNullOrWhiteSpace(table))
            return string.Empty;

        string idColumn = session.InlineEditEligibility.PrimaryKeyColumns.FirstOrDefault() ?? "id";
        string limitClause = session.Provider == DatabaseProvider.SqlServer ? "TOP 100 " : string.Empty;
        string limitTail = session.Provider == DatabaseProvider.SqlServer ? string.Empty : " LIMIT 100";
        string recentOrder = "created_at";
        string modifiedOrder = "updated_at";

        return templateKey switch
        {
            "select_top_100" => $"SELECT {limitClause}*\nFROM {table}\nORDER BY {idColumn} DESC{limitTail};",
            "find_by_pk" => $"SELECT *\nFROM {table}\nWHERE {idColumn} = :{idColumn};",
            "count_by_column" => $"SELECT {idColumn}, COUNT(*) AS total\nFROM {table}\nGROUP BY {idColumn}\nORDER BY total DESC;",
            "find_duplicates" => $"SELECT {idColumn}, COUNT(*) AS total\nFROM {table}\nGROUP BY {idColumn}\nHAVING COUNT(*) > 1\nORDER BY total DESC;",
            "find_nulls" => $"SELECT *\nFROM {table}\nWHERE {idColumn} IS NULL;",
            "recent_records" => $"SELECT {limitClause}*\nFROM {table}\nORDER BY {recentOrder} DESC{limitTail};",
            "recently_modified" => $"SELECT {limitClause}*\nFROM {table}\nORDER BY {modifiedOrder} DESC{limitTail};",
            _ => string.Empty,
        };
    }

    private static string ResolveTemplateTableName(SqlResultSession session)
    {
        string? tableName = session.InlineEditEligibility.TableFullName;
        if (!string.IsNullOrWhiteSpace(tableName))
            return tableName!;

        if (!string.IsNullOrWhiteSpace(session.SchemaName) && !string.IsNullOrWhiteSpace(session.DatabaseName))
            return $"{session.SchemaName}.table_name";

        return "table_name";
    }

    private void SendTableQuickActionToEditor(SqlTableQuickActionOption? action)
    {
        if (!CanSendTableQuickActionToEditor(action) || Session is null)
            return;

        string sql = BuildTableQuickActionSql(Session, action!.Key);
        if (string.IsNullOrWhiteSpace(sql))
            return;

        _appendSqlToEditor?.Invoke(_sourceSqlEditorDocumentId, sql);
    }

    private bool CanSendTableQuickActionToEditor(SqlTableQuickActionOption? action)
    {
        return _appendSqlToEditor is not null
            && Session is not null
            && action is not null
            && !string.IsNullOrWhiteSpace(action.Key);
    }

    private static IReadOnlyList<SqlTableQuickActionOption> BuildTableQuickActions(SqlResultSession session)
    {
        string table = ResolveTemplateTableName(session);
        if (string.IsNullOrWhiteSpace(table) || string.Equals(table, "table_name", StringComparison.Ordinal))
            return [];

        return
        [
            new SqlTableQuickActionOption("table_structure", "Table Structure", "Generate SQL to inspect columns."),
            new SqlTableQuickActionOption("table_indexes", "Table Indexes", "Generate SQL to inspect indexes."),
            new SqlTableQuickActionOption("table_constraints", "Table Constraints", "Generate SQL to inspect constraints."),
            new SqlTableQuickActionOption("table_foreign_keys", "Table Foreign Keys", "Generate SQL to inspect foreign keys."),
            new SqlTableQuickActionOption("table_select_basic", "Generate SELECT", "Generate a basic SELECT preview SQL."),
            new SqlTableQuickActionOption("table_insert_template", "Generate INSERT", "Generate an INSERT template."),
            new SqlTableQuickActionOption("table_update_pk", "Generate UPDATE by PK", "Generate an UPDATE template with PK WHERE."),
        ];
    }

    private static string BuildTableQuickActionSql(SqlResultSession session, string actionKey)
    {
        string tableFullName = ResolveTemplateTableName(session);
        ParseSchemaAndTable(tableFullName, out string? schemaName, out string tableName);
        string schema = string.IsNullOrWhiteSpace(schemaName)
            ? GetDefaultSchema(session.Provider)
            : schemaName!;
        string fullTable = string.IsNullOrWhiteSpace(schema) ? tableName : $"{schema}.{tableName}";
        string quotedFullTable = QuoteCompositeIdentifier(session.Provider, fullTable);
        string[] resultColumns = session.ResultSet.Data?.Columns
            .Cast<DataColumn>()
            .Select(column => column.ColumnName)
            .ToArray()
            ?? [];
        string firstColumn = resultColumns.FirstOrDefault() ?? "id";
        string insertColumns = resultColumns.Length == 0 ? firstColumn : string.Join(", ", resultColumns.Select(column => QuoteIdentifier(session.Provider, column)));
        string insertValues = resultColumns.Length == 0 ? ":id" : string.Join(", ", resultColumns.Select(column => $":{column}"));
        string pkColumn = session.InlineEditEligibility.PrimaryKeyColumns.FirstOrDefault() ?? firstColumn;
        string limitClause = session.Provider == DatabaseProvider.SqlServer ? "TOP 100 " : string.Empty;
        string limitTail = session.Provider == DatabaseProvider.SqlServer ? string.Empty : " LIMIT 100";

        return actionKey switch
        {
            "table_structure" => session.Provider switch
            {
                DatabaseProvider.SqlServer =>
                    $"SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH\nFROM INFORMATION_SCHEMA.COLUMNS\nWHERE TABLE_SCHEMA = '{EscapeSqlLiteral(schema)}' AND TABLE_NAME = '{EscapeSqlLiteral(tableName)}'\nORDER BY ORDINAL_POSITION;",
                DatabaseProvider.MySql =>
                    $"SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH\nFROM INFORMATION_SCHEMA.COLUMNS\nWHERE TABLE_SCHEMA = '{EscapeSqlLiteral(session.DatabaseName ?? string.Empty)}' AND TABLE_NAME = '{EscapeSqlLiteral(tableName)}'\nORDER BY ORDINAL_POSITION;",
                DatabaseProvider.SQLite =>
                    $"PRAGMA table_info('{EscapeSqlLiteral(tableName)}');",
                _ =>
                    $"SELECT column_name, data_type, is_nullable, character_maximum_length\nFROM information_schema.columns\nWHERE table_schema = '{EscapeSqlLiteral(schema)}' AND table_name = '{EscapeSqlLiteral(tableName)}'\nORDER BY ordinal_position;",
            },
            "table_indexes" => session.Provider switch
            {
                DatabaseProvider.SqlServer =>
                    $"SELECT i.name AS index_name, i.type_desc, i.is_unique\nFROM sys.indexes i\nINNER JOIN sys.tables t ON i.object_id = t.object_id\nINNER JOIN sys.schemas s ON t.schema_id = s.schema_id\nWHERE s.name = '{EscapeSqlLiteral(schema)}' AND t.name = '{EscapeSqlLiteral(tableName)}' AND i.is_hypothetical = 0;",
                DatabaseProvider.MySql =>
                    $"SHOW INDEX FROM `{tableName}`;",
                DatabaseProvider.SQLite =>
                    $"PRAGMA index_list('{EscapeSqlLiteral(tableName)}');",
                _ =>
                    $"SELECT indexname, indexdef\nFROM pg_indexes\nWHERE schemaname = '{EscapeSqlLiteral(schema)}' AND tablename = '{EscapeSqlLiteral(tableName)}';",
            },
            "table_constraints" => session.Provider switch
            {
                DatabaseProvider.SqlServer =>
                    $"SELECT tc.CONSTRAINT_NAME, tc.CONSTRAINT_TYPE\nFROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc\nWHERE tc.TABLE_SCHEMA = '{EscapeSqlLiteral(schema)}' AND tc.TABLE_NAME = '{EscapeSqlLiteral(tableName)}';",
                DatabaseProvider.MySql =>
                    $"SELECT CONSTRAINT_NAME, CONSTRAINT_TYPE\nFROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS\nWHERE TABLE_SCHEMA = '{EscapeSqlLiteral(session.DatabaseName ?? string.Empty)}' AND TABLE_NAME = '{EscapeSqlLiteral(tableName)}';",
                DatabaseProvider.SQLite =>
                    $"PRAGMA table_info('{EscapeSqlLiteral(tableName)}'); -- SQLite exposes constraints primarily via table definition",
                _ =>
                    $"SELECT tc.constraint_name, tc.constraint_type\nFROM information_schema.table_constraints tc\nWHERE tc.table_schema = '{EscapeSqlLiteral(schema)}' AND tc.table_name = '{EscapeSqlLiteral(tableName)}';",
            },
            "table_foreign_keys" => session.Provider switch
            {
                DatabaseProvider.SqlServer =>
                    $"SELECT fk.name AS foreign_key_name, OBJECT_NAME(fk.parent_object_id) AS table_name\nFROM sys.foreign_keys fk\nINNER JOIN sys.tables t ON fk.parent_object_id = t.object_id\nINNER JOIN sys.schemas s ON t.schema_id = s.schema_id\nWHERE s.name = '{EscapeSqlLiteral(schema)}' AND t.name = '{EscapeSqlLiteral(tableName)}';",
                DatabaseProvider.MySql =>
                    $"SELECT CONSTRAINT_NAME, COLUMN_NAME, REFERENCED_TABLE_NAME, REFERENCED_COLUMN_NAME\nFROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE\nWHERE TABLE_SCHEMA = '{EscapeSqlLiteral(session.DatabaseName ?? string.Empty)}' AND TABLE_NAME = '{EscapeSqlLiteral(tableName)}' AND REFERENCED_TABLE_NAME IS NOT NULL;",
                DatabaseProvider.SQLite =>
                    $"PRAGMA foreign_key_list('{EscapeSqlLiteral(tableName)}');",
                _ =>
                    $"SELECT tc.constraint_name, kcu.column_name, ccu.table_name AS referenced_table, ccu.column_name AS referenced_column\nFROM information_schema.table_constraints tc\nJOIN information_schema.key_column_usage kcu ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema\nJOIN information_schema.constraint_column_usage ccu ON ccu.constraint_name = tc.constraint_name AND ccu.table_schema = tc.table_schema\nWHERE tc.constraint_type = 'FOREIGN KEY' AND tc.table_schema = '{EscapeSqlLiteral(schema)}' AND tc.table_name = '{EscapeSqlLiteral(tableName)}';",
            },
            "table_select_basic" =>
                $"SELECT {limitClause}*\nFROM {quotedFullTable}\nORDER BY {QuoteIdentifier(session.Provider, pkColumn)} DESC{limitTail};",
            "table_insert_template" =>
                $"INSERT INTO {quotedFullTable} ({insertColumns})\nVALUES ({insertValues});",
            "table_update_pk" =>
                $"UPDATE {quotedFullTable}\nSET {QuoteIdentifier(session.Provider, firstColumn)} = :{firstColumn}\nWHERE {QuoteIdentifier(session.Provider, pkColumn)} = :{pkColumn};",
            _ => string.Empty,
        };
    }

    private static void ParseSchemaAndTable(string fullName, out string? schema, out string table)
    {
        schema = null;
        table = "table_name";

        if (string.IsNullOrWhiteSpace(fullName))
            return;

        string[] parts = fullName.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            table = parts[0];
            return;
        }

        schema = parts[^2];
        table = parts[^1];
    }

    private static string GetDefaultSchema(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer => "dbo",
            DatabaseProvider.SQLite => "main",
            DatabaseProvider.MySql => string.Empty,
            _ => "public",
        };
    }

    private static string QuoteCompositeIdentifier(DatabaseProvider provider, string fullName)
    {
        string[] parts = fullName.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return string.Join('.', parts.Select(part => QuoteIdentifier(provider, part)));
    }

    private static string QuoteIdentifier(DatabaseProvider provider, string identifier)
    {
        string clean = (identifier ?? string.Empty).Trim().Trim('"', '`', '[', ']');
        return provider switch
        {
            DatabaseProvider.MySql => $"`{clean}`",
            DatabaseProvider.SqlServer => $"[{clean}]",
            _ => $"\"{clean}\"",
        };
    }

    private static string EscapeSqlLiteral(string value)
    {
        return (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);
    }

    private void NavigateSelectedForeignKey()
    {
        if (!TryResolveForeignKeyNavigationContext(out ForeignKeyRelation? relation, out object? value) || relation is null)
            return;

        string parentTable = relation.ParentFullTable;
        string where = value is null
            ? $"{QuoteIdentifier(Session!.Provider, relation.ParentColumn)} IS NULL"
            : $"{QuoteIdentifier(Session!.Provider, relation.ParentColumn)} = {ToSqlLiteral(value)}";

        string limitClause = Session!.Provider == DatabaseProvider.SqlServer ? "TOP 100 " : string.Empty;
        string limitTail = Session.Provider == DatabaseProvider.SqlServer ? string.Empty : " LIMIT 100";
        string sql = $"SELECT {limitClause}*\nFROM {QuoteCompositeIdentifier(Session.Provider, parentTable)}\nWHERE {where}{limitTail};";
        _appendSqlToEditor?.Invoke(_sourceSqlEditorDocumentId, sql);
    }

    private bool TryResolveForeignKeyNavigationContext(out ForeignKeyRelation? relation, out object? value)
    {
        relation = null;
        value = null;
        if (Session is null || _metadataResolver is null)
            return false;

        DbMetadata? metadata = _metadataResolver.Invoke();
        if (metadata is null)
            return false;

        if (!TryGetSelectedCellValue(out string? columnName, out object? selectedValue) || string.IsNullOrWhiteSpace(columnName))
            return false;

        string table = ResolveTemplateTableName(Session);
        if (string.IsNullOrWhiteSpace(table) || string.Equals(table, "table_name", StringComparison.OrdinalIgnoreCase))
            return false;

        List<ForeignKeyRelation> candidates = metadata.AllForeignKeys
            .Where(item =>
                string.Equals(item.ChildFullTable, table, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.ChildColumn, columnName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count != 1)
            return false;

        relation = candidates[0];
        value = selectedValue;
        return true;
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
        Session.ViewState.SelectedCell = null;
        SelectedRowFields = [];
        SelectedRowJson = "{}";
        IsResultDetailVisible = false;
        GeneratedWhereClause = string.Empty;
        _selectedRowItem = null;
        RaisePropertyChanged(nameof(SelectedRowItem));
        RaisePropertyChanged(nameof(SelectedRowSummary));
        RaisePropertyChanged(nameof(HasSelectedCell));
        RaisePropertyChanged(nameof(SelectedCellSummary));
        RaisePropertyChanged(nameof(CanGenerateWhereClause));
        RaisePropertyChanged(nameof(CanNavigateSelectedForeignKey));
    }

    private void ShowSelectedRowDetails()
    {
        if (!CanShowSelectedRowDetails)
            return;

        IsResultDetailVisible = true;
        NotifyCommands();
    }

    private void HideSelectedRowDetails()
    {
        if (!IsResultDetailVisible)
            return;

        IsResultDetailVisible = false;
        NotifyCommands();
    }

    public void SelectCell(DataRowView rowView, string columnName)
    {
        if (Session is null || _pagedRowsTable is null || string.IsNullOrWhiteSpace(columnName))
            return;

        if (!_pagedRowsTable.Columns.Contains(columnName))
            return;

        int rowIndex = _pagedRowsTable.Rows.IndexOf(rowView.Row);
        if (rowIndex < 0)
            return;

        Session.ViewState.SelectedCell = new CellSelection(rowIndex, columnName);
        SelectedRowItem = rowView;
        RaisePropertyChanged(nameof(HasSelectedCell));
        RaisePropertyChanged(nameof(SelectedCellSummary));
        RaisePropertyChanged(nameof(CanNavigateSelectedForeignKey));
        NotifyCommands();
    }

    public bool IsColumnEditable(string? columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName) || Session?.InlineEditEligibility.IsEligible != true)
            return false;

        return Session.InlineEditEligibility.EditableColumns.Contains(columnName, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsCellPending(DataRowView? rowView, string? columnName)
    {
        if (Session is null
            || rowView is null
            || string.IsNullOrWhiteSpace(columnName)
            || Session.ViewState.PendingEdits.Count == 0
            || _pagedRowsTable is null)
        {
            return false;
        }

        int rowIndex = _pagedRowsTable.Rows.IndexOf(rowView.Row);
        if (rowIndex < 0 || rowIndex >= _pagedSourceRows.Count)
            return false;

        DataRow sourceRow = _pagedSourceRows[rowIndex];
        foreach (PendingCellEdit pendingEdit in Session.ViewState.PendingEdits)
        {
            if (!string.Equals(pendingEdit.ColumnName, columnName, StringComparison.OrdinalIgnoreCase))
                continue;

            bool matches = pendingEdit.KeyValues.All(kvp =>
            {
                if (!sourceRow.Table.Columns.Contains(kvp.Key))
                    return false;

                object raw = sourceRow[kvp.Key];
                object? normalized = raw == DBNull.Value ? null : raw;
                return ValuesEqual(normalized, kvp.Value);
            });
            if (matches)
                return true;
        }

        return false;
    }

    public bool TryApplyInlineCellEdit(DataRowView rowView, string? columnName, string? editedText, out string? errorMessage)
    {
        errorMessage = null;
        if (Session is null || _pagedRowsTable is null || string.IsNullOrWhiteSpace(columnName))
            return false;

        if (!IsColumnEditable(columnName))
            return false;

        int rowIndex = _pagedRowsTable.Rows.IndexOf(rowView.Row);
        if (rowIndex < 0 || rowIndex >= _pagedSourceRows.Count)
            return false;

        DataRow sourceRow = _pagedSourceRows[rowIndex];
        DataColumn? sourceColumn = Session.ResultSet.Data?.Columns[columnName];
        if (sourceColumn is null)
            return false;

        if (!TryConvertEditedValue(sourceColumn.DataType, editedText, out object? convertedValue))
        {
            errorMessage = $"Invalid value for column '{columnName}'.";
            return false;
        }

        object? currentValue = sourceRow[columnName];
        object? normalizedCurrent = currentValue == DBNull.Value ? null : currentValue;
        object? normalizedNew = convertedValue == DBNull.Value ? null : convertedValue;

        if (ValuesEqual(normalizedCurrent, normalizedNew))
            return true;

        if (!TryCreatePendingEdit(sourceRow, columnName, normalizedCurrent, normalizedNew, out PendingCellEdit? pendingEdit)
            || pendingEdit is null)
        {
            errorMessage = "Unable to build pending edit metadata.";
            return false;
        }

        UpsertPendingEdit(pendingEdit);

        object persisted = normalizedNew ?? DBNull.Value;
        sourceRow[columnName] = persisted;
        rowView.Row[columnName] = persisted;
        UpdateSelectedRowState(rowView);
        ResetPendingChangeArtifacts();

        RaisePropertyChanged(nameof(HasPendingEdits));
        RaisePropertyChanged(nameof(PendingEditsCount));
        RaisePropertyChanged(nameof(CanCancelPendingEdits));
        RaisePropertyChanged(nameof(CanPreparePendingChangesPreview));
        RaisePropertyChanged(nameof(CanGeneratePendingSql));
        NotifyCommands();
        return true;
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
            IsResultDetailVisible = false;
            _selectedRowItem = null;
            RaisePropertyChanged(nameof(SelectedRowItem));
            RaisePropertyChanged(nameof(SelectedRowSummary));
            return;
        }

        SelectedRowItem = _pagedRowsTable.DefaultView[selectedRowIndex.Value];
    }

    private void RestoreSelectedCellFromSessionState()
    {
        CellSelection? selectedCell = Session?.ViewState.SelectedCell;
        if (Session is null || _pagedRowsTable is null || selectedCell is null)
        {
            if (Session is not null)
                Session.ViewState.SelectedCell = null;
            RaisePropertyChanged(nameof(HasSelectedCell));
            RaisePropertyChanged(nameof(SelectedCellSummary));
            RaisePropertyChanged(nameof(CanNavigateSelectedForeignKey));
            return;
        }

        bool isRowValid = selectedCell.RowIndex >= 0 && selectedCell.RowIndex < _pagedRowsTable.Rows.Count;
        bool isColumnValid = _pagedRowsTable.Columns.Contains(selectedCell.ColumnName);
        if (isRowValid && isColumnValid)
        {
            RaisePropertyChanged(nameof(HasSelectedCell));
            RaisePropertyChanged(nameof(SelectedCellSummary));
            RaisePropertyChanged(nameof(CanNavigateSelectedForeignKey));
            return;
        }

        Session.ViewState.SelectedCell = null;
        RaisePropertyChanged(nameof(HasSelectedCell));
        RaisePropertyChanged(nameof(SelectedCellSummary));
        RaisePropertyChanged(nameof(CanNavigateSelectedForeignKey));
    }

    private void UpdateSelectedRowState(object? selectedItem)
    {
        if (Session is null || selectedItem is not DataRowView rowView)
        {
            if (Session is not null)
                Session.ViewState.SelectedRowIndex = null;
            SelectedRowFields = [];
            SelectedRowJson = "{}";
            IsResultDetailVisible = false;
            GeneratedWhereClause = string.Empty;
            RaisePropertyChanged(nameof(SelectedRowSummary));
            RaisePropertyChanged(nameof(CanGenerateWhereClause));
            NotifyCommands();
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
        RaisePropertyChanged(nameof(CanGenerateWhereClause));
        NotifyCommands();
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

    private void CopySelectedCell()
    {
        if (!TryGetSelectedCellValue(out _, out object? value))
            return;

        RaiseClipboardCopyRequested(FormatDisplayValue(value));
    }

    private void CopySelectedRowAsJson()
    {
        if (!TryGetSelectedRowObject(out Dictionary<string, object?>? rowObject) || rowObject is null)
            return;

        RaiseClipboardCopyRequested(JsonSerializer.Serialize(rowObject, RowJsonSerializerOptions));
    }

    private void CopySelectedRowAsCsv()
    {
        if (!TryGetSelectedRowObject(out Dictionary<string, object?>? rowObject) || rowObject is null)
            return;

        string header = string.Join(",", rowObject.Keys.Select(EscapeCsv));
        string values = string.Join(",", rowObject.Values.Select(value => EscapeCsv(FormatDisplayValue(value))));
        RaiseClipboardCopyRequested($"{header}\r\n{values}");
    }

    private void CopySelectedRowAsMarkdown()
    {
        if (!TryGetSelectedRowObject(out Dictionary<string, object?>? rowObject) || rowObject is null)
            return;

        string header = $"| {string.Join(" | ", rowObject.Keys.Select(EscapeMarkdown))} |";
        string separator = $"| {string.Join(" | ", rowObject.Keys.Select(_ => "---"))} |";
        string values = $"| {string.Join(" | ", rowObject.Values.Select(value => EscapeMarkdown(FormatDisplayValue(value))))} |";
        RaiseClipboardCopyRequested($"{header}\n{separator}\n{values}");
    }

    private void CopyVisibleRowsAsJson()
    {
        if (!TryBuildVisibleRowsJsonPayload(out string? payload) || string.IsNullOrWhiteSpace(payload))
            return;

        RaiseClipboardCopyRequested(payload);
    }

    private void CopyVisibleRowsAsCsv()
    {
        if (!TryBuildVisibleRowsCsvPayload(out string? payload) || string.IsNullOrWhiteSpace(payload))
            return;

        RaiseClipboardCopyRequested(payload);
    }

    private void CopyVisibleRowsAsMarkdown()
    {
        if (!TryBuildVisibleRowsMarkdownPayload(out string? payload) || string.IsNullOrWhiteSpace(payload))
            return;

        RaiseClipboardCopyRequested(payload);
    }

    private void ExportVisibleRowsAsJson()
    {
        if (!TryBuildVisibleRowsJsonPayload(out string? payload) || string.IsNullOrWhiteSpace(payload))
            return;

        RaiseExportRequested(new SqlResultExportRequest(
            SuggestedFileName: BuildSuggestedExportFileName("json"),
            DefaultExtension: "json",
            FileTypeTitle: "JSON",
            Patterns: ["*.json"],
            MimeTypes: ["application/json"],
            Content: payload));
    }

    private void ExportVisibleRowsAsCsv()
    {
        if (!TryBuildVisibleRowsCsvPayload(out string? payload) || string.IsNullOrWhiteSpace(payload))
            return;

        RaiseExportRequested(new SqlResultExportRequest(
            SuggestedFileName: BuildSuggestedExportFileName("csv"),
            DefaultExtension: "csv",
            FileTypeTitle: "CSV",
            Patterns: ["*.csv"],
            MimeTypes: ["text/csv"],
            Content: payload));
    }

    private void CopySelectedColumnAsSqlIn()
    {
        if (!TryGetSelectedCellValue(out string? columnName, out _)
            || string.IsNullOrWhiteSpace(columnName)
            || _filteredRows.Count == 0)
        {
            return;
        }

        var values = new List<string>(_filteredRows.Count);
        foreach (DataRow row in _filteredRows)
        {
            object? raw = row[columnName];
            values.Add(ToSqlLiteral(raw == DBNull.Value ? null : raw));
        }

        string payload = $"({string.Join(", ", values)})";
        RaiseClipboardCopyRequested(payload);
    }

    private void GenerateWhereClause()
    {
        if (!TryBuildWhereClause(out string whereClause))
            return;

        GeneratedWhereClause = whereClause;
        RaiseClipboardCopyRequested(whereClause);
    }

    private void FilterBySelectedCellValue()
    {
        if (Session is null || !TryGetSelectedCellValue(out string? columnName, out object? value) || string.IsNullOrWhiteSpace(columnName))
            return;

        string normalizedValue = value?.ToString() ?? string.Empty;
        Session.ViewState.Filters = Session.ViewState.Filters
            .Where(filter => !(string.Equals(filter.ColumnName, columnName, StringComparison.Ordinal)
                               && string.Equals(filter.Operation, FilterOperationEquals, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        Session.ViewState.Filters.Add(new SqlColumnFilter(columnName, FilterOperationEquals, normalizedValue));

        ActiveFilterCriteria = new ObservableCollection<SqlResultFilterCriterionItemViewModel>(
            Session.ViewState.Filters.Select(filter => new SqlResultFilterCriterionItemViewModel(
                filter.ColumnName,
                filter.Operation,
                filter.Value)));

        CurrentPage = 1;
        RebuildRowsProjection();
    }

    private void HideSelectedCellColumn()
    {
        if (Session is null || !TryGetSelectedCellValue(out string? columnName, out _) || string.IsNullOrWhiteSpace(columnName))
            return;

        if (Session.ViewState.VisibleColumns.Count <= 1 || !Session.ViewState.VisibleColumns.Contains(columnName))
            return;

        Session.ViewState.VisibleColumns.Remove(columnName);
        if (Session.ViewState.SelectedCell?.ColumnName.Equals(columnName, StringComparison.Ordinal) == true)
            Session.ViewState.SelectedCell = null;

        CurrentPage = 1;
        RebuildRowsProjection();
    }

    private void SortSelectedColumn(bool descending)
    {
        if (Session is null || !TryGetSelectedCellValue(out string? columnName, out _) || string.IsNullOrWhiteSpace(columnName))
            return;

        Session.ViewState.Sorts = Session.ViewState.Sorts
            .Where(sort => !string.Equals(sort.ColumnName, columnName, StringComparison.Ordinal))
            .ToList();
        Session.ViewState.Sorts.Insert(0, new SqlColumnSort(columnName, Descending: descending));

        ActiveSortCriteria = new ObservableCollection<SqlResultSortCriterionItemViewModel>(
            Session.ViewState.Sorts.Select(sort => new SqlResultSortCriterionItemViewModel(sort.ColumnName, !sort.Descending)));

        CurrentPage = 1;
        RebuildRowsProjection();
    }

    private bool TryBuildWhereClause(out string whereClause)
    {
        whereClause = string.Empty;
        if (Session?.InlineEditEligibility.IsEligible != true || Session.InlineEditEligibility.PrimaryKeyColumns.Count == 0)
            return false;

        if (!TryGetSelectedSourceRow(out DataRow? sourceRow) || sourceRow is null)
            return false;

        var predicates = new List<string>(Session.InlineEditEligibility.PrimaryKeyColumns.Count);
        foreach (string keyColumn in Session.InlineEditEligibility.PrimaryKeyColumns)
        {
            if (!sourceRow.Table.Columns.Contains(keyColumn))
                return false;

            object raw = sourceRow[keyColumn];
            object? keyValue = raw == DBNull.Value ? null : raw;
            predicates.Add(keyValue is null
                ? $"{keyColumn} IS NULL"
                : $"{keyColumn} = {ToSqlLiteral(keyValue)}");
        }

        whereClause = $"WHERE {string.Join(" AND ", predicates)}";
        return true;
    }

    private bool TryGetSelectedSourceRow(out DataRow? row)
    {
        row = null;
        if (Session is null || Session.ViewState.SelectedRowIndex is not int rowIndex)
            return false;

        if (rowIndex < 0 || rowIndex >= _pagedSourceRows.Count)
            return false;

        row = _pagedSourceRows[rowIndex];
        return true;
    }

    private bool TryGetSelectedRowObject(out Dictionary<string, object?>? rowObject)
    {
        rowObject = null;
        if (Session is null || _pagedRowsTable is null || Session.ViewState.SelectedRowIndex is not int rowIndex)
            return false;

        if (rowIndex < 0 || rowIndex >= _pagedRowsTable.Rows.Count)
            return false;

        DataRow row = _pagedRowsTable.Rows[rowIndex];
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (DataColumn column in _pagedRowsTable.Columns)
        {
            object? rawValue = row[column];
            values[column.ColumnName] = rawValue == DBNull.Value ? null : rawValue;
        }

        rowObject = values;
        return true;
    }

    private bool TryGetVisibleRows(out List<Dictionary<string, object?>>? rows)
    {
        rows = null;
        if (_pagedRowsTable is null || _pagedRowsTable.Rows.Count == 0)
            return false;

        var collection = new List<Dictionary<string, object?>>(_pagedRowsTable.Rows.Count);
        foreach (DataRow row in _pagedRowsTable.Rows)
        {
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (DataColumn column in _pagedRowsTable.Columns)
            {
                object? rawValue = row[column];
                values[column.ColumnName] = rawValue == DBNull.Value ? null : rawValue;
            }

            collection.Add(values);
        }

        rows = collection;
        return true;
    }

    private bool TryBuildVisibleRowsJsonPayload(out string? payload)
    {
        payload = null;
        if (!TryGetVisibleRows(out List<Dictionary<string, object?>>? rows) || rows is null || rows.Count == 0)
            return false;

        payload = JsonSerializer.Serialize(rows, RowJsonSerializerOptions);
        return !string.IsNullOrWhiteSpace(payload);
    }

    private bool TryBuildVisibleRowsCsvPayload(out string? payload)
    {
        payload = null;
        if (!TryGetVisibleRows(out List<Dictionary<string, object?>>? rows) || rows is null || rows.Count == 0)
            return false;

        string[] columns = rows[0].Keys.ToArray();
        string header = string.Join(",", columns.Select(EscapeCsv));
        string body = string.Join(
            "\r\n",
            rows.Select(row => string.Join(",", columns.Select(column => EscapeCsv(FormatDisplayValue(row[column]))))));
        payload = $"{header}\r\n{body}";
        return !string.IsNullOrWhiteSpace(payload);
    }

    private bool TryBuildVisibleRowsMarkdownPayload(out string? payload)
    {
        payload = null;
        if (!TryGetVisibleRows(out List<Dictionary<string, object?>>? rows) || rows is null || rows.Count == 0)
            return false;

        string[] columns = rows[0].Keys.ToArray();
        string header = $"| {string.Join(" | ", columns.Select(EscapeMarkdown))} |";
        string separator = $"| {string.Join(" | ", columns.Select(_ => "---"))} |";
        string body = string.Join(
            "\n",
            rows.Select(row => $"| {string.Join(" | ", columns.Select(column => EscapeMarkdown(FormatDisplayValue(row[column]))))} |"));
        payload = $"{header}\n{separator}\n{body}";
        return !string.IsNullOrWhiteSpace(payload);
    }

    private bool TryGetSelectedCellValue(out string? columnName, out object? value)
    {
        columnName = null;
        value = null;

        if (Session is null || _pagedRowsTable is null)
            return false;

        CellSelection? selectedCell = Session.ViewState.SelectedCell;
        if (selectedCell is null)
            return false;

        if (selectedCell.RowIndex < 0 || selectedCell.RowIndex >= _pagedRowsTable.Rows.Count)
            return false;

        if (!_pagedRowsTable.Columns.Contains(selectedCell.ColumnName))
            return false;

        columnName = selectedCell.ColumnName;
        object? rawValue = _pagedRowsTable.Rows[selectedCell.RowIndex][selectedCell.ColumnName];
        value = rawValue == DBNull.Value ? null : rawValue;
        return true;
    }

    private void RaiseClipboardCopyRequested(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        ClipboardCopyRequested?.Invoke(text);
    }

    private void RaiseExportRequested(SqlResultExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Content))
            return;

        ExportRequested?.Invoke(request);
    }

    private string BuildSuggestedExportFileName(string extension)
    {
        string? baseName = Session?.InlineEditEligibility.TableFullName;
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "sql-result";

        string normalized = new string(
            baseName
                .Trim()
                .Select(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '-')
                .ToArray())
            .Trim('-');
        if (string.IsNullOrWhiteSpace(normalized))
            normalized = "sql-result";

        return $"{normalized}.{extension.TrimStart('.')}";
    }

    private static string FormatDisplayValue(object? value)
    {
        if (value is null || value == DBNull.Value)
            return "<null>";

        return value switch
        {
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            TimeSpan ts => ts.ToString("c", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    private static string EscapeCsv(string value)
    {
        string normalized = value ?? string.Empty;
        bool requiresQuotes = normalized.Contains(',') || normalized.Contains('"') || normalized.Contains('\n') || normalized.Contains('\r');
        if (!requiresQuotes)
            return normalized;

        return $"\"{normalized.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string EscapeMarkdown(string value)
    {
        string normalized = value ?? string.Empty;
        return normalized
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", "<br/>", StringComparison.Ordinal);
    }

    private static IReadOnlyList<SqlEditorReportSchemaDetail> BuildReportSchemaDetails(
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var details = new List<SqlEditorReportSchemaDetail>(columns.Count);

        foreach (string column in columns)
        {
            long nullCount = 0;
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            string? example = null;
            string? minValue = null;
            string? maxValue = null;
            string kind = "null";

            foreach (IReadOnlyDictionary<string, object?> row in rows)
            {
                row.TryGetValue(column, out object? value);
                if (value is null)
                {
                    nullCount += 1;
                    continue;
                }

                string text = value.ToString() ?? string.Empty;
                distinct.Add(text);
                example ??= text;

                if (minValue is null || string.CompareOrdinal(text, minValue) < 0)
                    minValue = text;

                if (maxValue is null || string.CompareOrdinal(text, maxValue) > 0)
                    maxValue = text;

                string detectedKind = DetectReportValueKind(value);
                if (kind is "null" or "text")
                {
                    kind = detectedKind;
                }
                else if (!string.Equals(kind, detectedKind, StringComparison.Ordinal))
                {
                    kind = "text";
                }
            }

            details.Add(new SqlEditorReportSchemaDetail(
                Name: column,
                Kind: kind,
                NullCount: nullCount,
                DistinctCount: distinct.Count,
                Example: example,
                MinValue: minValue,
                MaxValue: maxValue));
        }

        return details;
    }

    private static object? NormalizeReportCellValue(object? value)
    {
        if (value is null || value is DBNull)
            return null;

        return value switch
        {
            DateTimeOffset dto => dto.ToString("O"),
            DateTime dt => dt.ToString("O"),
            TimeSpan ts => ts.ToString(),
            Guid guid => guid.ToString("D"),
            byte[] bytes => Convert.ToBase64String(bytes),
            _ => value,
        };
    }

    private static string DetectReportValueKind(object value)
    {
        return value switch
        {
            bool => "bool",
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => "number",
            DateTime or DateTimeOffset => "date",
            _ => "text",
        };
    }

    private static string BuildReportTabTitle(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return "SQL Result";

        string trimmed = sql.Trim();
        if (trimmed.Length <= 60)
            return trimmed;

        return $"{trimmed[..57]}...";
    }

    private static string ToSqlLiteral(object? value)
    {
        if (value is null || value is DBNull)
            return "NULL";

        return value switch
        {
            bool b => b ? "TRUE" : "FALSE",
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal =>
                Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0",
            DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss.fffffff}'",
            DateTimeOffset dto => $"'{dto:yyyy-MM-dd HH:mm:ss.fffffff zzz}'",
            TimeSpan ts => $"'{ts:c}'",
            Guid guid => $"'{guid:D}'",
            _ => $"'{(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty).Replace("'", "''", StringComparison.Ordinal)}'",
        };
    }

    private void CancelPendingEdits()
    {
        if (Session is null || Session.ViewState.PendingEdits.Count == 0 || Session.ResultSet.Data is null)
            return;

        foreach (PendingCellEdit pendingEdit in Session.ViewState.PendingEdits.ToList())
        {
            if (!TryFindSourceRowByKeyValues(Session.ResultSet.Data, pendingEdit.KeyValues, out DataRow? sourceRow)
                || sourceRow is null)
                continue;

            object value = pendingEdit.OriginalValue ?? DBNull.Value;
            sourceRow[pendingEdit.ColumnName] = value;
        }

        Session.ViewState.PendingEdits.Clear();
        GeneratedWhereClause = string.Empty;
        ResetPendingChangeArtifacts();
        SetPendingExecutionConfirmationVisible(false);
        RaisePropertyChanged(nameof(HasPendingEdits));
        RaisePropertyChanged(nameof(PendingEditsCount));
        RaisePropertyChanged(nameof(CanCancelPendingEdits));
        RaisePropertyChanged(nameof(CanPreparePendingChangesPreview));
        RaisePropertyChanged(nameof(CanGeneratePendingSql));
        RebuildRowsProjection();
    }

    public async Task<bool> PreparePendingChangesPreviewAsync(CancellationToken ct = default)
    {
        if (!TryBuildPendingChangeSet(out SqlResultChangeSet? changeSet)
            || changeSet is null
            || Session is null)
        {
            return false;
        }

        IReadOnlyList<string> statements = BuildPendingUpdateStatements(Session, changeSet);
        if (statements.Count == 0)
            return false;

        _isPreparingPendingPreview = true;
        RaisePropertyChanged(nameof(CanPreparePendingChangesPreview));
        NotifyCommands();

        try
        {
            _pendingChangeSetPreview = changeSet;
            GeneratedPendingSqlText = string.Join(Environment.NewLine, statements);
            RaisePropertyChanged(nameof(HasPendingChangeSetPreview));
            RaisePropertyChanged(nameof(HasGeneratedPendingSqlText));
            RaisePropertyChanged(nameof(HasPendingPreviewPanel));
            RaisePropertyChanged(nameof(CanCopyGeneratedPendingSql));
            RaisePropertyChanged(nameof(CanSendGeneratedPendingSqlToEditor));

            PendingChangeSetSummaryText = BuildPendingChangeSetSummaryText(changeSet);

            ConnectionConfig? config = _connectionConfigBySessionResolver?.Invoke(Session.ConnectionId);
            var previewLines = new List<string>();
            foreach (string statement in statements.Take(6))
            {
                MutationGuardResult guard = _mutationGuardService.Analyze(statement);
                SqlMutationDiffPreview diffPreview = await _mutationDiffService.BuildPreviewAsync(
                    statement,
                    guard,
                    config,
                    estimatedAffectedRows: null,
                    ct);
                previewLines.Add(diffPreview.Message);
            }

            PendingDiffPreviewText = string.Join(Environment.NewLine + Environment.NewLine, previewLines);
            return true;
        }
        finally
        {
            _isPreparingPendingPreview = false;
            RaisePropertyChanged(nameof(CanPreparePendingChangesPreview));
            NotifyCommands();
        }
    }

    private void ClearPendingChangesPreview()
    {
        ResetPendingChangeArtifacts();
    }

    private void GeneratePendingSqlFromCurrentEdits()
    {
        if (!TryBuildPendingChangeSet(out SqlResultChangeSet? changeSet)
            || changeSet is null
            || Session is null)
        {
            return;
        }

        IReadOnlyList<string> statements = BuildPendingUpdateStatements(Session, changeSet);
        if (statements.Count == 0)
            return;

        _pendingChangeSetPreview = changeSet;
        GeneratedPendingSqlText = string.Join(Environment.NewLine, statements);
        PendingChangeSetSummaryText = BuildPendingChangeSetSummaryText(changeSet);
        RaisePropertyChanged(nameof(HasPendingChangeSetPreview));
        RaisePropertyChanged(nameof(HasGeneratedPendingSqlText));
        RaisePropertyChanged(nameof(HasPendingPreviewPanel));
        RaisePropertyChanged(nameof(CanCopyGeneratedPendingSql));
        RaisePropertyChanged(nameof(CanSendGeneratedPendingSqlToEditor));
        NotifyCommands();
    }

    private void CopyGeneratedPendingSql()
    {
        if (string.IsNullOrWhiteSpace(GeneratedPendingSqlText))
            return;

        ClipboardCopyRequested?.Invoke(GeneratedPendingSqlText);
    }

    private void SendGeneratedPendingSqlToEditor()
    {
        if (string.IsNullOrWhiteSpace(GeneratedPendingSqlText) || _appendSqlToEditor is null)
            return;

        _appendSqlToEditor.Invoke(_sourceSqlEditorDocumentId, GeneratedPendingSqlText);
    }

    private void RequestExecutePendingChanges()
    {
        if (Session is null || !HasPendingEdits)
            return;

        if (IsProductionLikeConnectionContext)
        {
            UpdatePendingExecutionStatus(
                "Direct execution is blocked for production-like connection context. Use Generate SQL/Send to Editor.",
                hasError: true);
            return;
        }

        if (UseTransactionalExecution && !IsTransactionModeAvailable)
        {
            UpdatePendingExecutionStatus(
                TransactionModeStatusText,
                hasError: true);
            return;
        }

        if (!HasGeneratedPendingSqlText)
            GeneratePendingSqlFromCurrentEdits();

        SetPendingExecutionConfirmationVisible(true);
        UpdatePendingExecutionStatus(
            UseTransactionalExecution
                ? "Direct execution in transaction mode requires explicit confirmation. Choose commit or rollback."
                : "Direct execution requires explicit confirmation. Review preview and confirm execution.",
            hasError: false);
    }

    public async Task<bool> RefreshCurrentSessionAsync(CancellationToken ct = default)
    {
        if (Session is null)
            return false;

        ConnectionConfig? connectionConfig = _connectionConfigBySessionResolver?.Invoke(Session.ConnectionId);
        if (connectionConfig is null)
        {
            UpdatePendingExecutionStatus("No active connection available to refresh result set.", hasError: true);
            return false;
        }

        _isRefreshingSession = true;
        RaisePropertyChanged(nameof(CanRefreshSession));
        NotifyCommands();
        try
        {
            SqlEditorResultSet refreshed = await _executeSqlAsync(
                Session.SqlText,
                connectionConfig,
                DefaultRefreshMaxRows,
                ct);

            if (!refreshed.Success || refreshed.Data is null)
            {
                string reason = refreshed.ErrorMessage ?? "Failed to refresh result set.";
                UpdatePendingExecutionStatus(reason, hasError: true);
                return false;
            }

            Session.ResultSet = refreshed;
            Session.Status = SqlResultSessionStatus.Success;
            Session.ExecutedAt = refreshed.ExecutedAt;
            Session.ExecutionTime = refreshed.ExecutionTime;
            Session.ViewState.PendingEdits.Clear();
            GeneratedWhereClause = string.Empty;

            RaisePropertyChanged(nameof(StatusText));
            RaisePropertyChanged(nameof(ExecutedAtText));
            RaisePropertyChanged(nameof(DurationText));
            RaisePropertyChanged(nameof(HasPendingEdits));
            RaisePropertyChanged(nameof(PendingEditsCount));
            RaisePropertyChanged(nameof(CanCancelPendingEdits));
            RaisePropertyChanged(nameof(CanPreparePendingChangesPreview));
            RaisePropertyChanged(nameof(CanGeneratePendingSql));
            RaisePropertyChanged(nameof(CanRequestExecutePendingChanges));
            ResetPendingChangeArtifacts();
            UpdatePendingExecutionStatus("Result set refreshed successfully.", hasError: false);
            RebuildRowsProjection();
            RefreshSessions();
            return true;
        }
        finally
        {
            _isRefreshingSession = false;
            RaisePropertyChanged(nameof(CanRefreshSession));
            NotifyCommands();
        }
    }

    public async Task<bool> BuildColumnProfilesAsync(CancellationToken ct = default)
    {
        DataTable? table = Session?.ResultSet.Data;
        if (table is null)
        {
            UpdateColumnProfileStatus("No result set available for column profiling.");
            return false;
        }

        _isBuildingColumnProfiles = true;
        RaisePropertyChanged(nameof(IsBuildingColumnProfiles));
        NotifyCommands();
        UpdateColumnProfileStatus("Building column profiles...");

        try
        {
            IReadOnlyList<SqlResultColumnProfile> profiles = await _columnProfilingService.BuildProfilesAsync(table, ct);
            ColumnProfiles = new ObservableCollection<SqlResultColumnProfileItemViewModel>(
                profiles.Select(SqlResultColumnProfileItemViewModel.FromProfile));

            string status = profiles.Count == 0
                ? "No columns available for profiling."
                : $"Column profile generated for {profiles.Count} column(s).";
            UpdateColumnProfileStatus(status);
            return profiles.Count > 0;
        }
        catch (OperationCanceledException)
        {
            UpdateColumnProfileStatus("Column profiling was canceled.");
            return false;
        }
        catch (Exception ex)
        {
            UpdateColumnProfileStatus($"Failed to build column profile: {ex.Message}");
            return false;
        }
        finally
        {
            _isBuildingColumnProfiles = false;
            RaisePropertyChanged(nameof(IsBuildingColumnProfiles));
            NotifyCommands();
        }
    }

    public Task<bool> ConfirmExecutePendingChangesAsync(CancellationToken ct = default)
    {
        return ExecutePendingChangesAsync(commitChanges: true, ct);
    }

    public Task<bool> ConfirmExecutePendingChangesWithRollbackAsync(CancellationToken ct = default)
    {
        return ExecutePendingChangesAsync(commitChanges: false, ct);
    }

    private async Task<bool> ExecutePendingChangesAsync(bool commitChanges, CancellationToken ct = default)
    {
        if (Session is null || !IsPendingExecutionConfirmationVisible)
            return false;

        if (!TryBuildPendingChangeSet(out SqlResultChangeSet? changeSet)
            || changeSet is null)
        {
            UpdatePendingExecutionStatus("No pending changes to execute.", hasError: true);
            return false;
        }

        if (IsProductionLikeConnectionContext)
        {
            UpdatePendingExecutionStatus(
                "Direct execution remains blocked for production-like connection context.",
                hasError: true);
            return false;
        }

        if (UseTransactionalExecution && !IsTransactionModeAvailable)
        {
            UpdatePendingExecutionStatus(TransactionModeStatusText, hasError: true);
            return false;
        }

        ConnectionConfig? connectionConfig = _connectionConfigBySessionResolver?.Invoke(Session.ConnectionId);
        if (connectionConfig is null)
        {
            UpdatePendingExecutionStatus("No active connection available for direct execution.", hasError: true);
            return false;
        }

        IReadOnlyList<string> statements = BuildPendingUpdateStatements(Session, changeSet);
        if (statements.Count == 0)
        {
            UpdatePendingExecutionStatus("No executable SQL statements were generated.", hasError: true);
            return false;
        }

        _isExecutingPendingChanges = true;
        RaisePropertyChanged(nameof(IsExecutingPendingChanges));
        RaisePropertyChanged(nameof(CanRefreshSession));
        RaisePropertyChanged(nameof(CanRequestExecutePendingChanges));
        RaisePropertyChanged(nameof(CanConfirmExecutePendingChanges));
        RaisePropertyChanged(nameof(CanConfirmExecutePendingChangesWithRollback));
        RaisePropertyChanged(nameof(CanCancelExecutePendingChanges));
        NotifyCommands();

        try
        {
            if (UseTransactionalExecution)
            {
                SqlResultTransactionExecutionResult transactionalResult =
                    await _executeTransactionalPendingChangesAsync(connectionConfig, statements, commitChanges, ct);
                if (!transactionalResult.Success)
                {
                    string reason = transactionalResult.ErrorMessage ?? "Transactional execution failed.";
                    UpdatePendingExecutionStatus(reason, hasError: true);
                    return false;
                }

                SetPendingExecutionConfirmationVisible(false);
                if (!transactionalResult.WasCommitted)
                {
                    UpdatePendingExecutionStatus(
                        $"Executed {transactionalResult.ExecutedStatements} statement(s) in transaction and rolled back.",
                        hasError: false);
                    return true;
                }

                ClearPendingEditsAfterExecution();
                bool refreshedAfterCommit = await RefreshCurrentSessionAsync(ct);
                if (!refreshedAfterCommit)
                {
                    UpdatePendingExecutionStatus(
                        $"Committed {transactionalResult.ExecutedStatements} statement(s), but result refresh failed.",
                        hasError: true);
                }
                else
                {
                    UpdatePendingExecutionStatus(
                        $"Committed {transactionalResult.ExecutedStatements} statement(s) and refreshed result set.",
                        hasError: false);
                }

                return true;
            }

            int executedCount = 0;
            foreach (string statement in statements)
            {
                SqlEditorMutationExecutionOutcome outcome = await _mutationExecutionOrchestrator.ExecuteAsync(
                    statement,
                    connectionConfig,
                    maxRows: 1,
                    enforceMutationGuard: true,
                    estimateCacheKey: null,
                    ct: ct);

                if (outcome.RequiresConfirmation)
                {
                    string reason = outcome.ConfirmationState?.Guard.Issues.FirstOrDefault()?.Message
                        ?? "Execution blocked by mutation guard.";
                    UpdatePendingExecutionStatus(reason, hasError: true);
                    return false;
                }

                if (!outcome.Result.Success)
                {
                    string reason = outcome.Result.ErrorMessage ?? "Execution failed.";
                    UpdatePendingExecutionStatus(reason, hasError: true);
                    return false;
                }

                executedCount++;
            }

            ClearPendingEditsAfterExecution();
            bool refreshed = await RefreshCurrentSessionAsync(ct);
            if (!refreshed)
            {
                UpdatePendingExecutionStatus(
                    $"Executed {executedCount} statement(s), but result refresh failed.",
                    hasError: true);
            }
            else
            {
                UpdatePendingExecutionStatus($"Executed {executedCount} statement(s) and refreshed result set.", hasError: false);
            }
            return true;
        }
        finally
        {
            _isExecutingPendingChanges = false;
            RaisePropertyChanged(nameof(IsExecutingPendingChanges));
            RaisePropertyChanged(nameof(CanRefreshSession));
            RaisePropertyChanged(nameof(CanRequestExecutePendingChanges));
            RaisePropertyChanged(nameof(CanConfirmExecutePendingChanges));
            RaisePropertyChanged(nameof(CanConfirmExecutePendingChangesWithRollback));
            RaisePropertyChanged(nameof(CanCancelExecutePendingChanges));
            NotifyCommands();
        }
    }

    private void CancelExecutePendingChanges()
    {
        if (!IsPendingExecutionConfirmationVisible)
            return;

        SetPendingExecutionConfirmationVisible(false);
        UpdatePendingExecutionStatus("Direct execution canceled.", hasError: false);
    }

    private void ClearPendingEditsAfterExecution()
    {
        if (Session is null)
            return;

        Session.ViewState.PendingEdits.Clear();
        SetPendingExecutionConfirmationVisible(false);
        ResetPendingChangeArtifacts();
        GeneratedWhereClause = string.Empty;
        RaisePropertyChanged(nameof(HasPendingEdits));
        RaisePropertyChanged(nameof(PendingEditsCount));
        RaisePropertyChanged(nameof(CanCancelPendingEdits));
        RaisePropertyChanged(nameof(CanPreparePendingChangesPreview));
        RaisePropertyChanged(nameof(CanGeneratePendingSql));
        RaisePropertyChanged(nameof(CanRequestExecutePendingChanges));
    }

    private bool TryBuildPendingChangeSet(out SqlResultChangeSet? changeSet)
    {
        changeSet = null;
        if (Session is null || Session.ViewState.PendingEdits.Count == 0)
            return false;

        changeSet = new SqlResultChangeSet(
            ResultSessionId: Session.Id,
            ConnectionId: Session.ConnectionId,
            Provider: Session.Provider,
            Edits: Session.ViewState.PendingEdits.ToList());
        return true;
    }

    private static IReadOnlyList<string> BuildPendingUpdateStatements(SqlResultSession session, SqlResultChangeSet changeSet)
    {
        var statements = new List<string>(changeSet.Edits.Count);
        foreach (PendingCellEdit edit in changeSet.Edits)
        {
            string tableFullName = ResolvePendingEditTableFullName(session, edit);
            string sql = SqlInlineUpdateStatementBuilder.Build(
                session.Provider,
                tableFullName,
                edit.ColumnName,
                edit.NewValue,
                edit.KeyValues);
            statements.Add(sql);
        }

        return statements;
    }

    private static string ResolvePendingEditTableFullName(SqlResultSession session, PendingCellEdit edit)
    {
        string? tableFromEligibility = session.InlineEditEligibility.TableFullName;
        if (!string.IsNullOrWhiteSpace(tableFromEligibility))
            return tableFromEligibility;

        return string.IsNullOrWhiteSpace(edit.SchemaName)
            ? edit.TableName
            : $"{edit.SchemaName}.{edit.TableName}";
    }

    private static string BuildPendingChangeSetSummaryText(SqlResultChangeSet changeSet)
    {
        IEnumerable<IGrouping<string, PendingCellEdit>> rowGroups = changeSet.Edits
            .GroupBy(static edit => BuildPendingEditIdentity(edit.KeyValues, string.Empty), StringComparer.Ordinal);

        int rowCount = rowGroups.Count();
        int editCount = changeSet.Edits.Count;
        string header = $"Pending edits: {editCount} cell(s) in {rowCount} row(s).";

        IEnumerable<string> samples = changeSet.Edits
            .Take(8)
            .Select(edit =>
            {
                string keys = string.Join(", ", edit.KeyValues.Select(kvp => $"{kvp.Key}={FormatPendingValue(kvp.Value)}"));
                return $"{edit.ColumnName}: {FormatPendingValue(edit.OriginalValue)} -> {FormatPendingValue(edit.NewValue)} ({keys})";
            });

        return $"{header}{Environment.NewLine}{string.Join(Environment.NewLine, samples)}";
    }

    private static string FormatPendingValue(object? value)
    {
        if (value is null || value == DBNull.Value)
            return "<null>";

        return value switch
        {
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            byte[] bytes => $"base64:{Convert.ToBase64String(bytes)}",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    private void ResetColumnProfiles()
    {
        ColumnProfiles = [];
        UpdateColumnProfileStatus(string.Empty);
        _isBuildingColumnProfiles = false;
        RaisePropertyChanged(nameof(IsBuildingColumnProfiles));
        NotifyCommands();
    }

    private void UpdateColumnProfileStatus(string statusText)
    {
        ColumnProfileStatusText = statusText;
        RaisePropertyChanged(nameof(HasColumnProfileStatusText));
    }

    private void ResetPendingChangeArtifacts()
    {
        _pendingChangeSetPreview = null;
        PendingChangeSetSummaryText = string.Empty;
        PendingDiffPreviewText = string.Empty;
        GeneratedPendingSqlText = string.Empty;
        SetPendingExecutionConfirmationVisible(false);
        RaisePropertyChanged(nameof(HasPendingChangeSetPreview));
        RaisePropertyChanged(nameof(HasGeneratedPendingSqlText));
        RaisePropertyChanged(nameof(HasPendingPreviewPanel));
        RaisePropertyChanged(nameof(CanCopyGeneratedPendingSql));
        RaisePropertyChanged(nameof(CanSendGeneratedPendingSqlToEditor));
        NotifyCommands();
    }

    private void SetPendingExecutionConfirmationVisible(bool visible)
    {
        if (_isPendingExecutionConfirmationVisible == visible)
            return;

        _isPendingExecutionConfirmationVisible = visible;
        RaisePropertyChanged(nameof(IsPendingExecutionConfirmationVisible));
        RaisePropertyChanged(nameof(HasPendingPreviewPanel));
        RaisePropertyChanged(nameof(CanConfirmExecutePendingChanges));
        RaisePropertyChanged(nameof(CanConfirmExecutePendingChangesWithRollback));
        RaisePropertyChanged(nameof(CanCancelExecutePendingChanges));
        NotifyCommands();
    }

    private void UpdatePendingExecutionStatus(string statusText, bool hasError)
    {
        PendingExecutionStatusText = statusText;
        RaisePropertyChanged(nameof(HasPendingExecutionStatusText));
        RaisePropertyChanged(nameof(HasPendingPreviewPanel));

        if (_hasPendingExecutionError != hasError)
        {
            _hasPendingExecutionError = hasError;
            RaisePropertyChanged(nameof(HasPendingExecutionError));
        }
    }

    private bool DetectProductionLikeConnectionContext()
    {
        if (Session is null)
            return false;

        string connectionToken = Session.ConnectionId ?? string.Empty;
        string databaseToken = Session.DatabaseName ?? string.Empty;
        ConnectionConfig? config = _connectionConfigBySessionResolver?.Invoke(Session.ConnectionId);
        string configDatabaseToken = config?.Database ?? string.Empty;

        string source = $"{connectionToken} {databaseToken} {configDatabaseToken}".ToLowerInvariant();
        return source.Contains("prod", StringComparison.Ordinal)
               || source.Contains("production", StringComparison.Ordinal);
    }

    private void ApplySessionTransactionModePreference(SqlResultSession? session)
    {
        bool preferredMode = session?.ViewState.UseTransactionalExecution == true;
        bool normalizedMode = preferredMode && session is not null && SupportsTransactionProvider(session.Provider);
        _useTransactionalExecution = normalizedMode;
        if (session is not null)
            session.ViewState.UseTransactionalExecution = normalizedMode;
    }

    private bool SupportsTransactionProvider(DatabaseProvider provider)
    {
        return _transactionExecutionService.SupportsProvider(provider);
    }

    private bool TryCreatePendingEdit(
        DataRow sourceRow,
        string columnName,
        object? originalValue,
        object? newValue,
        out PendingCellEdit? pendingEdit)
    {
        pendingEdit = null;
        if (Session?.InlineEditEligibility.IsEligible != true || Session.ResultSet.Data is null)
            return false;

        IReadOnlyList<string> keyColumns = Session.InlineEditEligibility.PrimaryKeyColumns;
        if (keyColumns.Count == 0)
            return false;

        var keyValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (string keyColumn in keyColumns)
        {
            if (!Session.ResultSet.Data.Columns.Contains(keyColumn))
                return false;

            object raw = sourceRow[keyColumn];
            keyValues[keyColumn] = raw == DBNull.Value ? null : raw;
        }

        (string? schemaName, string tableName) = ResolveTableIdentity(Session.InlineEditEligibility.TableFullName);
        pendingEdit = new PendingCellEdit(
            TableName: tableName,
            SchemaName: schemaName,
            ColumnName: columnName,
            OriginalValue: originalValue,
            NewValue: newValue,
            KeyValues: keyValues);
        return true;
    }

    private void UpsertPendingEdit(PendingCellEdit pendingEdit)
    {
        if (Session is null)
            return;

        string identity = BuildPendingEditIdentity(pendingEdit.KeyValues, pendingEdit.ColumnName);
        int existingIndex = Session.ViewState.PendingEdits
            .Select((item, index) => (item, index))
            .Where(tuple => string.Equals(
                BuildPendingEditIdentity(tuple.item.KeyValues, tuple.item.ColumnName),
                identity,
                StringComparison.Ordinal))
            .Select(tuple => tuple.index)
            .DefaultIfEmpty(-1)
            .First();

        if (existingIndex < 0)
        {
            Session.ViewState.PendingEdits.Add(pendingEdit);
            return;
        }

        PendingCellEdit existing = Session.ViewState.PendingEdits[existingIndex];
        if (ValuesEqual(existing.OriginalValue, pendingEdit.NewValue))
        {
            Session.ViewState.PendingEdits.RemoveAt(existingIndex);
            return;
        }

        Session.ViewState.PendingEdits[existingIndex] = pendingEdit with
        {
            OriginalValue = existing.OriginalValue,
        };
    }

    private static string BuildPendingEditIdentity(IReadOnlyDictionary<string, object?> keyValues, string columnName)
    {
        IEnumerable<string> keyParts = keyValues
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => $"{entry.Key}={InvariantString(entry.Value)}");
        return $"{columnName}::{string.Join(";", keyParts)}";
    }

    private static (string? SchemaName, string TableName) ResolveTableIdentity(string? tableFullName)
    {
        if (string.IsNullOrWhiteSpace(tableFullName))
            return (null, "unknown_table");

        string[] parts = tableFullName
            .Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
            return (null, parts[0]);

        return (parts[0], parts[^1]);
    }

    private static bool TryFindSourceRowByKeyValues(
        DataTable sourceTable,
        IReadOnlyDictionary<string, object?> keyValues,
        out DataRow? row)
    {
        row = sourceTable.Rows
            .Cast<DataRow>()
            .FirstOrDefault(candidate => keyValues.All(kvp =>
            {
                if (!sourceTable.Columns.Contains(kvp.Key))
                    return false;

                object raw = candidate[kvp.Key];
                object? normalized = raw == DBNull.Value ? null : raw;
                return ValuesEqual(normalized, kvp.Value);
            }));

        return row is not null;
    }

    private static bool TryConvertEditedValue(Type targetType, string? editedText, out object? convertedValue)
    {
        Type normalizedType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        string input = editedText?.Trim() ?? string.Empty;
        if (input.Length == 0)
        {
            convertedValue = null;
            return true;
        }

        if (normalizedType == typeof(string))
        {
            convertedValue = input;
            return true;
        }

        if (normalizedType == typeof(bool))
        {
            if (bool.TryParse(input, out bool boolValue))
            {
                convertedValue = boolValue;
                return true;
            }

            convertedValue = null;
            return false;
        }

        if (normalizedType == typeof(DateTime))
        {
            if (DateTime.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime dateValue))
            {
                convertedValue = dateValue;
                return true;
            }

            convertedValue = null;
            return false;
        }

        if (normalizedType == typeof(DateTimeOffset))
        {
            if (DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset dateOffsetValue))
            {
                convertedValue = dateOffsetValue;
                return true;
            }

            convertedValue = null;
            return false;
        }

        if (normalizedType == typeof(TimeSpan))
        {
            if (TimeSpan.TryParse(input, CultureInfo.InvariantCulture, out TimeSpan spanValue))
            {
                convertedValue = spanValue;
                return true;
            }

            convertedValue = null;
            return false;
        }

        if (normalizedType == typeof(Guid))
        {
            if (Guid.TryParse(input, out Guid guidValue))
            {
                convertedValue = guidValue;
                return true;
            }

            convertedValue = null;
            return false;
        }

        if (normalizedType == typeof(byte[]))
        {
            try
            {
                convertedValue = Convert.FromBase64String(input);
                return true;
            }
            catch (FormatException)
            {
                convertedValue = null;
                return false;
            }
        }

        try
        {
            convertedValue = Convert.ChangeType(input, normalizedType, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            convertedValue = null;
            return false;
        }
    }

    private static bool ValuesEqual(object? left, object? right)
    {
        bool leftNull = left is null || left == DBNull.Value;
        bool rightNull = right is null || right == DBNull.Value;
        if (leftNull && rightNull)
            return true;
        if (leftNull || rightNull)
            return false;

        if (left is byte[] leftBytes && right is byte[] rightBytes)
            return leftBytes.SequenceEqual(rightBytes);

        return Equals(left, right);
    }

    private static string InvariantString(object? value)
    {
        if (value is null || value == DBNull.Value)
            return "<null>";

        return value switch
        {
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            TimeSpan ts => ts.ToString("c", CultureInfo.InvariantCulture),
            byte[] bytes => Convert.ToBase64String(bytes),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
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
            _filteredRows = [];
            _pagedSourceRows = [];
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
            IsResultDetailVisible = false;
            GeneratedWhereClause = string.Empty;
            if (Session is not null)
                Session.ViewState.SelectedCell = null;
            _selectedRowItem = null;
            RaisePropertyChanged(nameof(SelectedRowItem));
            RaisePropertyChanged(nameof(SelectedRowSummary));
            RaisePropertyChanged(nameof(HasSelectedCell));
            RaisePropertyChanged(nameof(SelectedCellSummary));
            RaisePropertyChanged(nameof(CanGenerateWhereClause));
            RaisePropertyChanged(nameof(CanNavigateSelectedForeignKey));
            RaisePropertyChanged(nameof(HasPendingEdits));
            RaisePropertyChanged(nameof(PendingEditsCount));
            RaisePropertyChanged(nameof(CanCancelPendingEdits));
            RaisePropertyChanged(nameof(CanPreparePendingChangesPreview));
            RaisePropertyChanged(nameof(CanGeneratePendingSql));
            RaisePropertyChanged(nameof(CanRefreshSession));
            RaisePropertyChanged(nameof(CanRequestExecutePendingChanges));
            RaisePropertyChanged(nameof(RowsView));
            RaisePropertyChanged(nameof(HasVisibleRows));
            RaisePropertyChanged(nameof(RowCountText));
            NotifyCommands();
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
        _filteredRows = filteredRows;
        _totalFilteredRows = filteredRows.Count;
        int computedTotalPages = Math.Max(1, (int)Math.Ceiling(_totalFilteredRows / (double)DefaultPageSize));
        TotalPages = computedTotalPages;
        CurrentPage = Math.Min(CurrentPage, TotalPages);

        int skip = (CurrentPage - 1) * DefaultPageSize;
        List<DataRow> pageRows = filteredRows.Skip(skip).Take(DefaultPageSize).ToList();
        _pagedSourceRows = pageRows;

        DataTable projected = BuildProjectedTable(table, visibleOrderedColumns);
        foreach (DataRow row in pageRows)
            projected.Rows.Add(BuildProjectedRow(projected, row, visibleOrderedColumns));

        _pagedRowsTable = projected;
        RaisePropertyChanged(nameof(RowsView));
        RaisePropertyChanged(nameof(HasVisibleRows));
        RaisePropertyChanged(nameof(RowCountText));
        RestoreSelectedRowFromSessionState();
        RestoreSelectedCellFromSessionState();
        RaisePropertyChanged(nameof(CanGenerateWhereClause));
        RaisePropertyChanged(nameof(CanNavigateSelectedForeignKey));
        RaisePropertyChanged(nameof(HasPendingEdits));
        RaisePropertyChanged(nameof(PendingEditsCount));
        RaisePropertyChanged(nameof(CanCancelPendingEdits));
        RaisePropertyChanged(nameof(CanPreparePendingChangesPreview));
        RaisePropertyChanged(nameof(CanGeneratePendingSql));
        RaisePropertyChanged(nameof(CanRefreshSession));
        RaisePropertyChanged(nameof(CanRequestExecutePendingChanges));
        NotifyCommands();
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
        (BuildColumnProfilesCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (MoveColumnUpCommand as RelayCommand<SqlResultColumnVisibilityItemViewModel>)?.NotifyCanExecuteChanged();
        (MoveColumnDownCommand as RelayCommand<SqlResultColumnVisibilityItemViewModel>)?.NotifyCanExecuteChanged();
        (ClearSelectedRowCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ShowSelectedRowDetailsCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (HideSelectedRowDetailsCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (CopySelectedCellCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (CopySelectedRowAsJsonCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (CopySelectedRowAsCsvCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (CopySelectedRowAsMarkdownCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (CopyVisibleRowsAsJsonCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (CopyVisibleRowsAsCsvCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (CopyVisibleRowsAsMarkdownCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ExportVisibleRowsAsJsonCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ExportVisibleRowsAsCsvCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (CopySelectedColumnAsSqlInCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (GenerateWhereClauseCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (FilterBySelectedCellValueCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (HideSelectedCellColumnCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (SortSelectedColumnAscendingCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (SortSelectedColumnDescendingCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (CancelPendingEditsCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (PreparePendingChangesPreviewCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ClearPendingChangesPreviewCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (GeneratePendingSqlCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (CopyGeneratedPendingSqlCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (SendGeneratedPendingSqlToEditorCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (RefreshSessionCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (RequestExecutePendingChangesCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ConfirmExecutePendingChangesCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ConfirmExecutePendingChangesRollbackCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (CancelExecutePendingChangesCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (SaveSessionAnnotationCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ClearSessionAnnotationCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (SaveCurrentSqlAsSnippetCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ToggleCurrentSqlFavoriteCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (OpenSelectedSnippetInEditorCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (OpenSnippetInEditorCommand as RelayCommand<SqlSavedQuerySnippet>)?.NotifyCanExecuteChanged();
        (DeleteSelectedSnippetCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (DeleteSnippetCommand as RelayCommand<SqlSavedQuerySnippet>)?.NotifyCanExecuteChanged();
        (SendSelectedSqlTemplateToEditorCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (SendTableQuickActionToEditorCommand as RelayCommand<SqlTableQuickActionOption>)?.NotifyCanExecuteChanged();
        (NavigateSelectedForeignKeyCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    public sealed record SqlQuickTemplateOption(
        string Key,
        string Name,
        string Description);

    public sealed record SqlTableQuickActionOption(
        string Key,
        string Name,
        string Description);

    public sealed record SqlResultExportRequest(
        string SuggestedFileName,
        string DefaultExtension,
        string FileTypeTitle,
        IReadOnlyList<string> Patterns,
        IReadOnlyList<string> MimeTypes,
        string Content);

    public sealed class SqlResultColumnProfileItemViewModel
    {
        private SqlResultColumnProfileItemViewModel(
            string columnName,
            string kindText,
            string summaryText,
            string topValuesText)
        {
            ColumnName = columnName;
            KindText = kindText;
            SummaryText = summaryText;
            TopValuesText = topValuesText;
        }

        public string ColumnName { get; }
        public string KindText { get; }
        public string SummaryText { get; }
        public string TopValuesText { get; }

        public static SqlResultColumnProfileItemViewModel FromProfile(SqlResultColumnProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            string summary = profile.Kind switch
            {
                SqlResultColumnProfileKind.Numeric =>
                    $"nulls={profile.NullCount} | min={FormatDouble(profile.NumericMin)} | max={FormatDouble(profile.NumericMax)} | avg={FormatDouble(profile.NumericAverage)}",
                SqlResultColumnProfileKind.Temporal =>
                    $"nulls={profile.NullCount} | min={FormatDate(profile.TemporalMin)} | max={FormatDate(profile.TemporalMax)} | future={profile.SuspectFutureValueCount}",
                SqlResultColumnProfileKind.Text =>
                    $"nulls={profile.NullCount} | empty={profile.EmptyCount} | distinct={profile.DistinctCount}",
                _ =>
                    $"nulls={profile.NullCount} | distinct={profile.DistinctCount}",
            };

            return new SqlResultColumnProfileItemViewModel(
                profile.ColumnName,
                profile.Kind.ToString(),
                summary,
                profile.TopValuesSummary);
        }

        private static string FormatDouble(double? value)
        {
            return value.HasValue
                ? value.Value.ToString("0.###", CultureInfo.InvariantCulture)
                : "-";
        }

        private static string FormatDate(DateTimeOffset? value)
        {
            return value.HasValue
                ? value.Value.ToString("O", CultureInfo.InvariantCulture)
                : "-";
        }
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
