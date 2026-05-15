using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Material.Icons;
using Material.Icons.Avalonia;
using AkkornStudio.UI.Services.SqlEditor.Reports;
using AkkornStudio.UI.ViewModels;
using System.ComponentModel;
using System.Data;
using System.IO;

namespace AkkornStudio.UI.Controls.SqlEditor;

public partial class SqlResultPageControl : UserControl
{
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
            string columnType = ResolveColumnTypeLabel(column.DataType);
            MaterialIconKind columnIcon = ResolveColumnTypeIcon(column.DataType);

            Grid headerPanel = BuildColumnHeader(columnName, columnType, columnIcon);

            ResultGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = headerPanel,
                SortMemberPath = columnName,
                IsReadOnly = !viewModel.IsColumnEditable(columnName),
                CellTemplate = new FuncDataTemplate<DataRowView>((rowView, _) =>
                {
                    var textBlock = new TextBlock
                    {
                        Foreground = ResolveBrush("TextPrimaryBrush", new SolidColorBrush(Color.Parse("#E8EAED"))),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    };

                    object? value = ResolveCellValue(rowView, columnName);
                    string text = FormatCellText(value);
                    textBlock.Text = text;
                    if (string.Equals(text, "NULL", StringComparison.Ordinal))
                    {
                        textBlock.FontStyle = FontStyle.Italic;
                        textBlock.Foreground = ResolveBrush("TextMutedBrush", new SolidColorBrush(Color.Parse("#97A2B8")));
                        textBlock.Opacity = 0.82;
                    }

                    return textBlock;
                }),
                CellEditingTemplate = new FuncDataTemplate<DataRowView>((rowView, _) =>
                {
                    var textBox = new TextBox
                    {
                        Text = FormatCellText(ResolveCellValue(rowView, columnName)),
                    };
                    return textBox;
                }),
            });
        }
    }

    private static object? ResolveCellValue(DataRowView? rowView, string columnName)
    {
        if (rowView?.Row is null || string.IsNullOrWhiteSpace(columnName))
            return null;

        if (!rowView.Row.Table.Columns.Contains(columnName))
            return null;

        return rowView.Row[columnName];
    }

    private static string FormatCellText(object? value)
    {
        if (value is null || value == DBNull.Value)
            return "NULL";

        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static Grid BuildColumnHeader(string columnName, string columnType, MaterialIconKind columnIcon)
    {
        var headerRoot = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 6,
        };

        var typeIcon = new MaterialIcon
        {
            Kind = columnIcon,
            Width = 13,
            Height = 13,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Foreground = ResolveBrush("AccentSecondaryHoverBrush", new SolidColorBrush(Color.Parse("#79A2FF"))),
            Margin = new Thickness(0, 1, 0, 0),
        };
        headerRoot.Children.Add(typeIcon);

        var identityPanel = new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        identityPanel.SetValue(Grid.ColumnProperty, 1);
        identityPanel.Children.Add(new TextBlock
        {
            Text = columnName,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        identityPanel.Children.Add(new TextBlock
        {
            Text = columnType,
            FontSize = 11,
            Foreground = ResolveBrush("TextMutedBrush", new SolidColorBrush(Color.Parse("#9AA3B2"))),
        });
        headerRoot.Children.Add(identityPanel);

        var menuButton = new Button
        {
            Content = new MaterialIcon
            {
                Kind = MaterialIconKind.DotsHorizontal,
                Width = 14,
                Height = 14,
            },
            Classes = { "secondary", "compact" },
            Padding = new Thickness(6, 2),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        menuButton.SetValue(Grid.ColumnProperty, 2);
        menuButton.Flyout = BuildColumnHeaderFlyout(columnName);
        headerRoot.Children.Add(menuButton);

        return headerRoot;
    }

    private static Flyout BuildColumnHeaderFlyout(string columnName)
    {
        var stack = new StackPanel
        {
            Spacing = 6,
        };
        stack.Children.Add(CreateHeaderCommandButton("Ordenar ASC", "SortColumnAscendingByNameCommand", columnName));
        stack.Children.Add(CreateHeaderCommandButton("Ordenar DESC", "SortColumnDescendingByNameCommand", columnName));
        stack.Children.Add(CreateHeaderCommandButton("Filtrar por esta coluna", "PrepareFilterForColumnCommand", columnName));
        stack.Children.Add(CreateHeaderCommandButton("Agrupar por esta coluna", "GroupByColumnNameCommand", columnName));
        stack.Children.Add(CreateHeaderCommandButton("Ocultar coluna", "HideColumnByNameCommand", columnName));
        stack.Children.Add(CreateHeaderCommandButton("Copiar nome da coluna", "CopyColumnNameCommand", columnName));
        stack.Children.Add(CreateHeaderCommandButton("Copiar tipo da coluna", "CopyColumnTypeCommand", columnName));
        stack.Children.Add(CreateHeaderCommandButton("Ver perfil da coluna", "OpenColumnProfileForColumnCommand", columnName));

        return new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Content = new Border
            {
                Classes = { "surface-card" },
                Padding = new Thickness(8),
                MinWidth = 210,
                Child = stack,
            },
        };
    }

    private static Button CreateHeaderCommandButton(string content, string commandBindingPath, string columnName)
    {
        var button = new Button
        {
            Content = content,
            Classes = { "secondary", "compact" },
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        };
        button.Bind(Button.CommandProperty, new Binding(commandBindingPath));
        button.CommandParameter = columnName;
        return button;
    }

    private static string ResolveColumnTypeLabel(Type columnType)
    {
        if (columnType == typeof(string))
            return "text";
        if (columnType == typeof(int) || columnType == typeof(long) || columnType == typeof(short))
            return "int";
        if (columnType == typeof(decimal) || columnType == typeof(double) || columnType == typeof(float))
            return "number";
        if (columnType == typeof(DateTime) || columnType == typeof(DateTimeOffset))
            return "datetime";
        if (columnType == typeof(bool))
            return "bool";

        return columnType.Name.ToLowerInvariant();
    }

    private static MaterialIconKind ResolveColumnTypeIcon(Type columnType)
    {
        Type normalizedType = Nullable.GetUnderlyingType(columnType) ?? columnType;
        if (normalizedType == typeof(string))
            return MaterialIconKind.FormatLetterCase;
        if (normalizedType == typeof(byte[]) || normalizedType == typeof(ReadOnlyMemory<byte>) || normalizedType == typeof(Memory<byte>))
            return MaterialIconKind.File;
        if (normalizedType == typeof(bool))
            return MaterialIconKind.ToggleSwitchOutline;
        if (normalizedType == typeof(DateTime) || normalizedType == typeof(DateTimeOffset))
            return MaterialIconKind.CalendarClockOutline;
        if (normalizedType == typeof(Guid))
            return MaterialIconKind.Pound;
        if (normalizedType == typeof(decimal) || normalizedType == typeof(double) || normalizedType == typeof(float))
            return MaterialIconKind.Sigma;
        if (normalizedType == typeof(byte) || normalizedType == typeof(sbyte)
            || normalizedType == typeof(short) || normalizedType == typeof(ushort)
            || normalizedType == typeof(int) || normalizedType == typeof(uint)
            || normalizedType == typeof(long) || normalizedType == typeof(ulong))
            return MaterialIconKind.Numeric;
        if (string.Equals(normalizedType.Name, "JsonDocument", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedType.Name, "JObject", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedType.Name, "JToken", StringComparison.OrdinalIgnoreCase))
            return MaterialIconKind.CodeJson;

        return MaterialIconKind.TableColumn;
    }

    private void SearchBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        _ = sender;
        if (e.Key != Key.Escape || DataContext is not SqlResultPageViewModel viewModel)
            return;

        viewModel.ClearSearchTextCommand.Execute(null);
        e.Handled = true;
    }

    private void ColumnSearchBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        _ = sender;
        if (e.Key != Key.Escape || DataContext is not SqlResultPageViewModel viewModel)
            return;

        viewModel.ClearColumnSearchTextCommand.Execute(null);
        e.Handled = true;
    }

    private void DensityCompact_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ResultGrid.RowHeight = 32;
        ResultGrid.ColumnHeaderHeight = 40;
    }

    private void DensityComfortable_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ResultGrid.RowHeight = 40;
        ResultGrid.ColumnHeaderHeight = 44;
    }

    private void DensitySpacious_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ResultGrid.RowHeight = 48;
        ResultGrid.ColumnHeaderHeight = 48;
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

