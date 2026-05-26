using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.Nodes;
using AkkornStudio.UI.Controls;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.Services.Workspace.Models;
using AkkornStudio.UI.Serialization;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Services;

/// <summary>
/// Handles file save/open dialogs and canvas serialization.
/// </summary>
public class FileOperationsService(
    Window window,
    CanvasViewModel vm,
    CanvasViewModel? ddlVm = null,
    Func<CanvasViewModel?>? ddlVmResolver = null,
    Action<bool, bool>? afterLoadCanvasStateChanged = null,
    Func<WorkspaceDocumentType>? activeDocumentTypeResolver = null,
    Action<WorkspaceDocumentType>? applyActiveDocumentType = null,
    Func<IReadOnlyList<OpenWorkspaceDocument>>? workspaceDocumentsResolver = null,
    Func<Guid?>? activeWorkspaceDocumentIdResolver = null,
    Action<SavedWorkspaceDocumentsCanvas>? applyWorkspaceDocumentsSnapshot = null,
    Action<IReadOnlyList<string>>? applySqlEditorSeedScripts = null,
    Action? invalidateActiveCanvasWires = null,
    ILogger<FileOperationsService>? logger = null)
{
    private static readonly FilePickerFileType FileType = new("SQL Architect Canvas")
    {
        Patterns = ["*.vsaq"],
        MimeTypes = ["application/json"],
    };

    private readonly Window _window = window;
    private readonly CanvasViewModel _vm = vm;
    private readonly CanvasViewModel? _ddlVm = ddlVm;
    private readonly Func<CanvasViewModel?>? _ddlVmResolver = ddlVmResolver;
    private readonly Action<bool, bool>? _afterLoadCanvasStateChanged = afterLoadCanvasStateChanged;
    private readonly Func<WorkspaceDocumentType>? _activeDocumentTypeResolver = activeDocumentTypeResolver;
    private readonly Action<WorkspaceDocumentType>? _applyActiveDocumentType = applyActiveDocumentType;
    private readonly Func<IReadOnlyList<OpenWorkspaceDocument>>? _workspaceDocumentsResolver = workspaceDocumentsResolver;
    private readonly Func<Guid?>? _activeWorkspaceDocumentIdResolver = activeWorkspaceDocumentIdResolver;
    private readonly Action<SavedWorkspaceDocumentsCanvas>? _applyWorkspaceDocumentsSnapshot = applyWorkspaceDocumentsSnapshot;
    private readonly Action<IReadOnlyList<string>>? _applySqlEditorSeedScripts = applySqlEditorSeedScripts;
    private readonly Action? _invalidateActiveCanvasWires = invalidateActiveCanvasWires;
    private readonly ILogger<FileOperationsService> _logger = logger ?? NullLogger<FileOperationsService>.Instance;

    public async Task SaveAsync(bool saveAs = false)
    {
        string? path = (!saveAs && _vm.CurrentFilePath is not null) ? _vm.CurrentFilePath : null;

        if (path is null)
        {
            IStorageFile? r = await _window.StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = L("file.saveDialog.title", "Save Canvas"),
                    DefaultExtension = "vsaq",
                    FileTypeChoices = [FileType],
                    SuggestedFileName = L("file.saveDialog.suggestedName", "Query1"),
                }
            );
            path = r?.TryGetLocalPath();
        }

        if (path is null)
            return;

        try
        {
            CanvasViewModel? ddlVm = ResolveDdlCanvas();
            DatabaseProvider effectiveProvider = _vm.ActiveConnectionConfig?.Provider
                ?? ddlVm?.Provider
                ?? DatabaseProvider.Postgres;
            string querySnapshotJson = _vm.SerializeForPersistence();
            await CanvasSerializer.SaveToFileAsync(
                path,
                _vm,
                ddlVm,
                provider: effectiveProvider.ToString(),
                queryCanvasOverrideJson: querySnapshotJson,
                activeDocumentType: _activeDocumentTypeResolver?.Invoke() ?? WorkspaceDocumentType.QueryCanvas,
                workspaceDocuments: _workspaceDocumentsResolver?.Invoke(),
                activeDocumentId: _activeWorkspaceDocumentIdResolver?.Invoke()
            );
            _vm.CurrentFilePath = path;
            _vm.IsDirty = false;
            RecentFilesStore.Touch(path);
            _vm.NotifySuccess(L("file.save.success", "Canvas saved successfully."), path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save canvas to file");
            _vm.DataPreview.ShowError(
                string.Format(L("file.save.failedWithReason", "Save failed: {0}"), ex.Message),
                ex
            );
        }
    }

    public async Task OpenAsync()
    {
        IReadOnlyList<IStorageFile> results = await _window.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = L("file.openDialog.title", "Open Canvas"),
                FileTypeFilter = [FileType],
                AllowMultiple = false,
            }
        );

        string? path = results.FirstOrDefault()?.TryGetLocalPath();
        if (path is null)
            return;

        await OpenPathAsync(path);
    }

    public async Task OpenPathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        try
        {
            CanvasViewModel? ddlVm = ResolveDdlCanvas();
            if (!TryAbortActiveSubEditors(_vm, ddlVm))
                return;

            CanvasLoadResult result = await CanvasSerializer.LoadFromFileAsync(path, _vm, ddlVm, BuildColumnLookup());
            if (!result.Success)
            {
                _vm.DataPreview.ShowError(
                    string.Format(L("file.open.failedWithReason", "Open failed: {0}"), result.Error),
                    null
                );
                return;
            }

            _vm.CurrentFilePath = path;
            _vm.IsDirty = false;
            RecentFilesStore.Touch(path);
            _invalidateActiveCanvasWires?.Invoke();

            string? warningDetails = result.Warnings is { Count: > 0 }
                ? string.Join(Environment.NewLine, result.Warnings)
                : null;
            if (warningDetails is null)
                _vm.NotifySuccess(L("file.open.success", "Canvas opened successfully."), path);
            else
                _vm.NotifyWarning(
                    L("file.open.successWithWarnings", "Canvas opened with warnings."),
                    $"{path}{Environment.NewLine}{Environment.NewLine}{warningDetails}"
                );

            if (result.Warnings is { Count: > 0 })
                foreach (string w in result.Warnings)
                    _vm.Diagnostics.AddWarning(
                        area: L("diagnostics.canvasMigration", "Canvas Migration"),
                        message: string.Format(L("diagnostics.canvasMigration.openWarning", "Open: {0}"), w),
                        recommendation: L("diagnostics.recommendation.resaveLatestSchema", "Review diagnostics and re-save the canvas to persist the latest schema."),
                        openPanel: true
                    );

            if (ddlVm is not null && _vm.ActiveConnectionConfig is { } config)
                ddlVm.Provider = config.Provider;

            if (_applyWorkspaceDocumentsSnapshot is not null)
            {
                SavedWorkspaceDocumentsCanvas? snapshot = await CanvasSerializer.TryReadWorkspaceDocumentsFromFileAsync(path);
                if (snapshot is not null)
                    _applyWorkspaceDocumentsSnapshot(snapshot);
            }

            if (result.SqlEditorSeedScripts is { Count: > 0 })
                _applySqlEditorSeedScripts?.Invoke(result.SqlEditorSeedScripts);

            if (result.ActiveDocumentType is WorkspaceDocumentType activeDocumentType)
                _applyActiveDocumentType?.Invoke(activeDocumentType);

            bool hasQueryContent = _vm.Nodes.Count > 0 || _vm.Connections.Count > 0;
            bool hasDdlContent = ddlVm is not null
                && (ddlVm.Nodes.Count > 0 || ddlVm.Connections.Count > 0);
            _afterLoadCanvasStateChanged?.Invoke(hasQueryContent, hasDdlContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open canvas from path {Path}", path);
            _vm.DataPreview.ShowError(
                string.Format(L("file.open.failedWithReason", "Open failed: {0}"), ex.Message),
                ex
            );
        }
    }

    private IReadOnlyDictionary<string, IReadOnlyList<(string Name, PinDataType Type)>> BuildColumnLookup()
    {
        if (_vm.DatabaseMetadata is DbMetadata metadata)
            return BuildLookupFromMetadata(metadata);

        return CanvasViewModel.DemoCatalog.ToDictionary(t => t.FullName, t => t.Cols);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<(string Name, PinDataType Type)>> BuildLookupFromMetadata(
        DbMetadata metadata
    )
    {
        return metadata.Schemas
            .SelectMany(schema => schema.Tables)
            .ToDictionary(
                table => $"{table.Schema}.{table.Name}",
                table => (IReadOnlyList<(string Name, PinDataType Type)>)table.Columns
                    .OrderBy(column => column.OrdinalPosition)
                    .Select(column => (column.Name, MapDataTypeToPinDataType(column)))
                    .ToList(),
                StringComparer.OrdinalIgnoreCase
            );
    }

    private static PinDataType MapDataTypeToPinDataType(ColumnMetadata column)
    {
        return column.SemanticType switch
        {
            ColumnSemanticType.Numeric => PinDataType.Number,
            ColumnSemanticType.Text => PinDataType.Text,
            ColumnSemanticType.DateTime => PinDataType.DateTime,
            ColumnSemanticType.Boolean => PinDataType.Boolean,
            ColumnSemanticType.Guid => PinDataType.Text,
            ColumnSemanticType.Document => PinDataType.Json,
            ColumnSemanticType.Binary => PinDataType.Text,
            ColumnSemanticType.Spatial => PinDataType.Json,
            ColumnSemanticType.Other => PinDataType.Text,
            _ => PinDataType.Text,
        };
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

    private static bool TryAbortActiveSubEditors(CanvasViewModel queryVm, CanvasViewModel? ddlVm)
    {
        if (!queryVm.TryAbortActiveSubEditorSession())
            return false;

        if (ddlVm is not null && !ReferenceEquals(ddlVm, queryVm) && !ddlVm.TryAbortActiveSubEditorSession())
            return false;

        return true;
    }
}
