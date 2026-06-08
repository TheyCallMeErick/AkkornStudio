using System.IO;
using Avalonia.Controls;
using AkkornStudio.Nodes;
using AkkornStudio.UI.Controls;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.Services.Workspace.Models;
using AkkornStudio.UI.Serialization;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Services;

/// <summary>
/// Manages auto-save scheduling and session restoration.
/// Handles debounced saves, session file cleanup, and corruption detection.
/// </summary>
public class SessionManagementService(
    Window window,
    CanvasViewModel vm,
    CanvasViewModel? ddlVm = null,
    Func<CanvasViewModel?>? ddlVmResolver = null,
    Func<WorkspaceDocumentType>? activeDocumentTypeResolver = null,
    Action<WorkspaceDocumentType>? applyActiveDocumentType = null,
    Func<IReadOnlyList<OpenWorkspaceDocument>>? workspaceDocumentsResolver = null,
    Func<Guid?>? activeWorkspaceDocumentIdResolver = null,
    Action<SavedWorkspaceDocumentsCanvas>? applyWorkspaceDocumentsSnapshot = null,
    Action? invalidateActiveCanvasWires = null,
    Func<WorkspaceTabsSaveSnapshot?>? workspaceTabsSnapshotResolver = null)
{
    private readonly Window _window = window;
    private readonly CanvasViewModel _vm = vm;
    private readonly CanvasViewModel? _ddlVm = ddlVm;
    private readonly Func<CanvasViewModel?>? _ddlVmResolver = ddlVmResolver;
    private readonly Func<WorkspaceDocumentType>? _activeDocumentTypeResolver = activeDocumentTypeResolver;
    private readonly Action<WorkspaceDocumentType>? _applyActiveDocumentType = applyActiveDocumentType;
    private readonly Func<IReadOnlyList<OpenWorkspaceDocument>>? _workspaceDocumentsResolver = workspaceDocumentsResolver;
    private readonly Func<Guid?>? _activeWorkspaceDocumentIdResolver = activeWorkspaceDocumentIdResolver;
    private readonly Action<SavedWorkspaceDocumentsCanvas>? _applyWorkspaceDocumentsSnapshot = applyWorkspaceDocumentsSnapshot;
    private readonly Action? _invalidateActiveCanvasWires = invalidateActiveCanvasWires;
    private readonly Func<WorkspaceTabsSaveSnapshot?>? _workspaceTabsSnapshotResolver = workspaceTabsSnapshotResolver;
    private readonly object _autoSaveLock = new();  // Synchronization for _autoSaveCts
    private CancellationTokenSource? _autoSaveCts;

    // Column lookup for restoring TableSource nodes from the demo catalog
    private static readonly IReadOnlyDictionary<
        string,
        IReadOnlyList<(string Name, PinDataType Type)>
    > ColumnLookup = CanvasViewModel.DemoCatalog.ToDictionary(t => t.FullName, t => t.Cols);

    private static string AppDataDir =>
        global::AkkornStudio.UI.AppConstants.AppDataDirectory;

    private static string SessionFile => Path.Combine(AppDataDir, "last-session.vsaq");
    private static string SessionTmp => Path.Combine(AppDataDir, "last-session.vsaq.tmp");

    public void Wire()
    {
        // Wire banner buttons
        Button? restoreBtn = _window.FindControl<Button>("RestoreSessionBtn");
        Button? dismissBtn = _window.FindControl<Button>("DismissRestoreBtn");
        if (restoreBtn is not null)
            restoreBtn.Click += async (_, _) => await RestoreSessionAsync();
        if (dismissBtn is not null)
            dismissBtn.Click += (_, _) => DismissSession();

        // Subscribe to canvas changes via IsDirty
        _vm.PropertyChanged += (_, e) =>
        {
            if (
                e.PropertyName
                is nameof(CanvasViewModel.IsDirty)
                    or nameof(CanvasViewModel.Zoom)
                    or nameof(CanvasViewModel.PanOffset)
            )
                ScheduleAutoSave();
        };

        // Force save on close (in addition to layout save)
        // Avoid async void: use fire-and-forget with proper exception handling
        _window.Closing += (_, _) =>
        {
            // Fire and forget the save operation - exceptions are caught in SaveSessionNowAsync
            _ = SaveSessionNowAsync();
        };
    }

    private void ScheduleAutoSave()
    {
        lock (_autoSaveLock)
        {
            _autoSaveCts?.Cancel();
            _autoSaveCts?.Dispose();
            _autoSaveCts = new CancellationTokenSource();
            CancellationToken token = _autoSaveCts.Token;

            Task.Delay(1500, token)
                .ContinueWith(
                    _ =>
                    {
                        if (!token.IsCancellationRequested)
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                _ = SaveSessionNowAsync()
                            );
                    },
                    TaskScheduler.Default
                );
        }
    }

    private async Task SaveSessionNowAsync()
    {
        try
        {
            CanvasViewModel? ddlVm = ResolveDdlCanvas();
            Directory.CreateDirectory(AppDataDir);
            string json = CanvasSerializer.SerializeWorkspace(
                _vm,
                ddlVm,
                activeDocumentType: _activeDocumentTypeResolver?.Invoke() ?? WorkspaceDocumentType.QueryCanvas);
            if (_workspaceDocumentsResolver is not null)
            {
                IReadOnlyList<OpenWorkspaceDocument> workspaceDocuments = _workspaceDocumentsResolver.Invoke();
                if (workspaceDocuments.Count > 0)
                {
                    WorkspaceTabsSaveSnapshot? tabsSnapshot = _workspaceTabsSnapshotResolver?.Invoke();
                    json = CanvasSerializer.SerializeWorkspaceDocuments(
                        workspaceDocuments,
                        _activeWorkspaceDocumentIdResolver?.Invoke(),
                        documentTabs: tabsSnapshot?.DocumentTabs,
                        tabs: tabsSnapshot?.Tabs,
                        activeTabId: tabsSnapshot?.ActiveTabId);
                }
            }
            await File.WriteAllTextAsync(SessionTmp, json);
            File.Move(SessionTmp, SessionFile, overwrite: true);
        }
        catch
        { /* best effort */
        }
    }

    public void CheckForSession()
    {
        if (!CanvasSerializer.IsValidFile(SessionFile))
            return;
        Border? banner = _window.FindControl<Border>("RestoreBanner");
        if (banner is not null)
            banner.IsVisible = true;
    }

    private async Task RestoreSessionAsync()
    {
        Border? banner = _window.FindControl<Border>("RestoreBanner");
        if (banner is not null)
            banner.IsVisible = false;

        try
        {
            CanvasLoadResult result = await CanvasSerializer.LoadFromFileAsync(
                SessionFile,
                _vm,
                ResolveDdlCanvas(),
                ColumnLookup
            );
            if (!result.Success)
            {
                _vm.DataPreview.ShowError(
                    string.Format(L("session.restore.failedWithReason", "Restore failed: {0}"), result.Error),
                    null
                );
                try
                {
                    File.Delete(SessionFile);
                }
                catch
                { /* ignore */
                }
                return;
            }

            _vm.IsDirty = false;
            _invalidateActiveCanvasWires?.Invoke();
            if (_applyWorkspaceDocumentsSnapshot is not null)
            {
                SavedWorkspaceDocumentsCanvas? snapshot = await CanvasSerializer.TryReadWorkspaceDocumentsFromFileAsync(SessionFile);
                if (snapshot is not null)
                    _applyWorkspaceDocumentsSnapshot(snapshot);
            }
            if (result.ActiveDocumentType is WorkspaceDocumentType activeDocumentType)
                _applyActiveDocumentType?.Invoke(activeDocumentType);
            if (result.Warnings is { Count: > 0 })
                _vm.NotifyWarning(
                    L("session.restore.successWithWarnings", "Session restored with warnings."),
                    $"{SessionFile}{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine, result.Warnings)}"
                );
            else
                _vm.NotifySuccess(L("session.restore.success", "Session restored successfully."), SessionFile);

            // Surface migration warnings as diagnostics
            if (result.Warnings is { Count: > 0 })
                foreach (string w in result.Warnings)
                    _vm.Diagnostics.AddWarning(
                        area: L("diagnostics.canvasMigration", "Canvas Migration"),
                        message: string.Format(L("diagnostics.canvasMigration.sessionRestoreWarning", "Session restore: {0}"), w),
                        recommendation: L("diagnostics.recommendation.saveMigratedSchema", "Review diagnostics and save the canvas to persist the migrated schema."),
                        openPanel: true
                    );
        }
        catch (Exception ex)
        {
            _vm.DataPreview.ShowError(
                string.Format(L("session.restore.failedWithReason", "Restore failed: {0}"), ex.Message),
                ex
            );
            // Session corrupted — delete it
            try
            {
                File.Delete(SessionFile);
            }
            catch
            { /* ignore */
            }
        }
    }

    private void DismissSession()
    {
        Border? banner = _window.FindControl<Border>("RestoreBanner");
        if (banner is not null)
            banner.IsVisible = false;
        try
        {
            File.Delete(SessionFile);
        }
        catch
        { /* ignore */
        }
    }

    private static string L(string key, string fallback)
    {
        string value = LocalizationService.Instance[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    private CanvasViewModel? ResolveDdlCanvas()
    {
        return _ddlVmResolver?.Invoke() ?? _ddlVm;
    }
}
