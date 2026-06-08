using System.Text.Json;
using AkkornStudio.UI.Serialization;
using AkkornStudio.UI.Services.ConnectionManager;
using AkkornStudio.UI.Services.Workspace.Models;
using AkkornStudio.UI.ViewModels;
using Xunit;

namespace AkkornStudio.Tests.Unit.ViewModels.Shell;

public sealed class ShellViewModelTabsPersistenceTests
{
    private static ShellViewModel CreateShell() =>
        new(connectionManagerViewModelFactory: ConnectionManagerViewModelFactory.CreateDefault());

    private static string SerializeWithTabs(ShellViewModel shell)
    {
        WorkspaceTabsSaveSnapshot snapshot = shell.BuildTabsSaveSnapshot();
        return CanvasSerializer.SerializeWorkspaceDocuments(
            shell.OpenWorkspaceDocuments,
            shell.ActiveWorkspaceDocumentId,
            documentTabs: snapshot.DocumentTabs,
            tabs: snapshot.Tabs,
            activeTabId: snapshot.ActiveTabId);
    }

    [Fact]
    public void Serialize_IncludesTabMetadata()
    {
        using ShellViewModel shell = CreateShell();
        shell.CreateTab(WorkspaceDocumentType.SqlEditor);

        string json = SerializeWithTabs(shell);

        Assert.Contains("\"Tabs\"", json, StringComparison.Ordinal);
        Assert.Contains("\"TabId\"", json, StringComparison.Ordinal);
        Assert.Contains("\"ActiveTabId\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveAndRestore_PreservesTabsModesAndActiveTab()
    {
        using ShellViewModel shell1 = CreateShell();
        shell1.CreateTab(WorkspaceDocumentType.DdlCanvas); // second tab, becomes active

        string json = SerializeWithTabs(shell1);
        SavedWorkspaceDocumentsCanvas saved =
            JsonSerializer.Deserialize<SavedWorkspaceDocumentsCanvas>(json)!;

        using ShellViewModel shell2 = CreateShell();
        shell2.RestoreWorkspaceDocuments(saved);

        Assert.Equal(2, shell2.Tabs.Count);
        Assert.Contains(shell2.Tabs, t => t.CurrentMode == WorkspaceDocumentType.QueryCanvas);
        Assert.Contains(shell2.Tabs, t => t.CurrentMode == WorkspaceDocumentType.DdlCanvas);
        Assert.NotNull(shell2.ActiveTab);
        Assert.Equal(WorkspaceDocumentType.DdlCanvas, shell2.ActiveTab!.CurrentMode);
    }

    [Fact]
    public void Restore_OldSaveWithoutTabs_WrapsEverythingInOneTab()
    {
        using ShellViewModel shell1 = CreateShell();
        shell1.CreateTab(WorkspaceDocumentType.DdlCanvas);

        string json = SerializeWithTabs(shell1);
        SavedWorkspaceDocumentsCanvas saved =
            JsonSerializer.Deserialize<SavedWorkspaceDocumentsCanvas>(json)! with { Tabs = null, ActiveTabId = null };

        using ShellViewModel shell2 = CreateShell();
        shell2.RestoreWorkspaceDocuments(saved);

        Assert.Single(shell2.Tabs);
    }
}
