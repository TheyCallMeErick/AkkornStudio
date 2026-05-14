using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AkkornStudio.UI.Services.SqlEditor.Reports;
using AkkornStudio.UI.ViewModels;
using System.ComponentModel;
using System.Data;
using System.IO;

namespace AkkornStudio.UI.Controls.SqlEditor;

public partial class SqlResultPageControl : UserControl
{
    private static readonly IValueConverter DbNullToTextConverter = new DbNullToTextValueConverter();
    private SqlResultPageViewModel? _subscribedViewModel;
    private readonly SqlEditorReportExportService _reportExportService = new();
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
            _subscribedViewModel.ExportRequested -= OnExportRequested;
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _subscribedViewModel = DataContext as SqlResultPageViewModel;
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.ClipboardCopyRequested += OnClipboardCopyRequested;
            _subscribedViewModel.ExportRequested += OnExportRequested;
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        RebuildResultGridColumns();
        Dispatcher.UIThread.Post(ApplyPendingHighlightsForLoadedRows);
    }

    private async void OnClipboardCopyRequested(string text)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is null)
            return;

        await topLevel.Clipboard.SetTextAsync(text);
    }

    private async void OnExportRequested(SqlResultPageViewModel.SqlResultExportRequest request)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
            return;

        var fileType = new FilePickerFileType(request.FileTypeTitle)
        {
            Patterns = request.Patterns.ToList(),
            MimeTypes = request.MimeTypes.ToList(),
        };

        IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Export SQL Result",
                DefaultExtension = request.DefaultExtension,
                SuggestedFileName = request.SuggestedFileName,
                FileTypeChoices = [fileType],
            });

        string? path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        await File.WriteAllTextAsync(path, request.Content);
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
        if (e.PropertyName is nameof(SqlResultPageViewModel.RowsView)
            or nameof(SqlResultPageViewModel.Session))
            RebuildResultGridColumns();

        if (e.PropertyName is nameof(SqlResultPageViewModel.PendingEditsCount)
            or nameof(SqlResultPageViewModel.RowsView)
            or nameof(SqlResultPageViewModel.Session))
            Dispatcher.UIThread.Post(ApplyPendingHighlightsForLoadedRows);
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

    private async void ExportReportButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (DataContext is not SqlResultPageViewModel viewModel)
            return;

        if (!viewModel.TryBuildReportExportContext(out SqlEditorReportExportContext? context) || context is null)
            return;

        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is not Window owner || topLevel.StorageProvider is null)
            return;

        var dialogVm = new SqlEditorReportExportDialogViewModel(context.TabTitle);
        var dialog = new SqlEditorReportExportDialogWindow(dialogVm);
        await dialog.ShowDialog(owner);
        if (!dialog.WasConfirmed)
            return;

        string normalizedExtension = dialogVm.SuggestedExtension.TrimStart('.');
        FilePickerFileType reportFileType = GetExportFileType(dialogVm.SelectedType?.Type ?? SqlEditorReportType.HtmlFullFeature);
        IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Export SQL Report",
                DefaultExtension = normalizedExtension,
                SuggestedFileName = dialogVm.FileName,
                FileTypeChoices = [reportFileType],
            });

        string? outputPath = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(outputPath))
            return;

        SqlEditorReportExportRequest request = dialogVm.BuildRequest(outputPath);
        await _reportExportService.ExportAsync(context, request);
    }

    private void RebuildResultGridColumns()
    {
        ResultGrid.Columns.Clear();
        if (DataContext is not SqlResultPageViewModel viewModel || viewModel.RowsView?.Table is not DataTable table)
            return;

        foreach (DataColumn column in table.Columns)
        {
            string columnName = column.ColumnName;
            var displayBinding = new Binding($"[{columnName}]")
            {
                Mode = BindingMode.OneWay,
                Converter = DbNullToTextConverter,
            };
            ResultGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = columnName,
                SortMemberPath = columnName,
                IsReadOnly = !viewModel.IsColumnEditable(columnName),
                CellTemplate = new FuncDataTemplate<DataRowView>((_, _) =>
                {
                    var textBlock = new TextBlock
                    {
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    };
                    textBlock.Bind(TextBlock.TextProperty, displayBinding);
                    return textBlock;
                }),
                CellEditingTemplate = new FuncDataTemplate<DataRowView>((_, _) =>
                {
                    var textBox = new TextBox();
                    textBox.Bind(TextBox.TextProperty, displayBinding);
                    return textBox;
                }),
            });
        }
    }

    private sealed class DbNullToTextValueConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            _ = targetType;
            _ = parameter;
            _ = culture;
            if (value is null || value == DBNull.Value)
                return string.Empty;
            return value.ToString();
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            _ = targetType;
            _ = parameter;
            _ = culture;
            return value;
        }
    }

    private static FilePickerFileType GetExportFileType(SqlEditorReportType reportType)
    {
        return reportType switch
        {
            SqlEditorReportType.JsonContract => new FilePickerFileType("JSON")
            {
                Patterns = ["*.json"],
                MimeTypes = ["application/json", "text/plain"],
            },
            SqlEditorReportType.CsvData => new FilePickerFileType("CSV")
            {
                Patterns = ["*.csv"],
                MimeTypes = ["text/csv", "text/plain"],
            },
            SqlEditorReportType.ExcelWorkbook => new FilePickerFileType("Excel")
            {
                Patterns = ["*.xlsx"],
                MimeTypes = ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"],
            },
            _ => new FilePickerFileType("HTML")
            {
                Patterns = ["*.html", "*.htm"],
                MimeTypes = ["text/html", "text/plain"],
            },
        };
    }

    private static IBrush ResolveBrush(string key, IBrush fallback)
    {
        if (Application.Current?.TryFindResource(key, out object? resource) == true && resource is IBrush brush)
            return brush;

        return fallback;
    }
}
