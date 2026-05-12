using System.Data;
using System.Windows.Input;
using AkkornStudio.UI.Services.SqlEditor.Results;

namespace AkkornStudio.UI.ViewModels;

public sealed class SqlResultPageViewModel : ViewModelBase
{
    private SqlResultSession? _session;
    private Guid? _sourceSqlEditorDocumentId;
    private Action<Guid?>? _navigateBackToEditor;

    public SqlResultPageViewModel()
    {
        BackToEditorCommand = new RelayCommand(
            () => _navigateBackToEditor?.Invoke(_sourceSqlEditorDocumentId),
            () => _navigateBackToEditor is not null);
    }

    public ICommand BackToEditorCommand { get; }

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
            RaisePropertyChanged(nameof(RowsView));
            RaisePropertyChanged(nameof(RowCountText));
            RaisePropertyChanged(nameof(ColumnCountText));
        }
    }

    public bool HasSession => Session is not null;

    public string SqlText => Session?.SqlText ?? string.Empty;

    public string ConnectionId => Session?.ConnectionId ?? string.Empty;

    public string DatabaseName => Session?.DatabaseName ?? "-";

    public string ExecutedAtText => Session?.ExecutedAt.LocalDateTime.ToString("dd/MM/yyyy HH:mm:ss") ?? "-";

    public string DurationText => Session is null ? "-" : $"{Session.ExecutionTime.TotalMilliseconds:0} ms";

    public string StatusText => Session?.Status == SqlResultSessionStatus.Success ? "Sucesso" : "Erro";

    public DataView? RowsView => Session?.ResultSet.Data?.DefaultView;

    public string RowCountText => Session?.ResultSet.Data?.Rows.Count.ToString() ?? "0";

    public string ColumnCountText => Session?.ResultSet.Data?.Columns.Count.ToString() ?? "0";

    public void ConfigureBackNavigation(Action<Guid?> navigateBackToEditor)
    {
        _navigateBackToEditor = navigateBackToEditor ?? throw new ArgumentNullException(nameof(navigateBackToEditor));
        (BackToEditorCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    public void SetSession(SqlResultSession session, Guid? sourceSqlEditorDocumentId)
    {
        ArgumentNullException.ThrowIfNull(session);
        _sourceSqlEditorDocumentId = sourceSqlEditorDocumentId;
        Session = session;
    }
}
