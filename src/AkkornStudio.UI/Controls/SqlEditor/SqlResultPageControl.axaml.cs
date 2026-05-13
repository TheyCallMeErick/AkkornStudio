using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AkkornStudio.UI.ViewModels;
using System.ComponentModel;
using System.Data;

namespace AkkornStudio.UI.Controls.SqlEditor;

public partial class SqlResultPageControl : UserControl
{
    private SqlResultPageViewModel? _subscribedViewModel;
    private readonly IBrush _pendingCellBackgroundBrush = ResolveBrush(
        "BtnWarningBgBrush",
        new SolidColorBrush(Color.Parse("#3A2A12")));
    private readonly IBrush _pendingCellBorderBrush = ResolveBrush(
        "StatusWarningBrush",
        new SolidColorBrush(Color.Parse("#D9A441")));

    public SqlResultPageControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        ResultGrid.LoadingRow += ResultGrid_OnLoadingRow;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _ = sender;
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.ClipboardCopyRequested -= OnClipboardCopyRequested;
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _subscribedViewModel = DataContext as SqlResultPageViewModel;
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.ClipboardCopyRequested += OnClipboardCopyRequested;
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        Dispatcher.UIThread.Post(ApplyPendingHighlightsForLoadedRows);
    }

    private async void OnClipboardCopyRequested(string text)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is null)
            return;

        await topLevel.Clipboard.SetTextAsync(text);
    }

    private void ResultGrid_OnCellPointerPressed(object? sender, DataGridCellPointerPressedEventArgs e)
    {
        _ = sender;
        if (DataContext is not SqlResultPageViewModel viewModel)
            return;

        if (e.Row?.DataContext is not DataRowView rowView)
            return;

        string? columnName = e.Column?.SortMemberPath;
        if (string.IsNullOrWhiteSpace(columnName))
            return;

        viewModel.SelectCell(rowView, columnName);
    }

    private void ResultGrid_OnAutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        _ = sender;
        if (DataContext is not SqlResultPageViewModel viewModel)
            return;

        string columnName = string.IsNullOrWhiteSpace(e.PropertyName)
            ? e.Column.SortMemberPath
            : e.PropertyName;
        if (string.IsNullOrWhiteSpace(columnName))
            return;

        e.Column.SortMemberPath = columnName;
        e.Column.IsReadOnly = !viewModel.IsColumnEditable(columnName);
    }

    private void ResultGrid_OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        _ = sender;
        if (DataContext is not SqlResultPageViewModel viewModel)
            return;

        ApplyPendingHighlightsToRow(e.Row, viewModel);
    }

    private void ResultGrid_OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        _ = sender;
        if (DataContext is not SqlResultPageViewModel viewModel)
            return;

        if (e.EditAction != DataGridEditAction.Commit)
            return;

        if (e.Row.DataContext is not DataRowView rowView)
            return;

        string? columnName = e.Column.SortMemberPath;
        if (string.IsNullOrWhiteSpace(columnName))
            return;

        if (!TryExtractEditedText(e.EditingElement, out string? editedText))
            return;

        viewModel.SelectCell(rowView, columnName);
        if (viewModel.TryApplyInlineCellEdit(rowView, columnName, editedText, out string? errorMessage))
        {
            Dispatcher.UIThread.Post(ApplyPendingHighlightsForLoadedRows);
            return;
        }

        if (!string.IsNullOrWhiteSpace(errorMessage))
            e.Cancel = true;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        if (e.PropertyName is nameof(SqlResultPageViewModel.PendingEditsCount)
            or nameof(SqlResultPageViewModel.RowsView)
            or nameof(SqlResultPageViewModel.Session))
        {
            Dispatcher.UIThread.Post(ApplyPendingHighlightsForLoadedRows);
        }
    }

    private void ApplyPendingHighlightsForLoadedRows()
    {
        if (DataContext is not SqlResultPageViewModel viewModel)
            return;

        foreach (DataGridRow row in ResultGrid.GetVisualDescendants().OfType<DataGridRow>())
            ApplyPendingHighlightsToRow(row, viewModel);
    }

    private void ApplyPendingHighlightsToRow(DataGridRow row, SqlResultPageViewModel viewModel)
    {
        if (row.DataContext is not DataRowView rowView)
            return;

        foreach (DataGridColumn column in ResultGrid.Columns)
        {
            string? columnName = column.SortMemberPath;
            if (string.IsNullOrWhiteSpace(columnName))
                continue;

            Control? cellContent = column.GetCellContent(row);
            if (cellContent is null)
                continue;

            DataGridCell? cell = cellContent.FindAncestorOfType<DataGridCell>();
            if (cell is null)
                continue;

            if (viewModel.IsCellPending(rowView, columnName))
            {
                cell.Background = _pendingCellBackgroundBrush;
                cell.BorderBrush = _pendingCellBorderBrush;
                ToolTip.SetTip(cell, "Pending local edit");
                continue;
            }

            cell.ClearValue(BackgroundProperty);
            cell.ClearValue(BorderBrushProperty);
            ToolTip.SetTip(cell, null);
        }
    }

    private static bool TryExtractEditedText(Control editingElement, out string? editedText)
    {
        switch (editingElement)
        {
            case TextBox textBox:
                editedText = textBox.Text;
                return true;
            case CheckBox checkBox:
                editedText = checkBox.IsChecked switch
                {
                    true => "true",
                    false => "false",
                    _ => string.Empty,
                };
                return true;
            default:
                editedText = null;
                return false;
        }
    }

    private static IBrush ResolveBrush(string key, IBrush fallback)
    {
        if (Application.Current?.TryFindResource(key, out object? resource) == true && resource is IBrush brush)
            return brush;

        return fallback;
    }
}
