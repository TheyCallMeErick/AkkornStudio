using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ClosedXML.Excel;
using AkkornStudio.Core;
using AkkornStudio.UI.Extensions;
using AkkornStudio.UI.Services.Theming;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Services.SqlEditor.Reports;

public sealed class SqlEditorReportExportService
{
    private const string ReportVersion = "3.0";

    public async Task<string> ExportAsync(
        SqlEditorReportExportContext context,
        SqlEditorReportExportRequest request,
        CancellationToken cancellationToken = default)
    {
        string? directory = Path.GetDirectoryName(request.FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        switch (request.ReportType)
        {
            case SqlEditorReportType.HtmlFullFeature:
                await File.WriteAllTextAsync(request.FilePath, BuildHtml(context, request), Encoding.UTF8, cancellationToken);
                break;
            case SqlEditorReportType.JsonContract:
                await File.WriteAllTextAsync(request.FilePath, BuildJson(context, request), Encoding.UTF8, cancellationToken);
                break;
            case SqlEditorReportType.CsvData:
                await File.WriteAllTextAsync(request.FilePath, BuildCsv(context), new UTF8Encoding(true), cancellationToken);
                break;
            case SqlEditorReportType.ExcelWorkbook:
                await File.WriteAllBytesAsync(request.FilePath, BuildExcelWorkbook(context), cancellationToken);
                break;
            default:
                throw new NotSupportedException($"Unsupported report type: {request.ReportType}");
        }

        return Path.GetFullPath(request.FilePath);
    }

    private static string BuildJson(SqlEditorReportExportContext context, SqlEditorReportExportRequest request)
    {
        object payload = BuildJsonPayload(context, request);
        return JsonSerializer.Serialize(
            payload,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
    }

    private static string BuildCsv(SqlEditorReportExportContext context)
    {
        IReadOnlyList<string> columns = GetDataColumns(context);
        var rows = new List<string>();

        if (columns.Count > 0)
            rows.Add(string.Join(',', columns.Select(EscapeCsvCell)));

        foreach (IReadOnlyDictionary<string, object?> row in context.ResultRows)
            rows.Add(string.Join(',', columns.Select(column => EscapeCsvCell(row.TryGetValue(column, out object? value) ? value : null))));

        return string.Join(Environment.NewLine, rows);
    }

    private static byte[] BuildExcelWorkbook(SqlEditorReportExportContext context)
    {
        IReadOnlyList<string> columns = GetDataColumns(context);

        using var workbook = new XLWorkbook();
        IXLWorksheet worksheet = workbook.Worksheets.Add("Results");

        int currentRow = 1;
        if (columns.Count > 0)
        {
            for (int columnIndex = 0; columnIndex < columns.Count; columnIndex += 1)
                worksheet.Cell(currentRow, columnIndex + 1).Value = columns[columnIndex];

            worksheet.Row(currentRow).Style.Font.Bold = true;
            currentRow += 1;
        }

        foreach (IReadOnlyDictionary<string, object?> row in context.ResultRows)
        {
            for (int columnIndex = 0; columnIndex < columns.Count; columnIndex += 1)
            {
                string column = columns[columnIndex];
                row.TryGetValue(column, out object? value);
                worksheet.Cell(currentRow, columnIndex + 1).SetValue(value?.ToString() ?? string.Empty);
            }

            currentRow += 1;
        }

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static IReadOnlyList<string> GetDataColumns(SqlEditorReportExportContext context)
    {
        if (context.SchemaColumns.Count > 0)
            return context.SchemaColumns;

        if (context.ResultRows.Count > 0)
            return [.. context.ResultRows[0].Keys];

        return [];
    }

    private static string EscapeCsvCell(object? value)
    {
        string text = value?.ToString() ?? string.Empty;
        if (text.IndexOfAny([',', '"', '\n', '\r']) < 0)
            return text;

        return $"\"{text.Replace("\"", "\"\"")}\"";
    }

    private static string BuildHtml(SqlEditorReportExportContext context, SqlEditorReportExportRequest request) =>
        SqlEditorReportHtmlBuilder.Build(context, request);

    private static object BuildJsonPayload(SqlEditorReportExportContext context, SqlEditorReportExportRequest request)
    {
        string generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        string generatedAtIso = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);
        object meta = BuildMeta(context, request, generatedAt, generatedAtIso);
        object result = BuildExecutionResultObject(context.ExecutionResult);
        bool includeGraphSections = request.IncludeLineage;
        object[] nodes =
            includeGraphSections && context.NodeRows is { Count: > 0 }
                ? [.. context.NodeRows.Select(node => new { node.Category, node.Type, node.Title, node.Status })]
                : [];
        object[] connections =
            includeGraphSections && context.ConnectionRows is { Count: > 0 }
                ? [.. context.ConnectionRows.Select(connection => new { connection.FromNode, connection.FromPin, connection.ToNode, connection.ToPin, connection.DataType })]
                : [];
        bool hasSql = request.IncludeSql && !string.IsNullOrWhiteSpace(context.Sql);

        object schema =
            request.IncludeSchema
                ? context.SchemaDetails is { Count: > 0 }
                    ? context.SchemaDetails
                    : context.SchemaColumns
                : Array.Empty<object>();

        return new
        {
            version = ReportVersion,
            meta,
            sql = request.IncludeSql ? context.Sql : string.Empty,
            hasSql,
            result,
            schema,
            rows = context.ResultRows,
            nodes,
            connections,
        };
    }

