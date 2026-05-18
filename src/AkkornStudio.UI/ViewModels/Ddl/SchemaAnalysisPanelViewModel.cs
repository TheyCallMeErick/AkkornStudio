using System.Collections.ObjectModel;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Contracts;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Enums;
using AkkornStudio.Metadata;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.Services.Search;

namespace AkkornStudio.UI.ViewModels;

public sealed class SchemaAnalysisPanelViewModel : ViewModelBase
{
    private const string EmptyMessageFallback = "Nenhum problema estrutural inferível foi detectado.";
    private const string MetadataUnavailableMessageFallback = "Metadata indisponível para análise estrutural.";
    private const string PartialTimeoutMessageFallback = "Análise finalizada parcialmente por timeout.";
    private const string CancelledMessageFallback = "Análise cancelada pelo usuário.";
    private const string FailedMessageFallback = "Falha na análise estrutural.";
    private const string NoFilterMatchMessageFallback = "Nenhuma issue corresponde aos filtros selecionados.";
    private const string NoIssueSelectedMessageFallback = "Nenhuma issue selecionada.";
    private const string NoSqlCandidateMessageFallback = "Nenhum SQL candidate disponível.";
    private const string ActionBlockedTooltipFallback = "Ação indisponível para o nível de risco ou capacidade atual.";
    private const string IgnoreTablePlaceholderFallback = "schema.tabela";

    private readonly Action<string>? _copySql;
    private readonly Action<SchemaIssue, SchemaSuggestion?, SqlFixCandidate?>? _applyToCanvas;
    private static readonly TextSearchService TextSearch = new();

    private readonly List<SchemaIssue> _rawIssues = [];
    private readonly List<SchemaRuleExecutionDiagnostic> _diagnostics = [];
    private readonly HashSet<SchemaIssueSeverity> _severityFilter = [];
    private readonly HashSet<SchemaRuleCode> _ruleFilter = [];

    private double _overallScore;
    public double OverallScore
    {
        get => _overallScore;
        private set => Set(ref _overallScore, value);
    }

    private int _quickWinCount;
    public int QuickWinCount
    {
        get => _quickWinCount;
        private set => Set(ref _quickWinCount, value);
    }

    private string _dominantNamingConvention = string.Empty;
    public string DominantNamingConvention
    {
        get => _dominantNamingConvention;
        private set => Set(ref _dominantNamingConvention, value);
    }

    private string _dominantPkPattern = string.Empty;
    public string DominantPkPattern
    {
        get => _dominantPkPattern;
        private set => Set(ref _dominantPkPattern, value);
    }

    private string _dominantFkPattern = string.Empty;
    public string DominantFkPattern
    {
        get => _dominantFkPattern;
        private set => Set(ref _dominantFkPattern, value);
    }

    private SchemaAnalysisViewState _state;
    private string _stateMessage = L("preview.schemaAnalysis.state.metadataUnavailable", MetadataUnavailableMessageFallback);
    private double _minConfidenceFilter;
    private string _tableTextFilter = string.Empty;
    private SchemaIssue? _selectedIssue;
    private SchemaSuggestion? _selectedSuggestion;
    private SqlFixCandidate? _selectedSqlCandidate;
    private bool _includeInfo = true;
    private bool _includeWarning = true;
    private bool _includeCritical = true;
    private bool _includeFkCatalogInconsistent = true;
    private bool _includeMissingFk = true;
    private bool _includeNamingConventionViolation = true;
    private bool _includeLowSemanticName = true;
    private bool _includeMissingRequiredComment = true;
    private bool _includeNf1HintMultiValued = true;
    private bool _includeNf2HintPartialDependency = true;
    private bool _includeNf3HintTransitiveDependency = true;
    private bool _quickWinsOnly;
    private bool _ignoreViews;
    private string _ignoredTableInput = string.Empty;
    private string? _selectedIgnoredTable;
    private SchemaIssueGroupViewModel? _selectedGroup;

