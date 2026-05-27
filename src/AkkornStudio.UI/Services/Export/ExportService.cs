using Avalonia.Controls;
using Avalonia.Platform.Storage;
using AkkornStudio.Nodes;
using AkkornStudio.UI.Services.Export;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.Serialization;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Services;

/// <summary>
/// Handles export operations for multiple formats.
/// Supports HTML, JSON, CSV, Excel, and Markdown documentation exports.
/// </summary>
public class ExportService(Window window, CanvasViewModel vm)
{
    private readonly Window _window = window;
    private readonly CanvasViewModel _vm = vm;

    public async Task RunExportDocumentationAsync()
    {
        string suggestedName = _vm.CurrentFilePath is not null
            ? Path.GetFileNameWithoutExtension(_vm.CurrentFilePath)
            : "flow-documentation";

        var mdType = new FilePickerFileType("Markdown Files") { Patterns = ["*.md"] };
        IStorageFile? result = await _window.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = L("export.documentation.dialogTitle", "Export Flow Documentation"),
                DefaultExtension = "md",
                FileTypeChoices = [mdType],
                SuggestedFileName = suggestedName,
            }
        );

        string? path = result?.TryGetLocalPath();
        if (path is null)
            return;

        string? written = await FlowDocumentExporter.WriteAsync(_vm, path);
        if (HasSuccessfulWrite(written))
            _vm.NotifySuccess(L("export.documentation.success", "Documentation exported successfully."), written);
        else
            _vm.NotifyError(
                L("export.documentation.failed", "Documentation export failed."),
                L("export.failed.pathPermissionsHint", "Check file path and permissions.")
            );
    }

    public async Task RunExportWithDialogAsync(
        NodeType exportType,
        string fileTypeName,
        string extension
    )
    {
        NodeViewModel? exportNode = _vm.Nodes.FirstOrDefault(n => n.Type == exportType);
        if (exportNode is null)
        {
            string msg =
                string.Format(
                    L(
                        "export.nodeNotFound",
                        "No {0} Export node found on the canvas. Add one via the node search menu."
                    ),
                    fileTypeName.Replace(" Files", string.Empty)
                );
            _vm.NotifyError(msg);
            return;
        }

        string suggestedName = exportNode.Parameters.TryGetValue("file_name", out string? fn)
            ? fn
            : $"export.{extension}";
        var fileType = new FilePickerFileType(fileTypeName) { Patterns = [$"*.{extension}"] };
        IStorageFile? result = await _window.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = string.Format(L("export.dialogTitleByExtension", "Export as {0}"), extension.ToUpperInvariant()),
                DefaultExtension = extension,
                FileTypeChoices = [fileType],
                SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedName),
            }
        );

        string? path = result?.TryGetLocalPath();
        if (path is null)
            return;

        string? written = await _vm.TriggerExportAsync(exportType, path);
        if (HasSuccessfulWrite(written))
            _vm.NotifySuccess(L("export.success", "Export completed successfully."), written);
        else
            _vm.NotifyError(
                L("export.failed", "Export failed."),
                L("export.failed.pathPermissionsHint", "Check file path and permissions.")
            );
    }

    private static bool HasSuccessfulWrite(string? writtenPath)
    {
        if (string.IsNullOrWhiteSpace(writtenPath))
            return false;

        try
        {
            var fileInfo = new FileInfo(writtenPath);
            return fileInfo.Exists && fileInfo.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string L(string key, string fallback)
    {
        string value = LocalizationService.Instance[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }
}
