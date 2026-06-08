using System.Data;
using AkkornStudio.Core;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Services.SqlEditor;

public sealed class SqlEditorMutationExecutionOrchestrator
{
    private readonly Func<string?, ConnectionConfig?, int, CancellationToken, Task<SqlEditorResultSet>> _executeAsync;
    private readonly Func<string?, MutationGuardResult> _analyzeGuard;
    private readonly Func<string, MutationGuardResult, ConnectionConfig?, long?, CancellationToken, Task<SqlMutationDiffPreview>> _buildDiffPreviewAsync;
    private readonly ILocalizationService _localization;
    private readonly Func<DateTimeOffset> _nowProvider;
    private readonly TimeSpan _estimateCacheTtl;
    private readonly Dictionary<string, SqlEditorMutationEstimateCacheEntry> _estimateCache = new(StringComparer.Ordinal);

    public SqlEditorMutationExecutionOrchestrator(
        SqlEditorExecutionService executionService,
        MutationGuardService mutationGuardService,
        SqlMutationDiffService mutationDiffService,
        ILocalizationService? localization = null,
        TimeSpan? estimateCacheTtl = null)
    {
        ArgumentNullException.ThrowIfNull(executionService);
        ArgumentNullException.ThrowIfNull(mutationGuardService);
        ArgumentNullException.ThrowIfNull(mutationDiffService);
        _executeAsync = executionService.ExecuteAsync;
        _analyzeGuard = mutationGuardService.Analyze;
        _buildDiffPreviewAsync = mutationDiffService.BuildPreviewAsync;
        _localization = localization ?? LocalizationService.Instance;
        _nowProvider = () => DateTimeOffset.UtcNow;
        _estimateCacheTtl = estimateCacheTtl ?? TimeSpan.FromSeconds(20);
    }

    internal SqlEditorMutationExecutionOrchestrator(
        Func<string?, ConnectionConfig?, int, CancellationToken, Task<SqlEditorResultSet>> executeAsync,
        Func<string?, MutationGuardResult> analyzeGuard,
        Func<string, MutationGuardResult, ConnectionConfig?, long?, CancellationToken, Task<SqlMutationDiffPreview>> buildDiffPreviewAsync,
        ILocalizationService? localization = null,
        Func<DateTimeOffset>? nowProvider = null,
        TimeSpan? estimateCacheTtl = null)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        _analyzeGuard = analyzeGuard ?? throw new ArgumentNullException(nameof(analyzeGuard));
        _buildDiffPreviewAsync = buildDiffPreviewAsync ?? throw new ArgumentNullException(nameof(buildDiffPreviewAsync));
        _localization = localization ?? LocalizationService.Instance;
        _nowProvider = nowProvider ?? (() => DateTimeOffset.UtcNow);
        _estimateCacheTtl = estimateCacheTtl ?? TimeSpan.FromSeconds(20);
    }

    public async Task<SqlEditorMutationExecutionOutcome> ExecuteAsync(
        string? statementSql,
        ConnectionConfig? config,
        int maxRows,
        bool enforceMutationGuard,
        string? estimateCacheKey = null,
        CancellationToken ct = default)
    {
        string sql = statementSql?.Trim() ?? string.Empty;
        if (!enforceMutationGuard)
        {
            SqlEditorResultSet directResult = await _executeAsync(sql, config, maxRows, ct);
            return new SqlEditorMutationExecutionOutcome { Result = directResult };
        }

        MutationGuardResult guard = _analyzeGuard(sql);
        if (!guard.RequiresConfirmation)
        {
            SqlEditorResultSet directResult = await _executeAsync(sql, config, maxRows, ct);
            return new SqlEditorMutationExecutionOutcome { Result = directResult };
        }

        long? estimatedRows = await EstimateImpactAsync(guard.CountQuery, config, estimateCacheKey, ct);
        SqlMutationDiffPreview diff = await _buildDiffPreviewAsync(sql, guard, config, estimatedRows, ct);
        return new SqlEditorMutationExecutionOutcome
        {
            Result = new SqlEditorResultSet
            {
                StatementSql = sql,
                Success = false,
                ErrorMessage = L("sqlEditor.error.mutationConfirmationRequired", "Mutation confirmation required."),
                ErrorCategory = SqlExecutionErrorCategory.Validation,
                ExecutedAt = DateTimeOffset.UtcNow,
            },
            ConfirmationState = new SqlEditorMutationConfirmationState
            {
                StatementSql = sql,
                Guard = guard,
                DiffPreview = diff,
                EstimatedRows = estimatedRows,
            },
        };
    }

    public Task<SqlEditorResultSet> ConfirmAsync(
        string statementSql,
        ConnectionConfig? config,
        int maxRows,
        CancellationToken ct = default)
    {
        return _executeAsync(statementSql?.Trim(), config, maxRows, ct);
    }

    private async Task<long?> EstimateImpactAsync(string? countQuery, ConnectionConfig? config, string? estimateCacheKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(countQuery))
            return null;

        if (TryGetCachedEstimate(countQuery, estimateCacheKey, out long? cachedEstimatedRows))
            return cachedEstimatedRows;

        SqlEditorResultSet estimateResult = await _executeAsync(countQuery, config, 1, ct);
        if (!estimateResult.Success || estimateResult.Data is null || estimateResult.Data.Rows.Count == 0 || estimateResult.Data.Columns.Count == 0)
            return null;

        object? firstValue = estimateResult.Data.Rows[0][0];
        if (firstValue is null || firstValue is DBNull)
            return null;

        long? estimatedRows = long.TryParse(firstValue.ToString(), out long parsedEstimatedRows) ? parsedEstimatedRows : null;
        StoreEstimate(countQuery, estimateCacheKey, estimatedRows);
        return estimatedRows;
    }

    private bool TryGetCachedEstimate(string countQuery, string? estimateCacheKey, out long? estimatedRows)
    {
        estimatedRows = null;
        if (string.IsNullOrWhiteSpace(estimateCacheKey))
            return false;

        if (!_estimateCache.TryGetValue(estimateCacheKey, out SqlEditorMutationEstimateCacheEntry? cacheEntry))
            return false;

        if (!string.Equals(cacheEntry.CountQuery, countQuery, StringComparison.Ordinal))
            return false;

        if ((_nowProvider() - cacheEntry.CachedAt) > _estimateCacheTtl)
            return false;

        estimatedRows = cacheEntry.EstimatedRows;
        return true;
    }

    private void StoreEstimate(string countQuery, string? estimateCacheKey, long? estimatedRows)
    {
        if (string.IsNullOrWhiteSpace(estimateCacheKey))
            return;

        _estimateCache[estimateCacheKey] = new SqlEditorMutationEstimateCacheEntry
        {
            CountQuery = countQuery,
            EstimatedRows = estimatedRows,
            CachedAt = _nowProvider(),
        };
    }

    private string L(string key, string fallback)
    {
        string value = _localization[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }
}
