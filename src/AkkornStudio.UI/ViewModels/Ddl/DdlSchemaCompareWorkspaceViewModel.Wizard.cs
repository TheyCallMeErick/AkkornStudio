using System.Collections.ObjectModel;
using System.Text;
using AkkornStudio.Core;
using AkkornStudio.Ddl.Compare;
using Avalonia.Media;
using AkkornStudio.UI.Services.Localization;
using Material.Icons;

namespace AkkornStudio.UI.ViewModels;

public enum DdlSchemaCompareWizardStep
{
    Selection = 1,
    Review = 2,
    SqlOptions = 3,
    SqlPreview = 4,
}

public enum DdlSchemaCompareDiffSeverity
{
    Info,
    Low,
    Medium,
    High,
}

public enum DdlSchemaCompareDiffReviewStatus
{
    Pending,
    Included,
    Ignored,
    Reviewed,
}

public enum DdlSchemaCompareSqlGenerationMode
{
    Safe,
    Complete,
    Informative,
    AssistedManual,
}

public sealed class DdlSchemaCompareDifferenceItemViewModel : ViewModelBase
{
    private bool _isIncluded;
    private DdlSchemaCompareDiffReviewStatus _reviewStatus;
    private bool _isSelectedForInspection;

    public required string Id { get; init; }
    public required string Category { get; init; }
    public required string Item { get; init; }
    public required string SourceValue { get; init; }
    public required string TargetValue { get; init; }
    public required string SuggestedAction { get; init; }
    public required DdlSchemaCompareDiffSeverity Severity { get; init; }
    public required bool IsDestructive { get; init; }

    private string _suggestedSql = string.Empty;

    public required string SuggestedSql
    {
        get => _suggestedSql;
        set => Set(ref _suggestedSql, value);
    }

    public required string RiskSummary { get; init; }
    public string Notes { get; init; } = string.Empty;

    /// <summary>The structured engine difference this UI item was built from (used to regenerate SQL).</summary>
    internal SchemaDifference EngineDifference { get; init; } = null!;

    /// <summary>Schema of the table this difference targets (for SQL generation in schema-wide mode).</summary>
    internal string TargetSchema { get; init; } = string.Empty;

    /// <summary>Name of the table this difference targets.</summary>
    internal string TargetTable { get; init; } = string.Empty;

    public event Action? Changed;

    public bool IsIncluded
    {
        get => _isIncluded;
        set
        {
            if (!Set(ref _isIncluded, value))
                return;

            if (_reviewStatus != DdlSchemaCompareDiffReviewStatus.Reviewed)
                _reviewStatus = value ? DdlSchemaCompareDiffReviewStatus.Included : DdlSchemaCompareDiffReviewStatus.Ignored;

            RaisePropertyChanged(nameof(ReviewStatus));
            Changed?.Invoke();
        }
    }

    public DdlSchemaCompareDiffReviewStatus ReviewStatus
    {
        get => _reviewStatus;
        set
        {
            if (!Set(ref _reviewStatus, value))
                return;

            Changed?.Invoke();
        }
    }

    public bool IsSelectedForInspection
    {
        get => _isSelectedForInspection;
        set => Set(ref _isSelectedForInspection, value);
    }

    public bool IsHighSeverity => Severity == DdlSchemaCompareDiffSeverity.High;
    public bool IsMediumSeverity => Severity == DdlSchemaCompareDiffSeverity.Medium;
    public bool IsLowSeverity => Severity == DdlSchemaCompareDiffSeverity.Low;
    public bool IsInfoSeverity => Severity == DdlSchemaCompareDiffSeverity.Info;
    public bool IsIgnored => !IsIncluded;
    public bool IsReviewed => ReviewStatus == DdlSchemaCompareDiffReviewStatus.Reviewed;

    public string SourceTargetSummary =>
        $"Origem: {NormalizePreviewValue(SourceValue)}  ->  Destino: {NormalizePreviewValue(TargetValue)}";

    public string InclusionStateLabel => ReviewStatus switch
    {
        DdlSchemaCompareDiffReviewStatus.Included => "Incluida no SQL",
        DdlSchemaCompareDiffReviewStatus.Ignored => "Ignorada no script",
        DdlSchemaCompareDiffReviewStatus.Reviewed => IsIncluded ? "Revisada e incluida" : "Revisada e ignorada",
        _ => IsIncluded ? "Incluida no SQL" : "Pendente",
    };

    public MaterialIconKind CategoryIconKind => ResolveCategoryIcon(Category, SuggestedAction, IsDestructive);

    public string SeverityLabel => Severity switch
    {
        DdlSchemaCompareDiffSeverity.High => Loc("ddl.compare.severity.high", "High"),
        DdlSchemaCompareDiffSeverity.Medium => Loc("ddl.compare.severity.medium", "Medium"),
        DdlSchemaCompareDiffSeverity.Low => Loc("ddl.compare.severity.low", "Low"),
        _ => Loc("ddl.compare.severity.info", "Info"),
    };

    public string StatusLabel => ReviewStatus switch
    {
        DdlSchemaCompareDiffReviewStatus.Included => Loc("ddl.compare.status.included", "Included"),
        DdlSchemaCompareDiffReviewStatus.Ignored => Loc("ddl.compare.status.ignored", "Ignored"),
        DdlSchemaCompareDiffReviewStatus.Reviewed => Loc("ddl.compare.status.reviewed", "Reviewed"),
        _ => Loc("ddl.compare.status.pending", "Pending"),
    };

    private static string Loc(string key, string fallback)
    {
        string value = LocalizationService.Instance[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    private static string NormalizePreviewValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "-";

        string normalized = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        return normalized.Length <= 88
            ? normalized
            : normalized[..85] + "...";
    }

    private static MaterialIconKind ResolveCategoryIcon(string category, string action, bool isDestructive)
    {
        if (isDestructive)
            return MaterialIconKind.AlertOutline;

        if (action.Contains("ADD FK", StringComparison.OrdinalIgnoreCase)
            || action.Contains("FK", StringComparison.OrdinalIgnoreCase)
            || category.Contains("fk", StringComparison.OrdinalIgnoreCase))
            return MaterialIconKind.LinkVariant;

        if (category.Contains("pk", StringComparison.OrdinalIgnoreCase)
            || category.Contains("primary", StringComparison.OrdinalIgnoreCase))
            return MaterialIconKind.KeyVariant;

        if (category.Contains("unique", StringComparison.OrdinalIgnoreCase))
            return MaterialIconKind.ShieldCheck;

        if (category.Contains("indice", StringComparison.OrdinalIgnoreCase)
            || category.Contains("index", StringComparison.OrdinalIgnoreCase))
            return MaterialIconKind.FormatListNumbered;

        if (category.Contains("dependencia", StringComparison.OrdinalIgnoreCase)
            || category.Contains("extern", StringComparison.OrdinalIgnoreCase))
            return MaterialIconKind.ShareVariant;

        if (action.Contains("ADD", StringComparison.OrdinalIgnoreCase)
            || category.Contains("ausente", StringComparison.OrdinalIgnoreCase))
            return MaterialIconKind.PlusBoxOutline;

        if (action.Contains("DROP", StringComparison.OrdinalIgnoreCase)
            || category.Contains("extra", StringComparison.OrdinalIgnoreCase))
            return MaterialIconKind.MinusBoxOutline;

        if (action.Contains("ALTER", StringComparison.OrdinalIgnoreCase)
            || category.Contains("tipo", StringComparison.OrdinalIgnoreCase))
            return MaterialIconKind.SwapHorizontal;

        if (category.Contains("null", StringComparison.OrdinalIgnoreCase))
            return MaterialIconKind.LockOpenVariant;

        if (category.Contains("default", StringComparison.OrdinalIgnoreCase))
            return MaterialIconKind.EqualBox;

        return MaterialIconKind.InformationOutline;
    }
}

public sealed partial class DdlSchemaCompareWorkspaceViewModel
{
    private readonly record struct EndpointDisplayInfo(
        string Connection,
        string Database,
        string Schema,
        string Table);

    private enum DifferenceQuickFilter
    {
        None,
        HighRisk,
        Destructive,
        Included,
        Pending,
        Ignored,
        Reviewed,
    }

    private DdlSchemaCompareWizardStep _wizardStep = DdlSchemaCompareWizardStep.Selection;
    private DdlSchemaCompareDifferenceItemViewModel? _selectedDifference;
    private string _differenceSearchText = string.Empty;
    private string _selectedCategoryFilter = "All";
    private string _selectedSeverityFilter = "All";
    private string _selectedActionFilter = "All";
    private string _selectedStatusFilter = "All";
    private string _reviewMessage = "Run a comparison to review differences.";
    private bool _advanceWithoutSelectionRequested;
    private bool _hasCompared;
    private string _lastCompareError = string.Empty;
    private DdlSchemaCompareSqlGenerationMode _sqlGenerationMode = DdlSchemaCompareSqlGenerationMode.Safe;
    private bool _includeTransaction = true;
    private bool _includeTryCatch = true;
    private bool _autoRollbackOnError = true;
    private bool _useIfExists = true;
    private bool _useCatalogChecks = true;
    private bool _commentDestructive = true;
    private bool _requireConfirmDropColumn = true;
    private bool _requireConfirmDropConstraint = true;
    private bool _requireConfirmHighRiskAlter = true;
    private bool _includeColumns = true;
    private bool _includeConstraints = true;
    private bool _includeIndexes = true;
    private bool _includeRelationships = true;
    private bool _includeExternalDependenciesAsComments = true;
    private bool _includeHeader = true;
    private bool _includeTimestamp = true;
    private bool _includeDiffSummary = true;
    private string _sqlPreviewSummary = "Configure and compare to generate SQL.";
    private bool _isTargetProductionLike;
    private string _directionImpactText = "Generate SQL to make TARGET match SOURCE.";
    private bool _sqlPreviewWrapLines;
    private bool _sqlPreviewShowComments = true;
    private string _displaySqlPreview = string.Empty;
    private DifferenceQuickFilter _activeQuickFilter = DifferenceQuickFilter.None;
    private bool _suppressQuickFilterReset;

