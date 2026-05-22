using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using AkkornStudio.Core;
using AkkornStudio.UI.Controls.Query;
using AkkornStudio.UI.Controls;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.ViewModels;
using System.ComponentModel;

namespace AkkornStudio.UI.Services;

/// <summary>
/// Manages data preview pane wiring and query execution.
/// Supports real async cancellation via CancellationTokenSource,
/// live elapsed-time feedback, and clean state transitions.
/// </summary>
public class PreviewService(Window window, CanvasViewModel vm, ILogger<PreviewService>? logger = null) : IDisposable
{
    private readonly Window _window = window;
    private readonly CanvasViewModel _vm = vm;
    private readonly QueryExecutorService _queryExecutor = new();
    private readonly ILogger<PreviewService> _logger = logger ?? NullLogger<PreviewService>.Instance;

    // Active run — cancelled when user clicks Cancel or a new run starts
    private CancellationTokenSource? _runCts;

    // Event handlers stored for proper cleanup (prevent memory leaks)
    private PropertyChangedEventHandler? _canvasPropertyChangedHandler;
    private PropertyChangedEventHandler? _liveSqlPropertyChangedHandler;
    private bool _disposed = false;

    // ── Wiring ────────────────────────────────────────────────────────────────

    public void Wire()
    {
        ThrowIfDisposed();

        _logger.LogDebug("Wire() called - scheduling control lookup with delay");

        // Clean up previous subscriptions if Wire is called again
        UnsubscribeFromPropertyChangedEvents();

        // Schedule the wiring to happen after layout is updated
        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            // Wait for layout to complete
            await Task.Delay(AppConstants.PreviewDebounceMs);

            _logger.LogDebug("Looking for DataPreviewPanel first...");
            // First, find the PreviewPanel (UserControl)
            var previewPanel = _window.FindControl<Control>("PreviewPanel");
            _logger.LogDebug("Found PreviewPanel: {Found}", previewPanel is not null);

            Button? run = null;
            Button? cancel = null;
            Button? cls = null;

            if (previewPanel is not null)
            {
                _logger.LogDebug("Searching for buttons within PreviewPanel...");
                // Search for buttons within the panel
                run = previewPanel.FindControl<Button>("RunButton");
                cancel = previewPanel.FindControl<Button>("CancelButton");
                cls = previewPanel.FindControl<Button>("CloseButton");
            }
            else
            {
                _logger.LogDebug("PreviewPanel not found, trying direct window search...");
                // Fallback: search from window
                run = _window.FindControl<Button>("RunButton");
                cancel = _window.FindControl<Button>("CancelButton");
                cls = _window.FindControl<Button>("CloseButton");
            }

            _logger.LogDebug(
                "Found buttons: run={RunFound}, cancel={CancelFound}, close={CloseFound}",
                run is not null,
                cancel is not null,
                cls is not null
            );

            if (run is not null)
            {
                _logger.LogDebug("Wiring RunButton click event");
                run.Click += async (_, _) =>
                {
                    _logger.LogDebug("RunButton clicked");
                    try
                    {
                        _logger.LogDebug("Starting RunPreviewAsync...");
                        await RunPreviewAsync();
                        _logger.LogDebug("RunPreviewAsync completed");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "RunPreviewAsync threw an exception");
                    }
                };
                UpdateRunEnabled(run);

                // Store handlers for proper disposal
                _canvasPropertyChangedHandler = (_, e) =>
                {
                    if (e.PropertyName == nameof(CanvasViewModel.HasErrors))
                        UpdateRunEnabled(run);
                };
                _liveSqlPropertyChangedHandler = (_, e) =>
                {
                    if (e.PropertyName is nameof(LiveSqlBarViewModel.IsMutatingCommand)
                        or nameof(LiveSqlBarViewModel.RawSql)
                        or nameof(LiveSqlBarViewModel.ExecutionSqlTemplate)
                        or nameof(LiveSqlBarViewModel.IsValid))
                        UpdateRunEnabled(run);
                };

                _vm.PropertyChanged += _canvasPropertyChangedHandler;
                _vm.LiveSql.PropertyChanged += _liveSqlPropertyChangedHandler;
            }

