using System.Text;
using AkkornStudio.Nodes;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Services.Export;

/// <summary>
/// Exports a human-readable Markdown document describing the current canvas flow.
///
/// The document includes:
///   • Metadata  — date, provider hint, canvas file path, node/connection counts
///   • SQL       — the last generated SQL, fenced as a sql code block
///   • Node inventory — table grouped by category, with alias and parameters
///   • Connection map — directed list of wires (From → To)
///   • Export nodes  — file names and delimiters configured on export nodes
/// </summary>
public static class FlowDocumentExporter
{
    private const string AppVersion = "1.0";

    // ── Public entry point ────────────────────────────────────────────────────

    /// <summary>
    /// Generates the Markdown document as a string.
    /// </summary>
    public static string Build(CanvasViewModel canvas)
    {
        var sb = new StringBuilder();

        string title = canvas.CurrentFilePath is not null
            ? Path.GetFileNameWithoutExtension(canvas.CurrentFilePath)
            : "Untitled Flow";
        string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        int nodeCount = canvas.Nodes.Count;
        int connCount = canvas.Connections.Count;

        // ── Front-matter ──────────────────────────────────────────────────────
        sb.AppendLine($"# {title}");
        sb.AppendLine();
        sb.AppendLine("| Field | Value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Generated at | `{ts}` |");
        sb.AppendLine($"| Tool version | AkkornStudio {AppVersion} |");
        sb.AppendLine($"| Canvas file | `{canvas.CurrentFilePath ?? "(unsaved)"}` |");
        sb.AppendLine($"| Nodes | {nodeCount} |");
        sb.AppendLine($"| Connections | {connCount} |");
        sb.AppendLine($"| Validation errors | {canvas.ErrorCount} |");
        sb.AppendLine($"| Validation warnings | {canvas.WarningCount} |");
        sb.AppendLine();

        // ── Generated SQL ─────────────────────────────────────────────────────
        sb.AppendLine("## Generated SQL");
        sb.AppendLine();
        string sql = canvas.QueryText?.Trim() ?? "";
        if (string.IsNullOrEmpty(sql))
            sb.AppendLine("> _No SQL generated yet — connect nodes to a Result Output._");
        else
        {
            sb.AppendLine("```sql");
            sb.AppendLine(sql);
            sb.AppendLine("```");
        }
        sb.AppendLine();

        // ── Node inventory ────────────────────────────────────────────────────
        sb.AppendLine("## Node Inventory");
        sb.AppendLine();

        IOrderedEnumerable<IGrouping<NodeCategory, NodeViewModel>> byCategory = canvas
            .Nodes.GroupBy(n => n.Category)
            .OrderBy(g => g.Key.ToString());

        foreach (IGrouping<NodeCategory, NodeViewModel>? group in byCategory)
        {
            sb.AppendLine($"### {group.Key}");
            sb.AppendLine();
            sb.AppendLine("| # | Type | Title | Alias | Parameters |");
            sb.AppendLine("|---|---|---|---|---|");

            int idx = 1;
            foreach (NodeViewModel? node in group)
            {
                string alias = string.IsNullOrWhiteSpace(node.Alias) ? "—" : $"`{node.Alias}`";
                string paramsText =
                    node.Parameters.Count > 0
                        ? string.Join(", ", node.Parameters.Select(kv => $"`{kv.Key}={kv.Value}`"))
                        : "—";
                sb.AppendLine(
                    $"| {idx++} | {node.Type} | {EscapeMd(node.Title)} | {alias} | {paramsText} |"
                );
            }
            sb.AppendLine();
        }

        // ── Connection map ────────────────────────────────────────────────────
        sb.AppendLine("## Connection Map");
        sb.AppendLine();

        if (canvas.Connections.Count == 0)
        {
            sb.AppendLine("> _No connections on canvas._");
        }
        else
        {
            sb.AppendLine("| From Node | Pin | To Node | Pin |");
            sb.AppendLine("|---|---|---|---|");

            foreach (ConnectionViewModel conn in canvas.Connections)
            {
                NodeViewModel fromNode = conn.FromPin.Owner;
                NodeViewModel? toNode = conn.ToPin?.Owner;

                string fromLabel = NodeLabel(fromNode);
                string toLabel = toNode is not null ? NodeLabel(toNode) : "_unconnected_";
                string toPin = conn.ToPin?.Name ?? "—";

                sb.AppendLine($"| {fromLabel} | `{conn.FromPin.Name}` | {toLabel} | `{toPin}` |");
            }
        }
        sb.AppendLine();

        // ── Export nodes config ───────────────────────────────────────────────
        var exportNodes = canvas
            .Nodes.Where(n =>
                n.Type
                    is NodeType.HtmlExport
                        or NodeType.JsonExport
                        or NodeType.CsvExport
                        or NodeType.ExcelExport
            )
            .ToList();

        if (exportNodes.Count > 0)
        {
            sb.AppendLine("## Export Configuration");
            sb.AppendLine();
            sb.AppendLine("| Node | File Name | Extra |");
            sb.AppendLine("|---|---|---|");

            foreach (NodeViewModel? n in exportNodes)
            {
                string fileName = n.Parameters.TryGetValue("file_name", out string? fn)
                    ? fn
                    : "(default)";
                string extra =
                    n.Type == NodeType.CsvExport
                    && n.Parameters.TryGetValue("delimiter", out string? d)
                        ? $"delimiter=`{d}`"
                        : "—";
                sb.AppendLine($"| {n.Type} | `{fileName}` | {extra} |");
            }
            sb.AppendLine();
        }

        // ── Orphan / quality notes ────────────────────────────────────────────
        if (canvas.OrphanCount > 0 || canvas.HasNamingViolations)
        {
            sb.AppendLine("## Quality Notes");
            sb.AppendLine();
            if (canvas.OrphanCount > 0)
                sb.AppendLine(
                    $"- ⚠ **{canvas.OrphanCount} orphan node(s)** not connected to any output."
                );
            if (canvas.HasNamingViolations)
                sb.AppendLine(
                    $"- ⚠ **Naming violations detected** — conformance: {canvas.NamingConformance}%. Run _Auto-Fix Naming_ to correct."
                );
            sb.AppendLine();
        }

        // ── Footer ────────────────────────────────────────────────────────────
        sb.AppendLine("---");
        sb.AppendLine($"_Document generated by [AkkornStudio]({AppVersion}) on {ts}_");

        return sb.ToString();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string NodeLabel(NodeViewModel n)
    {
        string? alias = string.IsNullOrWhiteSpace(n.Alias) ? null : n.Alias;
        string label = alias ?? n.Title;
        return EscapeMd(label);
    }

    private static string EscapeMd(string s) =>
        s.Replace("|", "\\|").Replace("*", "\\*").Replace("_", "\\_");

    // ── File writer ───────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the Markdown document to the given file path.
    /// Returns the absolute path on success, null on error.
    /// </summary>
    public static async Task<string?> WriteAsync(CanvasViewModel canvas, string outputPath)
    {
        try
        {
            string fullPath = Path.GetFullPath(outputPath);
            string? dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string content = Build(canvas);
            string tempPath = $"{fullPath}.tmp-{Guid.NewGuid():N}";
            try
            {
                await File.WriteAllTextAsync(tempPath, content, Encoding.UTF8);
                File.Move(tempPath, fullPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                        // Best-effort cleanup only.
                    }
                }
            }

            if (!File.Exists(fullPath))
                return null;

            return fullPath;
        }
        catch
        {
            return null;
        }
    }
}