    public SchemaAnalysisPanelViewModel(
        Action<string>? copySql = null,
        Action<SchemaIssue, SchemaSuggestion?, SqlFixCandidate?>? applyToCanvas = null
    )
    {
        _copySql = copySql;
        _applyToCanvas = applyToCanvas;

        CopySqlCommand = new RelayCommand(
            () =>
            {
                if (SelectedSqlCandidate is not null)
                    _copySql?.Invoke(SelectedSqlCandidate.Sql);
            },
            () => CanCopySql
        );

        ApplyToCanvasCommand = new RelayCommand(
            () =>
            {
                if (SelectedIssue is not null)
                    _applyToCanvas?.Invoke(SelectedIssue, SelectedSuggestion, SelectedSqlCandidate);
            },
            () => CanApplyToCanvas
        );

        ClearFiltersCommand = new RelayCommand(ClearFilters);
        AddIgnoredTableCommand = new RelayCommand(AddIgnoredTable, () => CanAddIgnoredTable);
        RemoveIgnoredTableCommand = new RelayCommand<string>(RemoveIgnoredTable);
        ClearIgnoredTablesCommand = new RelayCommand(ClearIgnoredTables, () => IgnoredTables.Count > 0);
        RemoveSelectedIgnoredTableCommand = new RelayCommand(
            RemoveSelectedIgnoredTable,
            () => CanRemoveSelectedIgnoredTable
        );
        SelectNextIssueCommand = new RelayCommand(SelectNextIssue, () => CanSelectNextIssue);
        SelectPreviousIssueCommand = new RelayCommand(SelectPreviousIssue, () => CanSelectPreviousIssue);
    }

    public ObservableCollection<SchemaIssue> VisibleIssues { get; } = [];

    public ObservableCollection<SchemaIssueGroupViewModel> GroupedIssues { get; } = [];

    public ObservableCollection<string> IgnoredTables { get; } = [];

    public SchemaAnalysisViewState State
    {
        get => _state;
        private set => Set(ref _state, value);
    }

    public string StateMessage
    {
        get => _stateMessage;
        private set => Set(ref _stateMessage, value);
    }

    public IReadOnlyList<SchemaRuleExecutionDiagnostic> Diagnostics => _diagnostics;

    public SchemaIssue? SelectedIssue
    {
        get => _selectedIssue;
        set
        {
            if (!Set(ref _selectedIssue, value))
                return;

            SelectedSuggestion = value?.Suggestions.OrderByDescending(s => s.Confidence).ThenBy(s => s.Title, StringComparer.Ordinal).ThenBy(s => s.SuggestionId, StringComparer.Ordinal).FirstOrDefault();
            SelectedGroup = value is null
                ? null
                : GroupedIssues.FirstOrDefault(group => group.Items.Contains(value));
            RaisePropertyChanged(nameof(SelectedIssueEvidence));
            RaisePropertyChanged(nameof(SelectedIssueDiagnostics));
            RaisePropertyChanged(nameof(DetailsMessage));
            RaisePropertyChanged(nameof(HasSelectedIssue));
            RaisePropertyChanged(nameof(SelectedIssuePath));
            RaisePropertyChanged(nameof(SelectedIssueConfidencePercent));
            SelectNextIssueCommand.NotifyCanExecuteChanged();
            SelectPreviousIssueCommand.NotifyCanExecuteChanged();
        }
    }

    public SchemaSuggestion? SelectedSuggestion
    {
        get => _selectedSuggestion;
        set
        {
            if (!Set(ref _selectedSuggestion, value))
                return;

            SelectedSqlCandidate = VisibleCandidates.FirstOrDefault();
            RaisePropertyChanged(nameof(VisibleCandidates));
            RaisePropertyChanged(nameof(SqlCandidatesMessage));
            RaisePropertyChanged(nameof(HasSelectedSuggestion));
        }
    }

    public SqlFixCandidate? SelectedSqlCandidate
    {
        get => _selectedSqlCandidate;
        set
        {
            if (!Set(ref _selectedSqlCandidate, value))
                return;

            RaisePropertyChanged(nameof(CanCopySql));
            RaisePropertyChanged(nameof(CanApplyToCanvas));
            RaisePropertyChanged(nameof(ActionBlockedTooltip));
            RaisePropertyChanged(nameof(HasSelectedSqlCandidate));
            CopySqlCommand.NotifyCanExecuteChanged();
            ApplyToCanvasCommand.NotifyCanExecuteChanged();
        }
    }

    public SchemaIssueGroupViewModel? SelectedGroup
    {
        get => _selectedGroup;
        set => Set(ref _selectedGroup, value);
    }

    public IReadOnlyList<SchemaEvidence> SelectedIssueEvidence =>
        SelectedIssue?.Evidence.OrderByDescending(e => e.Weight).ThenBy(e => e.Key, StringComparer.Ordinal).ToArray()
        ?? [];

    public IReadOnlyList<SchemaRuleExecutionDiagnostic> SelectedIssueDiagnostics =>
        SelectedIssue is null
            ? []
            : _diagnostics
                .Where(d => d.RuleCode == SelectedIssue.RuleCode)
                .OrderByDescending(d => d.IsFatal)
                .ThenBy(d => d.Code, StringComparer.Ordinal)
                .ThenBy(d => d.Message, StringComparer.Ordinal)
                .ToArray();

