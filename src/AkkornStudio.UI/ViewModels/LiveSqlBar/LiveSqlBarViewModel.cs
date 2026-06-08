using System.Collections.ObjectModel;
using System.ComponentModel;
using AkkornStudio.Core;
using AkkornStudio.Nodes;
using AkkornStudio.QueryEngine;
using AkkornStudio.Registry;
using AkkornStudio.UI.Services.LiveSqlBar;
using AkkornStudio.UI.Services;
using AkkornStudio.UI.Services.QueryPreview.Models;
using AkkornStudio.UI.Services.QueryPreview;

namespace AkkornStudio.UI.ViewModels;

// â”€â”€â”€ Live SQL bar view model â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

/// <summary>
/// Maintains a real-time SQL preview that updates every time the canvas graph
/// changes (node added/removed, pin connected/disconnected, parameter edited).
///
/// The VM walks <see cref="CanvasViewModel"/> collections, compiles the graph
/// via <see cref="QueryGeneratorService"/>, and exposes:
///   â€¢ <see cref="RawSql"/>     â€” plain string for copy/paste
///   â€¢ <see cref="Tokens"/>     â€” syntax-highlighted token list for the UI
///   â€¢ <see cref="IsValid"/>    â€” false when the graph has errors
///   â€¢ <see cref="ErrorHints"/> â€” per-error messages for the validation panel
/// </summary>
public sealed class LiveSqlBarViewModel : ViewModelBase
{
    private readonly CanvasViewModel _canvas;
    private readonly SqlSyntaxHighlighter _highlighter;
    private QueryGraphBuilder? _graphBuilder;
    private readonly object _debounceLock = new();  // Synchronization for _debounce
    private readonly object _recompileLock = new(); // Synchronization for Recompile state transitions

    private string _rawSql = string.Empty;
    private string _displaySql = string.Empty;
    private string? _executionSqlTemplate;
    private bool _isValid = true;
    private bool _isCompiling;
    private string? _compileError;
    private bool _isMutatingCommand;
    private DatabaseProvider _provider = DatabaseProvider.Postgres;

    // â”€â”€ Observable collections â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public ObservableCollection<SqlToken> Tokens { get; } = [];
    public ObservableCollection<string> ErrorHints { get; } = [];
    public ObservableCollection<PreviewDiagnostic> Diagnostics { get; } = [];
    public ObservableCollection<LiveSqlDiagnosticItem> DiagnosticItems { get; } = [];
    public ObservableCollection<GuardIssue> GuardIssues { get; } = [];

    // â”€â”€ Properties â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public string RawSql
    {
        get => _rawSql;
        private set
        {
            if (!Set(ref _rawSql, value))
                return;

            _canvas.UpdateQueryText(value);
            RaisePropertyChanged(nameof(HasSql));
            RaisePropertyChanged(nameof(CanExecutePreviewActions));
        }
    }

    public string DisplaySql
    {
        get => _displaySql;
        private set => Set(ref _displaySql, value);
    }

    public string? ExecutionSqlTemplate
    {
        get => _executionSqlTemplate;
        private set
        {
            if (!Set(ref _executionSqlTemplate, value))
                return;

            RaisePropertyChanged(nameof(HasExecutionSqlTemplate));
            RaisePropertyChanged(nameof(CanExecutePreviewActions));
        }
    }

    public bool IsValid
    {
        get => _isValid;
        private set
        {
            if (!Set(ref _isValid, value))
                return;

            RaisePropertyChanged(nameof(CanExecutePreviewActions));
        }
    }

    public bool IsCompiling
    {
        get => _isCompiling;
        private set => Set(ref _isCompiling, value);
    }

    public string? CompileError
    {
        get => _compileError;
        private set => Set(ref _compileError, value);
    }

    public DatabaseProvider Provider
    {
        get => _provider;
        set
        {
            Set(ref _provider, value);
            Recompile();
        }
    }

    public string ProviderLabel =>
        _provider switch
        {
            DatabaseProvider.SqlServer => "SQL Server",
            DatabaseProvider.MySql => "MySQL",
            DatabaseProvider.SQLite => "SQLite",
            _ => "PostgreSQL",
        };

