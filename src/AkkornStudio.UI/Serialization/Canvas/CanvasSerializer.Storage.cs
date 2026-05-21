using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AkkornStudio.Core;
using AkkornStudio.UI.Services.Workspace.Models;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Serialization;

public static partial class CanvasSerializer
{
    public static async Task SaveToFileAsync(
        string path,
        CanvasViewModel vm,
        string provider = "Postgres",
        string connection = "untitled",
        string? description = null,
        WorkspaceDocumentType activeDocumentType = WorkspaceDocumentType.QueryCanvas
    )
    {
        await SaveToFileAsync(path, vm, null, provider, connection, description, null, activeDocumentType);
    }

    public static async Task SaveToFileAsync(
        string path,
        CanvasViewModel queryVm,
        CanvasViewModel? ddlVm,
        string provider = "Postgres",
        string connection = "untitled",
        string? description = null,
        string? queryCanvasOverrideJson = null,
        WorkspaceDocumentType activeDocumentType = WorkspaceDocumentType.QueryCanvas,
        IReadOnlyList<OpenWorkspaceDocument>? workspaceDocuments = null,
        Guid? activeDocumentId = null
    )
    {
        string json = workspaceDocuments is { Count: > 0 }
            ? SerializeWorkspaceDocuments(workspaceDocuments, activeDocumentId, provider, connection, description)
            : SerializeWorkspace(
                queryVm,
                ddlVm,
                provider,
                connection,
                description,
                queryCanvasOverrideJson,
                activeDocumentType);
        byte[] utf8 = Encoding.UTF8.GetBytes(json);
        bool useCompression = utf8.Length >= CompressionThresholdBytes;
        byte[] payload = useCompression ? CompressBytes(utf8) : utf8;

        string? parentDir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parentDir))
            Directory.CreateDirectory(parentDir);

        await CreateAutomaticBackupAsync(path);

        await File.WriteAllBytesAsync(path, payload);
        await AddLocalFileVersionAsync(path, payload);
    }

    /// <summary>
    /// Loads a canvas file and returns a <see cref="CanvasLoadResult"/>.
    /// Check <see cref="CanvasLoadResult.Success"/> before using the canvas.
    /// </summary>
    public static async Task<CanvasLoadResult> LoadFromFileAsync(
        string path,
        CanvasViewModel vm,
        IReadOnlyDictionary<string, IReadOnlyList<(string Name, PinDataType Type)>>? columnLookup =
            null
    )
    {
        return await LoadFromFileAsync(path, vm, null, columnLookup);
    }

    public static async Task<CanvasLoadResult> LoadFromFileAsync(
        string path,
        CanvasViewModel queryVm,
        CanvasViewModel? ddlVm,
        IReadOnlyDictionary<string, IReadOnlyList<(string Name, PinDataType Type)>>? columnLookup =
            null
    )
    {
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path);
        }
        catch (Exception ex)
        {
            return CanvasLoadResult.Fail($"Could not read file: {ex.Message}");
        }

        string json;
        try
        {
            json = DecodeCanvasJson(bytes);
        }
        catch (Exception ex)
        {
            return CanvasLoadResult.Fail($"Could not decode canvas file: {ex.Message}");
        }

        return DeserializeWorkspace(json, queryVm, ddlVm, columnLookup);
    }

    /// <summary>
    /// Returns true if the file is a readable canvas file with a supported schema version.
    /// </summary>
    public static bool IsValidFile(string path)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            return IsValidCanvasPayload(bytes);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reads just the metadata fields from a file without fully loading the canvas.
    /// Returns null if the file cannot be parsed.
    /// </summary>
    public static (
        int Version,
        string? AppVersion,
        string? CreatedAt,
        string? Description
    )? ReadMeta(string path)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            string json = DecodeCanvasJson(bytes);
            if (LooksLikeDocumentWorkspaceEnvelope(json))
            {
                SavedWorkspaceDocumentsCanvas? workspace = JsonSerializer.Deserialize<SavedWorkspaceDocumentsCanvas>(json, _opts);
                if (workspace is null)
                    return null;

                return (
                    workspace.Version,
                    workspace.AppVersion,
                    workspace.CreatedAt,
                    workspace.Description
                );
            }

            if (LooksLikeLegacyWorkspaceEnvelope(json))
            {
                SavedWorkspaceCanvas? workspace = JsonSerializer.Deserialize<SavedWorkspaceCanvas>(json, _opts);
                if (workspace is null)
                    return null;

                return (
                    workspace.Version,
                    workspace.AppVersion ?? workspace.QueryCanvas.AppVersion,
                    workspace.CreatedAt ?? workspace.QueryCanvas.CreatedAt,
                    workspace.Description ?? workspace.QueryCanvas.Description
                );
            }

            SavedCanvas? saved = JsonSerializer.Deserialize<SavedCanvas>(json, _opts);
            if (saved is null)
                return null;

            return (saved.Version, saved.AppVersion, saved.CreatedAt, saved.Description);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<SavedWorkspaceDocumentsCanvas?> TryReadWorkspaceDocumentsFromFileAsync(string path)
    {
        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(path);
            string json = DecodeCanvasJson(bytes);
            if (!LooksLikeDocumentWorkspaceEnvelope(json))
                return null;

            return JsonSerializer.Deserialize<SavedWorkspaceDocumentsCanvas>(json, _opts);
        }
        catch
        {
            return null;
        }
    }

    public static IReadOnlyList<LocalFileVersionInfo> GetLocalFileVersions(string targetFilePath)
    {
        string historyDir = GetHistoryDirectory(targetFilePath);
        if (!Directory.Exists(historyDir))
            return [];

        return Directory
            .EnumerateFiles(historyDir, "*.vsaq*")
            .Select(path =>
            {
                var fi = new FileInfo(path);
                DateTimeOffset createdAt = fi.LastWriteTimeUtc;
                string fileName = Path.GetFileNameWithoutExtension(path);
                int sep = fileName.IndexOf('_');
                if (sep > 0
                    && DateTimeOffset.TryParseExact(
                        fileName[..sep],
                        "yyyyMMddHHmmssfff",
                        null,
                        System.Globalization.DateTimeStyles.AssumeUniversal,
                        out DateTimeOffset parsed
                    ))
                {
                    createdAt = parsed;
                }

                return new LocalFileVersionInfo(
                    VersionId: fileName,
                    VersionPath: path,
                    CreatedAt: createdAt,
                    SizeBytes: fi.Length,
                    IsCompressed: path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                );
            })
            .OrderByDescending(v => v.CreatedAt)
            .ThenByDescending(v => v.VersionId, StringComparer.Ordinal)
            .ThenByDescending(v => v.VersionPath, StringComparer.Ordinal)
            .ToList();
    }

    public static async Task RestoreLocalVersionAsync(string targetFilePath, string versionFilePath)
    {
        byte[] bytes = await File.ReadAllBytesAsync(versionFilePath);
        if (!IsValidCanvasPayload(bytes))
        {
            throw new InvalidDataException(
                $"Version file '{versionFilePath}' is not a valid supported canvas payload."
            );
        }

        await CreateAutomaticBackupAsync(targetFilePath);

        string? parentDir = Path.GetDirectoryName(targetFilePath);
        if (!string.IsNullOrWhiteSpace(parentDir))
            Directory.CreateDirectory(parentDir);

        await File.WriteAllBytesAsync(targetFilePath, bytes);
    }

    private static bool IsValidCanvasPayload(byte[] bytes)
    {
        try
        {
            string json = DecodeCanvasJson(bytes);
            if (LooksLikeDocumentWorkspaceEnvelope(json))
            {
                SavedWorkspaceDocumentsCanvas? workspace = JsonSerializer.Deserialize<SavedWorkspaceDocumentsCanvas>(json, _opts);
                if (workspace?.Documents is null || workspace.Documents.Count == 0)
                    return false;

                return workspace.Version is >= 5 and <= CurrentSchemaVersion;
            }

            if (LooksLikeLegacyWorkspaceEnvelope(json))
            {
                SavedWorkspaceCanvas? workspace = JsonSerializer.Deserialize<SavedWorkspaceCanvas>(json, _opts);
                if (workspace?.QueryCanvas is null || workspace.DdlCanvas is null)
                    return false;

                return workspace.Version == 4
                    && workspace.QueryCanvas.Version == CurrentCanvasSchemaVersion
                    && workspace.DdlCanvas.Version == CurrentCanvasSchemaVersion;
            }

            SavedCanvas? saved = JsonSerializer.Deserialize<SavedCanvas>(json, _opts);
            return saved?.Version == CurrentCanvasSchemaVersion;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsGZipPayload(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B;

    private static string DecodeCanvasJson(byte[] bytes)
    {
        if (!IsGZipPayload(bytes))
            return Encoding.UTF8.GetString(bytes);

        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static byte[] CompressBytes(byte[] utf8)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(utf8, 0, utf8.Length);
        return output.ToArray();
    }

    private static async Task CreateAutomaticBackupAsync(string targetFilePath)
    {
        if (!File.Exists(targetFilePath))
            return;

        string backupDir = GetBackupDirectory(targetFilePath);
        Directory.CreateDirectory(backupDir);

        string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        string backupPath = Path.Combine(
            backupDir,
            $"{stamp}_{Path.GetFileName(targetFilePath)}.bak"
        );

        await using (FileStream src = File.OpenRead(targetFilePath))
        await using (FileStream dst = File.Create(backupPath))
            await src.CopyToAsync(dst);

        PruneOldFiles(backupDir, MaxAutomaticBackups);
    }

    private static async Task AddLocalFileVersionAsync(string targetFilePath, byte[] payload)
    {
        string historyDir = GetHistoryDirectory(targetFilePath);
        Directory.CreateDirectory(historyDir);

        string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        bool compressed = IsGZipPayload(payload);
        string ext = compressed ? ".vsaq.gz" : ".vsaq";
        string versionPath = Path.Combine(historyDir, $"{stamp}_{Path.GetFileNameWithoutExtension(targetFilePath)}{ext}");

        await File.WriteAllBytesAsync(versionPath, payload);
        PruneOldFiles(historyDir, MaxLocalFileVersions);
    }

    private static void PruneOldFiles(string dir, int keep)
    {
        FileInfo[] files;
        try
        {
            files = new DirectoryInfo(dir)
                .EnumerateFiles()
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToArray();
        }
        catch (Exception ex)
        {
            RaiseWarning($"Could not enumerate prune candidates in '{dir}': {ex.Message}");
            return;
        }

        foreach (FileInfo stale in files.Skip(keep))
        {
            try
            {
                stale.Delete();
            }
            catch (Exception ex)
            {
                RaiseWarning($"Could not delete stale file '{stale.FullName}': {ex.Message}");
            }
        }
    }

    private static string GetHistoryDirectory(string targetFilePath)
    {
        string parent = Path.GetDirectoryName(targetFilePath) ?? ".";
        string baseName = Path.GetFileNameWithoutExtension(targetFilePath);
        return Path.Combine(parent, ".vsaq_history", baseName);
    }

    private static string GetBackupDirectory(string targetFilePath)
    {
        string parent = Path.GetDirectoryName(targetFilePath) ?? ".";
        return Path.Combine(parent, ".vsaq_backups");
    }
}