    public IReadOnlyList<SqlFixCandidate> VisibleCandidates =>
        SelectedSuggestion?.SqlCandidates.Where(c => c.Visibility != CandidateVisibility.Hidden).ToArray() ?? [];

    public string DetailsMessage => SelectedIssue is null
        ? L("preview.schemaAnalysis.state.noIssueSelected", NoIssueSelectedMessageFallback)
        : SelectedIssue.Message;

    public bool HasSelectedIssue => SelectedIssue is not null;

    public bool HasSelectedSuggestion => SelectedSuggestion is not null;

    public bool HasSelectedSqlCandidate => SelectedSqlCandidate is not null;

    public string SelectedIssuePath
    {
        get
        {
            if (SelectedIssue is null)
                return "-";

            string schema = SelectedIssue.SchemaName ?? string.Empty;
            string table = SelectedIssue.TableName ?? string.Empty;
            string column = SelectedIssue.ColumnName ?? string.Empty;

            if (string.IsNullOrWhiteSpace(schema) && string.IsNullOrWhiteSpace(table) && string.IsNullOrWhiteSpace(column))
                return "-";

            if (string.IsNullOrWhiteSpace(schema))
                return string.IsNullOrWhiteSpace(column) ? table : $"{table}.{column}";

            return string.IsNullOrWhiteSpace(column)
                ? $"{schema}.{table}"
                : $"{schema}.{table}.{column}";
        }
    }

    public string SelectedIssueConfidencePercent => SelectedIssue is null
        ? "-"
        : $"{Math.Round(SelectedIssue.Confidence * 100d, 0):0}%";

    public string SqlCandidatesMessage => VisibleCandidates.Count == 0
        ? L("preview.schemaAnalysis.state.noSqlCandidate", NoSqlCandidateMessageFallback)
        : string.Empty;

    public bool CanCopySql =>
        SelectedSqlCandidate is not null
        && SelectedSqlCandidate.Visibility is CandidateVisibility.VisibleReadOnly or CandidateVisibility.VisibleActionable;

    public bool CanApplyToCanvas =>
        SelectedIssue is not null
        && (
            SelectedSqlCandidate?.Visibility == CandidateVisibility.VisibleActionable
            || IsCanvasAutoApplicableIssue(SelectedIssue)
        );

    public string ActionBlockedTooltip =>
        CanCopySql || CanApplyToCanvas
            ? string.Empty
            : L("preview.schemaAnalysis.actionBlockedTooltip", ActionBlockedTooltipFallback);

    private static bool IsCanvasAutoApplicableIssue(SchemaIssue issue)
    {
        return issue.RuleCode is SchemaRuleCode.NAMING_CONVENTION_VIOLATION
            or SchemaRuleCode.MISSING_REQUIRED_COMMENT;
    }

    public RelayCommand CopySqlCommand { get; }

    public RelayCommand ApplyToCanvasCommand { get; }

    public RelayCommand ClearFiltersCommand { get; }

    public RelayCommand AddIgnoredTableCommand { get; }

    public RelayCommand<string> RemoveIgnoredTableCommand { get; }

    public RelayCommand ClearIgnoredTablesCommand { get; }

    public RelayCommand RemoveSelectedIgnoredTableCommand { get; }

    public RelayCommand SelectNextIssueCommand { get; }

    public RelayCommand SelectPreviousIssueCommand { get; }

    public int RawTotalIssues => _rawIssues.Count;

    public int RawInfoCount => _rawIssues.Count(static i => i.Severity == SchemaIssueSeverity.Info);

    public int RawWarningCount => _rawIssues.Count(static i => i.Severity == SchemaIssueSeverity.Warning);

    public int RawCriticalCount => _rawIssues.Count(static i => i.Severity == SchemaIssueSeverity.Critical);

    public int FilteredTotalIssues => VisibleIssues.Count;

    public int FilteredInfoCount => VisibleIssues.Count(static i => i.Severity == SchemaIssueSeverity.Info);

    public int FilteredWarningCount => VisibleIssues.Count(static i => i.Severity == SchemaIssueSeverity.Warning);

    public int FilteredCriticalCount => VisibleIssues.Count(static i => i.Severity == SchemaIssueSeverity.Critical);