    public bool HasSql => !string.IsNullOrWhiteSpace(RawSql);
    public bool HasExecutionSqlTemplate => !string.IsNullOrWhiteSpace(ExecutionSqlTemplate);
    public bool CanExecutePreviewActions =>
        HasSql && HasExecutionSqlTemplate && IsValid && !IsMutatingCommand;
    public IReadOnlyList<QueryParameter> ExecutionParameters { get; private set; } = [];
    public IReadOnlyDictionary<string, QueryExecutionParameterContext> ExecutionParameterContexts { get; private set; }
        = new Dictionary<string, QueryExecutionParameterContext>(StringComparer.OrdinalIgnoreCase);

    public bool IsMutatingCommand
    {
        get => _isMutatingCommand;
        private set
        {
            if (!Set(ref _isMutatingCommand, value))
                return;

            RaisePropertyChanged(nameof(CanExecutePreviewActions));
        }
    }

    public string? BlockedReason =>
        IsMutatingCommand
            ? "Blocked in Safe Preview Mode â€” SQL contains a data-mutating command (INSERT/UPDATE/DELETE/DROP/ALTER/TRUNCATE)"
            : null;

    public bool HasGuardWarning => GuardIssues.Count > 0;
    public bool HasDiagnostics => DiagnosticItems.Count > 0;

    // â”€â”€ Constructor â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    // Tracks per-node PropertyChanged handlers so they can be unsubscribed on node removal.
    private readonly Dictionary<NodeViewModel, PropertyChangedEventHandler> _nodeHandlers = new();

    public LiveSqlBarViewModel(CanvasViewModel canvas)
    {
        _canvas = canvas;
        _highlighter = new SqlSyntaxHighlighter();

        canvas.Connections.CollectionChanged += (_, _) => ScheduleRecompile();

        canvas.Nodes.CollectionChanged += (_, e) =>
        {
            ScheduleRecompile();

            // Subscribe new nodes â€” skip pure visual/UI properties that don't affect SQL
            if (e.NewItems is not null)
                foreach (NodeViewModel vm in e.NewItems)
                    SubscribeNode(vm);

            // Unsubscribe removed nodes to prevent memory leaks
            if (e.OldItems is not null)
                foreach (NodeViewModel vm in e.OldItems)
                    UnsubscribeNode(vm);
        };

        Recompile();
    }

    private void SubscribeNode(NodeViewModel vm)
    {
        PropertyChangedEventHandler handler = (_, e) =>
        {
            if (e.PropertyName is nameof(NodeViewModel.Position)
                or nameof(NodeViewModel.IsSelected)
                or nameof(NodeViewModel.IsOrphan)
                or nameof(NodeViewModel.IsHovered)
                or nameof(NodeViewModel.Width))
                return;
            ScheduleRecompile();
        };
        vm.PropertyChanged += handler;
        _nodeHandlers[vm] = handler;
    }

    private void UnsubscribeNode(NodeViewModel vm)
    {
        if (_nodeHandlers.TryGetValue(vm, out PropertyChangedEventHandler? handler))
        {
            vm.PropertyChanged -= handler;
            _nodeHandlers.Remove(vm);
        }
    }

    // â”€â”€ Debounced recompile â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private CancellationTokenSource? _debounce;
    private long _recompileVersion;