    public RelayCommand SwapSourceTargetCommand { get; private set; } = null!;
    public RelayCommand RefreshMetadataCommand { get; private set; } = null!;
    public RelayCommand CompareAndContinueCommand { get; private set; } = null!;
    public RelayCommand CancelWizardCommand { get; private set; } = null!;
    public RelayCommand BackStepCommand { get; private set; } = null!;
    public RelayCommand AdvanceReviewStepCommand { get; private set; } = null!;
    public RelayCommand GeneratePreviewStepCommand { get; private set; } = null!;
    public RelayCommand SelectSafeDifferencesCommand { get; private set; } = null!;
    public RelayCommand SelectAllDifferencesCommand { get; private set; } = null!;
    public RelayCommand ClearDifferenceSelectionCommand { get; private set; } = null!;
    public RelayCommand IgnoreDestructiveDifferencesCommand { get; private set; } = null!;
    public RelayCommand ReviewHighRiskCommand { get; private set; } = null!;
    public RelayCommand IncludeSelectedDifferenceCommand { get; private set; } = null!;
    public RelayCommand IgnoreSelectedDifferenceCommand { get; private set; } = null!;
    public RelayCommand MarkSelectedDifferenceReviewedCommand { get; private set; } = null!;
    public RelayCommand CopySelectedSqlCommand { get; private set; } = null!;
    public RelayCommand ClosePreviewCommand { get; private set; } = null!;
    public RelayCommand<DdlSchemaCompareDifferenceItemViewModel> SelectDifferenceForInspectionCommand { get; private set; } = null!;
    public RelayCommand QuickFilterHighRiskCommand { get; private set; } = null!;
    public RelayCommand QuickFilterDestructiveCommand { get; private set; } = null!;
    public RelayCommand QuickFilterIncludedCommand { get; private set; } = null!;
    public RelayCommand QuickFilterPendingCommand { get; private set; } = null!;
    public RelayCommand QuickFilterIgnoredCommand { get; private set; } = null!;
    public RelayCommand QuickFilterReviewedCommand { get; private set; } = null!;
    public RelayCommand ClearQuickFilterCommand { get; private set; } = null!;

    public ObservableCollection<DdlSchemaCompareDifferenceItemViewModel> Differences { get; } = [];
    public ObservableCollection<DdlSchemaCompareDifferenceItemViewModel> FilteredDifferences { get; } = [];
    public ObservableCollection<string> CategoryFilterOptions { get; } = [];
    public ObservableCollection<string> SeverityFilterOptions { get; } = [];
    public ObservableCollection<string> ActionFilterOptions { get; } = [];
    public ObservableCollection<string> StatusFilterOptions { get; } = [];
    public ObservableCollection<string> WizardWarnings { get; } = [];
    public DdlSchemaCompareWizardStep WizardStep
    {
        get => _wizardStep;
        private set
        {
            if (!Set(ref _wizardStep, value))
                return;

            RaisePropertyChanged(nameof(IsSelectionStep));
            RaisePropertyChanged(nameof(IsReviewStep));
            RaisePropertyChanged(nameof(IsSqlOptionsStep));
            RaisePropertyChanged(nameof(IsSqlPreviewStep));
            RaisePropertyChanged(nameof(IsSelectionStepCurrent));
            RaisePropertyChanged(nameof(IsReviewStepCurrent));
            RaisePropertyChanged(nameof(IsSqlOptionsStepCurrent));
            RaisePropertyChanged(nameof(IsSqlPreviewStepCurrent));
            RaisePropertyChanged(nameof(IsSelectionStepCompleted));
            RaisePropertyChanged(nameof(IsReviewStepCompleted));
            RaisePropertyChanged(nameof(IsSqlOptionsStepCompleted));
            RaisePropertyChanged(nameof(IsSqlPreviewStepCompleted));
            RaisePropertyChanged(nameof(SelectionStepState));
            RaisePropertyChanged(nameof(ReviewStepState));
            RaisePropertyChanged(nameof(SqlOptionsStepState));
            RaisePropertyChanged(nameof(SqlPreviewStepState));
            RaisePropertyChanged(nameof(FooterContextText));
            BackStepCommand.NotifyCanExecuteChanged();
            AdvanceReviewStepCommand.NotifyCanExecuteChanged();
            GeneratePreviewStepCommand.NotifyCanExecuteChanged();
            ClosePreviewCommand?.NotifyCanExecuteChanged();
        }
    }

    public bool IsSelectionStep => WizardStep == DdlSchemaCompareWizardStep.Selection;
    public bool IsReviewStep => WizardStep == DdlSchemaCompareWizardStep.Review;
    public bool IsSqlOptionsStep => WizardStep == DdlSchemaCompareWizardStep.SqlOptions;
    public bool IsSqlPreviewStep => WizardStep == DdlSchemaCompareWizardStep.SqlPreview;

    public bool IsSelectionStepCurrent => WizardStep == DdlSchemaCompareWizardStep.Selection;
    public bool IsReviewStepCurrent => WizardStep == DdlSchemaCompareWizardStep.Review;
    public bool IsSqlOptionsStepCurrent => WizardStep == DdlSchemaCompareWizardStep.SqlOptions;
    public bool IsSqlPreviewStepCurrent => WizardStep == DdlSchemaCompareWizardStep.SqlPreview;

    public bool IsSelectionStepCompleted => (int)WizardStep > (int)DdlSchemaCompareWizardStep.Selection;
    public bool IsReviewStepCompleted => (int)WizardStep > (int)DdlSchemaCompareWizardStep.Review;
    public bool IsSqlOptionsStepCompleted => (int)WizardStep > (int)DdlSchemaCompareWizardStep.SqlOptions;
    public bool IsSqlPreviewStepCompleted => (int)WizardStep > (int)DdlSchemaCompareWizardStep.SqlPreview;

    public string SelectionStepState => ResolveStepState(DdlSchemaCompareWizardStep.Selection);
    public string ReviewStepState => ResolveStepState(DdlSchemaCompareWizardStep.Review);
    public string SqlOptionsStepState => ResolveStepState(DdlSchemaCompareWizardStep.SqlOptions);
    public string SqlPreviewStepState => ResolveStepState(DdlSchemaCompareWizardStep.SqlPreview);
    public string SelectionStepDescription => L("ddl.compare.step.selectionDesc", "Source and target");
    public string ReviewStepDescription => L("ddl.compare.step.reviewDesc", "Found differences");
    public string SqlOptionsStepDescription => L("ddl.compare.step.sqlOptionsDesc", "Script safety");
    public string SqlPreviewStepDescription => L("ddl.compare.step.previewDesc", "Final script");

    public DdlSchemaCompareDifferenceItemViewModel? SelectedDifference
    {
        get => _selectedDifference;
        set
        {
            if (!Set(ref _selectedDifference, value))
                return;

            UpdateDifferenceInspectionSelection();

            IncludeSelectedDifferenceCommand.NotifyCanExecuteChanged();
            IgnoreSelectedDifferenceCommand.NotifyCanExecuteChanged();
            MarkSelectedDifferenceReviewedCommand.NotifyCanExecuteChanged();
            CopySelectedSqlCommand.NotifyCanExecuteChanged();
            RaisePropertyChanged(nameof(HasSelectedDifference));
        }
    }

    public bool HasSelectedDifference => SelectedDifference is not null;

    public string DifferenceSearchText
    {
        get => _differenceSearchText;
        set
        {
            if (!Set(ref _differenceSearchText, value ?? string.Empty))
                return;

            if (!_suppressQuickFilterReset)
                ClearQuickFilterState();
            ApplyDifferenceFilters();
        }
    }

    public string SelectedCategoryFilter
    {
        get => _selectedCategoryFilter;
        set
        {
            if (!Set(ref _selectedCategoryFilter, value ?? FilterOptionAll))
                return;

            if (!_suppressQuickFilterReset)
                ClearQuickFilterState();
            ApplyDifferenceFilters();
        }
    }

    public string SelectedSeverityFilter
    {
        get => _selectedSeverityFilter;
        set
        {
            if (!Set(ref _selectedSeverityFilter, value ?? FilterOptionAll))
                return;

            if (!_suppressQuickFilterReset)
                ClearQuickFilterState();
            ApplyDifferenceFilters();
        }
    }

