using System.Globalization;
using System.Reflection;
using System.Text;
using AkkornStudio.Core;
using AkkornStudio.UI.Extensions;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Services.SqlEditor.Reports;

internal static class SqlEditorReportHtmlBuilder
{
    private const string ReportVersion = "4.0";
    private const string CssResourceName = "AkkornStudio.UI.Assets.ReportFrontend.dist.report-app.css";
    private const string JsResourceName = "AkkornStudio.UI.Assets.ReportFrontend.dist.report-app.js";
    private static readonly Lazy<string> CssBundle = new(() => ReadResource(CssResourceName));
    private static readonly Lazy<string> JsBundle = new(() => ReadResource(JsResourceName));

    public static string Build(SqlEditorReportExportContext context, SqlEditorReportExportRequest request)
    {
        string language = CultureInfo.CurrentUICulture.ResolveReportLanguageTag();
        string title = string.IsNullOrWhiteSpace(request.Title) ? context.TabTitle : request.Title.Trim();
        string description = string.IsNullOrWhiteSpace(request.Description) ? string.Empty : request.Description.Trim();
        string generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        string generatedAtIso = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);

        IReadOnlyList<SqlEditorReportSchemaDetail> schemaRows = request.IncludeSchema ? BuildSchemaRows(context) : [];
        IReadOnlyList<object> metadataRows = BuildMetadataRows(context, request, generatedAtIso, language);
        IReadOnlyList<object> lineageNodes = request.IncludeLineage && context.NodeRows is { Count: > 0 }
            ? [.. context.NodeRows.Select(x => (object)new { x.Category, x.Type, x.Title, x.Status })]
            : [];
        IReadOnlyList<object> lineageConnections = request.IncludeLineage && context.ConnectionRows is { Count: > 0 }
            ? [.. context.ConnectionRows.Select(x => (object)new { x.FromNode, x.FromPin, x.ToNode, x.ToPin, x.DataType })]
            : [];

        object payload = new
        {
            version = ReportVersion,
            meta = BuildMeta(context, request, generatedAt, generatedAtIso, title, description),
            rows = context.ResultRows,
            schema = schemaRows,
            metadata = metadataRows,
            lineageNodes,
            lineageConnections,
            sql = request.IncludeSql ? context.Sql : string.Empty,
            labels = BuildLabels(language),
        };