    public int FilteredAffectedTablesCount => VisibleIssues
        .Select(static issue =>
        {
            string schema = issue.SchemaName ?? string.Empty;
            string table = issue.TableName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(schema) && string.IsNullOrWhiteSpace(table))
                return string.Empty;

            return string.IsNullOrWhiteSpace(schema) ? table : $"{schema}.{table}";
        })
        .Where(static value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    public int FilteredQuickWinIssuesCount => VisibleIssues.Count(IsQuickWinIssue);

    public bool CanSelectNextIssue => TryGetSelectionIndex(out int index) && index < VisibleIssues.Count - 1;

    public bool CanSelectPreviousIssue => TryGetSelectionIndex(out int index) && index > 0;

    public bool QuickWinsOnly
    {
        get => _quickWinsOnly;
        set
        {
            if (!Set(ref _quickWinsOnly, value))
                return;

            ApplyFilters();
        }
    }

    public bool IgnoreViews
    {
        get => _ignoreViews;
        set => Set(ref _ignoreViews, value);
    }

    public string IgnoredTableInput
    {
        get => _ignoredTableInput;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (!Set(ref _ignoredTableInput, normalized))
                return;

            RaisePropertyChanged(nameof(CanAddIgnoredTable));
            AddIgnoredTableCommand.NotifyCanExecuteChanged();
        }
    }

    public bool CanAddIgnoredTable => !string.IsNullOrWhiteSpace(NormalizeTablePattern(IgnoredTableInput));

    public bool HasIgnoredTables => IgnoredTables.Count > 0;

    public string? SelectedIgnoredTable
    {
        get => _selectedIgnoredTable;
        set
        {
            if (!Set(ref _selectedIgnoredTable, value))
                return;

            RaisePropertyChanged(nameof(CanRemoveSelectedIgnoredTable));
            RemoveSelectedIgnoredTableCommand.NotifyCanExecuteChanged();
        }
    }

    public bool CanRemoveSelectedIgnoredTable => !string.IsNullOrWhiteSpace(SelectedIgnoredTable);

    public string IgnoredTableInputPlaceholder =>
        L("preview.schemaAnalysis.ignoreTable.placeholder", IgnoreTablePlaceholderFallback);

    public double MinConfidenceFilter
    {
        get => _minConfidenceFilter;
        set
        {
            double clamped = Math.Clamp(value, 0d, 1d);
            if (!Set(ref _minConfidenceFilter, clamped))
                return;

            ApplyFilters();
        }
    }

    public string TableTextFilter
    {
        get => _tableTextFilter;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (!Set(ref _tableTextFilter, normalized))
                return;

            ApplyFilters();
        }
    }

    public bool IncludeInfo
    {
        get => _includeInfo;
        set
        {
            if (!Set(ref _includeInfo, value))
                return;

            RebuildSeverityFilterFromFlags();
        }
    }

    public bool IncludeWarning
    {
        get => _includeWarning;
        set
        {
            if (!Set(ref _includeWarning, value))
                return;

            RebuildSeverityFilterFromFlags();
        }
    }

    public bool IncludeCritical
    {
        get => _includeCritical;
        set
        {
            if (!Set(ref _includeCritical, value))
                return;

            RebuildSeverityFilterFromFlags();
        }
    }

    public bool IncludeFkCatalogInconsistent
    {
        get => _includeFkCatalogInconsistent;
        set
        {
            if (!Set(ref _includeFkCatalogInconsistent, value))
                return;

            RebuildRuleFilterFromFlags();
        }
    }

    public bool IncludeMissingFk
    {
        get => _includeMissingFk;
        set
        {
            if (!Set(ref _includeMissingFk, value))
                return;

            RebuildRuleFilterFromFlags();
        }
    }

    public bool IncludeNamingConventionViolation
    {
        get => _includeNamingConventionViolation;
        set
        {
            if (!Set(ref _includeNamingConventionViolation, value))
                return;

            RebuildRuleFilterFromFlags();
        }
    }

    public bool IncludeLowSemanticName
    {
        get => _includeLowSemanticName;
        set
        {
            if (!Set(ref _includeLowSemanticName, value))
                return;

            RebuildRuleFilterFromFlags();
        }
    }

    public bool IncludeMissingRequiredComment
    {
        get => _includeMissingRequiredComment;
        set
        {
            if (!Set(ref _includeMissingRequiredComment, value))
                return;

            RebuildRuleFilterFromFlags();
        }
    }

    public bool IncludeNf1HintMultiValued
    {
        get => _includeNf1HintMultiValued;
        set
        {
            if (!Set(ref _includeNf1HintMultiValued, value))
                return;

            RebuildRuleFilterFromFlags();
        }
    }

    public bool IncludeNf2HintPartialDependency
    {
        get => _includeNf2HintPartialDependency;
        set
        {
            if (!Set(ref _includeNf2HintPartialDependency, value))
                return;

            RebuildRuleFilterFromFlags();
        }
    }

    public bool IncludeNf3HintTransitiveDependency
    {
        get => _includeNf3HintTransitiveDependency;
        set
        {
            if (!Set(ref _includeNf3HintTransitiveDependency, value))
                return;

            RebuildRuleFilterFromFlags();
        }
    }

    public void SetSeverityFilter(IEnumerable<SchemaIssueSeverity> severities)
    {
        _severityFilter.Clear();
        foreach (SchemaIssueSeverity severity in severities)
            _severityFilter.Add(severity);

        _includeInfo = _severityFilter.Contains(SchemaIssueSeverity.Info);
        _includeWarning = _severityFilter.Contains(SchemaIssueSeverity.Warning);
        _includeCritical = _severityFilter.Contains(SchemaIssueSeverity.Critical);
        RaisePropertyChanged(nameof(IncludeInfo));
        RaisePropertyChanged(nameof(IncludeWarning));
        RaisePropertyChanged(nameof(IncludeCritical));

        ApplyFilters();
    }

    public void SetRuleFilter(IEnumerable<SchemaRuleCode> rules)
    {
        _ruleFilter.Clear();
        foreach (SchemaRuleCode rule in rules)
            _ruleFilter.Add(rule);

        _includeFkCatalogInconsistent = _ruleFilter.Contains(SchemaRuleCode.FK_CATALOG_INCONSISTENT);
        _includeMissingFk = _ruleFilter.Contains(SchemaRuleCode.MISSING_FK);
        _includeNamingConventionViolation = _ruleFilter.Contains(SchemaRuleCode.NAMING_CONVENTION_VIOLATION);
        _includeLowSemanticName = _ruleFilter.Contains(SchemaRuleCode.LOW_SEMANTIC_NAME);
        _includeMissingRequiredComment = _ruleFilter.Contains(SchemaRuleCode.MISSING_REQUIRED_COMMENT);
        _includeNf1HintMultiValued = _ruleFilter.Contains(SchemaRuleCode.NF1_HINT_MULTI_VALUED);
        _includeNf2HintPartialDependency = _ruleFilter.Contains(SchemaRuleCode.NF2_HINT_PARTIAL_DEPENDENCY);
        _includeNf3HintTransitiveDependency = _ruleFilter.Contains(SchemaRuleCode.NF3_HINT_TRANSITIVE_DEPENDENCY);
        RaisePropertyChanged(nameof(IncludeFkCatalogInconsistent));
        RaisePropertyChanged(nameof(IncludeMissingFk));
        RaisePropertyChanged(nameof(IncludeNamingConventionViolation));
        RaisePropertyChanged(nameof(IncludeLowSemanticName));
        RaisePropertyChanged(nameof(IncludeMissingRequiredComment));
        RaisePropertyChanged(nameof(IncludeNf1HintMultiValued));
        RaisePropertyChanged(nameof(IncludeNf2HintPartialDependency));
        RaisePropertyChanged(nameof(IncludeNf3HintTransitiveDependency));

        ApplyFilters();
    }

    public void ClearFilters()
    {
        _includeInfo = true;
        _includeWarning = true;
        _includeCritical = true;
        _includeFkCatalogInconsistent = true;
        _includeMissingFk = true;
        _includeNamingConventionViolation = true;
        _includeLowSemanticName = true;
        _includeMissingRequiredComment = true;
        _includeNf1HintMultiValued = true;
        _includeNf2HintPartialDependency = true;
        _includeNf3HintTransitiveDependency = true;
        RaisePropertyChanged(nameof(IncludeInfo));
        RaisePropertyChanged(nameof(IncludeWarning));
        RaisePropertyChanged(nameof(IncludeCritical));
        RaisePropertyChanged(nameof(IncludeFkCatalogInconsistent));
        RaisePropertyChanged(nameof(IncludeMissingFk));
        RaisePropertyChanged(nameof(IncludeNamingConventionViolation));
        RaisePropertyChanged(nameof(IncludeLowSemanticName));
        RaisePropertyChanged(nameof(IncludeMissingRequiredComment));
        RaisePropertyChanged(nameof(IncludeNf1HintMultiValued));
        RaisePropertyChanged(nameof(IncludeNf2HintPartialDependency));
        RaisePropertyChanged(nameof(IncludeNf3HintTransitiveDependency));

        _minConfidenceFilter = 0d;
        _tableTextFilter = string.Empty;
        RaisePropertyChanged(nameof(MinConfidenceFilter));
        RaisePropertyChanged(nameof(TableTextFilter));
        QuickWinsOnly = false;
        IgnoreViews = false;
        ClearIgnoredTables();
        RebuildSeverityFilterFromFlags(applyFilters: false);
        RebuildRuleFilterFromFlags(applyFilters: false);
        ApplyFilters();
    }

    public void SetMetadataUnavailable()
    {
        _rawIssues.Clear();
        _diagnostics.Clear();
        VisibleIssues.Clear();
        GroupedIssues.Clear();
        SelectedGroup = null;
        SelectedIssue = null;
        State = SchemaAnalysisViewState.Idle;
        StateMessage = L("preview.schemaAnalysis.state.metadataUnavailable", MetadataUnavailableMessageFallback);
        RaiseSummaryCountersChanged();
    }

    public void SetLoading()
    {
        State = SchemaAnalysisViewState.Loading;
        StateMessage = string.Empty;
    }

    public void SetCancelled()
    {
        State = SchemaAnalysisViewState.Cancelled;
        StateMessage = L("preview.schemaAnalysis.state.cancelled", CancelledMessageFallback);
    }

    public void ApplyResult(SchemaAnalysisResult result)
    {
        _rawIssues.Clear();
        _rawIssues.AddRange(result.Issues);

        _diagnostics.Clear();
        _diagnostics.AddRange(result.Diagnostics);
        RaisePropertyChanged(nameof(Diagnostics));

        OverallScore = result.Summary.OverallScore;
        QuickWinCount = result.Summary.QuickWinCount;
        DominantNamingConvention = result.Summary.ObservedPatterns.DominantNamingConvention.ToString();
        DominantPkPattern = result.Summary.ObservedPatterns.DominantPkPattern ?? "-";
        DominantFkPattern = result.Summary.ObservedPatterns.DominantFkPattern ?? "-";

        ApplyFilters();

        (SchemaAnalysisViewState state, string message) = MapState(result.Status, result.PartialState.ReasonCode, _rawIssues.Count, VisibleIssues.Count);
        State = state;
        StateMessage = message;
    }

    private static (SchemaAnalysisViewState State, string Message) MapState(
        SchemaAnalysisStatus status,
        string reasonCode,
        int rawIssueCount,
        int filteredIssueCount
    )
    {
        if (status == SchemaAnalysisStatus.Failed)
            return (SchemaAnalysisViewState.Failed, L("preview.schemaAnalysis.state.failed", FailedMessageFallback));

        if (status == SchemaAnalysisStatus.Cancelled)
            return (SchemaAnalysisViewState.Cancelled, L("preview.schemaAnalysis.state.cancelled", CancelledMessageFallback));

        if (status == SchemaAnalysisStatus.Partial)
        {
            if (string.Equals(reasonCode, "TIMEOUT", StringComparison.Ordinal))
                return (SchemaAnalysisViewState.Partial, L("preview.schemaAnalysis.state.partialTimeout", PartialTimeoutMessageFallback));

            if (string.Equals(reasonCode, "CANCELLED", StringComparison.Ordinal))
                return (SchemaAnalysisViewState.Partial, L("preview.schemaAnalysis.state.cancelled", CancelledMessageFallback));

            return (SchemaAnalysisViewState.Partial, string.Empty);
        }

        if (rawIssueCount == 0)
            return (SchemaAnalysisViewState.Empty, L("preview.schemaAnalysis.state.empty", EmptyMessageFallback));

        if (filteredIssueCount == 0)
            return (SchemaAnalysisViewState.Completed, L("preview.schemaAnalysis.state.noFilterMatch", NoFilterMatchMessageFallback));

        return (SchemaAnalysisViewState.Completed, string.Empty);
    }

    private void ApplyFilters()
    {
        List<SchemaIssue> filtered = _rawIssues
            .Where(i => _severityFilter.Count == 0 || _severityFilter.Contains(i.Severity))
            .Where(i => _ruleFilter.Count == 0 || _ruleFilter.Contains(i.RuleCode))
            .Where(i => i.Confidence >= MinConfidenceFilter)
            .Where(i => MatchesTableFilter(i, TableTextFilter))
            .Where(i => !IsBlacklistedTable(i.SchemaName, i.TableName))
            .Where(i => !_quickWinsOnly || i.Suggestions.Count > 0)
            .ToList();

        VisibleIssues.Clear();
        foreach (SchemaIssue issue in filtered)
            VisibleIssues.Add(issue);

        RebuildGroups();

        ReconcileSelection();
        RaiseSummaryCountersChanged();

        if (State is SchemaAnalysisViewState.Completed or SchemaAnalysisViewState.Empty)
        {
            (_, string message) = MapState(
                SchemaAnalysisStatus.Completed,
                "NONE",
                _rawIssues.Count,
                VisibleIssues.Count
            );
            StateMessage = message;
        }
    }

    private void ReconcileSelection()
    {
        if (VisibleIssues.Count == 0)
        {
            SelectedIssue = null;
            return;
        }

        if (SelectedIssue is null || !VisibleIssues.Contains(SelectedIssue))
        {
            SelectedIssue = VisibleIssues[0];
        }

        SelectNextIssueCommand.NotifyCanExecuteChanged();
        SelectPreviousIssueCommand.NotifyCanExecuteChanged();
    }

    private void RebuildGroups()
    {
        GroupedIssues.Clear();

        foreach (IGrouping<SchemaIssueSeverity, SchemaIssue> group in VisibleIssues
                     .GroupBy(static issue => issue.Severity)
                     .OrderBy(group => GetSeverityOrder(group.Key)))
        {
            string title = group.Key switch
            {
                SchemaIssueSeverity.Critical => L("preview.schemaAnalysis.severity.critical", "Critical"),
                SchemaIssueSeverity.Warning => L("preview.schemaAnalysis.severity.warning", "Warning"),
                _ => L("preview.schemaAnalysis.severity.info", "Info")
            };

            GroupedIssues.Add(new SchemaIssueGroupViewModel(group.Key, title, group.ToArray()));
        }

        if (SelectedIssue is not null)
            SelectedGroup = GroupedIssues.FirstOrDefault(group => group.Items.Contains(SelectedIssue));
        else
            SelectedGroup = GroupedIssues.FirstOrDefault();
    }

    private static int GetSeverityOrder(SchemaIssueSeverity severity)
    {
        return severity switch
        {
            SchemaIssueSeverity.Critical => 0,
            SchemaIssueSeverity.Warning => 1,
            _ => 2
        };
    }

    private bool TryGetSelectionIndex(out int index)
    {
        if (SelectedIssue is null)
        {
            index = -1;
            return false;
        }

        index = VisibleIssues.IndexOf(SelectedIssue);
        return index >= 0;
    }

    private void SelectNextIssue()
    {
        if (!TryGetSelectionIndex(out int index))
            return;

        if (index < VisibleIssues.Count - 1)
            SelectedIssue = VisibleIssues[index + 1];
    }

    private void SelectPreviousIssue()
    {
        if (!TryGetSelectionIndex(out int index))
            return;

        if (index > 0)
            SelectedIssue = VisibleIssues[index - 1];
    }

    private static bool MatchesTableFilter(SchemaIssue issue, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        string schema = issue.SchemaName ?? string.Empty;
        string table = issue.TableName ?? string.Empty;
        string value = string.IsNullOrWhiteSpace(schema) ? table : $"{schema}.{table}";

        return TextSearch.Matches(filter, value);
    }

    private void RaiseSummaryCountersChanged()
    {
        RaisePropertyChanged(nameof(RawTotalIssues));
        RaisePropertyChanged(nameof(RawInfoCount));
        RaisePropertyChanged(nameof(RawWarningCount));
        RaisePropertyChanged(nameof(RawCriticalCount));
        RaisePropertyChanged(nameof(FilteredTotalIssues));
        RaisePropertyChanged(nameof(FilteredInfoCount));
        RaisePropertyChanged(nameof(FilteredWarningCount));
        RaisePropertyChanged(nameof(FilteredCriticalCount));
        RaisePropertyChanged(nameof(FilteredAffectedTablesCount));
        RaisePropertyChanged(nameof(FilteredQuickWinIssuesCount));
    }

    private static bool IsQuickWinIssue(SchemaIssue issue)
    {
        if (issue.Suggestions.Count == 0)
            return false;

        return issue.Suggestions.Any(static suggestion =>
            suggestion.SqlCandidates.Any(candidate =>
                candidate.Visibility is CandidateVisibility.VisibleActionable or CandidateVisibility.VisibleReadOnly));
    }

    private void RebuildSeverityFilterFromFlags(bool applyFilters = true)
    {
        _severityFilter.Clear();
        if (IncludeInfo)
            _severityFilter.Add(SchemaIssueSeverity.Info);
        if (IncludeWarning)
            _severityFilter.Add(SchemaIssueSeverity.Warning);
        if (IncludeCritical)
            _severityFilter.Add(SchemaIssueSeverity.Critical);

        if (applyFilters)
            ApplyFilters();
    }

    private void RebuildRuleFilterFromFlags(bool applyFilters = true)
    {
        _ruleFilter.Clear();
        if (IncludeFkCatalogInconsistent)
            _ruleFilter.Add(SchemaRuleCode.FK_CATALOG_INCONSISTENT);
        if (IncludeMissingFk)
            _ruleFilter.Add(SchemaRuleCode.MISSING_FK);
        if (IncludeNamingConventionViolation)
            _ruleFilter.Add(SchemaRuleCode.NAMING_CONVENTION_VIOLATION);
        if (IncludeLowSemanticName)
            _ruleFilter.Add(SchemaRuleCode.LOW_SEMANTIC_NAME);
        if (IncludeMissingRequiredComment)
            _ruleFilter.Add(SchemaRuleCode.MISSING_REQUIRED_COMMENT);
        if (IncludeNf1HintMultiValued)
            _ruleFilter.Add(SchemaRuleCode.NF1_HINT_MULTI_VALUED);
        if (IncludeNf2HintPartialDependency)
            _ruleFilter.Add(SchemaRuleCode.NF2_HINT_PARTIAL_DEPENDENCY);
        if (IncludeNf3HintTransitiveDependency)
            _ruleFilter.Add(SchemaRuleCode.NF3_HINT_TRANSITIVE_DEPENDENCY);

        if (applyFilters)
            ApplyFilters();
    }

    public bool ShouldIgnoreTableForAnalysis(string? schemaName, string? tableName, TableKind tableKind)
    {
        if (IgnoreViews && tableKind != TableKind.Table)
            return true;

        return IsBlacklistedTable(schemaName, tableName);
    }

    private void AddIgnoredTable()
    {
        string normalized = NormalizeTablePattern(IgnoredTableInput);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        if (IgnoredTables.Any(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            IgnoredTableInput = string.Empty;
            return;
        }

        IgnoredTables.Add(normalized);
        IgnoredTableInput = string.Empty;
        SelectedIgnoredTable ??= normalized;
        RaisePropertyChanged(nameof(HasIgnoredTables));
        ClearIgnoredTablesCommand.NotifyCanExecuteChanged();
        RemoveSelectedIgnoredTableCommand.NotifyCanExecuteChanged();
        ApplyFilters();
    }

    private void RemoveIgnoredTable(string? tablePattern)
    {
        string normalized = NormalizeTablePattern(tablePattern);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        string? existing = IgnoredTables.FirstOrDefault(item =>
            string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase)
        );
        if (existing is null)
            return;

        IgnoredTables.Remove(existing);
        if (string.Equals(SelectedIgnoredTable, existing, StringComparison.OrdinalIgnoreCase))
            SelectedIgnoredTable = IgnoredTables.FirstOrDefault();
        RaisePropertyChanged(nameof(HasIgnoredTables));
        ClearIgnoredTablesCommand.NotifyCanExecuteChanged();
        RemoveSelectedIgnoredTableCommand.NotifyCanExecuteChanged();
        ApplyFilters();
    }

    private void ClearIgnoredTables()
    {
        if (IgnoredTables.Count == 0)
            return;

        IgnoredTables.Clear();
        SelectedIgnoredTable = null;
        RaisePropertyChanged(nameof(HasIgnoredTables));
        ClearIgnoredTablesCommand.NotifyCanExecuteChanged();
        RemoveSelectedIgnoredTableCommand.NotifyCanExecuteChanged();
        ApplyFilters();
    }

    private void RemoveSelectedIgnoredTable()
    {
        RemoveIgnoredTable(SelectedIgnoredTable);
    }

    private bool IsBlacklistedTable(string? schemaName, string? tableName)
    {
        if (IgnoredTables.Count == 0)
            return false;

        string normalizedTable = NormalizeIdentifier(tableName);
        if (string.IsNullOrWhiteSpace(normalizedTable))
            return false;

        string normalizedSchema = NormalizeIdentifier(schemaName);
        string qualified = string.IsNullOrWhiteSpace(normalizedSchema)
            ? normalizedTable
            : $"{normalizedSchema}.{normalizedTable}";

        return IgnoredTables.Any(item =>
            string.Equals(item, normalizedTable, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item, qualified, StringComparison.OrdinalIgnoreCase)
        );
    }

    private static string NormalizeTablePattern(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        string[] parts = raw
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeIdentifier)
            .Where(static part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        if (parts.Length == 0)
            return string.Empty;

        if (parts.Length == 1)
            return parts[0];

        return $"{parts[^2]}.{parts[^1]}";
    }

    private static string NormalizeIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value
            .Trim()
            .Trim('[', ']', '"', '`')
            .ToLowerInvariant();
    }

    private static string L(string key, string fallback)
    {
        string value = LocalizationService.Instance[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }
}

public sealed class SchemaIssueGroupViewModel
{
    public SchemaIssueGroupViewModel(SchemaIssueSeverity severity, string title, IReadOnlyList<SchemaIssue> items)
    {
        Severity = severity;
        Title = title;
        Items = items;
    }

    public SchemaIssueSeverity Severity { get; }

    public string Title { get; }

    public int Count => Items.Count;

    public IReadOnlyList<SchemaIssue> Items { get; }
}