    public string SelectedActionFilter
    {
        get => _selectedActionFilter;
        set
        {
            if (!Set(ref _selectedActionFilter, value ?? FilterOptionAll))
                return;

            if (!_suppressQuickFilterReset)
                ClearQuickFilterState();
            ApplyDifferenceFilters();
        }
    }

    public string SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set
        {
            if (!Set(ref _selectedStatusFilter, value ?? FilterOptionAll))
                return;

            if (!_suppressQuickFilterReset)
                ClearQuickFilterState();
            ApplyDifferenceFilters();
        }
    }

    public string ReviewMessage
    {
        get => _reviewMessage;
        private set => Set(ref _reviewMessage, value);
    }

    public bool HasCompared
    {
        get => _hasCompared;
        private set => Set(ref _hasCompared, value);
    }

    public string LastCompareError
    {
        get => _lastCompareError;
        private set => Set(ref _lastCompareError, value);
    }

    public bool HasCompareError => !string.IsNullOrWhiteSpace(LastCompareError);
    public bool HasDifferences => Differences.Count > 0;
    public bool HasFilteredDifferences => FilteredDifferences.Count > 0;
    public bool HasIncludedDifferences => Differences.Any(d => d.IsIncluded);

    public int TotalDifferenceCount => Differences.Count;
    public int HighRiskCount => Differences.Count(d => d.Severity == DdlSchemaCompareDiffSeverity.High);
    public int MediumRiskCount => Differences.Count(d => d.Severity == DdlSchemaCompareDiffSeverity.Medium);
    public int LowRiskCount => Differences.Count(d => d.Severity == DdlSchemaCompareDiffSeverity.Low);
    public int InformativeCount => Differences.Count(d => d.Severity == DdlSchemaCompareDiffSeverity.Info);
    public int DestructiveCount => Differences.Count(d => d.IsDestructive);
    public int IncludedCount => Differences.Count(d => d.IsIncluded);
    public int IgnoredCount => Differences.Count(d => !d.IsIncluded);
    public int ReviewedCount => Differences.Count(d => d.ReviewStatus == DdlSchemaCompareDiffReviewStatus.Reviewed);
    public int PendingCount => Differences.Count(d => d.ReviewStatus == DdlSchemaCompareDiffReviewStatus.Pending);
    public int ExecutableDestructiveCount => Differences.Count(d => d.IsIncluded && d.IsDestructive && !CommentDestructiveOperations);
    public int CommentedDestructiveCount => Differences.Count(d => d.IsIncluded && d.IsDestructive && CommentDestructiveOperations);
    public bool HasExecutableDestructive => ExecutableDestructiveCount > 0;
    public bool HasIncludedDestructive => Differences.Any(d => d.IsIncluded && d.IsDestructive);

    public bool IsQuickFilterHighRiskActive => _activeQuickFilter == DifferenceQuickFilter.HighRisk;
    public bool IsQuickFilterDestructiveActive => _activeQuickFilter == DifferenceQuickFilter.Destructive;
    public bool IsQuickFilterIncludedActive => _activeQuickFilter == DifferenceQuickFilter.Included;
    public bool IsQuickFilterPendingActive => _activeQuickFilter == DifferenceQuickFilter.Pending;
    public bool IsQuickFilterIgnoredActive => _activeQuickFilter == DifferenceQuickFilter.Ignored;
    public bool IsQuickFilterReviewedActive => _activeQuickFilter == DifferenceQuickFilter.Reviewed;

    public DdlSchemaCompareSqlGenerationMode SqlGenerationMode
    {
        get => _sqlGenerationMode;
        set
        {
            if (!Set(ref _sqlGenerationMode, value))
                return;

            if (value == DdlSchemaCompareSqlGenerationMode.Safe)
                CommentDestructiveOperations = true;

            RaisePropertyChanged(nameof(SqlGenerationModeIndex));
            RebuildGeneratedSqlFromWizard();
            RaisePropertyChanged(nameof(GenerationConfigSummary));
        }
    }

    public int SqlGenerationModeIndex
    {
        get => (int)SqlGenerationMode;
        set
        {
            int normalized = Math.Clamp(value, 0, Enum.GetValues<DdlSchemaCompareSqlGenerationMode>().Length - 1);
            SqlGenerationMode = (DdlSchemaCompareSqlGenerationMode)normalized;
        }
    }

    public bool IncludeTransaction
    {
        get => _includeTransaction;
        set
        {
            if (!Set(ref _includeTransaction, value))
                return;
            RebuildGeneratedSqlFromWizard();
            RaisePropertyChanged(nameof(GenerationConfigSummary));
        }
    }

    public bool IncludeTryCatch
    {
        get => _includeTryCatch;
        set
        {
            if (!Set(ref _includeTryCatch, value))
                return;
            RebuildGeneratedSqlFromWizard();
            RaisePropertyChanged(nameof(GenerationConfigSummary));
        }
    }

    public bool AutoRollbackOnError
    {
        get => _autoRollbackOnError;
        set
        {
            if (!Set(ref _autoRollbackOnError, value))
                return;
            RebuildGeneratedSqlFromWizard();
            RaisePropertyChanged(nameof(GenerationConfigSummary));
        }
    }

    public bool UseIfExistsChecks
    {
        get => _useIfExists;
        set
        {
            if (!Set(ref _useIfExists, value))
                return;
            RecomputeSqlForCurrentOptions();
            RebuildGeneratedSqlFromWizard();
        }
    }

    public bool UseCatalogChecks
    {
        get => _useCatalogChecks;
        set
        {
            if (!Set(ref _useCatalogChecks, value))
                return;
            RecomputeSqlForCurrentOptions();
            RebuildGeneratedSqlFromWizard();
        }
    }
    public bool CommentDestructiveOperations
    {
        get => _commentDestructive;
        set
        {
            if (!Set(ref _commentDestructive, value))
                return;
            RebuildGeneratedSqlFromWizard();
            RaisePropertyChanged(nameof(GenerationConfigSummary));
        }
    }

    public bool RequireConfirmDropColumn
    {
        get => _requireConfirmDropColumn;
        set => Set(ref _requireConfirmDropColumn, value);
    }

    public bool RequireConfirmDropConstraint
    {
        get => _requireConfirmDropConstraint;
        set => Set(ref _requireConfirmDropConstraint, value);
    }

    public bool RequireConfirmHighRiskAlter
    {
        get => _requireConfirmHighRiskAlter;
        set => Set(ref _requireConfirmHighRiskAlter, value);
    }

    public bool IncludeColumns
    {
        get => _includeColumns;
        set
        {
            if (!Set(ref _includeColumns, value))
                return;
            RebuildGeneratedSqlFromWizard();
        }
    }

    public bool IncludeConstraints
    {
        get => _includeConstraints;
        set
        {
            if (!Set(ref _includeConstraints, value))
                return;
            RebuildGeneratedSqlFromWizard();
        }
    }

    public bool IncludeIndexes
    {
        get => _includeIndexes;
        set
        {
            if (!Set(ref _includeIndexes, value))
                return;
            RebuildGeneratedSqlFromWizard();
        }
    }

    public bool IncludeRelationships
    {
        get => _includeRelationships;
        set
        {
            if (!Set(ref _includeRelationships, value))
                return;
            RebuildGeneratedSqlFromWizard();
        }
    }

    public bool IncludeExternalDependenciesAsComments
    {
        get => _includeExternalDependenciesAsComments;
        set
        {
            if (!Set(ref _includeExternalDependenciesAsComments, value))
                return;
            RebuildGeneratedSqlFromWizard();
        }
    }

    public bool IncludeHeader
    {
        get => _includeHeader;
        set
        {
            if (!Set(ref _includeHeader, value))
                return;
            RebuildGeneratedSqlFromWizard();
        }
    }

    public bool IncludeTimestamp
    {
        get => _includeTimestamp;
        set
        {
            if (!Set(ref _includeTimestamp, value))
                return;
            RebuildGeneratedSqlFromWizard();
        }
    }

    public bool IncludeDiffSummary
    {
        get => _includeDiffSummary;
        set
        {
            if (!Set(ref _includeDiffSummary, value))
                return;
            RebuildGeneratedSqlFromWizard();
        }
    }

    public string SqlPreviewSummary
    {
        get => _sqlPreviewSummary;
        private set => Set(ref _sqlPreviewSummary, value);
    }

    public bool SqlPreviewWrapLines
    {
        get => _sqlPreviewWrapLines;
        set
        {
            if (!Set(ref _sqlPreviewWrapLines, value))
                return;
            RaisePropertyChanged(nameof(SqlPreviewWrapMode));
        }
    }

    public bool SqlPreviewShowComments
    {
        get => _sqlPreviewShowComments;
        set
        {
            if (!Set(ref _sqlPreviewShowComments, value))
                return;
            UpdateDisplaySqlPreview();
        }
    }

    public string DisplaySqlPreview
    {
        get => _displaySqlPreview;
        private set => Set(ref _displaySqlPreview, value);
    }

    public TextWrapping SqlPreviewWrapMode => SqlPreviewWrapLines ? TextWrapping.Wrap : TextWrapping.NoWrap;

    public bool IsTargetProductionLike
    {
        get => _isTargetProductionLike;
        private set => Set(ref _isTargetProductionLike, value);
    }

