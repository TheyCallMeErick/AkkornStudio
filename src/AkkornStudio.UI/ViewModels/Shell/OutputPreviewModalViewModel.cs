using System.ComponentModel;
using AkkornStudio.Core;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.Canvas;

namespace AkkornStudio.UI.ViewModels;

public sealed class OutputPreviewModalViewModel : ViewModelBase
{
    public enum EOutputPreviewMode
    {
        Query,
        Ddl,
        SqlBenchmark,
        SqlExplain,
        Unavailable,
    }

    public enum EOutputPreviewTab
    {
        Primary,
        CanvasDiagnostics,
        StructureDiagnostics,
    }

    private bool _isVisible;
    private EOutputPreviewMode _mode = EOutputPreviewMode.Unavailable;
    private EOutputPreviewTab _activeTab = EOutputPreviewTab.Primary;
    private AppDiagnosticsViewModel? _diagnostics;
    private string _querySqlText = string.Empty;
    private string _queryProviderLabel = string.Empty;
    private string _ddlSqlText = string.Empty;
    private string _ddlProviderLabel = string.Empty;
    private string _title = "Preview";
    private string _primaryTabLabel = "Preview";
    private string _unavailableMessage = "Preview is unavailable for this document.";
    private BenchmarkViewModel? _benchmarkTool;
    private ExplainPlanViewModel? _explainPlanTool;
    private LiveDdlBarViewModel? _ddlTool;
    private PropertyChangedEventHandler? _benchmarkToolPropertyChanged;
    private PropertyChangedEventHandler? _explainPlanToolPropertyChanged;

    public OutputPreviewModalViewModel()
    {
        CloseCommand = new RelayCommand(Close);
        ShowPrimaryTabCommand = new RelayCommand(() => ActiveTab = EOutputPreviewTab.Primary);
        ShowCanvasDiagnosticsTabCommand = new RelayCommand(() => ActiveTab = EOutputPreviewTab.CanvasDiagnostics);
        ShowStructureDiagnosticsTabCommand = new RelayCommand(() => ActiveTab = EOutputPreviewTab.StructureDiagnostics);
        ShowDiagnosticsTabCommand = new RelayCommand(() => ActiveTab = EOutputPreviewTab.CanvasDiagnostics);
    }

    public RelayCommand CloseCommand { get; }
    public RelayCommand ShowPrimaryTabCommand { get; }
    public RelayCommand ShowCanvasDiagnosticsTabCommand { get; }
    public RelayCommand ShowStructureDiagnosticsTabCommand { get; }
    public RelayCommand ShowDiagnosticsTabCommand { get; }

    public bool IsVisible
    {
        get => _isVisible;
        set => Set(ref _isVisible, value);
    }

    public EOutputPreviewMode Mode
    {
        get => _mode;
        private set
        {
            if (!Set(ref _mode, value))
                return;

            RaisePropertyChanged(nameof(IsDdlMode));
            RaisePropertyChanged(nameof(IsQueryMode));
            RaisePropertyChanged(nameof(IsSqlBenchmarkMode));
            RaisePropertyChanged(nameof(IsSqlExplainMode));
            RaisePropertyChanged(nameof(IsUnavailableMode));
            RaisePropertyChanged(nameof(Title));
            RaisePropertyChanged(nameof(PrimaryTabLabel));
            RaisePropertyChanged(nameof(CanvasDiagnosticsTabLabel));
            RaisePropertyChanged(nameof(StructureDiagnosticsTabLabel));
            RaisePropertyChanged(nameof(HasCanvasDiagnostics));
            RaisePropertyChanged(nameof(HasStructureDiagnostics));
            RaisePropertyChanged(nameof(ShowPrimaryContent));
            RaisePropertyChanged(nameof(ShowCanvasDiagnosticsContent));
            RaisePropertyChanged(nameof(ShowStructureDiagnosticsContent));
            RaisePropertyChanged(nameof(ShowQueryPrimaryContent));
            RaisePropertyChanged(nameof(ShowDdlPrimaryContent));
            RaisePropertyChanged(nameof(ShowSqlBenchmarkPrimaryContent));
            RaisePropertyChanged(nameof(ShowSqlExplainPrimaryContent));
            RaisePropertyChanged(nameof(ShowUnavailablePrimaryContent));
            RaisePropertyChanged(nameof(ShowShellCard));
        }
    }

