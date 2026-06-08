using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using AkkornStudio.UI.ViewModels;
using System.IO;

namespace AkkornStudio.UI.Controls.Shell;

public partial class DdlSchemaCompareWorkspaceControl : UserControl
{
    public DdlSchemaCompareWorkspaceControl()
    {
        InitializeComponent();
    }

    private async void CopySqlButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        if (DataContext is not DdlSchemaCompareWorkspaceViewModel vm || string.IsNullOrWhiteSpace(vm.GeneratedSql))
            return;

        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is null)
            return;

        await topLevel.Clipboard.SetTextAsync(vm.GeneratedSql);
    }

    private async void ExportSqlButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        if (DataContext is not DdlSchemaCompareWorkspaceViewModel vm || string.IsNullOrWhiteSpace(vm.GeneratedSql))
            return;

        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
            return;

        var sqlFileType = new FilePickerFileType("Arquivos SQL")
        {
            Patterns = ["*.sql"],
        };

        IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Exportar script de sincronizacao",
                DefaultExtension = "sql",
                FileTypeChoices = [sqlFileType],
                SuggestedFileName = "ddl-sync-preview.sql",
            }
        );

        string? path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            await File.WriteAllTextAsync(path, vm.GeneratedSql);
    }

    private void SelectAllSqlButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        SqlPreviewTextBox?.SelectAll();
        SqlPreviewTextBox?.Focus();
    }

    private void DifferenceIncludeCheckBox_OnTapped(object? sender, TappedEventArgs e)
    {
        _ = sender;
        e.Handled = true;
    }
}
