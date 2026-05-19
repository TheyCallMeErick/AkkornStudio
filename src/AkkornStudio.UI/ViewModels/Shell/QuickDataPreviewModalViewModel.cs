using System.Collections.ObjectModel;
using System.Data;
using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.UI.Services.Preview;
using AkkornStudio.UI.Services.Workspace.Models;

namespace AkkornStudio.UI.ViewModels;

public sealed class QuickDataPreviewModalViewModel : ViewModelBase
{
    private readonly QuickDataPreviewService _previewService;
    private readonly Action<string, WorkspaceDocumentType?> _openSqlInEditor;
    private CancellationTokenSource? _runCts;

    private bool _isVisible;
    private bool _isLoading;
    private string _title = "Preview rapido";
    private string _subtitle = string.Empty;
    private string _sqlText = string.Empty;
    private string _providerLabel = string.Empty;
    private string _statusText = "Pronto";
    private string? _errorMessage;
    private DataTable? _resultData;
    private WorkspaceDocumentType? _sourceDocumentType;
    private ConnectionConfig? _contextConnection;
    private DatabaseProvider _contextProvider = DatabaseProvider.Postgres;
    private DbMetadata? _contextMetadata;

    public QuickDataPreviewModalViewModel(
        QuickDataPreviewService? previewService = null,
        Action<string, WorkspaceDocumentType?>? openSqlInEditor = null)
    {
        _previewService = previewService ?? new QuickDataPreviewService();
        _openSqlInEditor = openSqlInEditor ?? ((_, _) => { });

        CloseCommand = new RelayCommand(Close);
        OpenInSqlEditorCommand = new RelayCommand(OpenInSqlEditor, () => CanOpenInSqlEditor);
        PreviewRelationshipCommand = new RelayCommand<QuickDataPreviewRelationshipItemViewModel>(item => _ = PreviewRelationshipAsync(item));
    }

    public RelayCommand CloseCommand { get; }
    public RelayCommand OpenInSqlEditorCommand { get; }
    public RelayCommand<QuickDataPreviewRelationshipItemViewModel> PreviewRelationshipCommand { get; }

    public ObservableCollection<QuickDataPreviewRelationshipItemViewModel> Relationships { get; } = [];

    public bool IsVisible
    {
        get => _isVisible;
        private set => Set(ref _isVisible, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (!Set(ref _isLoading, value))
                return;

            RaisePropertyChanged(nameof(ShowEmptyMessage));
        }
    }

    public string Title
    {
        get => _title;
        private set => Set(ref _title, value);
    }

    public string Subtitle
    {
        get => _subtitle;
        private set => Set(ref _subtitle, value);
    }