    public bool IsQueryMode => Mode == EOutputPreviewMode.Query;
    public bool IsDdlMode => Mode == EOutputPreviewMode.Ddl;
    public bool IsSqlBenchmarkMode => Mode == EOutputPreviewMode.SqlBenchmark;
    public bool IsSqlExplainMode => Mode == EOutputPreviewMode.SqlExplain;
    public bool IsUnavailableMode => Mode == EOutputPreviewMode.Unavailable;

    public EOutputPreviewTab ActiveTab
    {
        get => _activeTab;
        private set
        {
            if (!Set(ref _activeTab, value))
                return;

            RaisePropertyChanged(nameof(ShowPrimaryContent));
            RaisePropertyChanged(nameof(ShowCanvasDiagnosticsContent));
            RaisePropertyChanged(nameof(ShowStructureDiagnosticsContent));
            RaisePropertyChanged(nameof(ShowQueryPrimaryContent));
            RaisePropertyChanged(nameof(ShowDdlPrimaryContent));
            RaisePropertyChanged(nameof(ShowSqlBenchmarkPrimaryContent));
            RaisePropertyChanged(nameof(ShowSqlExplainPrimaryContent));
            RaisePropertyChanged(nameof(ShowUnavailablePrimaryContent));
            RaisePropertyChanged(nameof(ShowShellCard));
        }
    }

    public string Title
    {
        get => _title;
        private set => Set(ref _title, value);
    }

    public string PrimaryTabLabel
    {
        get => _primaryTabLabel;
        private set => Set(ref _primaryTabLabel, value);
    }

    public string CanvasDiagnosticsTabLabel => "Diagnosticos do canvas";

    public string StructureDiagnosticsTabLabel => "Diagnosticos de estrutura";

    public bool ShowPrimaryContent => ActiveTab == EOutputPreviewTab.Primary;
    public bool ShowCanvasDiagnosticsContent => HasCanvasDiagnostics && ActiveTab == EOutputPreviewTab.CanvasDiagnostics;
    public bool ShowStructureDiagnosticsContent => HasStructureDiagnostics && ActiveTab == EOutputPreviewTab.StructureDiagnostics;
    public bool ShowQueryPrimaryContent => IsQueryMode && ShowPrimaryContent;
    public bool ShowDdlPrimaryContent => IsDdlMode && ShowPrimaryContent;
    public bool ShowSqlBenchmarkPrimaryContent => IsSqlBenchmarkMode && ShowPrimaryContent;
    public bool ShowSqlExplainPrimaryContent => IsSqlExplainMode && ShowPrimaryContent;
    public bool ShowUnavailablePrimaryContent => IsUnavailableMode && ShowPrimaryContent;
    public bool ShowShellCard => !(ShowSqlBenchmarkPrimaryContent || ShowSqlExplainPrimaryContent);

    public AppDiagnosticsViewModel? Diagnostics
    {
        get => _diagnostics;
        private set
        {
            if (!Set(ref _diagnostics, value))
                return;

            RaisePropertyChanged(nameof(HasCanvasDiagnostics));
            RaisePropertyChanged(nameof(ShowCanvasDiagnosticsContent));
        }
    }

    public bool HasCanvasDiagnostics => Diagnostics is not null;

    public bool HasStructureDiagnostics => IsDdlMode && DdlTool is not null;

    public string QuerySqlText
    {
        get => _querySqlText;
        private set
        {
            if (!Set(ref _querySqlText, value))
                return;

            RaisePropertyChanged(nameof(HasQuerySql));
        }
    }