    private static object BuildMeta(
        SqlEditorReportExportContext context,
        SqlEditorReportExportRequest request,
        string generatedAt,
        string generatedAtIso)
    {
        string normalizedStatus = context.ExecutionResult.Status.NormalizeReportStatus();
        int errorCount = normalizedStatus == "error" ? 1 : 0;
        int warningCount = normalizedStatus == "warning" ? 1 : 0;
        int columnCount = context.SchemaColumns.Count;
        int joinCount = context.Sql.CountRegexMatches(@"\bjoin\b");
        int aggregateCount = context.Sql.CountRegexMatches(@"\b(count|sum|avg|min|max|string_agg|group_concat)\s*\(");
        bool hasSubquery = context.Sql.ContainsSqlSubquery();
        string title = string.IsNullOrWhiteSpace(request.Title) ? context.TabTitle : request.Title;
        string? description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        ConnectionConfig? connection = context.Connection;
        string engineVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "-";
        string dialect = ((DatabaseProvider?)connection?.Provider).ToReportDialect();
        string provider = connection?.Provider.ToString() ?? "-";
        string database = connection?.Database ?? "-";
        string host = connection?.Host ?? "-";
        string filePath = context.ActiveFilePath ?? "-";

        object[] metadataRows = request.IncludeMetadata
            ?
            [
                new { field = "provider", label = "Provider", value = connection?.Provider.ToString(), kind = "text" },
                new { field = "database", label = "Database", value = connection?.Database, kind = "text" },
                new { field = "host", label = "Host", value = connection?.Host, kind = "text" },
                new { field = "dialect", label = "Dialect", value = dialect, kind = "text" },
                new { field = "executionDate", label = "Execution Date", value = generatedAtIso, kind = "date" },
                new { field = "locale", label = "Locale", value = CultureInfo.CurrentCulture.Name, kind = "text" },
                new { field = "timezone", label = "Timezone", value = TimeZoneInfo.Local.Id, kind = "text" },
                new { field = "engineVersion", label = "Engine Version", value = engineVersion, kind = "text" },
                new { field = "filePath", label = "File Path", value = context.ActiveFilePath, kind = "text" },
                new { field = "columnCount", label = "Columns", value = columnCount, kind = "number" },
                new { field = "joinCount", label = "Join Count", value = joinCount, kind = "number" },
                new { field = "aggregateCount", label = "Aggregate Count", value = aggregateCount, kind = "number" },
                new { field = "hasSubquery", label = "Has Subquery", value = hasSubquery, kind = "bool" },
                new { field = "warningCount", label = "Warnings", value = warningCount, kind = "number" },
                new { field = "errorCount", label = "Errors", value = errorCount, kind = "number" },
            ]
            : [];

        object summary = new
        {
            status = normalizedStatus,
            success = normalizedStatus == "success",
            executedAt = generatedAt,
            executionTimeMs = context.ExecutionResult.ExecutionTimeMs,
            rowCount = context.ExecutionResult.RowCount,
            sqlLength = context.Sql.Length,
            errorMessage = context.ExecutionResult.ErrorMessage,
        };

        return new
        {
            reportVersion = ReportVersion,
            includeMetadata = request.IncludeMetadata,
            useDashForEmptyFields = request.UseDashForEmptyFields,
            title,
            description,
            generatedAt,
            generatedAtIso,
            summary,
            metadata = metadataRows,
            provider,
            providerVersion = "-",
            database,
            host,
            dialect,
            engineVersion,
            timezone = TimeZoneInfo.Local.Id,
            locale = CultureInfo.CurrentCulture.Name,
            nodeCount = 0,
            connCount = 0,
            columnCount,
            errorCount,
            warningCount,
            orphanCount = 0,
            namingConformance = "-",
            joinCount,
            aggregateCount,
            hasSubquery,
            queryLength = context.Sql.Length,
            rowCount = context.ExecutionResult.RowCount,
            executionTimeMs = context.ExecutionResult.ExecutionTimeMs,
            status = normalizedStatus,
            filePath,
        };
    }

    private static object BuildExecutionResultObject(SqlEditorReportExecutionResult executionResult)
    {
        string normalizedStatus = executionResult.Status.NormalizeReportStatus();
        return new
        {
            rowCount = executionResult.RowCount,
            executionTimeMs = executionResult.ExecutionTimeMs,
            status = normalizedStatus,
            success = normalizedStatus == "success",
            errorMessage = executionResult.ErrorMessage,
        };
    }
}