    public string SqlText
    {
        get => _sqlText;
        private set
        {
            if (!Set(ref _sqlText, value))
                return;

            RaisePropertyChanged(nameof(CanOpenInSqlEditor));
            (OpenInSqlEditorCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    public string ProviderLabel
    {
        get => _providerLabel;
        private set => Set(ref _providerLabel, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (!Set(ref _errorMessage, value))
                return;

            RaisePropertyChanged(nameof(HasError));
            RaisePropertyChanged(nameof(ShowEmptyMessage));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public DataTable? ResultData
    {
        get => _resultData;
        private set
        {
            if (!Set(ref _resultData, value))
                return;

            RaisePropertyChanged(nameof(ResultView));
            RaisePropertyChanged(nameof(HasData));
            RaisePropertyChanged(nameof(ShowEmptyMessage));
        }
    }

    public DataView? ResultView => ResultData?.DefaultView;

    public bool HasData => ResultData is { Rows.Count: > 0 };

    public bool HasRelationships => Relationships.Count > 0;
    public bool ShowNoRelationshipsMessage => !HasRelationships;
    public bool ShowEmptyMessage => !IsLoading && !HasData && !HasError;

    public bool CanOpenInSqlEditor => !string.IsNullOrWhiteSpace(SqlText);

    public async Task OpenSqlPreviewAsync(
        string title,
        string subtitle,
        string sql,
        ConnectionConfig? connection,
        DatabaseProvider provider,
        DbMetadata? metadata,
        string? focusTableFullName,
        WorkspaceDocumentType? sourceDocumentType,
        int maxRows = 120,
        CancellationToken cancellationToken = default)
    {
        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _sourceDocumentType = sourceDocumentType;
        _contextConnection = connection;
        _contextProvider = provider;
        _contextMetadata = metadata;

        Title = string.IsNullOrWhiteSpace(title) ? "Preview rapido" : title.Trim();
        Subtitle = subtitle ?? string.Empty;
        SqlText = sql ?? string.Empty;
        ProviderLabel = provider.ToString();
        ResultData = null;
        ErrorMessage = null;
        Relationships.Clear();
        RaisePropertyChanged(nameof(HasRelationships));

        IsVisible = true;
        IsLoading = true;
        StatusText = "Executando consulta...";
        RaisePropertyChanged(nameof(ShowEmptyMessage));

        QuickDataPreviewResult result;
        try
        {
            result = await _previewService.ExecuteAsync(
                new QuickDataPreviewRequest(
                    Sql: SqlText,
                    Connection: connection,
                    Provider: provider,
                    Metadata: metadata,
                    FocusTableFullName: focusTableFullName,
                    MaxRows: maxRows),
                _runCts.Token);
        }
        catch (OperationCanceledException)
        {
            IsLoading = false;
            StatusText = "Consulta cancelada.";
            return;
        }

        SqlText = result.Sql;
        ResultData = result.Execution.Data;
        ErrorMessage = result.Execution.Success ? null : result.Execution.ErrorMessage;

        foreach (QuickDataPreviewRelationship relationship in result.Relationships)
            Relationships.Add(new QuickDataPreviewRelationshipItemViewModel(relationship));

        RaisePropertyChanged(nameof(HasRelationships));
        RaisePropertyChanged(nameof(ShowNoRelationshipsMessage));
        StatusText = result.Execution.Success
            ? BuildSuccessStatus(result.Execution)
            : "Falha ao executar preview.";
        IsLoading = false;
        RaisePropertyChanged(nameof(ShowEmptyMessage));
    }

    public async Task OpenTablePreviewAsync(
        string tableFullName,
        ConnectionConfig? connection,
        DatabaseProvider provider,
        DbMetadata? metadata,
        WorkspaceDocumentType? sourceDocumentType,
        int maxRows = 120,
        CancellationToken cancellationToken = default)
    {
        string sql = _previewService.BuildTablePreviewSql(provider, tableFullName, maxRows);
        await OpenSqlPreviewAsync(
            title: "Preview de dados",
            subtitle: tableFullName,
            sql,
            connection,
            provider,
            metadata,
            focusTableFullName: tableFullName,
            sourceDocumentType,
            maxRows,
            cancellationToken);
    }

    private async Task PreviewRelationshipAsync(QuickDataPreviewRelationshipItemViewModel? item)
    {
        if (item is null || _contextConnection is null)
            return;

        await OpenTablePreviewAsync(
            item.Relationship.TargetTable,
            _contextConnection,
            _contextProvider,
            _contextMetadata,
            _sourceDocumentType);
    }

    public void Close()
    {
        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = null;
        IsVisible = false;
    }

    private void OpenInSqlEditor()
    {
        if (!CanOpenInSqlEditor)
            return;

        _openSqlInEditor.Invoke(SqlText, _sourceDocumentType);
    }

    private static string BuildSuccessStatus(SqlEditorResultSet result)
    {
        int rows = result.Data?.Rows.Count ?? 0;
        long elapsed = (long)Math.Round(result.ExecutionTime.TotalMilliseconds);
        return $"{rows} linha(s) em {elapsed}ms";
    }

    public sealed class QuickDataPreviewRelationshipItemViewModel
    {
        public QuickDataPreviewRelationshipItemViewModel(QuickDataPreviewRelationship relationship)
        {
            Relationship = relationship;
        }

        public QuickDataPreviewRelationship Relationship { get; }
        public string Label => $"{Relationship.SourceColumn} -> {Relationship.TargetTable}.{Relationship.TargetColumn}";
        public string Cardinality => Relationship.Cardinality;
        public string DirectionLabel => Relationship.DirectionLabel;
    }
}
