using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.Search;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Text.RegularExpressions;
using System;
using Avalonia.Threading;
using AkkornStudio.UI.Services;
using AkkornStudio.UI.Services.SqlEditor;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Controls.SqlEditor;

[ExcludeFromCodeCoverage]
public partial class SqlEditorControl : UserControl
{
    private const int LargeEditorCompletionThreshold = 10_000;
    private const int DefaultCompletionDebounceMs = 80;
    private const int AutoTriggerCompletionDebounceMs = 45;
    private const int TypingBurstWindowMs = 140;
    private const int HeavyMetadataAutoCompletionCooldownMs = 140;
    private const int HoverDocsDebounceMs = 400;
    private const int SignatureHelpDebounceMs = 70;
    private const int BracketHighlightDebounceMs = 35;
    private const int EditorTextSyncDebounceMs = 90;
    private const int MaxRenderedCompletionSuggestions = 90;

    private TextEditor? _editor;
    private Border? _goToLineOverlay;
    private TextBlock? _goToLineLabel;
    private TextBox? _goToLineInput;
    private IBrush? _goToLineDefaultBorderBrush;
    private IBrush? _goToLineHighlightBrush;
    private IBrush? _defaultTextViewCurrentLineBackground;
    private SqlBracketHighlightRenderer? _bracketHighlightRenderer;
    private SqlExecutionStatementHighlightRenderer? _executionStatementHighlightRenderer;
    private FoldingManager? _foldingManager;
    private readonly SqlFoldingStrategy _sqlFoldingStrategy = new();
    private readonly SqlTokenizer _sqlTokenizer = new();
    private DispatcherTimer? _foldingRefreshTimer;
    private CancellationTokenSource? _foldingRefreshCts;
    private long _foldingRefreshVersion;
    private DispatcherTimer? _foldingChordTimer;
    private bool _awaitingFoldingChord;
    private DispatcherTimer? _goToLineHighlightTimer;
    private int _goToLineOriginCaretOffset;
    private SqlEditorViewModel? _vm;
    private SearchPanel? _searchPanel;
    private CompletionWindow? _completionWindow;
    private DispatcherTimer? _completionDebounceTimer;
    private DispatcherTimer? _hoverDocsDebounceTimer;
    private DispatcherTimer? _signatureHelpDebounceTimer;
    private DispatcherTimer? _bracketHighlightDebounceTimer;
    private DispatcherTimer? _editorTextSyncDebounceTimer;
    private bool _completionOnDemandHintShown;
    private long _completionRequestVersion;
    private CancellationTokenSource? _completionRequestCts;
    private int _lastCompletionFingerprint;
    private int _lastCompletionItemCount = -1;
    private int _lastCompletionPrefixLength = -1;
    private long _lastHeavyMetadataAutoCompletionTickMs;
    private long _lastEditorTextInputTickMs;
    private Point _lastHoverPointerPosition;
    private bool _hasPendingEditorTextSync;
    private string? _pendingEditorTextSyncTabId;

    public SqlEditorControl()
    {
        InitializeComponent();
        ConfigureTextEditor();
        DataContextChanged += (_, _) => AttachViewModel();
        AttachedToVisualTree += (_, _) => FocusEditor();
        Loaded += (_, _) => EnsureEditorReady();
        DetachedFromVisualTree += (_, _) => OnDetachedFromVisualTree();
    }

    private void OnDetachedFromVisualTree()
    {
        DisposeFoldingManager();
        _foldingRefreshCts?.Cancel();
        _foldingRefreshCts?.Dispose();
        _foldingRefreshCts = null;
        _completionRequestCts?.Cancel();
        _completionRequestCts?.Dispose();
        _completionRequestCts = null;
        _signatureHelpDebounceTimer?.Stop();
        _bracketHighlightDebounceTimer?.Stop();
        _editorTextSyncDebounceTimer?.Stop();
        FlushPendingEditorTextToViewModel();
        ResetCompletionRenderCache();
    }

    private void ConfigureTextEditor()
    {
        _editor = this.FindControl<TextEditor>("SqlTextEditor");
        _goToLineOverlay = this.FindControl<Border>("GoToLineOverlay");
        _goToLineLabel = this.FindControl<TextBlock>("GoToLineLabel");
        _goToLineInput = this.FindControl<TextBox>("GoToLineInput");
        _goToLineDefaultBorderBrush = _goToLineInput?.BorderBrush;
        ConfigureGoToLineLocalization();
        if (_editor is null)
            return;

        _editor.Options.EnableHyperlinks = false;
        _editor.Options.EnableEmailHyperlinks = false;
        _editor.Options.HighlightCurrentLine = true;
        _editor.Options.AllowScrollBelowDocument = true;
        _editor.Options.EnableRectangularSelection = true;
        _editor.Options.IndentationSize = 2;
        _editor.Options.ConvertTabsToSpaces = true;
        _editor.IsReadOnly = false;
        _editor.Focusable = true;
        _editor.IsHitTestVisible = true;
        _editor.Cursor = new Cursor(StandardCursorType.Ibeam);
        _editor.SyntaxHighlighting = SqlEditorHighlightingService.GetSqlDefinition();
        _searchPanel = SearchPanel.Install(_editor);
        ConfigureBracketHighlighting();
        _defaultTextViewCurrentLineBackground = _editor.TextArea?.TextView.CurrentLineBackground;
        EnsureEditorDocument();
        _editor.TextChanged += OnEditorTextChanged;
        _editor.PointerMoved += OnEditorPointerMoved;
        _editor.PointerExited += OnEditorPointerExited;
        if (_editor.TextArea is TextArea textArea)
        {
            textArea.TextEntered += OnEditorTextEntered;
            textArea.Caret.PositionChanged += OnEditorCaretPositionChanged;
            ConfigureSqlFolding(textArea);
        }
        _editor.PointerPressed += (_, _) => FocusEditor();
        ApplyEditorExecutionState();
        RefreshBracketHighlight();
        ScheduleFoldingRefresh();
    }

    private void AttachViewModel()
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;

        _vm = DataContext as SqlEditorViewModel;
        if (_vm is not null)
            _vm.PropertyChanged += OnViewModelPropertyChanged;