    private string FilterOptionAll => L("common.all", "All");
    private string FilterStatusIncluded => L("ddl.compare.status.included", "Included");
    private string FilterStatusIgnored => L("ddl.compare.status.ignored", "Ignored");
    private string FilterStatusReviewed => L("ddl.compare.status.reviewed", "Reviewed");
    private string FilterStatusPending => L("ddl.compare.status.pending", "Pending");

    public string TargetProductionWarning =>
        L("ddl.compare.warning.productionLike", "The target appears to be a production environment. Review carefully before generating destructive scripts.");

    public string DirectionImpactText
    {
        get => _directionImpactText;
        private set => Set(ref _directionImpactText, value);
    }

    public string SourceEndpointLabel => ResolveSourceLabel();
    public string TargetEndpointLabel => ResolveTargetLabel();
    public string SourceConnectionName => ResolveSourceEndpointInfo().Connection;
    public string SourceDatabaseName => ResolveSourceEndpointInfo().Database;
    public string SourceSchemaName => ResolveSourceEndpointInfo().Schema;
    public string SourceTableName => ResolveSourceEndpointInfo().Table;
    public string SourceObjectPath => $"{SourceDatabaseName} / {SourceSchemaName} / {SourceTableName}";
    public string TargetConnectionName => ResolveTargetEndpointInfo().Connection;
    public string TargetDatabaseName => ResolveTargetEndpointInfo().Database;
    public string TargetSchemaName => ResolveTargetEndpointInfo().Schema;
    public string TargetTableName => ResolveTargetEndpointInfo().Table;
    public string TargetObjectPath => $"{TargetDatabaseName} / {TargetSchemaName} / {TargetTableName}";
    public string DirectionFlowSummary => "Origem -> Destino";
    public string DirectionCenterSummaryText => "O SQL sera gerado para alterar o destino e deixa-lo igual a origem.";
    public string DirectionTargetBadgeText => "Destino sera alterado";
    public string DirectionProductionBadgeText => "Producao detectada";
    public string DirectionArrowSummary =>
        SelectedDirection == DdlSchemaCompareDirection.LeftToRight
            ? $"{LeftSelectedProfile?.Name ?? "-"} -> {RightSelectedProfile?.Name ?? "-"}"
            : $"{RightSelectedProfile?.Name ?? "-"} -> {LeftSelectedProfile?.Name ?? "-"}";

    public string GenerationConfigSummary =>
        string.Format(
            L("ddl.compare.generation.summary", "Current configuration: {0} differences included; {1} destructive operations commented; transaction {2}; TRY/CATCH {3}; IF EXISTS {4}."),
            IncludedCount,
            CommentedDestructiveCount,
            IncludeTransaction ? L("common.enabled", "enabled") : L("common.disabled", "disabled"),
            IncludeTryCatch ? L("common.enabled", "enabled") : L("common.disabled", "disabled"),
            UseIfExistsChecks ? L("common.enabled", "enabled") : L("common.disabled", "disabled"));

    public string EmptyStateText =>
        !HasCompared
            ? L("ddl.compare.empty.notCompared", "Configure source and target to start the comparison.")
            : HasDifferences
                ? L("ddl.compare.empty.noFilteredDifferences", "Nenhuma diferenca encontrada para os filtros atuais.")
                : L("ddl.compare.empty.noDifferences", "As tabelas estao sincronizadas. Nenhuma alteracao e necessaria.");

    public string NoSelectionWarningText => L("ddl.compare.empty.noSelectedDifferences", "No differences selected for SQL generation.");

    public string DetailEmptyStateText => "Selecione uma diferenca para ver detalhes, riscos e SQL sugerido.";

    public string FooterDestructiveWarningText => "Atencao: ha operacoes destrutivas incluidas no SQL.";

    public bool CanRunComparison => !IsBusy && !IsCompatibilityBlocked;

    public bool CanAdvanceFromReview => HasIncludedDifferences || _advanceWithoutSelectionRequested;

    public string FooterContextText => WizardStep switch
    {
        DdlSchemaCompareWizardStep.Selection => string.Format(
            L("ddl.compare.footer.context.selection", "Configuracao da comparacao - {0}"),
            IsCompatibilityBlocked ? L("ddl.compare.footer.context.pendingMetadata", "metadata pending") : L("ddl.compare.footer.context.readyToCompare", "ready to compare")),
        DdlSchemaCompareWizardStep.Review => string.Format(
            L("ddl.compare.footer.context.review", "{0} incluidas de {1} - {2} alto risco - {3} destrutivas ignoradas"),
            IncludedCount,
            TotalDifferenceCount,
            HighRiskCount,
            Differences.Count(d => d.IsDestructive && !d.IsIncluded)),
        DdlSchemaCompareWizardStep.SqlOptions => string.Format(
            L("ddl.compare.footer.context.options", "Modo {0} - {1} incluidas - transacao {2}"),
            ResolveModeLabel(SqlGenerationMode),
            IncludedCount,
            IncludeTransaction ? L("common.enabled", "enabled") : L("common.disabled", "disabled")),
        DdlSchemaCompareWizardStep.SqlPreview => string.Format(
            L("ddl.compare.footer.context.preview", "SQL gerado - {0} destrutivas executaveis"),
            ExecutableDestructiveCount),
        _ => string.Empty,
    };

    private void InitializeWizardState()
    {
        ReviewMessage = L("ddl.compare.review.startHint", "Run a comparison to review differences.");
        SqlPreviewSummary = L("ddl.compare.preview.startHint", "Configure and compare to generate SQL.");
        DirectionImpactText = L("ddl.compare.direction.defaultImpact", "Generate SQL to make TARGET match SOURCE.");
        SelectedCategoryFilter = FilterOptionAll;
        SelectedSeverityFilter = FilterOptionAll;
        SelectedActionFilter = FilterOptionAll;
        SelectedStatusFilter = FilterOptionAll;

        SwapSourceTargetCommand = new RelayCommand(SwapSourceAndTarget, () => !IsBusy);
        RefreshMetadataCommand = new RelayCommand(() => _ = RefreshBothAsync(), () => !IsBusy);
        CompareAndContinueCommand = new RelayCommand(() => _ = CompareAsync(), () => CanRunComparison);
        CancelWizardCommand = new RelayCommand(CancelWizard, () => !IsBusy);
        BackStepCommand = new RelayCommand(BackStep, () => WizardStep != DdlSchemaCompareWizardStep.Selection);
        AdvanceReviewStepCommand = new RelayCommand(AdvanceFromReviewStep, () => WizardStep == DdlSchemaCompareWizardStep.Review);
        GeneratePreviewStepCommand = new RelayCommand(AdvanceFromOptionsStep, () => WizardStep == DdlSchemaCompareWizardStep.SqlOptions);
        SelectSafeDifferencesCommand = new RelayCommand(SelectSafeDifferences, () => Differences.Count > 0);
        SelectAllDifferencesCommand = new RelayCommand(() => SetAllDifferencesSelection(true), () => Differences.Count > 0);
        ClearDifferenceSelectionCommand = new RelayCommand(() => SetAllDifferencesSelection(false), () => Differences.Count > 0);
        IgnoreDestructiveDifferencesCommand = new RelayCommand(IgnoreDestructiveDifferences, () => Differences.Count > 0);
        ReviewHighRiskCommand = new RelayCommand(ReviewHighRiskDifferences, () => HighRiskCount > 0);
        IncludeSelectedDifferenceCommand = new RelayCommand(() => SetSelectedDifferenceInclusion(true), () => SelectedDifference is not null);
        IgnoreSelectedDifferenceCommand = new RelayCommand(() => SetSelectedDifferenceInclusion(false), () => SelectedDifference is not null);
        MarkSelectedDifferenceReviewedCommand = new RelayCommand(MarkSelectedDifferenceReviewed, () => SelectedDifference is not null);
        CopySelectedSqlCommand = new RelayCommand(
            () =>
            {
                if (SelectedDifference is not null)
                    CopySqlRequested?.Invoke(SelectedDifference.SuggestedSql);
            },
            () => SelectedDifference is not null);
        ClosePreviewCommand = new RelayCommand(ClosePreview, () => WizardStep == DdlSchemaCompareWizardStep.SqlPreview);
        SelectDifferenceForInspectionCommand = new RelayCommand<DdlSchemaCompareDifferenceItemViewModel>(
            diff =>
            {
                if (diff is not null)
                    SelectedDifference = diff;
            },
            diff => diff is not null);

        QuickFilterHighRiskCommand = new RelayCommand(() => ApplyQuickFilter(DifferenceQuickFilter.HighRisk), () => Differences.Count > 0);
        QuickFilterDestructiveCommand = new RelayCommand(() => ApplyQuickFilter(DifferenceQuickFilter.Destructive), () => Differences.Count > 0);
        QuickFilterIncludedCommand = new RelayCommand(() => ApplyQuickFilter(DifferenceQuickFilter.Included), () => Differences.Count > 0);
        QuickFilterPendingCommand = new RelayCommand(() => ApplyQuickFilter(DifferenceQuickFilter.Pending), () => Differences.Count > 0);
        QuickFilterIgnoredCommand = new RelayCommand(() => ApplyQuickFilter(DifferenceQuickFilter.Ignored), () => Differences.Count > 0);
        QuickFilterReviewedCommand = new RelayCommand(() => ApplyQuickFilter(DifferenceQuickFilter.Reviewed), () => Differences.Count > 0);
        ClearQuickFilterCommand = new RelayCommand(() => ApplyQuickFilter(DifferenceQuickFilter.None), () => Differences.Count > 0);

        CategoryFilterOptions.Clear();
        SeverityFilterOptions.Clear();
        ActionFilterOptions.Clear();
        StatusFilterOptions.Clear();

        CategoryFilterOptions.Add(FilterOptionAll);
        SeverityFilterOptions.Add(FilterOptionAll);
        ActionFilterOptions.Add(FilterOptionAll);
        StatusFilterOptions.Add(FilterOptionAll);
        StatusFilterOptions.Add(FilterStatusIncluded);
        StatusFilterOptions.Add(FilterStatusIgnored);
        StatusFilterOptions.Add(FilterStatusReviewed);
        StatusFilterOptions.Add(FilterStatusPending);

        UpdateSelectionStepState();
    }

