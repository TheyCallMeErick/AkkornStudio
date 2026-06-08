using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Contracts;
using AkkornStudio.UI.ViewModels;
using System.IO;

namespace AkkornStudio.UI.Controls.Shell;

public partial class DdlSchemaAnalysisWorkspaceControl : UserControl
{
    private SchemaAnalysisPanelViewModel? _subscribedSchemaAnalysisPanel;

    public DdlSchemaAnalysisWorkspaceControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UnsubscribePanelEvents();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        UnsubscribePanelEvents();

        if (DataContext is not DdlSchemaAnalysisWorkspaceViewModel vm)
            return;

        _subscribedSchemaAnalysisPanel = vm.SchemaAnalysisPanel;
        _subscribedSchemaAnalysisPanel.CopySqlRequested += OnCopySqlRequested;
    }

    private void OnHostKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not DdlSchemaAnalysisWorkspaceViewModel vm)
            return;

        if (e.Source is InputElement source && source is TextBox)
            return;

        if (e.Key == Key.Down)
        {
            if (vm.SchemaAnalysisPanel.SelectNextIssueCommand.CanExecute(null))
            {
                vm.SchemaAnalysisPanel.SelectNextIssueCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.Up)
        {
            if (vm.SchemaAnalysisPanel.SelectPreviousIssueCommand.CanExecute(null))
            {
                vm.SchemaAnalysisPanel.SelectPreviousIssueCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.Delete)
        {
            if (vm.SchemaAnalysisPanel.RemoveSelectedIgnoredTableCommand.CanExecute(null))
            {
                vm.SchemaAnalysisPanel.RemoveSelectedIgnoredTableCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void IgnoredTableInput_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        if (DataContext is not DdlSchemaAnalysisWorkspaceViewModel vm)
            return;

        if (!vm.SchemaAnalysisPanel.AddIgnoredTableCommand.CanExecute(null))
            return;

        vm.SchemaAnalysisPanel.AddIgnoredTableCommand.Execute(null);
        e.Handled = true;
    }

    private void IssueListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not DdlSchemaAnalysisWorkspaceViewModel vm)
            return;

        // Each issue group renders its own ListBox bound OneWay to the shared SelectedIssue.
        // Only react to a real user selection (AddedItems) so that the deselect that fires on
        // the other group lists does not clear the shared selection.
        foreach (object? added in e.AddedItems)
        {
            if (added is SchemaIssue issue)
            {
                vm.SchemaAnalysisPanel.SelectedIssue = issue;
                return;
            }
        }
    }

    private async void CopySqlButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        if (DataContext is DdlSchemaAnalysisWorkspaceViewModel vm)
            await CopySqlToClipboardAsync(vm.SchemaAnalysisPanel.SelectedSqlCandidate?.Sql);

        e.Handled = true;
    }

    private async void OnCopySqlRequested(string sql)
    {
        await CopySqlToClipboardAsync(sql);
    }

    private async Task CopySqlToClipboardAsync(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return;

        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is null)
            return;

        await topLevel.Clipboard.SetTextAsync(sql);
    }

    private void UnsubscribePanelEvents()
    {
        if (_subscribedSchemaAnalysisPanel is null)
            return;

        _subscribedSchemaAnalysisPanel.CopySqlRequested -= OnCopySqlRequested;
        _subscribedSchemaAnalysisPanel = null;
    }

    private void PlaygroundScope_OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (DataContext is not DdlSchemaAnalysisWorkspaceViewModel vm)
            return;

        if (sender is not Control control)
            return;

        string? focusKey = control.Tag?.ToString();
        vm.HighlightPlaygroundScope(focusKey);
    }

    private void PlaygroundScope_OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is not DdlSchemaAnalysisWorkspaceViewModel vm)
            return;

        vm.ClearPlaygroundScopeHighlight();
    }

    private void PlaygroundField_OnGotFocus(object? sender, GotFocusEventArgs e)
    {
        if (DataContext is not DdlSchemaAnalysisWorkspaceViewModel vm)
            return;

        if (sender is not Control control)
            return;

        vm.HighlightPlaygroundScope(control.Tag?.ToString());
    }

    private void PlaygroundField_OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DdlSchemaAnalysisWorkspaceViewModel vm)
            return;

        vm.ClearPlaygroundScopeHighlight();
    }

    private async void ExportTrendsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        if (DataContext is not DdlSchemaAnalysisWorkspaceViewModel vm)
            return;

        await ExportMarkdownAsync(
            title: "Exportar tendencias de estrutura",
            suggestedFileName: "tendencias-estrutura.md",
            content: vm.BuildTrendsExportMarkdown());
    }

    private async void ExportIssuesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        if (DataContext is not DdlSchemaAnalysisWorkspaceViewModel vm)
            return;

        await ExportMarkdownAsync(
            title: "Exportar issues de estrutura",
            suggestedFileName: "issues-estrutura.md",
            content: vm.BuildIssuesExportMarkdown());
    }

    private async Task ExportMarkdownAsync(string title, string suggestedFileName, string content)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
            return;

        var mdFileType = new FilePickerFileType("Markdown")
        {
            Patterns = ["*.md"],
        };

        IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = title,
                DefaultExtension = "md",
                FileTypeChoices = [mdFileType],
                SuggestedFileName = suggestedFileName,
            });

        string? path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        await File.WriteAllTextAsync(path, content);
    }

    private void Confidence95Button_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        if (DataContext is not DdlSchemaAnalysisWorkspaceViewModel vm)
            return;

        vm.SchemaAnalysisPanel.MinConfidenceFilter = 0.95d;
    }

    private void Confidence80Button_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        if (DataContext is not DdlSchemaAnalysisWorkspaceViewModel vm)
            return;

        vm.SchemaAnalysisPanel.MinConfidenceFilter = 0.80d;
    }

    private void OpenAdvancedFiltersModal_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        if (AdvancedFiltersModalOverlay is not null)
            AdvancedFiltersModalOverlay.IsVisible = true;
    }

    private void CloseAdvancedFiltersModal_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        if (AdvancedFiltersModalOverlay is not null)
            AdvancedFiltersModalOverlay.IsVisible = false;
    }

    private void AdvancedFiltersOverlay_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _ = sender;

        if (AdvancedFiltersModalOverlay is null)
            return;

        if (e.Source == AdvancedFiltersModalOverlay)
            AdvancedFiltersModalOverlay.IsVisible = false;
    }

    private void AdvancedFiltersModalCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _ = sender;
        e.Handled = true;
    }
}
