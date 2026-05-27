using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using AkkornStudio.Core;
using AkkornStudio.Nodes;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Serialization;

public static partial class CanvasSerializer
{
    public static event Action<string>? WarningRaised;

    private enum CanvasNodeFamily
    {
        Any,
        Query,
        Ddl,
    }

    /// <summary>Current workspace schema version written by this build.</summary>
    public const int CurrentSchemaVersion = 5;
    public const int CurrentCanvasSchemaVersion = 3;
    public const string CteSubgraphParameterKey = "__cteSubgraphJson";
    public const string SubquerySubgraphParameterKey = "__subquerySubgraphJson";
    public const string SubqueryInputBridgeNodeId = "__subquery_outer_inputs_bridge";
    public const string ViewSubgraphParameterKey = "ViewSubgraphGraphJson";
    public const string ViewEditorCanvasParameterKey = "__viewSubgraphCanvasJson";
    public const string ViewFromTableParameterKey = "ViewFromTable";

    /// <summary>Semantic version of the application (bumped per release).</summary>
    public const string AppVersion = AppConstants.AppVersion;
    public const int CompressionThresholdBytes = 64 * 1024;
    public const int MaxLocalFileVersions = 30;
    public const int MaxAutomaticBackups = 20;
    public const int MaxCteSubgraphDepth = 16;

    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static void RaiseWarning(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        WarningRaised?.Invoke(message);
    }
}