        SyncEditorTextFromViewModel();
        ApplyEditorExecutionState();
        FocusEditor();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SqlEditorViewModel.ActiveTab))
            SyncEditorTextFromViewModel();

        if (e.PropertyName == nameof(SqlEditorViewModel.IsExecuting))
            ApplyEditorExecutionState();

        if (e.PropertyName is nameof(SqlEditorViewModel.ActiveExecutionStatementStartLine)
            or nameof(SqlEditorViewModel.ActiveExecutionStatementEndLine)
            or nameof(SqlEditorViewModel.IsExecuting))
        {
            RefreshExecutionStatementHighlight();
        }

        if (e.PropertyName == nameof(SqlEditorViewModel.EditorFocusRequestVersion))
            FocusEditorAtDocumentEnd();
    }

    private void SyncEditorTextFromViewModel()
    {
        if (_editor is null || _vm is null)
            return;

        if (string.Equals(_pendingEditorTextSyncTabId, _vm.ActiveTab.Id, StringComparison.Ordinal))
            FlushPendingEditorTextToViewModel();
        else
        {
            _hasPendingEditorTextSync = false;
            _pendingEditorTextSyncTabId = null;
        }

        EnsureEditorDocument();
        string text = _vm.ActiveTab.SqlText;
        bool editorDocumentMissing = _editor.Document is null;
        if (!editorDocumentMissing && string.Equals(_editor.Text, text, StringComparison.Ordinal))
            return;

        _editor.Text = text ?? string.Empty;
        EnsureEditorReady();
    }

    private void EnsureEditorDocument()
    {
        if (_editor is null)
            return;

        _editor.Document ??= new TextDocument(string.Empty);
    }

    private void EnsureEditorReady()
    {
        if (_editor is null)
            return;

        EnsureEditorDocument();
        if (_editor.TextArea is null)
            return;

        _editor.TextArea.IsEnabled = true;
        _editor.TextArea.Caret.BringCaretToView();
        ApplyEditorExecutionState();
        UpdateViewModelCursorPosition();
        RefreshExecutionStatementHighlight();
        ScheduleBracketHighlightRefresh(immediate: true);
        ScheduleSignatureHelpRefresh(immediate: true);
    }

    private void ApplyEditorExecutionState()
    {
        if (_editor is null)
            return;

        bool isExecuting = _vm?.IsExecuting == true;
        _editor.IsReadOnly = isExecuting;
        _editor.Cursor = new Cursor(isExecuting ? StandardCursorType.Wait : StandardCursorType.Ibeam);
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_editor is null || _vm is null)
            return;

        _hasPendingEditorTextSync = true;
        _pendingEditorTextSyncTabId = _vm.ActiveTab.Id;
        ScheduleEditorTextSync();
        _vm.ActiveTab.IsDirty = true;
        _vm.NotifyActiveTabEdited();
        _vm.ClearHoverDocumentation();
        ScheduleFoldingRefresh();
    }

    private void ScheduleEditorTextSync()
    {
        if (_vm is null)
            return;

        _editorTextSyncDebounceTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(EditorTextSyncDebounceMs) };
        _editorTextSyncDebounceTimer.Interval = TimeSpan.FromMilliseconds(EditorTextSyncDebounceMs);
        _editorTextSyncDebounceTimer.Tick -= OnEditorTextSyncDebounceTick;
        _editorTextSyncDebounceTimer.Tick += OnEditorTextSyncDebounceTick;
        _editorTextSyncDebounceTimer.Stop();
        _editorTextSyncDebounceTimer.Start();
    }

    private void OnEditorTextSyncDebounceTick(object? sender, EventArgs e)
    {
        _editorTextSyncDebounceTimer?.Stop();
        FlushPendingEditorTextToViewModel();
    }

    private void FlushPendingEditorTextToViewModel()
    {
        if (_vm is null || _editor is null || !_hasPendingEditorTextSync)
            return;

        if (!string.Equals(_pendingEditorTextSyncTabId, _vm.ActiveTab.Id, StringComparison.Ordinal))
        {
            _hasPendingEditorTextSync = false;
            _pendingEditorTextSyncTabId = null;
            return;
        }

        string latestText = _editor.Text ?? string.Empty;
        if (!string.Equals(_vm.ActiveTab.SqlText, latestText, StringComparison.Ordinal))
            _vm.ActiveTab.SqlText = latestText;

        _hasPendingEditorTextSync = false;
        _pendingEditorTextSyncTabId = null;
    }

    private void OnEditorTextEntered(object? sender, TextInputEventArgs e)
    {
        if (_editor is null || _vm is null)
            return;

        _lastEditorTextInputTickMs = Environment.TickCount64;

        bool shouldRefreshSignatureHelp = string.Equals(e.Text, "(", StringComparison.Ordinal)
            || string.Equals(e.Text, ")", StringComparison.Ordinal)
            || string.Equals(e.Text, ",", StringComparison.Ordinal);

        if (string.Equals(e.Text, ".", StringComparison.Ordinal))
        {
            TriggerCompletion(autoTriggered: true, allowDebounce: true);
            if (shouldRefreshSignatureHelp)
                ScheduleSignatureHelpRefresh(immediate: true);
            return;
        }

        if (string.Equals(e.Text, "(", StringComparison.Ordinal)
            || string.Equals(e.Text, ",", StringComparison.Ordinal))
        {
            TriggerCompletion(autoTriggered: true, allowDebounce: true);
            if (shouldRefreshSignatureHelp)
                ScheduleSignatureHelpRefresh(immediate: true);
            return;
        }

        if (string.Equals(e.Text, " ", StringComparison.Ordinal) && ShouldAutoTriggerCompletionAfterSpace())
        {
            TriggerCompletion(autoTriggered: true, allowDebounce: true);
            if (shouldRefreshSignatureHelp)
                ScheduleSignatureHelpRefresh(immediate: true);
            return;
        }

        if (ShouldDebounceCompletionAfterTextInput(e.Text))
            TriggerCompletion(autoTriggered: true, allowDebounce: true);

        if (shouldRefreshSignatureHelp)
            ScheduleSignatureHelpRefresh(immediate: false);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (_vm is null || _editor is null || e.Handled)
            return;

        bool isExecuteSelection = e.Key == Key.F8;
        bool isExecuteAll = e.Key == Key.F5;
        bool isExplain = e.Key == Key.F4;
        bool isBenchmark = e.Key == Key.F6;
        bool isTab = e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None;
        bool isCompletion = e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool isFind = e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool isReplace = e.Key == Key.H && e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool isGoToLine = e.Key == Key.G && e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool isFormatSql = e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool isCollapseCurrent = e.Key == Key.OemOpenBrackets && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool isExpandCurrent = e.Key == Key.OemCloseBrackets && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool isStartFoldingChord = e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool isSave = e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool isOpen = e.Key == Key.O && e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool isNewTab = e.Key == Key.T && e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool isCloseTab = e.Key == Key.W && e.KeyModifiers.HasFlag(KeyModifiers.Control);
        int? tabShortcutIndex = ResolveTabShortcutIndex(e.Key, e.KeyModifiers);
        bool isExecuteCurrent = (e.Key is Key.Enter or Key.Return) && e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool isCancel = e.Key == Key.Escape;

        if (HandleFoldingChord(e))
            return;

        if (isTab && TryAdvanceSnippetTabStop())
        {
            e.Handled = true;
            return;
        }

        if (isCancel)
        {
            if (_vm.IsConnectionSwitcherOpen)
            {
                if (_vm.CloseConnectionSwitcherCommand.CanExecute(null))
                    _vm.CloseConnectionSwitcherCommand.Execute(null);

                e.Handled = true;
                return;
            }

            if (_vm.ShouldShowResultsSheet)
            {
                if (_vm.CloseResultsSheetCommand.CanExecute(null))
                    _vm.CloseResultsSheetCommand.Execute(null);

                e.Handled = true;
                return;
            }

            if (_searchPanel?.IsOpened == true)
            {
                _searchPanel.Close();
                e.Handled = true;
                return;
            }

            _vm.CancelExecution();
            e.Handled = true;
            return;
        }

        if (isFind)
        {
            OpenSearchPanel(isReplaceMode: false);
            e.Handled = true;
            return;
        }

        if (isReplace)
        {
            OpenSearchPanel(isReplaceMode: true);
            e.Handled = true;
            return;
        }

        if (isExecuteAll)
        {
            _ = ExecuteAllWithToastAsync();
            e.Handled = true;
            return;
        }

        if (isExplain)
        {
            // Regression anchor: RunExplainAsync
            _ = RunExplainWithModalAsync(includeAnalyze: false);
            e.Handled = true;
            return;
        }

        if (isBenchmark)
        {
            // Regression anchor: RunBenchmarkAsync
            _ = RunBenchmarkWithModalAsync();
            e.Handled = true;
            return;
        }

        if (isSave)
        {
            _ = SaveSqlFileFromPickerAsync();
            e.Handled = true;
            return;
        }

        if (isOpen)
        {
            _ = OpenSqlFileFromPickerAsync();
            e.Handled = true;
            return;
        }

        if (isCompletion)
        {
            TriggerCompletion(autoTriggered: false, allowDebounce: false);
            e.Handled = true;
            return;
        }

        if (isGoToLine)
        {
            ShowGoToLineOverlay();
            e.Handled = true;
            return;
        }

        if (isFormatSql)
        {
            _ = FormatSqlWithToastAsync();
            e.Handled = true;
            return;
        }

        if (isCollapseCurrent)
        {
            CollapseCurrentFolding();
            e.Handled = true;
            return;
        }

        if (isExpandCurrent)
        {
            ExpandCurrentFolding();
            e.Handled = true;
            return;
        }

        if (isStartFoldingChord)
        {
            StartFoldingChord();
            e.Handled = true;
            return;
        }

        if (isNewTab)
        {
            if (_vm.NewTabCommand.CanExecute(null))
                _vm.NewTabCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (isCloseTab)
        {
            if (_vm.CloseActiveTabCommand.CanExecute(null))
                _vm.CloseActiveTabCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (tabShortcutIndex is int targetIndex)
        {
            if (targetIndex < _vm.EditorTabs.Count)
                _vm.ActiveEditorTabIndex = targetIndex;
            e.Handled = true;
            return;
        }

        if (!isExecuteSelection && !isExecuteCurrent)
            return;

        _ = ExecuteSelectionOrCurrentWithToastAsync();
        e.Handled = true;
    }

    private async Task ExecuteSelectionOrCurrentWithToastAsync()
    {
        if (_vm is null || _editor is null)
            return;

        FlushPendingEditorTextToViewModel();

        SqlEditorResultSet result = await _vm.ExecuteSelectionOrCurrentAsync(_editor.SelectionStart, _editor.SelectionLength, _editor.CaretOffset);
        ShowToastForResult(result);
    }

    private void OpenSearchPanel(bool isReplaceMode)
    {
        if (_searchPanel is null)
            return;

        _searchPanel.IsReplaceMode = isReplaceMode;
        _searchPanel.Open();
    }

    private async void ExecuteOrCancelPrimaryButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm is null || _editor is null)
            return;

        if (_vm.IsExecuting)
        {
            if (_vm.CanExecuteOrCancel)
                _vm.CancelExecution();

            return;
        }

        FlushPendingEditorTextToViewModel();

        SqlEditorResultSet result = await _vm.ExecuteSelectionOrCurrentAsync(
            _editor.SelectionStart,
            _editor.SelectionLength,
            _editor.CaretOffset);
        ShowToastForResult(result);
    }

    private async Task ExecuteAllWithToastAsync()
    {
        if (_vm is null)
            return;

        FlushPendingEditorTextToViewModel();

        IReadOnlyList<SqlEditorResultSet> results = await _vm.ExecuteAllAsync();
        if (results.Count == 0)
            return;

        int failures = results.Count(static r => !r.Success);
        if (failures == 0)
        {
            ShowShellToastSuccess(
                L("sqlEditor.toast.scriptSuccessTitle", "Script executado."),
                string.Format(
                    L("sqlEditor.toast.scriptSuccessDetail", "{0} instrucao(oes) executada(s) com sucesso."),
                    results.Count));
            return;
        }

        ShowShellToastWarning(
            L("sqlEditor.toast.scriptWarningTitle", "Script executado com falhas."),
            string.Format(
                L("sqlEditor.toast.scriptWarningDetail", "{0} de {1} instrucao(oes) falharam."),
                failures,
                results.Count));
    }

    private async Task FormatSqlWithToastAsync()
    {
        if (_editor?.Document is null)
            return;

        int selectionStart = _editor.SelectionStart;
        int selectionLength = _editor.SelectionLength;

        bool formatSelectionOnly = selectionLength > 0;
        string sourceSql = formatSelectionOnly
            ? _editor.Document.GetText(selectionStart, selectionLength)
            : _editor.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(sourceSql))
            return;

        var stopwatch = Stopwatch.StartNew();
        Task<string> formatTask = Task.Run(() => SqlDisplayFormatter.Format(sourceSql));
        bool formattingToastShown = false;

        Task completed = await Task.WhenAny(formatTask, Task.Delay(300));
        if (completed != formatTask)
        {
            ShowShellToastSuccess(L("sqlEditor.format.status.runningTitle", "Formatando SQL..."), null);
            formattingToastShown = true;
        }

        string formatted;
        try
        {
            formatted = await formatTask;
        }
        catch
        {
            ShowShellToastError(
                L("sqlEditor.format.status.failedTitle", "Nao foi possivel formatar SQL."),
                L("sqlEditor.format.status.failedDetail", "Verifique a sintaxe SQL e tente novamente."));
            return;
        }

        if (string.IsNullOrWhiteSpace(formatted))
        {
            ShowShellToastError(
                L("sqlEditor.format.status.failedTitle", "Nao foi possivel formatar SQL."),
                L("sqlEditor.format.status.failedDetail", "Verifique a sintaxe SQL e tente novamente."));
            return;
        }

        if (string.Equals(formatted, sourceSql, StringComparison.Ordinal))
            return;

        if (formatSelectionOnly)
        {
            _editor.Document.Replace(selectionStart, selectionLength, formatted);
            _editor.Select(selectionStart, formatted.Length);
        }
        else
        {
            int caret = _editor.CaretOffset;
            _editor.Document.Replace(0, _editor.Document.TextLength, formatted);
            _editor.CaretOffset = Math.Clamp(caret, 0, _editor.Document.TextLength);
            _editor.TextArea?.Caret.BringCaretToView();
        }

        stopwatch.Stop();
        if (formattingToastShown)
        {
            ShowShellToastSuccess(
                L("sqlEditor.format.status.successTitle", "SQL formatado."),
                string.Format(
                    L("sqlEditor.format.status.successDetail", "Concluido em {0} ms."),
                    (long)Math.Round(stopwatch.Elapsed.TotalMilliseconds)));
        }
    }

    private void ShowToastForResult(SqlEditorResultSet result)
    {
        if (!result.Success)
        {
            ShowShellToastError(L("sqlEditor.toast.resultErrorTitle", "Falha ao executar instrucao."), result.ErrorMessage);
            return;
        }

        long elapsedMs = (long)Math.Round(result.ExecutionTime.TotalMilliseconds);
        long rows = result.Data?.Rows.Count ?? result.RowsAffected ?? 0;
        string details = string.Format(
            L("sqlEditor.result.summary", "Linhas: {0}    Tempo: {1} ms"),
            rows,
            elapsedMs);
        ShowShellToastSuccess(L("sqlEditor.toast.resultSuccessTitle", "Execucao concluida com sucesso."), details);
    }

    private void ShowShellToastSuccess(string message, string? details)
    {
        if (TopLevel.GetTopLevel(this) is Window window && window.DataContext is ShellViewModel shell)
            shell.Toasts.ShowSuccess(message, details);
    }

    private void ShowShellToastWarning(string message, string? details)
    {
        if (TopLevel.GetTopLevel(this) is Window window && window.DataContext is ShellViewModel shell)
            shell.Toasts.ShowWarning(message, details);
    }

    private void ShowShellToastError(string message, string? details)
    {
        if (TopLevel.GetTopLevel(this) is Window window && window.DataContext is ShellViewModel shell)
            shell.Toasts.ShowError(message, details);
    }

    private async Task OpenSqlFileFromPickerAsync()
    {
        if (_vm is null)
            return;

        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
            return;

        var sqlFileType = new FilePickerFileType(L("sqlEditor.saveSql.fileType", "Arquivos SQL"))
        {
            Patterns = ["*.sql", "*.txt"],
            MimeTypes = ["text/plain"],
        };

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = L("sqlEditor.openSql.pickerTitle", "Abrir arquivo SQL"),
                AllowMultiple = false,
                FileTypeFilter = [sqlFileType, FilePickerFileTypes.All],
            });

        string? path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        await _vm.OpenSqlFileAsync(path);
    }

    private async Task SaveSqlFileFromPickerAsync()
    {
        if (_vm is null)
            return;

        FlushPendingEditorTextToViewModel();

        if (!string.IsNullOrWhiteSpace(_vm.ActiveTab.FilePath))
        {
            await _vm.SaveActiveTabAsync(_vm.ActiveTab.FilePath);
            return;
        }

        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
            return;

        var sqlFileType = new FilePickerFileType(L("sqlEditor.saveSql.fileType", "Arquivos SQL"))
        {
            Patterns = ["*.sql"],
            MimeTypes = ["text/plain"],
        };

        string suggestedName = string.IsNullOrWhiteSpace(_vm.ActiveTab.FallbackTitle)
            ? "script.sql"
            : $"{Path.GetFileNameWithoutExtension(_vm.ActiveTab.FallbackTitle)}.sql";

        IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = L("sqlEditor.saveSql.pickerTitle", "Salvar arquivo SQL"),
                DefaultExtension = "sql",
                SuggestedFileName = suggestedName,
                FileTypeChoices = [sqlFileType],
            });

        string? path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        await _vm.SaveActiveTabAsync(path);
    }

    private void TriggerCompletion(bool autoTriggered, bool allowDebounce)
    {
        if (_editor is null || _vm is null)
            return;

        bool isHeavyMetadataContext = false;

        if (autoTriggered && IsLargeEditorText())
        {
            PublishCompletionOnDemandHintOnce();
            return;
        }

        if (autoTriggered)
            isHeavyMetadataContext = _vm.IsHeavyCompletionMetadataContext();

        if (autoTriggered && isHeavyMetadataContext)
        {
            allowDebounce = true;
            if (!CanScheduleHeavyMetadataAutoCompletion())
                return;
        }

        _completionOnDemandHintShown = false;

        if (!allowDebounce)
        {
            _completionDebounceTimer?.Stop();
            _ = ShowCompletionWindowAsync(allowUpdateExisting: true);
            return;
        }

        int debounceMs = ResolveCompletionDebounceMs(isHeavyMetadataContext);
        _completionDebounceTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(debounceMs) };
        _completionDebounceTimer.Interval = TimeSpan.FromMilliseconds(debounceMs);
        _completionDebounceTimer.Tick -= OnCompletionDebounceTick;
        _completionDebounceTimer.Tick += OnCompletionDebounceTick;
        _completionDebounceTimer.Stop();
        _completionDebounceTimer.Start();
    }

    private void OnCompletionDebounceTick(object? sender, EventArgs e)
    {
        _completionDebounceTimer?.Stop();
        _ = ShowCompletionWindowAsync(allowUpdateExisting: true);
    }

    private void OnEditorPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_editor is null)
            return;

        _lastHoverPointerPosition = e.GetPosition(_editor);
        _hoverDocsDebounceTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HoverDocsDebounceMs) };
        _hoverDocsDebounceTimer.Tick -= OnHoverDocsDebounceTick;
        _hoverDocsDebounceTimer.Tick += OnHoverDocsDebounceTick;
        _hoverDocsDebounceTimer.Stop();
        _hoverDocsDebounceTimer.Start();
    }

    private void OnEditorPointerExited(object? sender, PointerEventArgs e)
    {
        _hoverDocsDebounceTimer?.Stop();
        _vm?.ClearHoverDocumentation();
    }

    private void OnHoverDocsDebounceTick(object? sender, EventArgs e)
    {
        _hoverDocsDebounceTimer?.Stop();

        if (_editor is null || _vm is null)
            return;

        int? offset = ResolveEditorOffsetFromPointer(_lastHoverPointerPosition);
        if (offset is null)
        {
            _vm.ClearHoverDocumentation();
            return;
        }

        _vm.UpdateHoverDocumentation(_editor.Text ?? string.Empty, offset.Value);
    }

    private async Task ShowCompletionWindowAsync(bool allowUpdateExisting)
    {
        if (_vm is null || _editor is null)
            return;

        if (_completionWindow is not null && !allowUpdateExisting)
            return;

        _completionRequestCts?.Cancel();
        _completionRequestCts?.Dispose();
        _completionRequestCts = new CancellationTokenSource();

        long requestVersion = Interlocked.Increment(ref _completionRequestVersion);
        CancellationToken cancellationToken = _completionRequestCts.Token;
        string editorText = _editor.Text ?? string.Empty;
        int caretOffset = _editor.CaretOffset;
        bool firstProgressSnapshotApplied = false;

        var progress = new Progress<SqlCompletionStageSnapshot>(snapshot =>
        {
            if (requestVersion != _completionRequestVersion)
                return;

            if (!snapshot.HasSuggestions)
                return;

            if (firstProgressSnapshotApplied)
                return;

            firstProgressSnapshotApplied = true;
            ApplyCompletionRequest(snapshot.Request, allowUpdateExisting);
        });

        Task<SqlCompletionStageSnapshot> computeTask = _vm.RequestCompletionAsync(
            editorText,
            caretOffset,
            progress,
            cancellationToken);
        int debounceMs = ResolveCompletionDebounceMs(isHeavyMetadataContext: _vm.IsHeavyCompletionMetadataContext());
        Task completed = await Task.WhenAny(computeTask, Task.Delay(debounceMs));
        if (completed != computeTask && !IsTypingBurstActive())
            ShowCompletionLoadingPopup(allowUpdateExisting);

        SqlCompletionRequest request;
        SqlCompletionTelemetry? completionTelemetry = null;
        try
        {
            SqlCompletionStageSnapshot snapshot = await computeTask;
            request = snapshot.Request;
            completionTelemetry = snapshot.Telemetry;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            try
            {
                request = _vm.GetCompletionRequest(editorText, caretOffset);
            }
            catch
            {
                return;
            }
        }

        if (requestVersion != _completionRequestVersion)
            return;

        if (request.Suggestions.Count == 0)
        {
            _completionWindow?.Close();
            _completionWindow = null;
            ResetCompletionRenderCache();
            if (completionTelemetry is not null)
                _vm.RecordCompletionBreakdown(completionTelemetry);
            return;
        }

        ApplyCompletionRequest(request, allowUpdateExisting: true);

        if (completionTelemetry is not null)
            _vm.RecordCompletionBreakdown(completionTelemetry);
    }

    private int ResolveCompletionDebounceMs(bool isHeavyMetadataContext)
    {
        int recommended = _vm?.GetRecommendedCompletionDebounceMs(isHeavyMetadataContext) ?? DefaultCompletionDebounceMs;
        if (!isHeavyMetadataContext)
            recommended = Math.Max(AutoTriggerCompletionDebounceMs, recommended);

        return recommended;
    }

    private bool IsTypingBurstActive()
    {
        long lastTick = _lastEditorTextInputTickMs;
        if (lastTick <= 0)
            return false;

        return Environment.TickCount64 - lastTick <= TypingBurstWindowMs;
    }

    private void ApplyCompletionRequest(SqlCompletionRequest request, bool allowUpdateExisting)
    {
        if (_editor is null)
            return;

        Stopwatch? uiApplyStopwatch = _vm is null ? null : Stopwatch.StartNew();

        if (_completionWindow is not null && !allowUpdateExisting)
            return;

        IReadOnlyList<SqlCompletionSuggestion> orderedSuggestions = request.Suggestions
            .OrderByDescending(static suggestion => suggestion.Kind == SqlCompletionKind.Keyword)
            .ThenBy(static suggestion => suggestion.Label, StringComparer.OrdinalIgnoreCase)
            .Take(MaxRenderedCompletionSuggestions)
            .ToList();

        int fingerprint = ComputeCompletionFingerprint(orderedSuggestions, request.PrefixLength);
        if (_lastCompletionFingerprint == fingerprint
            && _lastCompletionItemCount == orderedSuggestions.Count
            && _lastCompletionPrefixLength == request.PrefixLength)
        {
            return;
        }

        if (_completionWindow is null)
        {
            _completionWindow = new CompletionWindow(_editor.TextArea);
            _completionWindow.Closed += (_, _) =>
            {
                _completionWindow = null;
                ResetCompletionRenderCache();
            };
            _completionWindow.Show();
        }

        if (_vm is null)
            return;

        _completionWindow.CompletionList.CompletionData.Clear();
        foreach (SqlCompletionSuggestion suggestion in orderedSuggestions)
        {
            _completionWindow.CompletionList.CompletionData.Add(
                new SqlEditorCompletionData(
                    suggestion.Label,
                    suggestion.InsertText,
                    suggestion.Detail,
                    suggestion.Kind,
                    request.PrefixLength,
                    acceptedCallback: label => _vm.RecordCompletionSuggestionAccepted(label)));
        }

        _lastCompletionFingerprint = fingerprint;
        _lastCompletionItemCount = orderedSuggestions.Count;
        _lastCompletionPrefixLength = request.PrefixLength;

        if (uiApplyStopwatch is not null)
        {
            uiApplyStopwatch.Stop();
            _vm.RecordCompletionUiApplyLatency(uiApplyStopwatch.Elapsed);
        }
    }

    private void ShowCompletionLoadingPopup(bool allowUpdateExisting)
    {
        if (_editor is null)
            return;

        if (_completionWindow is not null && !allowUpdateExisting)
            return;

        if (_completionWindow is null)
        {
            _completionWindow = new CompletionWindow(_editor.TextArea);
            _completionWindow.Closed += (_, _) => _completionWindow = null;
            _completionWindow.Show();
        }

        _completionWindow.CompletionList.CompletionData.Clear();
        _completionWindow.CompletionList.CompletionData.Add(
            new SqlEditorCompletionLoadingData(
                L("sqlEditor.completion.loading", "Carregando sugestoes...")));
        ResetCompletionRenderCache();
    }

    private static int ComputeCompletionFingerprint(
        IReadOnlyList<SqlCompletionSuggestion> suggestions,
        int prefixLength)
    {
        var hash = new HashCode();
        hash.Add(prefixLength);
        hash.Add(suggestions.Count);

        foreach (SqlCompletionSuggestion suggestion in suggestions)
        {
            hash.Add((int)suggestion.Kind);
            hash.Add(suggestion.Label, StringComparer.Ordinal);
            hash.Add(suggestion.InsertText, StringComparer.Ordinal);
            hash.Add(suggestion.Detail, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    private void ResetCompletionRenderCache()
    {
        _lastCompletionFingerprint = 0;
        _lastCompletionItemCount = -1;
        _lastCompletionPrefixLength = -1;
    }

    private bool ShouldDebounceCompletionAfterTextInput(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || _editor is null)
            return false;

        char c = text[0];
        if (char.IsLetter(c) || c == '_')
            return true;

        if (!char.IsDigit(c))
            return false;

        string editorText = _editor.Text ?? string.Empty;
        int caret = _editor.CaretOffset;
        if (caret <= 1 || caret > editorText.Length)
            return false;

        char previous = editorText[caret - 2];
        return char.IsLetter(previous) || previous is '_' or '.';
    }

    private bool IsLargeEditorText()
    {
        return (_editor?.Text?.Length ?? 0) > LargeEditorCompletionThreshold;
    }

    private bool CanScheduleHeavyMetadataAutoCompletion()
    {
        long now = Environment.TickCount64;
        if (_lastHeavyMetadataAutoCompletionTickMs > 0
            && now - _lastHeavyMetadataAutoCompletionTickMs < HeavyMetadataAutoCompletionCooldownMs)
        {
            return false;
        }

        _lastHeavyMetadataAutoCompletionTickMs = now;
        return true;
    }

    private void PublishCompletionOnDemandHintOnce()
    {
        if (_vm is null || _completionOnDemandHintShown)
            return;

        _vm.PublishStatus(
            L("sqlEditor.status.completionOnDemand", "Editor grande — completion sob demanda (Ctrl+Space)."),
            null,
            hasError: false);
        _completionOnDemandHintShown = true;
    }

    private bool ShouldAutoTriggerCompletionAfterSpace()
    {
        if (_editor is null)
            return false;

        string text = _editor.Text ?? string.Empty;
        int caret = _editor.CaretOffset;
        if (caret <= 0 || caret > text.Length)
            return false;

        string beforeCaret = text[..caret].TrimEnd();
        return Regex.IsMatch(
            beforeCaret,
            @"\b(SELECT|FROM|JOIN|LEFT\s+JOIN|RIGHT\s+JOIN|INNER\s+JOIN|FULL\s+JOIN|WHERE|ON|GROUP\s+BY|ORDER\s+BY)\s*$",
            RegexOptions.IgnoreCase);
    }

    private void ResultsModalBackdrop_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null)
            return;

        PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
        if (!properties.IsLeftButtonPressed)
            return;

        if (_vm.CloseResultsSheetCommand.CanExecute(null))
            _vm.CloseResultsSheetCommand.Execute(null);

        e.Handled = true;
    }

    private void ConnectionSwitcherBackdrop_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null)
            return;

        PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
        if (!properties.IsLeftButtonPressed)
            return;

        if (_vm.CloseConnectionSwitcherCommand.CanExecute(null))
            _vm.CloseConnectionSwitcherCommand.Execute(null);

        e.Handled = true;
    }

    private static int? ResolveTabShortcutIndex(Key key, KeyModifiers modifiers)
    {
        if (!modifiers.HasFlag(KeyModifiers.Control))
            return null;

        return key switch
        {
            Key.D1 or Key.NumPad1 => 0,
            Key.D2 or Key.NumPad2 => 1,
            Key.D3 or Key.NumPad3 => 2,
            Key.D4 or Key.NumPad4 => 3,
            Key.D5 or Key.NumPad5 => 4,
            Key.D6 or Key.NumPad6 => 5,
            Key.D7 or Key.NumPad7 => 6,
            Key.D8 or Key.NumPad8 => 7,
            Key.D9 or Key.NumPad9 => 8,
            _ => null,
        };
    }

    private int? ResolveEditorOffsetFromPointer(Point pointerPosition)
    {
        if (_editor?.Document is null)
            return null;

        var viewPosition = _editor.GetPositionFromPoint(pointerPosition);
        if (viewPosition is null)
            return null;

        int line = Math.Clamp(viewPosition.Value.Line, 1, _editor.Document.LineCount);
        DocumentLine documentLine = _editor.Document.GetLineByNumber(line);
        int column = Math.Clamp(viewPosition.Value.Column, 1, documentLine.Length + 1);
        return _editor.Document.GetOffset(line, column);
    }

    private bool TryAdvanceSnippetTabStop()
    {
        if (_editor?.Document is null || _editor.TextArea is null)
            return false;

        SqlEditorSnippetTabStopSession? session = SqlEditorSnippetTabStopSessionStore.TryGet(_editor.Document);
        if (session is null)
            return false;

        if (session.MoveToNext(_editor.TextArea))
            return true;

        SqlEditorSnippetTabStopSessionStore.Clear(_editor.Document);
        return false;
    }

    private void ExplainButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = RunExplainWithModalAsync(includeAnalyze: false);
    }

    private void BenchmarkButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = RunBenchmarkWithModalAsync();
    }

    private void ExecuteAllButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = ExecuteAllWithToastAsync();
    }

    private void FormatSqlButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = FormatSqlWithToastAsync();
    }

    private async Task RunExplainWithModalAsync(bool includeAnalyze)
    {
        if (_vm is null)
            return;

        string sql = ResolveCurrentExecutionSqlForTools();

        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is not Window owner)
            return;

        if (owner.DataContext is ShellViewModel shell
            && shell.TryOpenSqlExplainPreview(
                sql,
                _vm.ActiveTabProvider,
                _vm.GetActiveConnectionConfigForTools()))
        {
            return;
        }

        await _vm.RunExplainForSqlAsync(sql, includeAnalyze);

        var dialog = new SqlToolOutputDialogWindow(
            title: "SQL Explain",
            summary: _vm.ExplainSummaryText,
            details: _vm.ExplainRawOutput);
        await dialog.ShowDialog(owner);
    }

    private async Task RunBenchmarkWithModalAsync()
    {
        if (_vm is null)
            return;

        string sql = ResolveCurrentExecutionSqlForTools();

        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is not Window owner)
            return;

        if (owner.DataContext is ShellViewModel shell
            && shell.TryOpenSqlBenchmarkPreview(
                sql,
                _vm.GetActiveConnectionConfigForTools()))
        {
            return;
        }

        await _vm.RunBenchmarkForSqlAsync(sql);

        string details = string.IsNullOrWhiteSpace(_vm.BenchmarkSummaryText)
            ? _vm.BenchmarkProgressText
            : $"{_vm.BenchmarkProgressText}{Environment.NewLine}{Environment.NewLine}{_vm.BenchmarkSummaryText}";

        var dialog = new SqlToolOutputDialogWindow(
            title: "SQL Benchmark",
            summary: _vm.BenchmarkProgressText,
            details: details);
        await dialog.ShowDialog(owner);
    }

    private string ResolveCurrentExecutionSqlForTools()
    {
        if (_vm is null || _editor is null)
            return string.Empty;

        FlushPendingEditorTextToViewModel();

        string? sql = _vm.GetSqlForExecution(
            _editor.SelectionStart,
            _editor.SelectionLength,
            _editor.CaretOffset);
        return string.IsNullOrWhiteSpace(sql) ? _vm.ActiveTab.SqlText ?? string.Empty : sql;
    }

    private static string L(string key, string fallback)
    {
        string value = LocalizationService.Instance[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    private void ConfigureBracketHighlighting()
    {
        if (_editor?.TextArea?.TextView is not TextView textView)
            return;

        _bracketHighlightRenderer ??= new SqlBracketHighlightRenderer();
        if (!textView.BackgroundRenderers.Contains(_bracketHighlightRenderer))
            textView.BackgroundRenderers.Add(_bracketHighlightRenderer);

        _executionStatementHighlightRenderer ??= new SqlExecutionStatementHighlightRenderer();
        if (!textView.BackgroundRenderers.Contains(_executionStatementHighlightRenderer))
            textView.BackgroundRenderers.Add(_executionStatementHighlightRenderer);
    }

    private void OnEditorCaretPositionChanged(object? sender, EventArgs e)
    {
        ScheduleBracketHighlightRefresh(immediate: false);
        UpdateViewModelCursorPosition();
        ScheduleSignatureHelpRefresh(immediate: false);
    }

    private void ScheduleBracketHighlightRefresh(bool immediate)
    {
        if (_editor is null || _bracketHighlightRenderer is null)
            return;

        if (immediate)
        {
            _bracketHighlightDebounceTimer?.Stop();
            RefreshBracketHighlight();
            return;
        }

        _bracketHighlightDebounceTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(BracketHighlightDebounceMs) };
        _bracketHighlightDebounceTimer.Interval = TimeSpan.FromMilliseconds(BracketHighlightDebounceMs);
        _bracketHighlightDebounceTimer.Tick -= OnBracketHighlightDebounceTick;
        _bracketHighlightDebounceTimer.Tick += OnBracketHighlightDebounceTick;
        _bracketHighlightDebounceTimer.Stop();
        _bracketHighlightDebounceTimer.Start();
    }

    private void OnBracketHighlightDebounceTick(object? sender, EventArgs e)
    {
        _bracketHighlightDebounceTimer?.Stop();
        RefreshBracketHighlight();
    }

    private void ScheduleSignatureHelpRefresh(bool immediate)
    {
        if (_editor is null || _vm is null)
            return;

        if (immediate)
        {
            _signatureHelpDebounceTimer?.Stop();
            UpdateSignatureHelpFromCaret();
            return;
        }

        _signatureHelpDebounceTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SignatureHelpDebounceMs) };
        _signatureHelpDebounceTimer.Interval = TimeSpan.FromMilliseconds(SignatureHelpDebounceMs);
        _signatureHelpDebounceTimer.Tick -= OnSignatureHelpDebounceTick;
        _signatureHelpDebounceTimer.Tick += OnSignatureHelpDebounceTick;
        _signatureHelpDebounceTimer.Stop();
        _signatureHelpDebounceTimer.Start();
    }

    private void OnSignatureHelpDebounceTick(object? sender, EventArgs e)
    {
        _signatureHelpDebounceTimer?.Stop();
        UpdateSignatureHelpFromCaret();
    }

    private void RefreshExecutionStatementHighlight()
    {
        if (_editor?.TextArea?.TextView is not TextView textView)
            return;

        _executionStatementHighlightRenderer ??= new SqlExecutionStatementHighlightRenderer();
        int start = _vm?.IsExecuting == true ? _vm.ActiveExecutionStatementStartLine : 0;
        int end = _vm?.IsExecuting == true ? _vm.ActiveExecutionStatementEndLine : 0;
        _executionStatementHighlightRenderer.Update(start, end);
        textView.InvalidateLayer(KnownLayer.Selection);
    }

    private void UpdateViewModelCursorPosition()
    {
        if (_editor?.TextArea?.Caret is not Caret caret || _vm is null)
            return;

        _vm.UpdateCursorPosition(caret.Line, caret.Column);
    }

    private void UpdateSignatureHelpFromCaret()
    {
        if (_editor is null || _vm is null)
            return;

        _vm.UpdateSignatureHelp(_editor.Text ?? string.Empty, _editor.CaretOffset);
    }

    private void RefreshBracketHighlight()
    {
        if (_editor is null || _bracketHighlightRenderer is null)
            return;

        _bracketHighlightRenderer.Update(_editor.Text ?? string.Empty, _editor.CaretOffset);
        _editor.TextArea?.TextView.InvalidateLayer(_bracketHighlightRenderer.Layer);
    }

    private void ConfigureSqlFolding(TextArea textArea)
    {
        _foldingManager = FoldingManager.Install(textArea);
    }

    private void DisposeFoldingManager()
    {
        if (_foldingManager is not null)
            FoldingManager.Uninstall(_foldingManager);

        _foldingManager = null;
    }

    private void ScheduleFoldingRefresh()
    {
        if (_editor?.Document is null || _foldingManager is null)
            return;

        int intervalMs = (_editor.Text?.Length ?? 0) > 12_000 ? 320 : 220;
        _foldingRefreshTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(intervalMs) };
        _foldingRefreshTimer.Interval = TimeSpan.FromMilliseconds(intervalMs);
        _foldingRefreshTimer.Tick -= OnFoldingRefreshTimerTick;
        _foldingRefreshTimer.Tick += OnFoldingRefreshTimerTick;
        _foldingRefreshTimer.Stop();
        _foldingRefreshTimer.Start();
    }

    private async void OnFoldingRefreshTimerTick(object? sender, EventArgs e)
    {
        _foldingRefreshTimer?.Stop();

        if (_editor?.Document is null || _foldingManager is null)
            return;

        _foldingRefreshCts?.Cancel();
        _foldingRefreshCts?.Dispose();
        _foldingRefreshCts = new CancellationTokenSource();
        CancellationToken ct = _foldingRefreshCts.Token;
        long requestVersion = Interlocked.Increment(ref _foldingRefreshVersion);

        string textSnapshot = _editor.Text ?? string.Empty;
        IReadOnlyList<AkkornStudio.UI.Services.SqlEditor.SqlToken> tokens;
        try
        {
            tokens = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                IReadOnlyList<AkkornStudio.UI.Services.SqlEditor.SqlToken> localTokens = _sqlTokenizer.Tokenize(textSnapshot);
                ct.ThrowIfCancellationRequested();
                return localTokens;
            }, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (requestVersion != _foldingRefreshVersion)
            return;

        if (_editor?.Document is null || _foldingManager is null)
            return;

        _sqlFoldingStrategy.UpdateFoldings(_foldingManager, _editor.Document, tokens);
    }

    private void CollapseCurrentFolding()
    {
        if (_foldingManager is null || _editor is null)
            return;

        FoldingSection? current = _foldingManager
            .GetFoldingsContaining(_editor.CaretOffset)
            .OrderBy(f => f.Length)
            .FirstOrDefault();

        if (current is not null)
            current.IsFolded = true;
    }

    private void ExpandCurrentFolding()
    {
        if (_foldingManager is null || _editor is null)
            return;

        foreach (FoldingSection folding in _foldingManager.GetFoldingsContaining(_editor.CaretOffset))
            folding.IsFolded = false;
    }

    private bool HandleFoldingChord(KeyEventArgs e)
    {
        if (!_awaitingFoldingChord || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return false;

        if (e.Key == Key.D0)
        {
            CollapseAllFoldings();
            ResetFoldingChord();
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.J)
        {
            ExpandAllFoldings();
            ResetFoldingChord();
            e.Handled = true;
            return true;
        }

        ResetFoldingChord();
        return false;
    }

    private void StartFoldingChord()
    {
        _awaitingFoldingChord = true;
        _foldingChordTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _foldingChordTimer.Tick -= OnFoldingChordTimeout;
        _foldingChordTimer.Tick += OnFoldingChordTimeout;
        _foldingChordTimer.Stop();
        _foldingChordTimer.Start();
    }

    private void OnFoldingChordTimeout(object? sender, EventArgs e)
    {
        ResetFoldingChord();
    }

    private void ResetFoldingChord()
    {
        _awaitingFoldingChord = false;
        _foldingChordTimer?.Stop();
    }

    private void CollapseAllFoldings()
    {
        if (_foldingManager is null)
            return;

        foreach (FoldingSection folding in _foldingManager.AllFoldings)
            folding.IsFolded = true;
    }

    private void ExpandAllFoldings()
    {
        if (_foldingManager is null)
            return;

        foreach (FoldingSection folding in _foldingManager.AllFoldings)
            folding.IsFolded = false;
    }

    private void ConfigureGoToLineLocalization()
    {
        if (_goToLineLabel is not null)
            _goToLineLabel.Text = L("sqlEditor.gotoLine.label", "Ir para linha");

        if (_goToLineInput is not null)
            _goToLineInput.Watermark = L("sqlEditor.gotoLine.watermark", "Numero da linha");
    }

    private void FocusEditor()
    {
        if (_editor is null || !IsVisible)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_editor is null || !_editor.IsVisible)
                return;

            EnsureEditorReady();
            _editor.Focus();
            _editor.TextArea?.Focus();
        }, DispatcherPriority.Background);
    }

    private void FocusEditorAtDocumentEnd()
    {
        if (_editor is null || !IsVisible)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_editor is null || !_editor.IsVisible)
                return;

            EnsureEditorReady();
            int endOffset = _editor.Document?.TextLength ?? 0;
            _editor.CaretOffset = Math.Clamp(endOffset, 0, endOffset);
            _editor.Focus();
            _editor.TextArea?.Focus();
        }, DispatcherPriority.Input);
    }

    private void ShowGoToLineOverlay()
    {
        if (_editor is null || _goToLineOverlay is null || _goToLineInput is null)
            return;

        _goToLineOriginCaretOffset = _editor.CaretOffset;
        _goToLineInput.Text = (_editor.TextArea?.Caret.Line ?? 1).ToString(CultureInfo.InvariantCulture);
        ResetGoToLineValidationState();
        _goToLineOverlay.IsVisible = true;

        Dispatcher.UIThread.Post(() =>
        {
            _goToLineInput.Focus();
            _goToLineInput.SelectAll();
        }, DispatcherPriority.Background);
    }

    private void CursorPositionButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ShowGoToLineOverlay();
    }

    private void GoToLineInput_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseGoToLineOverlay(restoreOriginalCaret: true);
            e.Handled = true;
            return;
        }

        if (e.Key is not (Key.Enter or Key.Return))
            return;

        if (TryGoToLineFromInput())
        {
            CloseGoToLineOverlay(restoreOriginalCaret: false);
        }
        else
        {
            SetGoToLineValidationErrorState();
        }

        e.Handled = true;
    }

    private bool TryGoToLineFromInput()
    {
        if (_editor is null || _goToLineInput is null || _editor.Document is null)
            return false;

        if (!int.TryParse(_goToLineInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int lineNumber))
            return false;

        if (lineNumber < 1 || lineNumber > _editor.Document.LineCount)
            return false;

        DocumentLine line = _editor.Document.GetLineByNumber(lineNumber);
        _editor.CaretOffset = line.Offset;
        _editor.ScrollToLine(lineNumber);
        _editor.TextArea?.Caret.BringCaretToView();
        HighlightGoToLine(lineNumber);
        return true;
    }

    private void CloseGoToLineOverlay(bool restoreOriginalCaret)
    {
        if (_goToLineOverlay is null)
            return;

        _goToLineOverlay.IsVisible = false;
        ResetGoToLineValidationState();

        if (restoreOriginalCaret && _editor is not null)
        {
            int maxOffset = _editor.Document?.TextLength ?? 0;
            _editor.CaretOffset = Math.Clamp(_goToLineOriginCaretOffset, 0, maxOffset);
            _editor.TextArea?.Caret.BringCaretToView();
        }

        FocusEditor();
    }

    private void ResetGoToLineValidationState()
    {
        if (_goToLineInput is null)
            return;

        _goToLineInput.BorderBrush = _goToLineDefaultBorderBrush;
    }

    private void SetGoToLineValidationErrorState()
    {
        if (_goToLineInput is null)
            return;

        if (Application.Current?.TryFindResource("StatusErrorBrush", out object? resource) == true && resource is IBrush brush)
        {
            _goToLineInput.BorderBrush = brush;
            return;
        }

        _goToLineInput.BorderBrush = _goToLineDefaultBorderBrush;
    }

    private void HighlightGoToLine(int lineNumber)
    {
        if (_editor?.TextArea?.TextView is not TextView textView)
            return;

        _defaultTextViewCurrentLineBackground ??= textView.CurrentLineBackground;
        _goToLineHighlightBrush ??= ResolveGoToLineHighlightBrush();

        textView.CurrentLineBackground = _goToLineHighlightBrush;
        textView.HighlightedLine = lineNumber;
        textView.InvalidateLayer(KnownLayer.Background);

        _goToLineHighlightTimer?.Stop();
        _goToLineHighlightTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _goToLineHighlightTimer.Tick -= OnGoToLineHighlightTimerTick;
        _goToLineHighlightTimer.Tick += OnGoToLineHighlightTimerTick;
        _goToLineHighlightTimer.Start();
    }

    private IBrush ResolveGoToLineHighlightBrush()
    {
        if (Application.Current?.TryFindResource("AccentSubtleBrush", out object? accentBrush) == true && accentBrush is IBrush resolvedAccent)
            return resolvedAccent;

        return _defaultTextViewCurrentLineBackground ?? Brushes.Transparent;
    }

    private void OnGoToLineHighlightTimerTick(object? sender, EventArgs e)
    {
        _goToLineHighlightTimer?.Stop();

        if (_editor?.TextArea?.TextView is not TextView textView)
            return;

        textView.HighlightedLine = 0;
        if (_defaultTextViewCurrentLineBackground is not null)
            textView.CurrentLineBackground = _defaultTextViewCurrentLineBackground;

        textView.InvalidateLayer(KnownLayer.Background);
    }
}