    private void ScheduleRecompile()
    {
        CancellationToken token;
        long version;

        lock (_debounceLock)
        {
            _debounce?.Cancel();
            _debounce?.Dispose();
            _debounce = new CancellationTokenSource();
            token = _debounce.Token;
            version = ++_recompileVersion;
        }

        // 120ms debounce â€” avoids recompiling on every intermediate drag step.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(120, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            lock (_debounceLock)
            {
                if (version != _recompileVersion)
                    return;
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(Recompile);
        });
    }

    // â”€â”€ Compilation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public void Recompile()
    {
        lock (_recompileLock)
        {
            string previousRawSql = RawSql;
            string previousDisplaySql = DisplaySql;
            string? previousExecutionSqlTemplate = ExecutionSqlTemplate;
            bool previousIsMutatingCommand = IsMutatingCommand;
            IReadOnlyList<QueryParameter> previousExecutionParameters = ExecutionParameters.ToArray();
            IReadOnlyDictionary<string, QueryExecutionParameterContext> previousExecutionParameterContexts =
                new Dictionary<string, QueryExecutionParameterContext>(ExecutionParameterContexts, StringComparer.OrdinalIgnoreCase);
            List<SqlToken> previousTokens = Tokens.ToList();

            ErrorHints.Clear();
            Diagnostics.Clear();
            DiagnosticItems.Clear();
            GuardIssues.Clear();
            IsCompiling = true;

            try
            {
                // Apply in-flight edits from the property panel before reading node parameters.
                _canvas.PropertyPanel.CommitDirty();

                _graphBuilder = new QueryGraphBuilder(_canvas, _provider);

                // â”€â”€ Portability pre-check â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                List<GuardIssue> portabilityIssues = QueryValidationService.CheckPortability(
                    _canvas.Nodes.Select(n => n.Type),
                    _provider
                );
                foreach (GuardIssue issue in portabilityIssues)
                    GuardIssues.Add(issue);

                QuerySqlBuildResult build = _graphBuilder.BuildSqlWithDiagnostics();
                List<string> errors = build.Diagnostics.Select(d => d.Message).ToList();

                RawSql = build.PreviewSql;
                DisplaySql = FormatSqlText(build.PreviewSql);
                ExecutionSqlTemplate = build.ExecutionSqlTemplate;
                ExecutionParameters = build.Bindings
                    .Select(binding => new QueryParameter(binding.Key, binding.Value))
                    .ToArray();
                ExecutionParameterContexts = build.ParameterContexts;
                IsValid = errors.Count == 0;
                CompileError = errors.Count > 0 ? errors[0] : null;

                // Detect mutating commands â€” block preview execution
                IsMutatingCommand = QueryValidationService.IsMutating(build.PreviewSql);
                RaisePropertyChanged(nameof(BlockedReason));

                // Run guardrails (only when not a mutating command)
                if (!IsMutatingCommand)
                {
                    foreach (GuardIssue issue in QueryGuardrails.Check(build.PreviewSql))
                        GuardIssues.Add(issue);
                }
                RaisePropertyChanged(nameof(HasGuardWarning));

                foreach (string err in errors)
                    ErrorHints.Add(err);

                foreach (PreviewDiagnostic diagnostic in build.Diagnostics)
                {
                    Diagnostics.Add(diagnostic);
                    (string? actionLabel, string? actionHint) = ResolveQuickAction(diagnostic);
                    Action<PreviewDiagnostic>? quickAction = actionHint is null
                        ? null
                        : _ => _canvas.Toasts.ShowWarning(actionHint);

                    DiagnosticItems.Add(
                        new LiveSqlDiagnosticItem(diagnostic, FocusDiagnostic, quickAction, actionLabel)
                    );
                }

                SqlSyntaxHighlighter.Tokenize(DisplaySql, Tokens);
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(previousRawSql))
                {
                    // Preserve the last valid SQL preview/context when recompilation fails.
                    RawSql = previousRawSql;
                    DisplaySql = previousDisplaySql;
                    ExecutionSqlTemplate = previousExecutionSqlTemplate;
                    ExecutionParameters = previousExecutionParameters;
                    ExecutionParameterContexts = previousExecutionParameterContexts;
                    IsMutatingCommand = previousIsMutatingCommand;

                    Tokens.Clear();
                    foreach (SqlToken token in previousTokens)
                        Tokens.Add(token);
                }
                else
                {
                    RawSql = string.Empty;
                    DisplaySql = string.Empty;
                    ExecutionSqlTemplate = null;
                    ExecutionParameters = [];
                    ExecutionParameterContexts = new Dictionary<string, QueryExecutionParameterContext>(StringComparer.OrdinalIgnoreCase);
                    IsMutatingCommand = false;
                    Tokens.Clear();
                }

                IsValid = false;
                CompileError = ex.Message;
                ErrorHints.Add($"Compile error: {ex.Message}");
                Diagnostics.Clear();
                DiagnosticItems.Clear();
            }
            finally
            {
                IsCompiling = false;
                RaisePropertyChanged(nameof(HasSql));
                RaisePropertyChanged(nameof(ProviderLabel));
                RaisePropertyChanged(nameof(BlockedReason));
                RaisePropertyChanged(nameof(HasGuardWarning));
                RaisePropertyChanged(nameof(HasDiagnostics));
            }
        }
    }

    private void FocusDiagnostic(PreviewDiagnostic diagnostic)
    {
        if (diagnostic.NodeId is null)
            return;

        _canvas.FocusNodeById(diagnostic.NodeId);
    }

    private static (string? Label, string? Hint) ResolveQuickAction(PreviewDiagnostic diagnostic)
    {
        if (diagnostic.Message.Contains("alias ambiguity", StringComparison.OrdinalIgnoreCase))
        {
            return (
                "Definir aliases distintos",
                "Revise este source e atribua aliases unicos no painel de propriedades para eliminar ambiguidade."
            );
        }

        if (diagnostic.Message.Contains("without ORDER BY", StringComparison.OrdinalIgnoreCase))
        {
            return (
                "Adicionar ORDER BY",
                "Conecte colunas nos pins order_by/order_by_desc do Result Output para paginaÃ§Ã£o determinÃ­stica."
            );
        }

        if (diagnostic.Category == PreviewDiagnosticCategory.Comparison)
        {
            return (
                "Abrir propriedade pattern",
                "No painel de propriedades, ajuste pattern/operandos do nÃ³ de comparaÃ§Ã£o para uma condiÃ§Ã£o vÃ¡lida."
            );
        }

        if (diagnostic.Category == PreviewDiagnosticCategory.Join)
        {
            return (
                "Revisar JOIN",
                "Verifique tipo de JOIN e conecte left/right (e condition quando aplicÃ¡vel) no nÃ³ de junÃ§Ã£o."
            );
        }

        if (diagnostic.Category == PreviewDiagnosticCategory.Predicate)
        {
            return (
                "Abrir condiÃ§Ã£o",
                "Revise os nÃ³s da cadeia WHERE/HAVING/QUALIFY e preencha os inputs obrigatÃ³rios."
            );
        }

        if (diagnostic.Message.Contains("invalid 'path'", StringComparison.OrdinalIgnoreCase))
        {
            return (
                "Abrir propriedade path",
                "Defina um JSONPath vÃ¡lido (ex.: $.field ou $[0]) no parÃ¢metro path do nÃ³ JSON."
            );
        }

        return (null, null);
    }

    // â”€â”€ Panel shortcuts â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public void OpenExplainPlan() => _canvas.ExplainPlan.Open();
    public void OpenBenchmark()   => _canvas.Benchmark.Open();

    // â”€â”€ Format SQL â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public void FormatSql()
    {
        if (string.IsNullOrWhiteSpace(RawSql))
            return;

        string sql = FormatSqlText(RawSql);
        RawSql = sql;
        DisplaySql = sql;
        ExecutionSqlTemplate = sql;
        ExecutionParameters = [];
        ExecutionParameterContexts = new Dictionary<string, QueryExecutionParameterContext>(StringComparer.OrdinalIgnoreCase);
        SqlSyntaxHighlighter.Tokenize(sql, Tokens);
        RaisePropertyChanged(nameof(HasSql));
    }

    private static string FormatSqlText(string sqlText)
    {
        if (string.IsNullOrWhiteSpace(sqlText))
            return string.Empty;

        // Major clause keywords that get their own line
        string[] clauses = new[]
        {
            "SELECT",
            "FROM",
            "LEFT JOIN",
            "RIGHT JOIN",
            "INNER JOIN",
            "JOIN",
            "WHERE",
            "GROUP BY",
            "HAVING",
            "ORDER BY",
            "LIMIT",
            "OFFSET",
            "UNION ALL",
            "UNION",
        };

        string sql = sqlText.Trim();

        // Replace each clause keyword with newline + keyword
        foreach (string? kw in clauses)
        {
            int idx = 0;
            while (true)
            {
                idx = sql.IndexOf(kw, idx, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                    break;
                bool startOk = idx == 0 || char.IsWhiteSpace(sql[idx - 1]);
                bool endOk =
                    idx + kw.Length >= sql.Length || !char.IsLetterOrDigit(sql[idx + kw.Length]);
                if (startOk && endOk)
                {
                    string before = sql[..idx].TrimEnd();
                    string after = sql[(idx + kw.Length)..];
                    sql = before + (before.Length > 0 ? "\n" : "") + kw.ToUpperInvariant() + after;
                    idx = before.Length + kw.Length;
                }
                else
                {
                    idx += kw.Length;
                }
            }
        }

        // Indent items in SELECT clause (comma-separated columns â†’ one per line)
        int selectEnd = sql.IndexOf("\nFROM", StringComparison.OrdinalIgnoreCase);
        if (sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) && selectEnd > 0)
        {
            string selectBody = sql[6..selectEnd].Trim();
            string[] columns = selectBody.Split(',');
            if (columns.Length > 1)
            {
                string formatted = string.Join(",\n    ", columns.Select(c => c.Trim()));
                sql = "SELECT\n    " + formatted + sql[selectEnd..];
            }
        }

        return sql;
    }
}