    public string QueryProviderLabel
    {
        get => _queryProviderLabel;
        private set => Set(ref _queryProviderLabel, value);
    }

    public string DdlSqlText
    {
        get => _ddlSqlText;
        private set
        {
            if (!Set(ref _ddlSqlText, value))
                return;

            RaisePropertyChanged(nameof(HasDdlSql));
        }
    }

    public string DdlProviderLabel
    {
        get => _ddlProviderLabel;
        private set => Set(ref _ddlProviderLabel, value);
    }

    public BenchmarkViewModel? BenchmarkTool
    {
        get => _benchmarkTool;
        private set => Set(ref _benchmarkTool, value);
    }

    public ExplainPlanViewModel? ExplainPlanTool
    {
        get => _explainPlanTool;
        private set => Set(ref _explainPlanTool, value);
    }

    public LiveDdlBarViewModel? DdlTool
    {
        get => _ddlTool;
        private set
        {
            if (!Set(ref _ddlTool, value))
                return;

            RaisePropertyChanged(nameof(HasStructureDiagnostics));
            RaisePropertyChanged(nameof(ShowStructureDiagnosticsContent));
        }
    }

    public string UnavailableMessage
    {
        get => _unavailableMessage;
        private set => Set(ref _unavailableMessage, value);
    }

    public bool HasDdlSql => !string.IsNullOrWhiteSpace(DdlSqlText);
    public bool HasQuerySql => !string.IsNullOrWhiteSpace(QuerySqlText);

    public void OpenForQuery(CanvasViewModel canvas, LiveSqlBarViewModel liveSql, string providerLabel)
    {
        UnwireToolHandlers();
        Mode = EOutputPreviewMode.Query;
        Title = "SQL Preview";
        PrimaryTabLabel = "SQL";
        Diagnostics = canvas.Diagnostics;
        QuerySqlText = BuildQueryPreviewText(liveSql);
        QueryProviderLabel = providerLabel;
        DdlSqlText = string.Empty;
        DdlProviderLabel = string.Empty;
        BenchmarkTool = null;
        ExplainPlanTool = null;
        DdlTool = null;
        ActiveTab = EOutputPreviewTab.Primary;
        IsVisible = true;
        Diagnostics?.RunChecksCommand.Execute(null);
    }

    public void OpenForDdl(CanvasViewModel canvas, LiveDdlBarViewModel liveDdl, string providerLabel)
    {
        UnwireToolHandlers();
        Mode = EOutputPreviewMode.Ddl;
        Title = "SQL DDL Preview";
        PrimaryTabLabel = "SQL DDL";
        Diagnostics = canvas.Diagnostics;
        QuerySqlText = string.Empty;
        QueryProviderLabel = string.Empty;
        DdlSqlText = BuildDdlPreviewText(liveDdl);
        DdlProviderLabel = providerLabel;
        BenchmarkTool = null;
        ExplainPlanTool = null;
        DdlTool = liveDdl;
        ActiveTab = EOutputPreviewTab.Primary;
        IsVisible = true;
        Diagnostics?.RunChecksCommand.Execute(null);
    }

    public void OpenUnavailable(string title, string primaryTabLabel, string unavailableMessage)
    {
        UnwireToolHandlers();
        Mode = EOutputPreviewMode.Unavailable;
        Title = string.IsNullOrWhiteSpace(title) ? "Preview" : title;
        PrimaryTabLabel = string.IsNullOrWhiteSpace(primaryTabLabel) ? "Preview" : primaryTabLabel;
        UnavailableMessage = string.IsNullOrWhiteSpace(unavailableMessage)
            ? "Preview is unavailable for this document."
            : unavailableMessage;
        Diagnostics = null;
        QuerySqlText = string.Empty;
        QueryProviderLabel = string.Empty;
        DdlSqlText = string.Empty;
        DdlProviderLabel = string.Empty;
        BenchmarkTool = null;
        ExplainPlanTool = null;
        DdlTool = null;
        ActiveTab = EOutputPreviewTab.Primary;
        IsVisible = true;
    }

