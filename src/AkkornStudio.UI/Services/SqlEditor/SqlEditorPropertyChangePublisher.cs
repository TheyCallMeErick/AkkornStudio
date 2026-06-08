namespace AkkornStudio.UI.Services.SqlEditor;

public sealed class SqlEditorPropertyChangePublisher
{
    private static readonly string[] SqlPanelProperties =
    [
        "ActiveTabProvider",
        "FallbackDialect",
        "AvailableConnectionProfiles",
        "ActiveTabConnectionProfile",
        "ActiveTabConnectionProfileId",
        "HasConnectionProfiles",
        "SidebarSelectedConnectionProfile",
        "HasSidebarSelectedConnectionProfile",
        "SharedConnectionManager",
        "HasSharedConnectionManager",
        "IsConnectionSwitcherOpen",
        "ConnectionSwitcherProfiles",
        "ConnectionSwitcherSelectedProfile",
        "ConnectionSwitcherSelectedProfileId",
        "ConnectionSwitcherDatabases",
        "ConnectionSwitcherSelectedDatabase",
        "CanChangeDatabaseInSwitcher",
        "CurrentConnectionProfileLabel",
        "CurrentConnectionProviderLabel",
        "CurrentConnectionDatabaseLabel",
        "CurrentConnectionHostLabel",
        "CurrentConnectionUserLabel",
        "CurrentConnectionUrlLabel",
        "HasResolvedConnection",
        "ActiveConnectionDisplayName",
        "ActiveConnectionSubtitle",
        "HasActiveConnection",
        "HasNoActiveConnection",
        "IsProductionConnectionContext",
        "IsStagingConnectionContext",
        "IsNeutralConnectionContext",
        "ActiveConnectionContextBadgeText",
        "ActiveProviderStatusText",
        "CursorLine",
        "CursorColumn",
        "CursorPositionText",
        "IndentationStatusText",
        "ShowDialectSelector",
        "ResultTabs",
        "SelectedOutputPane",
        "IsResultsOutputPaneSelected",
        "IsMessagesOutputPaneSelected",
        "SelectedResultTabIndex",
        "ResultRowsView",
        "HasHiddenResultColumns",
        "HiddenResultColumnsCount",
        "ShowAllColumnsButtonText",
        "HasHiddenColumnUndo",
        "UndoHiddenColumnButtonText",
        "ResultGridFilterText",
        "ResultGridSortColumn",
        "ResultGridSortAscending",
        "HasResultRows",
        "IsResultRowsEmpty",
        "ResultsEmptyText",
        "OutputMessages",
        "HasOutputMessages",
        "IsOutputMessagesEmpty",
        "MessagesPanelEmptyText",
        "ExecutionHistory",
        "FilteredExecutionHistory",
        "HasExecutionHistory",
        "HasFilteredExecutionHistory",
        "IsExecutionHistoryEmpty",
        "IsFilteredExecutionHistoryEmpty",
        "HasHistorySearchNoResults",
        "HistoryFilterSummaryText",
        "SelectedExecutionHistoryEntry",
        "HasPendingHistoryClearConfirmation",
        "ClearHistoryButtonText",
        "HistoryEmptyText",
        "HistorySearchText",
        "ExecutionTelemetry",
        "ExecutionTelemetryText",
        "ExecutionTelemetryErrorsText",
        "CompletionTelemetry",
        "CompletionTelemetryText",
        "LastExecutionMessage",
        "MessagesEmptyText",
        "ResultSummaryText",
        "CanExecuteOrCancel",
        "ExecuteOrCancelButtonText",
        "ExecuteOrCancelTooltipText",
        "IsCancellationPending",
        "CursorPositionTooltipText",
        "ActiveExecutionStatementStartLine",
        "ActiveExecutionStatementEndLine",
        "HasActiveExecutionStatementRange",
        "IsResultsSheetOpen",
        "CanReopenResultsSheet",
        "RestoreResultsButtonText",
        "ResultsSheetHeight",
        "IsExplainRunning",
        "ExplainSummaryText",
        "ExplainRawOutput",
        "HasExplainRawOutput",
        "IsBenchmarkRunning",
        "BenchmarkProgressText",
        "BenchmarkSummaryText",
        "HasBenchmarkResult",
        "PendingMutationMessage",
        "PendingMutationIssues",
        "PendingMutationCountQuery",
        "PendingMutationEstimatedRows",
        "PendingMutationEstimateText",
        "PendingMutationDiff",
        "HasPendingMutationDiff",
        "PendingMutationDiffText",
        "HasPendingQueryParameterPrompt",
        "PendingQueryParameterPromptMessage",
        "PendingQueryParameters",
    ];

    private static readonly string[] TabStateProperties =
    [
        "EditorTabs",
        "ActiveTab",
        "ActiveEditorTabIndex",
        "HasPendingCloseTabConfirmation",
        "PendingCloseTabMessage",
        "HasManyTabsWarning",
        "ManyTabsWarningText",
    ];

    private static readonly string[] SchemaProperties =
    [
        "SchemaTables",
        "SchemaSearchText",
        "FilteredSchemaTables",
        "HasFilteredSchemaTables",
        "HasSchemaTables",
        "IsSchemaEmpty",
        "SchemaEmptyText",
    ];

    public void PublishSqlPanelChanges(Action<string> raisePropertyChanged)
    {
        ArgumentNullException.ThrowIfNull(raisePropertyChanged);
        foreach (string property in SqlPanelProperties)
            raisePropertyChanged(property);
    }

    public void PublishTabStateChanges(Action<string> raisePropertyChanged)
    {
        ArgumentNullException.ThrowIfNull(raisePropertyChanged);
        foreach (string property in TabStateProperties)
            raisePropertyChanged(property);
    }

    public void PublishSchemaChanges(Action<string> raisePropertyChanged)
    {
        ArgumentNullException.ThrowIfNull(raisePropertyChanged);
        foreach (string property in SchemaProperties)
            raisePropertyChanged(property);
    }
}