        return $$"""
<!DOCTYPE html>
<html lang="{{language.ToHtmlEncoded()}}" data-theme="dark">
<head>
  <meta charset="UTF-8"/>
  <meta name="viewport" content="width=device-width, initial-scale=1.0"/>
  {{(string.IsNullOrWhiteSpace(description) ? string.Empty : $"<meta name=\"description\" content=\"{description.ToHtmlEncoded()}\"/>")}}
  <title>{{title.ToHtmlEncoded()}} - AkkornStudio Report</title>
  <style>{{CssBundle.Value}}</style>
</head>
<body>
  <div id="app"></div>
  <script>window.__AKKORN_REPORT__={{payload.ToInlineScriptJson()}};</script>
  <script>{{JsBundle.Value}}</script>
</body>
</html>
""";
    }

    private static object BuildMeta(
        SqlEditorReportExportContext context,
        SqlEditorReportExportRequest request,
        string generatedAt,
        string generatedAtIso,
        string title,
        string description)
    {
        string? connectionName = context.Connection is null
            ? null
            : $"{context.Connection.Provider} • {context.Connection.Database}";

        List<string> warnings = [];
        if (!string.IsNullOrWhiteSpace(context.ExecutionResult.ErrorMessage))
            warnings.Add(context.ExecutionResult.ErrorMessage.Trim());

        return new
        {
            title,
            tabTitle = context.TabTitle,
            description,
            generatedAt,
            generatedAtIso,
            status = context.ExecutionResult.Status.NormalizeReportStatus(),
            rowCount = context.ExecutionResult.RowCount,
            executionTimeMs = context.ExecutionResult.ExecutionTimeMs,
            duration = context.ExecutionResult.ExecutionTimeMs is long ms ? $"{ms} ms" : null,
            columnCount = context.SchemaColumns.Count,
            joinCount = context.Sql.CountRegexMatches(@"\bjoin\b"),
            aggregateCount = context.Sql.CountRegexMatches(@"\b(count|sum|avg|min|max|string_agg|group_concat)\s*\("),
            hasSubquery = context.Sql.ContainsSqlSubquery(),
            emptyValueMode = request.EmptyValueDisplayMode.ToEmptyValueMode(),
            statusMessage = context.ExecutionResult.ErrorMessage,
            filePath = context.ActiveFilePath,
            metadataLevel = request.MetadataLevel.ToString(),
            connectionName,
            connectionHost = context.Connection?.Host,
            connectionDatabase = context.Connection?.Database,
            dialect = ((DatabaseProvider?)context.Connection?.Provider).ToReportDialect(),
            warnings,
        };
    }

    private static IReadOnlyList<SqlEditorReportSchemaDetail> BuildSchemaRows(SqlEditorReportExportContext context)
    {
        if (context.SchemaDetails is { Count: > 0 })
            return context.SchemaDetails;

        IReadOnlyList<string> columns = context.SchemaColumns.Count > 0
            ? context.SchemaColumns
            : [.. context.ResultRows.FirstOrDefault()?.Keys ?? []];

        var details = new List<SqlEditorReportSchemaDetail>(columns.Count);
        foreach (string column in columns)
        {
            long nullCount = 0;
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            string kind = "text";
            string? example = null;
            string? minValue = null;
            string? maxValue = null;

            foreach (IReadOnlyDictionary<string, object?> row in context.ResultRows)
            {
                row.TryGetValue(column, out object? value);
                if (value is null)
                {
                    nullCount += 1;
                    continue;
                }

                string text = value.ToString() ?? string.Empty;
                example ??= text;
                distinct.Add(text);
                minValue = minValue is null || string.CompareOrdinal(text, minValue) < 0 ? text : minValue;
                maxValue = maxValue is null || string.CompareOrdinal(text, maxValue) > 0 ? text : maxValue;
                kind = MergeKinds(kind, value.DetectReportValueKind());
            }

            details.Add(new SqlEditorReportSchemaDetail(column, kind, nullCount, distinct.Count, example, minValue, maxValue));
        }

        return details;
    }

    private static IReadOnlyList<object> BuildMetadataRows(
        SqlEditorReportExportContext context,
        SqlEditorReportExportRequest request,
        string generatedAtIso,
        string language)
    {
        if (request.MetadataLevel == SqlEditorReportMetadataLevel.None)
            return [];

        ConnectionConfig? connection = context.Connection;
        var rows = new List<object>
        {
            new { field = "provider", label = language.PickReportText("Provedor", "Provider"), value = connection?.Provider.ToString(), kind = "text" },
            new { field = "database", label = language.PickReportText("Banco", "Database"), value = connection?.Database, kind = "text" },
            new { field = "host", label = "Host", value = connection?.Host, kind = "text" },
            new { field = "executionDate", label = language.PickReportText("Data de execucao", "Execution date"), value = generatedAtIso, kind = "date" },
            new { field = "locale", label = "Locale", value = CultureInfo.CurrentCulture.Name, kind = "text" },
            new { field = "timezone", label = "Timezone", value = TimeZoneInfo.Local.Id, kind = "text" },
        };

        if (request.MetadataLevel == SqlEditorReportMetadataLevel.Complete)
        {
            rows.Add(new { field = "filePath", label = language.PickReportText("Arquivo", "File path"), value = context.ActiveFilePath, kind = "text" });
            rows.Add(new { field = "schemaColumnCount", label = language.PickReportText("Colunas", "Columns"), value = context.SchemaColumns.Count.ToString(CultureInfo.InvariantCulture), kind = "number" });
            rows.Add(new { field = "rowCount", label = language.PickReportText("Linhas", "Rows"), value = context.ExecutionResult.RowCount?.ToString(CultureInfo.InvariantCulture), kind = "number" });
        }

        return rows;
    }

    private static object BuildLabels(string language)
    {
        bool isPt = language.StartsWith("pt", StringComparison.OrdinalIgnoreCase);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Tema"] = isPt ? "Tema" : "Theme",
            ["Copiar resumo"] = isPt ? "Copiar resumo" : "Copy summary",
            ["Copiar SQL"] = "Copy SQL",
            ["Exportar visivel CSV"] = isPt ? "Exportar visível CSV" : "Export visible CSV",
            ["Visao geral"] = isPt ? "Visão geral" : "Overview",
            ["Resultados"] = isPt ? "Resultados" : "Results",
            ["Buscar resultados"] = isPt ? "Buscar resultados" : "Search results",
            ["Colunas e schema"] = isPt ? "Colunas e schema" : "Columns and schema",
            ["Buscar schema"] = isPt ? "Buscar schema" : "Search schema",
            ["Metadados"] = isPt ? "Metadados" : "Metadata",
            ["Buscar metadados"] = isPt ? "Buscar metadados" : "Search metadata",
            ["Nos de linhagem"] = isPt ? "Nós de linhagem" : "Lineage nodes",
            ["Buscar nos"] = isPt ? "Buscar nós" : "Search nodes",
            ["Conexoes de linhagem"] = isPt ? "Conexões de linhagem" : "Lineage connections",
            ["Buscar conexoes"] = isPt ? "Buscar conexões" : "Search connections",
        };
    }

    private static string ReadResource(string resourceName)
    {
        Assembly assembly = typeof(SqlEditorReportHtmlBuilder).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new InvalidOperationException($"Embedded report asset not found: {resourceName}");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string MergeKinds(string left, string right) => left == right ? left : "text";
}