    public void OpenForSqlBenchmark(CanvasViewModel canvas, string sql, ConnectionConfig? connectionConfig)
    {
        UnwireToolHandlers();
        BenchmarkViewModel benchmark = canvas.Benchmark;
        benchmark.OpenForSql(sql, connectionConfig);
        WireBenchmarkHandler(benchmark);

        Mode = EOutputPreviewMode.SqlBenchmark;
        Title = "SQL Benchmark";
        PrimaryTabLabel = "Benchmark";
        Diagnostics = null;
        QuerySqlText = string.Empty;
        QueryProviderLabel = string.Empty;
        DdlSqlText = string.Empty;
        DdlProviderLabel = string.Empty;
        BenchmarkTool = benchmark;
        ExplainPlanTool = null;
        DdlTool = null;
        ActiveTab = EOutputPreviewTab.Primary;
        IsVisible = true;
    }

    public void OpenForSqlExplain(
        CanvasViewModel canvas,
        string sql,
        DatabaseProvider provider,
        ConnectionConfig? connectionConfig)
    {
        UnwireToolHandlers();
        ExplainPlanViewModel explain = canvas.ExplainPlan;
        explain.OpenForSql(sql, provider, connectionConfig);
        WireExplainHandler(explain);

        Mode = EOutputPreviewMode.SqlExplain;
        Title = "SQL Explain";
        PrimaryTabLabel = "Explain";
        Diagnostics = null;
        QuerySqlText = string.Empty;
        QueryProviderLabel = string.Empty;
        DdlSqlText = string.Empty;
        DdlProviderLabel = string.Empty;
        BenchmarkTool = null;
        ExplainPlanTool = explain;
        DdlTool = null;
        ActiveTab = EOutputPreviewTab.Primary;
        IsVisible = true;
    }

    public void Close()
    {
        if (BenchmarkTool is not null)
            BenchmarkTool.Close();

        if (ExplainPlanTool is not null)
            ExplainPlanTool.Close();

        UnwireToolHandlers();
        BenchmarkTool = null;
        ExplainPlanTool = null;
        DdlTool = null;
        IsVisible = false;
    }

    private static string BuildDdlPreviewText(LiveDdlBarViewModel liveDdl)
    {
        if (!string.IsNullOrWhiteSpace(liveDdl.RawSql))
            return liveDdl.RawSql;

        return string.Empty;
    }

    private static string BuildQueryPreviewText(LiveSqlBarViewModel liveSql)
    {
        if (!string.IsNullOrWhiteSpace(liveSql.RawSql))
            return liveSql.RawSql;

        return string.Empty;
    }

    private void WireBenchmarkHandler(BenchmarkViewModel benchmark)
    {
        _benchmarkToolPropertyChanged = (_, e) =>
        {
            if (e.PropertyName == nameof(BenchmarkViewModel.IsVisible) && !benchmark.IsVisible)
                IsVisible = false;
        };
        benchmark.PropertyChanged += _benchmarkToolPropertyChanged;
    }

    private void WireExplainHandler(ExplainPlanViewModel explain)
    {
        _explainPlanToolPropertyChanged = (_, e) =>
        {
            if (e.PropertyName == nameof(ExplainPlanViewModel.IsVisible) && !explain.IsVisible)
                IsVisible = false;
        };
        explain.PropertyChanged += _explainPlanToolPropertyChanged;
    }

    private void UnwireToolHandlers()
    {
        if (BenchmarkTool is not null && _benchmarkToolPropertyChanged is not null)
            BenchmarkTool.PropertyChanged -= _benchmarkToolPropertyChanged;

        if (ExplainPlanTool is not null && _explainPlanToolPropertyChanged is not null)
            ExplainPlanTool.PropertyChanged -= _explainPlanToolPropertyChanged;

        _benchmarkToolPropertyChanged = null;
        _explainPlanToolPropertyChanged = null;
    }
}