    private void SwapSourceAndTarget()
    {
        (LeftSelectedProfile, RightSelectedProfile) = (RightSelectedProfile, LeftSelectedProfile);
        (LeftSelectedDatabase, RightSelectedDatabase) = (RightSelectedDatabase, LeftSelectedDatabase);
        (LeftSelectedSchema, RightSelectedSchema) = (RightSelectedSchema, LeftSelectedSchema);
        (LeftSelectedTable, RightSelectedTable) = (RightSelectedTable, LeftSelectedTable);
        SelectedDirection = SelectedDirection == DdlSchemaCompareDirection.LeftToRight
            ? DdlSchemaCompareDirection.RightToLeft
            : DdlSchemaCompareDirection.LeftToRight;
        UpdateSelectionStepState();
    }

    private void CancelWizard()
    {
        _advanceWithoutSelectionRequested = false;
        WizardStep = DdlSchemaCompareWizardStep.Selection;
    }

    private void ClosePreview()
    {
        WizardStep = DdlSchemaCompareWizardStep.Selection;
    }

    private void BackStep()
    {
        if (WizardStep == DdlSchemaCompareWizardStep.SqlPreview)
        {
            WizardStep = DdlSchemaCompareWizardStep.SqlOptions;
            return;
        }

        if (WizardStep == DdlSchemaCompareWizardStep.SqlOptions)
        {
            WizardStep = DdlSchemaCompareWizardStep.Review;
            return;
        }

        WizardStep = DdlSchemaCompareWizardStep.Selection;
    }

    private void AdvanceFromReviewStep()
    {
        if (!HasIncludedDifferences)
        {
            if (!_advanceWithoutSelectionRequested)
            {
                _advanceWithoutSelectionRequested = true;
                ReviewMessage = L("ddl.compare.review.advanceWithoutSelectionHint", "No differences selected. Click Next again to continue without generating changes.");
                return;
            }
        }

        _advanceWithoutSelectionRequested = false;
        WizardStep = DdlSchemaCompareWizardStep.SqlOptions;
    }

    private void AdvanceFromOptionsStep()
    {
        RebuildGeneratedSqlFromWizard();
        WizardStep = DdlSchemaCompareWizardStep.SqlPreview;
    }
    private void SelectSafeDifferences()
    {
        foreach (DdlSchemaCompareDifferenceItemViewModel diff in Differences)
            diff.IsIncluded = !diff.IsDestructive;

        RecomputeDiffSummary();
    }

    private void IgnoreDestructiveDifferences()
    {
        foreach (DdlSchemaCompareDifferenceItemViewModel diff in Differences.Where(static d => d.IsDestructive))
            diff.IsIncluded = false;

        RecomputeDiffSummary();
    }

    private void ReviewHighRiskDifferences()
    {
        DdlSchemaCompareDifferenceItemViewModel? firstHighRisk = FilteredDifferences.FirstOrDefault(
            static d => d.Severity == DdlSchemaCompareDiffSeverity.High);
        if (firstHighRisk is not null)
            SelectedDifference = firstHighRisk;
    }

    private void SetSelectedDifferenceInclusion(bool include)
    {
        if (SelectedDifference is null)
            return;

        SelectedDifference.IsIncluded = include;
        if (SelectedDifference.ReviewStatus == DdlSchemaCompareDiffReviewStatus.Pending)
            SelectedDifference.ReviewStatus = include
                ? DdlSchemaCompareDiffReviewStatus.Included
                : DdlSchemaCompareDiffReviewStatus.Ignored;
        RecomputeDiffSummary();
    }

    private void MarkSelectedDifferenceReviewed()
    {
        if (SelectedDifference is null)
            return;

        SelectedDifference.ReviewStatus = DdlSchemaCompareDiffReviewStatus.Reviewed;
        RecomputeDiffSummary();
    }

    private void SetAllDifferencesSelection(bool included)
    {
        foreach (DdlSchemaCompareDifferenceItemViewModel diff in Differences)
            diff.IsIncluded = included;

        RecomputeDiffSummary();
    }

    private void OnDifferenceChanged()
    {
        RecomputeDiffSummary();
    }

    private void RecomputeDiffSummary()
    {
        RaisePropertyChanged(nameof(HasDifferences));
        RaisePropertyChanged(nameof(HasFilteredDifferences));
        RaisePropertyChanged(nameof(HasIncludedDifferences));
        RaisePropertyChanged(nameof(TotalDifferenceCount));
        RaisePropertyChanged(nameof(HighRiskCount));
        RaisePropertyChanged(nameof(MediumRiskCount));
        RaisePropertyChanged(nameof(LowRiskCount));
        RaisePropertyChanged(nameof(InformativeCount));
        RaisePropertyChanged(nameof(DestructiveCount));
        RaisePropertyChanged(nameof(IncludedCount));
        RaisePropertyChanged(nameof(IgnoredCount));
        RaisePropertyChanged(nameof(ReviewedCount));
        RaisePropertyChanged(nameof(PendingCount));
        RaisePropertyChanged(nameof(ExecutableDestructiveCount));
        RaisePropertyChanged(nameof(CommentedDestructiveCount));
        RaisePropertyChanged(nameof(HasExecutableDestructive));
        RaisePropertyChanged(nameof(HasIncludedDestructive));
        RaisePropertyChanged(nameof(EmptyStateText));
        RaisePropertyChanged(nameof(GenerationConfigSummary));
        RaisePropertyChanged(nameof(FooterContextText));

        SelectSafeDifferencesCommand.NotifyCanExecuteChanged();
        SelectAllDifferencesCommand.NotifyCanExecuteChanged();
        ClearDifferenceSelectionCommand.NotifyCanExecuteChanged();
        IgnoreDestructiveDifferencesCommand.NotifyCanExecuteChanged();
        ReviewHighRiskCommand.NotifyCanExecuteChanged();
        AdvanceReviewStepCommand.NotifyCanExecuteChanged();
        GeneratePreviewStepCommand.NotifyCanExecuteChanged();
        BackStepCommand.NotifyCanExecuteChanged();
        QuickFilterHighRiskCommand.NotifyCanExecuteChanged();
        QuickFilterDestructiveCommand.NotifyCanExecuteChanged();
        QuickFilterIncludedCommand.NotifyCanExecuteChanged();
        QuickFilterPendingCommand.NotifyCanExecuteChanged();
        QuickFilterIgnoredCommand.NotifyCanExecuteChanged();
        QuickFilterReviewedCommand.NotifyCanExecuteChanged();
        ClearQuickFilterCommand.NotifyCanExecuteChanged();
    }

