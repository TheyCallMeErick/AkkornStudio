using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using AkkornStudio.Nodes;

namespace AkkornStudio.UI.ViewModels.Canvas;

/// <summary>
/// Manages query graph validation, including orphan node detection and validation issue tracking.
/// Runs validation asynchronously with debouncing to avoid excessive processing.
/// </summary>
public sealed class ValidationManager(CanvasViewModel canvasViewModel) : ViewModelBase
{
    private readonly CanvasViewModel _canvasViewModel = canvasViewModel;
    private readonly ILogger<ValidationManager> _logger = NullLogger<ValidationManager>.Instance;
    private readonly object _validationLock = new();  // Synchronization for _validationCts
    private CancellationTokenSource? _validationCts;
    private int _requestedValidationVersion;
    private int _completedValidationVersion;
    private bool _validationInProgress;

    private bool _hasErrors;
    private int _errorCount;
    private int _warningCount;
    private bool _hasOrphanNodes;
    private int _orphanCount;
    private bool _hasNamingViolations;
    private int _namingConformance;

    public bool HasErrors
    {
        get => _hasErrors;
        private set => Set(ref _hasErrors, value);
    }
    public int ErrorCount
    {
        get => _errorCount;
        private set => Set(ref _errorCount, value);
    }
    public int WarningCount
    {
        get => _warningCount;
        private set => Set(ref _warningCount, value);
    }
    public bool HasOrphanNodes
    {
        get => _hasOrphanNodes;
        private set => Set(ref _hasOrphanNodes, value);
    }
    public int OrphanCount
    {
        get => _orphanCount;
        private set => Set(ref _orphanCount, value);
    }
    public bool HasNamingViolations
    {
        get => _hasNamingViolations;
        private set => Set(ref _hasNamingViolations, value);
    }
    public int NamingConformance
    {
        get => _namingConformance;
        private set => Set(ref _namingConformance, value);
    }

    public RelayCommand? CleanupOrphansCommand { get; set; }
    public RelayCommand? AutoFixNamingCommand { get; set; }

    /// <summary>
    /// Schedules a validation run with 200ms debounce to avoid excessive processing.
    /// Cancels any pending validation before scheduling a new one.
    /// Thread-safe: uses lock to synchronize access to _validationCts.
    /// </summary>
    public void ScheduleValidation()
    {
        lock (_validationLock)
        {
            _requestedValidationVersion++;
            _validationCts?.Cancel();
            _validationCts?.Dispose();
            _validationCts = new CancellationTokenSource();
            CancellationToken token = _validationCts.Token;

            Task.Delay(AppConstants.ValidationDebounceMs, token)
                .ContinueWith(
                    _ =>
                    {
                        if (!token.IsCancellationRequested)
                            Avalonia.Threading.Dispatcher.UIThread.Post(RunValidationSafely);
                    },
                    TaskScheduler.Default
                );
        }
    }

    private async void RunValidationSafely()
    {
        int targetVersion;
        lock (_validationLock)
        {
            if (_validationInProgress || _completedValidationVersion >= _requestedValidationVersion)
                return;

            _validationInProgress = true;
            targetVersion = _requestedValidationVersion;
        }

        try
        {
            ValidationComputation computation = await Task.Run(ComputeValidation);
            ApplyValidation(computation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception during validation");
        }
        finally
        {
            bool shouldRerun;
            lock (_validationLock)
            {
                _completedValidationVersion = Math.Max(_completedValidationVersion, targetVersion);
                _validationInProgress = false;
                shouldRerun = _requestedValidationVersion > _completedValidationVersion;
            }

            // Coalesce rapid updates: if state changed while validation was running,
            // re-run immediately after completion.
            if (shouldRerun)
                Avalonia.Threading.Dispatcher.UIThread.Post(RunValidationSafely);
        }
    }

    private ValidationComputation ComputeValidation()
    {
        NamingConventionPolicy namingPolicy = _canvasViewModel.BuildNamingConventionPolicy();
        IReadOnlyList<ValidationIssue> allIssues = GraphValidator.Validate(
            _canvasViewModel,
            namingPolicy,
            _canvasViewModel.AliasConventions
        );
        Dictionary<string, IReadOnlyList<ValidationIssue>> byNode = allIssues
            .Where(i => !string.IsNullOrEmpty(i.NodeId))
            .GroupBy(i => i.NodeId)
            .ToDictionary(
                g => g.Key!,
                g => (IReadOnlyList<ValidationIssue>)g.ToList(),
                StringComparer.OrdinalIgnoreCase
            );

        HashSet<string> orphanIds = [.. OrphanNodeDetector.DetectOrphanIds(_canvasViewModel)];

        int errorCount = allIssues.Count(i => i.Severity == IssueSeverity.Error);
        int warningCount = allIssues.Count(i => i.Severity == IssueSeverity.Warning);
        bool hasNaming = allIssues.Any(i => i.Code.StartsWith("NAMING_", StringComparison.Ordinal));
        int namingConformance = NamingConventionValidator.ConformancePercent(
            _canvasViewModel,
            namingPolicy,
            _canvasViewModel.AliasConventions
        );

        return new ValidationComputation(
            ByNodeIssues: byNode,
            OrphanIds: orphanIds,
            ErrorCount: errorCount,
            WarningCount: warningCount,
            HasErrors: errorCount > 0,
            HasOrphanNodes: orphanIds.Count > 0,
            OrphanCount: orphanIds.Count,
            HasNamingViolations: hasNaming,
            NamingConformance: namingConformance
        );
    }

    /// <summary>
    /// Applies validation results to node/view-model state and refreshes command state.
    /// Must run on UI thread.
    /// </summary>
    private void ApplyValidation(ValidationComputation computation)
    {
        foreach (NodeViewModel node in _canvasViewModel.Nodes)
        {
            node.IsOrphan = computation.OrphanIds.Contains(node.Id);
            node.SetValidation(
                computation.ByNodeIssues.TryGetValue(node.Id, out IReadOnlyList<ValidationIssue>? issues)
                    ? issues
                    : []
            );
        }

        HasErrors = computation.HasErrors;
        ErrorCount = computation.ErrorCount;
        WarningCount = computation.WarningCount;
        HasOrphanNodes = computation.HasOrphanNodes;
        OrphanCount = computation.OrphanCount;
        HasNamingViolations = computation.HasNamingViolations;
        NamingConformance = computation.NamingConformance;

        // Notify command buttons of state changes
        CleanupOrphansCommand?.NotifyCanExecuteChanged();
        AutoFixNamingCommand?.NotifyCanExecuteChanged();
    }

    private sealed record ValidationComputation(
        IReadOnlyDictionary<string, IReadOnlyList<ValidationIssue>> ByNodeIssues,
        IReadOnlySet<string> OrphanIds,
        int ErrorCount,
        int WarningCount,
        bool HasErrors,
        bool HasOrphanNodes,
        int OrphanCount,
        bool HasNamingViolations,
        int NamingConformance
    );
}
