using System.Text.Json;

namespace AkkornStudio.UI.Serialization;

/// <summary>
/// Top-level canvas save file.
/// </summary>
public record SavedCanvas(
    int Version,
    string? DatabaseProvider,
    string? ConnectionName,
    double Zoom,
    double PanX,
    double PanY,
    List<SavedNode> Nodes,
    List<SavedConnection> Connections,
    List<string> SelectBindings,
    List<string> WhereBindings,
    Dictionary<string, string>? PreviewParameterInputs = null,
    string? AppVersion = null,
    string? CreatedAt = null,
    string? Description = null
);

/// <summary>
/// Top-level workspace save file (schema v4+).
/// Contains independent query and DDL canvases.
/// </summary>
public record SavedWorkspaceCanvas(
    int Version,
    SavedCanvas QueryCanvas,
    SavedCanvas DdlCanvas,
    string? AppVersion = null,
    string? CreatedAt = null,
    string? Description = null
);

/// <summary>
/// Top-level workspace save file (schema v5+).
/// Contains a typed collection of workspace documents.
/// </summary>
public record SavedWorkspaceDocumentsCanvas(
    int Version,
    List<SavedWorkspaceDocument> Documents,
    Guid? ActiveDocumentId,
    SavedCanvas? QueryCanvas = null,
    SavedCanvas? DdlCanvas = null,
    string? AppVersion = null,
    string? CreatedAt = null,
    string? Description = null
);

public record SavedWorkspaceDocument(
    Guid DocumentId,
    string DocumentType,
    string Title,
    bool IsDirty,
    string PersistenceSchemaVersion,
    SavedCanvas? CanvasPayload = null,
    JsonElement? DocumentPayload = null
);

public record SavedColumn(
    string Name,
    string Type
);

public record SavedNode(
    string NodeId,
    string NodeType,
    double X,
    double Y,
    int? ZOrder,
    string? Alias,
    string? TableFullName,
    Dictionary<string, string> Parameters,
    Dictionary<string, string> PinLiterals,
    List<SavedColumn>? Columns = null,
    SavedCteSubgraph? CteSubgraph = null,
    SavedViewSubgraph? ViewSubgraph = null
);

public record SavedCteSubgraph(
    List<SavedNode> Nodes,
    List<SavedConnection> Connections,
    string? ResultOutputNodeId
);

public record SavedSubqueryInputBinding(
    string InputPinName,
    string BridgePinName,
    string SourceNodeId,
    string SourcePinName,
    string? SourceLabel = null
);

public record SavedSubquerySubgraph(
    List<SavedNode> Nodes,
    List<SavedConnection> Connections,
    string? ResultOutputNodeId,
    string? BridgeNodeId,
    List<SavedSubqueryInputBinding>? InputBindings = null
);

public record SavedViewSubgraph(
    string? GraphJson,
    string? FromTable,
    string? EditorCanvasJson = null
);

public record SavedConnection(
    string FromNodeId,
    string FromPinName,
    string ToNodeId,
    string ToPinName,
    string? RoutingMode = null,
    List<SavedWireBreakpoint>? Breakpoints = null
);

public readonly record struct SavedWireBreakpoint(
    double X,
    double Y
);

public sealed record LocalFileVersionInfo(
    string VersionId,
    string VersionPath,
    DateTimeOffset CreatedAt,
    long SizeBytes,
    bool IsCompressed
)
{
    public string CreatedAtLocalLabel => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string SizeLabel => SizeBytes < 1024
        ? $"{SizeBytes} B"
        : SizeBytes < 1024 * 1024
            ? $"{SizeBytes / 1024.0:F1} KB"
            : $"{SizeBytes / (1024.0 * 1024.0):F1} MB";
}