    private void ApplyDifferenceFilters()
    {
        IEnumerable<DdlSchemaCompareDifferenceItemViewModel> query = Differences;
        if (!string.IsNullOrWhiteSpace(DifferenceSearchText))
        {
            string search = DifferenceSearchText.Trim();
            query = query.Where(diff =>
                diff.Item.Contains(search, StringComparison.OrdinalIgnoreCase)
                || diff.Category.Contains(search, StringComparison.OrdinalIgnoreCase)
                || diff.SourceValue.Contains(search, StringComparison.OrdinalIgnoreCase)
                || diff.TargetValue.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.Equals(SelectedCategoryFilter, FilterOptionAll, StringComparison.OrdinalIgnoreCase))
            query = query.Where(diff => string.Equals(diff.Category, SelectedCategoryFilter, StringComparison.OrdinalIgnoreCase));

        if (!string.Equals(SelectedSeverityFilter, FilterOptionAll, StringComparison.OrdinalIgnoreCase))
            query = query.Where(diff => string.Equals(diff.SeverityLabel, SelectedSeverityFilter, StringComparison.OrdinalIgnoreCase));

        if (!string.Equals(SelectedActionFilter, FilterOptionAll, StringComparison.OrdinalIgnoreCase))
            query = query.Where(diff => string.Equals(diff.SuggestedAction, SelectedActionFilter, StringComparison.OrdinalIgnoreCase));

        if (!string.Equals(SelectedStatusFilter, FilterOptionAll, StringComparison.OrdinalIgnoreCase))
            query = query.Where(diff => string.Equals(diff.StatusLabel, SelectedStatusFilter, StringComparison.OrdinalIgnoreCase));

        FilteredDifferences.Clear();
        foreach (DdlSchemaCompareDifferenceItemViewModel diff in query)
            FilteredDifferences.Add(diff);

        if (SelectedDifference is not null && !FilteredDifferences.Contains(SelectedDifference))
            SelectedDifference = FilteredDifferences.FirstOrDefault();
        else if (SelectedDifference is null && FilteredDifferences.Count > 0)
            SelectedDifference = FilteredDifferences[0];

        RaisePropertyChanged(nameof(HasFilteredDifferences));
        RaisePropertyChanged(nameof(EmptyStateText));
    }

    private void ApplyQuickFilter(DifferenceQuickFilter filter)
    {
        _activeQuickFilter = filter;
        RaiseQuickFilterStateChanged();

        _suppressQuickFilterReset = true;
        SelectedSeverityFilter = FilterOptionAll;
        SelectedStatusFilter = FilterOptionAll;
        DifferenceSearchText = string.Empty;
        _suppressQuickFilterReset = false;

        switch (filter)
        {
            case DifferenceQuickFilter.HighRisk:
                _suppressQuickFilterReset = true;
                SelectedSeverityFilter = Differences
                    .Select(static diff => diff.SeverityLabel)
                    .FirstOrDefault(label => label.Contains("high", StringComparison.OrdinalIgnoreCase)
                                             || label.Contains("alto", StringComparison.OrdinalIgnoreCase))
                    ?? FilterOptionAll;
                _suppressQuickFilterReset = false;
                break;
            case DifferenceQuickFilter.Included:
                _suppressQuickFilterReset = true;
                SelectedStatusFilter = FilterStatusIncluded;
                _suppressQuickFilterReset = false;
                break;
            case DifferenceQuickFilter.Pending:
                _suppressQuickFilterReset = true;
                SelectedStatusFilter = FilterStatusPending;
                _suppressQuickFilterReset = false;
                break;
            case DifferenceQuickFilter.Ignored:
                _suppressQuickFilterReset = true;
                SelectedStatusFilter = FilterStatusIgnored;
                _suppressQuickFilterReset = false;
                break;
            case DifferenceQuickFilter.Reviewed:
                _suppressQuickFilterReset = true;
                SelectedStatusFilter = FilterStatusReviewed;
                _suppressQuickFilterReset = false;
                break;
            case DifferenceQuickFilter.Destructive:
                ApplyDifferenceFilters();
                FilteredDifferences.Clear();
                foreach (DdlSchemaCompareDifferenceItemViewModel diff in Differences.Where(static d => d.IsDestructive))
                    FilteredDifferences.Add(diff);
                if (SelectedDifference is not null && !FilteredDifferences.Contains(SelectedDifference))
                    SelectedDifference = FilteredDifferences.FirstOrDefault();
                RaisePropertyChanged(nameof(HasFilteredDifferences));
                RaisePropertyChanged(nameof(EmptyStateText));
                return;
            case DifferenceQuickFilter.None:
            default:
                break;
        }

        ApplyDifferenceFilters();
    }

    private void RaiseQuickFilterStateChanged()
    {
        RaisePropertyChanged(nameof(IsQuickFilterHighRiskActive));
        RaisePropertyChanged(nameof(IsQuickFilterDestructiveActive));
        RaisePropertyChanged(nameof(IsQuickFilterIncludedActive));
        RaisePropertyChanged(nameof(IsQuickFilterPendingActive));
        RaisePropertyChanged(nameof(IsQuickFilterIgnoredActive));
        RaisePropertyChanged(nameof(IsQuickFilterReviewedActive));
    }

    private void ClearQuickFilterState()
    {
        if (_activeQuickFilter == DifferenceQuickFilter.None)
            return;

        _activeQuickFilter = DifferenceQuickFilter.None;
        RaiseQuickFilterStateChanged();
    }

    private void UpdateDifferenceInspectionSelection()
    {
        foreach (DdlSchemaCompareDifferenceItemViewModel diff in Differences)
            diff.IsSelectedForInspection = ReferenceEquals(diff, SelectedDifference);
    }

    private void BuildWizardDifferencesFromDiff()
    {
        foreach (DdlSchemaCompareDifferenceItemViewModel existing in Differences)
            existing.Changed -= OnDifferenceChanged;

        Differences.Clear();
        WizardWarnings.Clear();

        bool databaseWide = IsDatabaseWideComparison;
        bool qualifyByTable = IsSchemaWideComparison || databaseWide;
        foreach (FlatDifference flat in _flatDifferences)
        {
            SchemaDifference difference = flat.Difference;
            DdlSchemaCompareDiffSeverity severity = MapSeverity(difference.Severity);
            string action = ResolveActionLabel(difference);

            // In whole-schema / whole-database mode, qualify each item with its table so the
            // (potentially cross-schema) list stays unambiguous.
            string tablePrefix = databaseWide ? $"{flat.TargetSchema}.{flat.TargetTable}" : flat.TargetTable;
            string item_name = qualifyByTable && difference.Category != SchemaDiffCategory.Table
                ? $"{tablePrefix}.{difference.ObjectName}"
                : difference.ObjectName;

            var item = new DdlSchemaCompareDifferenceItemViewModel
            {
                Id = difference.Id,
                Category = ResolveCategoryLabel(difference),
                Item = item_name,
                SourceValue = difference.SourceDescription,
                TargetValue = difference.TargetDescription,
                SuggestedAction = action,
                Severity = severity,
                IsDestructive = difference.IsDestructive,
                SuggestedSql = GenerateSqlForDifference(difference, flat.TargetSchema, flat.TargetTable),
                RiskSummary = BuildRiskSummary(action, severity, difference.IsDestructive),
                Notes = ResolveContentGroup(difference.Category),
                EngineDifference = difference,
                TargetSchema = flat.TargetSchema,
                TargetTable = flat.TargetTable,
            };
            item.IsIncluded = !difference.IsDestructive && severity != DdlSchemaCompareDiffSeverity.High;
            item.ReviewStatus = DdlSchemaCompareDiffReviewStatus.Pending;
            item.Changed += OnDifferenceChanged;
            Differences.Add(item);
        }

        foreach (string warning in CompareWarnings)
            WizardWarnings.Add(warning);

        _activeQuickFilter = DifferenceQuickFilter.None;
        RaiseQuickFilterStateChanged();
        RefreshFilterOptionsFromDifferences();
        ApplyDifferenceFilters();
        RecomputeDiffSummary();
    }

    private string GenerateSqlForDifference(SchemaDifference difference, string targetSchema, string targetTable)
    {
        string sql = _generator.Generate(difference, _compareProvider, targetSchema, targetTable, BuildSqlOptions());
        return string.IsNullOrWhiteSpace(sql) ? BuildCommentFallback(difference) : sql;
    }

    private static DdlSchemaCompareDiffSeverity MapSeverity(SchemaDiffSeverity severity) => severity switch
    {
        SchemaDiffSeverity.High => DdlSchemaCompareDiffSeverity.High,
        SchemaDiffSeverity.Medium => DdlSchemaCompareDiffSeverity.Medium,
        SchemaDiffSeverity.Low => DdlSchemaCompareDiffSeverity.Low,
        _ => DdlSchemaCompareDiffSeverity.Info,
    };

    private static string ResolveCategoryLabel(SchemaDifference difference) => difference.Operation switch
    {
        CreateTableOperation => "Tabela ausente",
        DropTableOperation => "Tabela extra",
        AddColumnOperation => "Coluna ausente",
        DropColumnOperation => "Coluna extra",
        AlterColumnOperation => "Tipo",
        SetColumnDefaultOperation => "Default",
        SetColumnCommentOperation => "Comment",
        ColumnAttributeNote note => note.Attribute,
        RecreatePrimaryKeyOperation => "Primary Key",
        AddUniqueOperation or DropUniqueOperation => "Unique",
        CreateIndexOperation => "Indice ausente",
        DropIndexOperation => "Indice extra",
        AddCheckOperation => "Check ausente",
        DropCheckOperation => "Check extra",
        AddForeignKeyOperation => "FK ausente",
        DropForeignKeyOperation => "FK extra",
        _ => "Impacto externo",
    };

    private static string ResolveActionLabel(SchemaDifference difference) => difference.Operation switch
    {
        CreateTableOperation => "Criar tabela",
        DropTableOperation => "Remover tabela",
        AddColumnOperation => "Adicionar no destino",
        DropColumnOperation => "Remover do destino",
        AlterColumnOperation => "ALTER COLUMN",
        SetColumnDefaultOperation => "Sincronizar default",
        SetColumnCommentOperation => "Sincronizar comentario",
        ColumnAttributeNote => "Revisar manualmente",
        RecreatePrimaryKeyOperation => "Recriar PK",
        AddUniqueOperation or DropUniqueOperation => "Sincronizar UNIQUE",
        CreateIndexOperation => "Adicionar indice",
        DropIndexOperation => "Remover indice",
        AddCheckOperation => "Adicionar check",
        DropCheckOperation => "Remover check",
        AddForeignKeyOperation => "Adicionar FK",
        DropForeignKeyOperation => "Remover FK",
        _ => "Informativo: ajuste manual fora da tabela alvo",
    };

    private static string ResolveContentGroup(SchemaDiffCategory category) => category switch
    {
        SchemaDiffCategory.Table => "Tabelas",
        SchemaDiffCategory.Column => "Colunas",
        SchemaDiffCategory.PrimaryKey or SchemaDiffCategory.Unique or SchemaDiffCategory.Check => "Constraints",
        SchemaDiffCategory.Index => "Indices",
        SchemaDiffCategory.ForeignKey => "Relacionamentos",
        _ => "Dependencia externa",
    };

    private string BuildCommentFallback(SchemaDifference difference)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"-- [{ResolveCategoryLabel(difference)}] {difference.ObjectName}");
        builder.AppendLine($"-- {L("ddl.compare.sql.header.source", "Source")}: {difference.SourceDescription}");
        builder.AppendLine($"-- {L("ddl.compare.sql.header.target", "Target")}: {difference.TargetDescription}");
        builder.AppendLine($"-- {L("ddl.compare.review.grid.action", "Action")}: {ResolveActionLabel(difference)}");
        if (difference.IsDestructive)
            builder.AppendLine($"-- {L("ddl.compare.sql.risk", "RISK")}: {L("ddl.compare.risk.destructive", "Destructive operation: can cause permanent data loss or dependency breakage.")}");
        builder.Append($"-- {L("ddl.compare.sql.recommendedAdjustment", "Adjustment recommended by comparison")}.");
        return builder.ToString();
    }

    private string BuildRiskSummary(string action, DdlSchemaCompareDiffSeverity severity, bool destructive)
    {
        if (destructive)
            return L("ddl.compare.risk.destructive", "Destructive operation: can cause permanent data loss or dependency breakage.");

        if (action.Contains("ALTER COLUMN", StringComparison.OrdinalIgnoreCase) || severity == DdlSchemaCompareDiffSeverity.High)
            return L("ddl.compare.risk.highRiskAlter", "High-risk alteration: may fail with incompatible data.");

        if (action.Contains("FK", StringComparison.OrdinalIgnoreCase))
            return L("ddl.compare.risk.relationship", "Relationship change: may impact referential integrity.");

        return L("ddl.compare.risk.low", "Low execution risk when validated in staging.");
    }

    private void RefreshFilterOptionsFromDifferences()
    {
        string currentCategory = SelectedCategoryFilter;
        string currentSeverity = SelectedSeverityFilter;
        string currentAction = SelectedActionFilter;

        CategoryFilterOptions.Clear();
        SeverityFilterOptions.Clear();
        ActionFilterOptions.Clear();

        CategoryFilterOptions.Add(FilterOptionAll);
        SeverityFilterOptions.Add(FilterOptionAll);
        ActionFilterOptions.Add(FilterOptionAll);

        foreach (string category in Differences.Select(static diff => diff.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))
            CategoryFilterOptions.Add(category);

        foreach (string severity in Differences.Select(static diff => diff.SeverityLabel).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))
            SeverityFilterOptions.Add(severity);

        foreach (string action in Differences.Select(static diff => diff.SuggestedAction).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))
            ActionFilterOptions.Add(action);

        if (!CategoryFilterOptions.Contains(currentCategory))
            currentCategory = FilterOptionAll;
        if (!SeverityFilterOptions.Contains(currentSeverity))
            currentSeverity = FilterOptionAll;
        if (!ActionFilterOptions.Contains(currentAction))
            currentAction = FilterOptionAll;

        SelectedCategoryFilter = currentCategory;
        SelectedSeverityFilter = currentSeverity;
        SelectedActionFilter = currentAction;
    }

    private void RebuildGeneratedSqlFromWizard()
    {
        // Respect exactly what the user chose to include. Safe mode does not silently drop
        // included items; instead risky/destructive ones are emitted commented-out so the
        // visible "included" counters stay consistent with the generated script.
        List<DdlSchemaCompareDifferenceItemViewModel> selected = Differences.Where(static diff => diff.IsIncluded).ToList();

        // Use the provider the comparison SQL was generated for, so the transactional wrapper
        // matches the dialect of the statements inside it.
        DatabaseProvider provider = _compareProvider;
        var builder = new StringBuilder();
        bool hasAny = selected.Count > 0;

        if (IncludeHeader)
        {
            builder.AppendLine("-- =====================================================");
            builder.AppendLine($"-- {L("ddl.compare.sql.header.generatedBy", "SQL DDL generated by Akkorn Studio")}");
            builder.AppendLine($"-- {L("ddl.compare.sql.header.objective", "Objective: make target match source")}");
            builder.AppendLine($"-- {L("ddl.compare.sql.header.source", "Source")}: {ResolveSourceLabel()}");
            builder.AppendLine($"-- {L("ddl.compare.sql.header.target", "Target")}: {ResolveTargetLabel()}");
            if (IncludeTimestamp)
                builder.AppendLine($"-- {L("ddl.compare.sql.header.generatedAt", "Generated at")}: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
            builder.AppendLine("-- =====================================================");
            builder.AppendLine();
        }

        if (IncludeDiffSummary)
        {
            builder.AppendLine($"-- {L("ddl.compare.sql.summary.title", "Summary")}");
            builder.AppendLine($"-- {L("ddl.compare.sql.summary.included", "Included differences")}: {IncludedCount}");
            builder.AppendLine($"-- {L("ddl.compare.sql.summary.ignored", "Ignored differences")}: {IgnoredCount}");
            builder.AppendLine($"-- {L("ddl.compare.sql.summary.destructiveIncluded", "Included destructive operations")}: {Differences.Count(diff => diff.IsIncluded && diff.IsDestructive)}");
            builder.AppendLine($"-- {L("ddl.compare.sql.summary.destructiveCommented", "Commented destructive operations")}: {CommentedDestructiveCount}");
            builder.AppendLine();
        }

        bool hasTransactionalWrapper = IncludeTransaction && SqlGenerationMode != DdlSchemaCompareSqlGenerationMode.Informative;
        if (hasTransactionalWrapper)
            AppendTransactionPrologue(builder, provider);

        if (!hasAny)
        {
            builder.AppendLine($"-- {L("ddl.compare.empty.noSelectedDifferences", "No differences selected for SQL generation.")}");
        }
        else
        {
            var emittedBlocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DdlSchemaCompareDifferenceItemViewModel diff in selected)
            {
                bool shouldSkipByContent =
                    (!IncludeColumns && diff.Notes == "Colunas")
                    || (!IncludeConstraints && diff.Notes == "Constraints")
                    || (!IncludeIndexes && diff.Notes == "Indices")
                    || (!IncludeRelationships && diff.Notes == "Relacionamentos");

                if (shouldSkipByContent)
                    continue;

                if (diff.Notes == "Dependencia externa" && !IncludeExternalDependenciesAsComments)
                    continue;

                string sql = diff.SuggestedSql.TrimEnd();
                if (string.IsNullOrWhiteSpace(sql))
                    continue;
                if (!emittedBlocks.Add(sql))
                    continue;

                AppendDiffCommentBlock(builder, diff);

                if (SqlGenerationMode == DdlSchemaCompareSqlGenerationMode.Informative)
                {
                    builder.AppendLine("-- Modo informativo");
                    builder.AppendLine(sql);
                }
                else if (ShouldCommentOut(diff))
                {
                    if (SqlGenerationMode == DdlSchemaCompareSqlGenerationMode.Safe
                        && !diff.IsDestructive
                        && diff.Severity == DdlSchemaCompareDiffSeverity.High)
                    {
                        builder.AppendLine("-- Modo seguro: operacao de alto risco comentada. Revise antes de executar.");
                    }

                    foreach (string line in sql.Split('\n'))
                        builder.AppendLine(line.StartsWith("--", StringComparison.Ordinal) ? line : $"-- {line}");
                }
                else if (SqlGenerationMode == DdlSchemaCompareSqlGenerationMode.AssistedManual)
                {
                    builder.AppendLine("-- TODO: revisar bloco manualmente antes de executar");
                    builder.AppendLine(sql);
                }
                else
                {
                    builder.AppendLine(sql);
                }

                builder.AppendLine();
            }
        }

        if (hasTransactionalWrapper)
            AppendTransactionEpilogue(builder, provider);

        GeneratedSql = builder.ToString().Trim();
        SqlPreviewSummary = BuildSqlPreviewSummary();
        UpdateDisplaySqlPreview();
        RaisePropertyChanged(nameof(HasGeneratedSql));
    }

    private SchemaSyncOptions BuildSqlOptions()
        => new(ExistenceSafe: UseIfExistsChecks || UseCatalogChecks);

    // Re-emits the executable SQL for the cached comparison with the current generation options,
    // updating each difference's SuggestedSql in place so the user's review state is preserved.
    private void RecomputeSqlForCurrentOptions()
    {
        if (_flatDifferences.Count == 0)
            return;

        foreach (DdlSchemaCompareDifferenceItemViewModel diff in Differences)
            diff.SuggestedSql = GenerateSqlForDifference(diff.EngineDifference, diff.TargetSchema, diff.TargetTable);
    }

    private bool ShouldCommentOut(DdlSchemaCompareDifferenceItemViewModel diff)
    {
        if (diff.IsDestructive && CommentDestructiveOperations)
            return true;

        // Safe mode keeps high-risk (non-destructive) alterations in the script but commented.
        return SqlGenerationMode == DdlSchemaCompareSqlGenerationMode.Safe
            && diff.Severity == DdlSchemaCompareDiffSeverity.High;
    }

    private void AppendTransactionPrologue(StringBuilder builder, DatabaseProvider provider)
    {
        switch (provider)
        {
            case DatabaseProvider.SqlServer:
                builder.AppendLine("BEGIN TRANSACTION;");
                if (IncludeTryCatch)
                {
                    builder.AppendLine("BEGIN TRY");
                    builder.AppendLine();
                }

                break;
            case DatabaseProvider.MySql:
                builder.AppendLine("START TRANSACTION;");
                builder.AppendLine();
                break;
            case DatabaseProvider.Postgres:
            case DatabaseProvider.SQLite:
            default:
                builder.AppendLine("BEGIN;");
                builder.AppendLine();
                break;
        }
    }

    private void AppendTransactionEpilogue(StringBuilder builder, DatabaseProvider provider)
    {
        // Only SQL Server supports a top-level TRY/CATCH wrapper. Postgres/MySQL/SQLite
        // abort the transaction automatically on error, so a plain COMMIT is correct.
        if (provider == DatabaseProvider.SqlServer && IncludeTryCatch)
        {
            builder.AppendLine("COMMIT;");
            builder.AppendLine("END TRY");
            builder.AppendLine("BEGIN CATCH");
            if (AutoRollbackOnError)
                builder.AppendLine("    ROLLBACK;");
            builder.AppendLine("    THROW;");
            builder.AppendLine("END CATCH;");
            return;
        }

        builder.AppendLine("COMMIT;");
    }

    private void UpdateDisplaySqlPreview()
    {
        if (string.IsNullOrWhiteSpace(GeneratedSql))
        {
            DisplaySqlPreview = string.Empty;
            return;
        }

        if (SqlPreviewShowComments)
        {
            DisplaySqlPreview = GeneratedSql;
            return;
        }

        string filtered = string.Join(
            Environment.NewLine,
            GeneratedSql
                .Split('\n')
                .Where(static line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal))
                .Select(static line => line.TrimEnd('\r')));
        DisplaySqlPreview = filtered.Trim();
    }

    private string BuildSqlPreviewSummary()
    {
        return string.Format(
            L("ddl.compare.preview.summary", "Source: {0} | Target: {1} | Included: {2} | Ignored: {3} | Destructive: {4} | Commented: {5}."),
            ResolveSourceLabel(),
            ResolveTargetLabel(),
            IncludedCount,
            IgnoredCount,
            Differences.Count(d => d.IsIncluded && d.IsDestructive),
            CommentedDestructiveCount);
    }

    private string ResolveSourceLabel()
    {
        EndpointDisplayInfo endpoint = ResolveSourceEndpointInfo();
        return $"{endpoint.Connection}.{endpoint.Database}.{endpoint.Schema}.{endpoint.Table}";
    }

    private string ResolveTargetLabel()
    {
        EndpointDisplayInfo endpoint = ResolveTargetEndpointInfo();
        return $"{endpoint.Connection}.{endpoint.Database}.{endpoint.Schema}.{endpoint.Table}";
    }

    private EndpointDisplayInfo ResolveSourceEndpointInfo()
    {
        return SelectedDirection == DdlSchemaCompareDirection.LeftToRight
            ? BuildEndpointDisplayInfo(LeftSelectedProfile, LeftSelectedDatabase, LeftSelectedSchema, LeftSelectedTable)
            : BuildEndpointDisplayInfo(RightSelectedProfile, RightSelectedDatabase, RightSelectedSchema, RightSelectedTable);
    }

    private EndpointDisplayInfo ResolveTargetEndpointInfo()
    {
        return SelectedDirection == DdlSchemaCompareDirection.LeftToRight
            ? BuildEndpointDisplayInfo(RightSelectedProfile, RightSelectedDatabase, RightSelectedSchema, RightSelectedTable)
            : BuildEndpointDisplayInfo(LeftSelectedProfile, LeftSelectedDatabase, LeftSelectedSchema, LeftSelectedTable);
    }

    private static EndpointDisplayInfo BuildEndpointDisplayInfo(
        ConnectionProfile? profile,
        string? database,
        string? schema,
        string? table)
    {
        return new EndpointDisplayInfo(
            NormalizeEndpointPart(profile?.Name),
            NormalizeEndpointPart(database),
            NormalizeEndpointPart(schema),
            NormalizeEndpointPart(table));
    }

    private static string NormalizeEndpointPart(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "-"
            : value.Trim();
    }

    private static bool LooksLikeProduction(string text)
    {
        return text.Contains("prod", StringComparison.OrdinalIgnoreCase)
            || text.Contains("production", StringComparison.OrdinalIgnoreCase)
            || text.Contains("prd", StringComparison.OrdinalIgnoreCase)
            || text.Contains("live", StringComparison.OrdinalIgnoreCase);
    }

    internal void UpdateSelectionStepState()
    {
        string target = ResolveTargetLabel();
        DirectionImpactText = L("ddl.compare.direction.impact.short", "O SQL alterara apenas o destino.");
        IsTargetProductionLike = LooksLikeProduction(target);
        CompareAndContinueCommand?.NotifyCanExecuteChanged();
        RefreshMetadataCommand?.NotifyCanExecuteChanged();
        SwapSourceTargetCommand?.NotifyCanExecuteChanged();
        RaisePropertyChanged(nameof(CanRunComparison));
        RaisePropertyChanged(nameof(SourceEndpointLabel));
        RaisePropertyChanged(nameof(TargetEndpointLabel));
        RaisePropertyChanged(nameof(SourceConnectionName));
        RaisePropertyChanged(nameof(SourceDatabaseName));
        RaisePropertyChanged(nameof(SourceSchemaName));
        RaisePropertyChanged(nameof(SourceTableName));
        RaisePropertyChanged(nameof(SourceObjectPath));
        RaisePropertyChanged(nameof(TargetConnectionName));
        RaisePropertyChanged(nameof(TargetDatabaseName));
        RaisePropertyChanged(nameof(TargetSchemaName));
        RaisePropertyChanged(nameof(TargetTableName));
        RaisePropertyChanged(nameof(TargetObjectPath));
        RaisePropertyChanged(nameof(DirectionFlowSummary));
        RaisePropertyChanged(nameof(DirectionArrowSummary));
        RaisePropertyChanged(nameof(FooterContextText));
    }

    internal void OnComparisonCompleted()
    {
        LastCompareError = string.Empty;
        HasCompared = true;
        _advanceWithoutSelectionRequested = false;

        BuildWizardDifferencesFromDiff();
        if (Differences.Count == 0)
            ReviewMessage = L("ddl.compare.empty.noDifferences", "Tables are synchronized. No SQL is required.");
        else
            ReviewMessage = L("ddl.compare.review.impact", "These differences will be used to generate a script that alters the target.");

        RebuildGeneratedSqlFromWizard();
        WizardStep = DdlSchemaCompareWizardStep.Review;
        RaisePropertyChanged(nameof(FooterContextText));
    }

    internal void RebuildGeneratedSqlFromWizardForTesting()
    {
        RebuildGeneratedSqlFromWizard();
    }

    private void OnGeneratedSqlChanged()
    {
        UpdateDisplaySqlPreview();
    }

    private static void AppendDiffCommentBlock(StringBuilder builder, DdlSchemaCompareDifferenceItemViewModel diff)
    {
        builder.AppendLine($"-- [{NormalizeSqlCommentValue(diff.Category)}] {NormalizeSqlCommentValue(diff.Item)}");
        builder.AppendLine($"-- Severidade: {NormalizeSqlCommentValue(diff.SeverityLabel)}");
        builder.AppendLine($"-- Acao: {NormalizeSqlCommentValue(diff.SuggestedAction)}");
        builder.AppendLine($"-- Origem: {NormalizeSqlCommentValue(diff.SourceValue)}");
        builder.AppendLine($"-- Destino: {NormalizeSqlCommentValue(diff.TargetValue)}");
        builder.AppendLine($"-- Risco: {NormalizeSqlCommentValue(diff.RiskSummary)}");
    }

    private static string NormalizeSqlCommentValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "-";

        return value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }

    private string ResolveStepState(DdlSchemaCompareWizardStep step)
    {
        if (WizardStep == step)
            return L("ddl.compare.step.current", "Current");
        if ((int)WizardStep > (int)step)
            return L("ddl.compare.step.completed", "Completed");
        if (step != DdlSchemaCompareWizardStep.Selection && !HasCompared)
            return L("ddl.compare.step.blocked", "Blocked");
        return L("ddl.compare.step.pending", "Pending");
    }

    private string ResolveModeLabel(DdlSchemaCompareSqlGenerationMode mode)
    {
        return mode switch
        {
            DdlSchemaCompareSqlGenerationMode.Safe => L("ddl.compare.options.mode.safe", "Safe script"),
            DdlSchemaCompareSqlGenerationMode.Complete => L("ddl.compare.options.mode.complete", "Complete script"),
            DdlSchemaCompareSqlGenerationMode.Informative => L("ddl.compare.options.mode.informative", "Informative-only script"),
            DdlSchemaCompareSqlGenerationMode.AssistedManual => L("ddl.compare.options.mode.assisted", "Assisted manual script"),
            _ => mode.ToString(),
        };
    }

    private static string L(string key, string fallback)
    {
        string value = LocalizationService.Instance[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }
}