            if (cancel is not null)
                cancel.Click += (_, _) => CancelRun();

            _ = cls;
        });
    }

    /// <summary>
    /// Unsubscribe from PropertyChanged events to prevent accumulation
    /// </summary>
    private void UnsubscribeFromPropertyChangedEvents()
    {
        if (_canvasPropertyChangedHandler is not null)
        {
            _vm.PropertyChanged -= _canvasPropertyChangedHandler;
            _canvasPropertyChangedHandler = null;
        }

        if (_liveSqlPropertyChangedHandler is not null)
        {
            _vm.LiveSql.PropertyChanged -= _liveSqlPropertyChangedHandler;
            _liveSqlPropertyChangedHandler = null;
        }
    }

    private void UpdateRunEnabled(Button run)
    {
        run.IsEnabled = CanRunPreviewButton(
            _vm.LiveSql.IsMutatingCommand,
            _vm.LiveSql.RawSql,
            _vm.LiveSql.ExecutionSqlTemplate
        );
    }

    private static bool CanRunPreviewButton(
        bool isMutatingCommand,
        string rawSql,
        string? executionSqlTemplate
    )
    {
        if (isMutatingCommand)
            return false;

        // If Live SQL text exists, execution requires a concrete template.
        if (!string.IsNullOrWhiteSpace(rawSql))
            return !string.IsNullOrWhiteSpace(executionSqlTemplate);

        return true;
    }

    // ── Cancel ────────────────────────────────────────────────────────────────

    private void CancelRun()
    {
        _runCts?.Cancel();
    }

    // ── Run ───────────────────────────────────────────────────────────────────

    public async Task RunPreviewAsync()
    {
        _logger.LogDebug("RunPreviewAsync started");

        // Check if there are canvas errors and warn about them
        _logger.LogDebug("Canvas HasErrors={HasErrors}", _vm.HasErrors);
        if (_vm.HasErrors)
        {
            _logger.LogWarning("Canvas has validation errors - execution may have unexpected behavior");
        }

        // Safe-preview guard
        _logger.LogDebug("IsMutatingCommand={IsMutatingCommand}", _vm.LiveSql.IsMutatingCommand);
        if (_vm.LiveSql.IsMutatingCommand)
        {
            _logger.LogInformation("Blocked mutating query in preview mode");
            _vm.DataPreview.ShowError(
                L(
                    "preview.error.safePreviewBlocked",
                    "Safe Preview Mode: data-mutating commands (INSERT/UPDATE/DELETE/DROP/ALTER/TRUNCATE) cannot be executed in preview."
                )
            );
            return;
        }

        // Check if connection is available
        _logger.LogDebug(
            "ActiveConnectionConfig={ConnectionStatus}",
            _vm.ActiveConnectionConfig is not null ? "EXISTS" : "NULL"
        );
        if (_vm.ActiveConnectionConfig is null)
        {
            _logger.LogWarning("No active connection config");
            _vm.DataPreview.ShowError(
                L(
                    "preview.error.noActiveConnection",
                    "No active database connection. Please connect to a database first."
                )
            );
            return;
        }

        _logger.LogInformation(
            "Using connection {Provider} @ {Host}:{Port}/{Database}",
            _vm.ActiveConnectionConfig.Provider,
            _vm.ActiveConnectionConfig.Host,
            _vm.ActiveConnectionConfig.Port,
            _vm.ActiveConnectionConfig.Database
        );

        // Cancel any in-flight run before starting a new one
        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = new CancellationTokenSource();
        var ct = _runCts.Token;

        bool useLiveSql = !string.IsNullOrWhiteSpace(_vm.LiveSql.RawSql);
        if (useLiveSql && string.IsNullOrWhiteSpace(_vm.LiveSql.ExecutionSqlTemplate))
        {
            _logger.LogWarning("Live SQL execution blocked: execution template is empty");
            _vm.DataPreview.ShowError(
                L(
                    "preview.error.missingExecutionTemplate",
                    "Cannot execute preview: SQL template is incomplete. Review query diagnostics and fix compilation issues."
                )
            );
            return;
        }

        string sql = useLiveSql
            ? _vm.LiveSql.RawSql
            : (string.IsNullOrWhiteSpace(_vm.QueryText) ? "SELECT 1 AS test" : _vm.QueryText);
        string executionSql = useLiveSql ? _vm.LiveSql.ExecutionSqlTemplate! : sql;
        IReadOnlyList<QueryParameter>? suggestedParameters = useLiveSql && _vm.LiveSql.ExecutionParameters.Count > 0
            ? _vm.LiveSql.ExecutionParameters
            : null;
        IReadOnlyDictionary<string, QueryExecutionParameterContext>? structuralContexts =
            useLiveSql && _vm.LiveSql.ExecutionParameterContexts.Count > 0
                ? _vm.LiveSql.ExecutionParameterContexts
                : null;
        IReadOnlyList<QueryParameterPlaceholder> placeholders = QueryParameterPlaceholderParser.Parse(executionSql);
        IReadOnlyList<QueryParameter>? parameters = placeholders.Count > 0
            ? await CollectParametersIfNeededAsync(executionSql, _vm.ActiveConnectionConfig, suggestedParameters, structuralContexts)
            : suggestedParameters;
        if (parameters is null && placeholders.Count > 0)
        {
            _logger.LogInformation("Preview parameter prompt cancelled by user");
            _vm.DataPreview.ShowCancelled();
            return;
        }

        _logger.LogDebug("SQL query for preview: {Query}", sql);

        // Log guardrail warnings (non-blocking)
        foreach (GuardIssue g in _vm.LiveSql.GuardIssues)
            _logger.LogWarning("Guardrail {Code}: {Message}", g.Code, g.Message);

        _vm.DataPreview.ShowLoading(sql, _queryExecutor.CommandTimeoutSeconds);

        // Start elapsed-time ticker (updates status chip every 100ms)
        var sw = Stopwatch.StartNew();
        using var ticker = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        var tickTask = TickElapsedAsync(ticker, sw, ct);

        try
        {
            _logger.LogDebug("Starting query execution. SQL length={SqlLength}", sql.Length);

            // Execute query against the connected database
            var dt = await _queryExecutor.ExecuteQueryAsync(
                _vm.ActiveConnectionConfig,
                executionSql,
                parameters,
                maxRows: 1000,
                ct: ct
            );

            sw.Stop();
            ct.ThrowIfCancellationRequested();

            _logger.LogInformation("Query completed successfully. Rows: {RowCount}", dt.Rows.Count);

            _vm.DataPreview.ShowResults(dt, sw.ElapsedMilliseconds);
            _logger.LogDebug("ShowResults completed");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Query was cancelled");
            sw.Stop();
            _vm.DataPreview.ShowCancelled();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing query: {Message}", ex.Message);
            sw.Stop();
            _vm.DataPreview.ShowError(ex.Message, ex);
        }
        finally
        {
            await tickTask; // let ticker finish cleanly
            _logger.LogDebug("RunPreviewAsync finished");
        }
    }

    // ── Elapsed ticker ────────────────────────────────────────────────────────

    private async Task TickElapsedAsync(PeriodicTimer timer, Stopwatch sw, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
            {
                var ms = sw.ElapsedMilliseconds;
                await Dispatcher.UIThread.InvokeAsync(() =>
                    _vm.DataPreview.UpdateElapsed(ms));
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Elapsed ticker stopped due to cancellation");
        }
    }

    private async Task<IReadOnlyList<QueryParameter>?> CollectParametersIfNeededAsync(
        string sql,
        ConnectionConfig? config,
        IReadOnlyList<QueryParameter>? suggestedParameters = null,
        IReadOnlyDictionary<string, QueryExecutionParameterContext>? structuralContexts = null)
    {
        IReadOnlyList<QueryParameterPlaceholder> placeholders = QueryParameterPlaceholderParser.Parse(sql);
        if (placeholders.Count == 0)
            return [];

        Dictionary<string, string> initialValues = BuildRememberedValues(placeholders, config);
        IReadOnlyDictionary<string, QueryParameter> suggestedValues = BuildSuggestedParameters(placeholders, suggestedParameters);
        var dialog = new QueryParameterPromptWindow(
            sql,
            placeholders,
            initialValues,
            suggestedValues,
            structuralContexts,
            _vm.DatabaseMetadata,
            config?.Provider ?? DatabaseProvider.Postgres);
        await dialog.ShowDialog(_window);
        if (dialog.Result is not null)
            RememberParameterValues(dialog.EnteredValues, config);
        return dialog.Result;
    }

    private static IReadOnlyDictionary<string, QueryParameter> BuildSuggestedParameters(
        IReadOnlyList<QueryParameterPlaceholder> placeholders,
        IReadOnlyList<QueryParameter>? suggestedParameters)
    {
        if (suggestedParameters is null || suggestedParameters.Count == 0)
            return new Dictionary<string, QueryParameter>(StringComparer.OrdinalIgnoreCase);

        Dictionary<string, QueryParameter> named = suggestedParameters
            .Where(static parameter => !string.IsNullOrWhiteSpace(parameter.Name))
            .GroupBy(parameter => QueryParameterPlaceholderParser.NormalizeName(parameter.Name!))
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<QueryParameter> positional = suggestedParameters
            .Where(static parameter => string.IsNullOrWhiteSpace(parameter.Name))
            .ToArray();
        Dictionary<string, QueryParameter> result = new(StringComparer.OrdinalIgnoreCase);

        foreach (QueryParameterPlaceholder placeholder in placeholders)
        {
            string key = QueryParameterPlaceholderParser.GetStorageKey(placeholder);
            if (placeholder.Kind == QueryParameterPlaceholderKind.Named)
            {
                string normalized = QueryParameterPlaceholderParser.NormalizeName(placeholder.Token);
                if (named.TryGetValue(normalized, out QueryParameter? parameter))
                    result[key] = parameter;
                continue;
            }

            if (placeholder.Position is int position && position > 0 && position <= positional.Count)
                result[key] = positional[position - 1];
        }

        return result;
    }

    private Dictionary<string, string> BuildRememberedValues(
        IReadOnlyList<QueryParameterPlaceholder> placeholders,
        ConnectionConfig? config)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

        foreach (QueryParameterPlaceholder placeholder in placeholders)
        {
            string scopedKey = BuildRememberedParameterKey(config, QueryParameterPlaceholderParser.GetStorageKey(placeholder));
            if (_vm.PreviewParameterInputs.TryGetValue(scopedKey, out string? value))
                values[QueryParameterPlaceholderParser.GetStorageKey(placeholder)] = value;
        }

        return values;
    }

    private void RememberParameterValues(
        IReadOnlyDictionary<string, string> enteredValues,
        ConnectionConfig? config)
    {
        Dictionary<string, string> scopedValues = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in enteredValues)
            scopedValues[BuildRememberedParameterKey(config, key)] = value;
        _vm.RememberPreviewParameterInputs(scopedValues);
    }

    private static string BuildRememberedParameterKey(ConnectionConfig? config, string parameterKey)
    {
        if (config is null)
            return parameterKey;

        return string.Join(
            "|",
            config.Provider,
            config.Host ?? string.Empty,
            config.Port,
            config.Database ?? string.Empty,
            parameterKey);
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Disposes the PreviewService and releases event subscriptions.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Clean up event subscriptions
        UnsubscribeFromPropertyChangedEvents();

        // Cancel any running query
        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = null;

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Throws ObjectDisposedException if the service has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PreviewService));
    }

    private static string L(string key, string fallback)
    {
        string value = LocalizationService.Instance[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }
}
