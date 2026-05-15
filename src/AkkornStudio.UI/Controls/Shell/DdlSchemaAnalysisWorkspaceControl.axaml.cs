using Avalonia.Controls;
using Avalonia.Input;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Contracts;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Controls.Shell;

public partial class DdlSchemaAnalysisWorkspaceControl : UserControl
{
    public DdlSchemaAnalysisWorkspaceControl()
    {
        InitializeComponent();
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

        if (sender is not ListBox listBox)
            return;

        if (listBox.SelectedItem is SchemaIssue issue)
            vm.SchemaAnalysisPanel.SelectedIssue = issue;
    }

    private async void CopySqlButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not DdlSchemaAnalysisWorkspaceViewModel vm)
            return;

        string? sql = vm.SchemaAnalysisPanel.SelectedSqlCandidate?.Sql;
        if (string.IsNullOrWhiteSpace(sql))
            return;

        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is null)
            return;

        await topLevel.Clipboard.SetTextAsync(sql);
        e.Handled = true;
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
}

