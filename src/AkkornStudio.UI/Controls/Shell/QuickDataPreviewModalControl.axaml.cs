using System.ComponentModel;
using System.Data;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Controls.Shell;

public partial class QuickDataPreviewModalControl : UserControl
{
    private QuickDataPreviewModalViewModel? _vm;

    public QuickDataPreviewModalControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;

        _vm = DataContext as QuickDataPreviewModalViewModel;

        if (_vm is not null)
            _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuickDataPreviewModalViewModel.ResultData))
            RebuildGrid(_vm?.ResultData);
    }

    private void RebuildGrid(DataTable? dt)
    {
        DataGrid? grid = this.FindControl<DataGrid>("ResultDataGrid");
        if (grid is null)
            return;

        grid.Columns.Clear();
        grid.ItemsSource = null;

        if (dt is null || dt.Rows.Count == 0)
            return;

        for (int i = 0; i < dt.Columns.Count; i++)
        {
            int capturedIndex = i;
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = dt.Columns[i].ColumnName,
                IsReadOnly = true,
                CellTemplate = new FuncDataTemplate<object?[]>((row, _) =>
                    new TextBlock
                    {
                        Text = row?[capturedIndex]?.ToString() ?? string.Empty,
                        Padding = new Thickness(8, 4),
                        VerticalAlignment = VerticalAlignment.Center,
                    }),
            });
        }

        var rows = new System.Collections.ObjectModel.ObservableCollection<object?[]>();
        foreach (DataRow row in dt.Rows)
        {
            var arr = new object?[dt.Columns.Count];
            for (int i = 0; i < dt.Columns.Count; i++)
                arr[i] = row.IsNull(i) ? null : row[i];
            rows.Add(arr);
        }

        grid.ItemsSource = rows;
    }
}
