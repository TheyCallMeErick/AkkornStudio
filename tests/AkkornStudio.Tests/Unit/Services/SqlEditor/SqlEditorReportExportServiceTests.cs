using System.Text.Json;
using ClosedXML.Excel;
using AkkornStudio.Core;
using AkkornStudio.UI.Services.SqlEditor.Reports;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.Services.SqlEditor;

public sealed class SqlEditorReportExportServiceTests
{
    [Fact]
    public async Task ExportAsync_HtmlFullFeature_ContainsNewSectionsAndClientState()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");
        try
        {
            var sut = new SqlEditorReportExportService();
            SqlEditorReportExportContext context = BuildContext();
            var request = new SqlEditorReportExportRequest(
                ReportType: SqlEditorReportType.HtmlFullFeature,
                FilePath: path,
                Title: "Users audit",
                Description: "Execution report for support",
                Profile: SqlEditorReportExportProfile.Technical,
                MetadataLevel: SqlEditorReportMetadataLevel.Essential,
                EmptyValueDisplayMode: SqlEditorReportEmptyValueDisplayMode.Blank,
                IncludeSchema: true,
                IncludeSql: true,
                IncludeLineage: false);

            string exportedPath = await sut.ExportAsync(context, request);
            string html = await File.ReadAllTextAsync(exportedPath);

            Assert.Contains("id=\"app\"", html, StringComparison.Ordinal);
            Assert.Contains("window.__AKKORN_REPORT__=", html, StringComparison.Ordinal);
            Assert.Contains("\"rows\":[", html, StringComparison.Ordinal);
            Assert.Contains("\"schema\":[", html, StringComparison.Ordinal);
            Assert.Contains("\"metadata\":[", html, StringComparison.Ordinal);
            Assert.Contains("\"lineageNodes\":[]", html, StringComparison.Ordinal);
            Assert.Contains("\"lineageConnections\":[]", html, StringComparison.Ordinal);
            Assert.Contains(".report-shell", html, StringComparison.Ordinal);
            Assert.Contains(".mount(\"#app\")", html, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task ExportAsync_HtmlFullFeature_EscapesHeaderAndSqlPayload()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");
        try
        {
            var sut = new SqlEditorReportExportService();
            SqlEditorReportExportContext context = BuildContext() with
            {
                Sql = "SELECT '</script><script>alert(1)</script>' as payload;"
            };
            var request = new SqlEditorReportExportRequest(
                ReportType: SqlEditorReportType.HtmlFullFeature,
                FilePath: path,
                Title: "<img src=x onerror=alert(1)>",
                Description: "<b>unsafe</b>",
                Profile: SqlEditorReportExportProfile.Technical,
                MetadataLevel: SqlEditorReportMetadataLevel.Essential,
                EmptyValueDisplayMode: SqlEditorReportEmptyValueDisplayMode.Dash,
                IncludeSchema: true,
                IncludeSql: true,
                IncludeLineage: false);

            string exportedPath = await sut.ExportAsync(context, request);
            string html = await File.ReadAllTextAsync(exportedPath);

            Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", html, StringComparison.Ordinal);
            Assert.Contains("&lt;b&gt;unsafe&lt;/b&gt;", html, StringComparison.Ordinal);
            Assert.Contains("window.__AKKORN_REPORT__=", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<title><img src=x onerror=alert(1)></title>", html, StringComparison.Ordinal);
            Assert.DoesNotContain("</script><script>alert(1)</script>", html, StringComparison.Ordinal);
            Assert.Contains("<\\/script><script>alert(1)<\\/script>", html, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task ExportAsync_HtmlFullFeature_DoesNotRenderDescriptionWhenEmpty()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");
        try
        {
            var sut = new SqlEditorReportExportService();
            SqlEditorReportExportContext context = BuildContext();
            var request = new SqlEditorReportExportRequest(
                ReportType: SqlEditorReportType.HtmlFullFeature,
                FilePath: path,
                Title: "Users audit",
                Description: string.Empty,
                Profile: SqlEditorReportExportProfile.Technical,
                MetadataLevel: SqlEditorReportMetadataLevel.Essential,
                EmptyValueDisplayMode: SqlEditorReportEmptyValueDisplayMode.Blank,
                IncludeSchema: true,
                IncludeSql: true,
                IncludeLineage: false);

            string exportedPath = await sut.ExportAsync(context, request);
            string html = await File.ReadAllTextAsync(exportedPath);

            Assert.DoesNotContain("<div class=\"description\">", html, StringComparison.Ordinal);
            Assert.DoesNotContain(">null<", html, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task ExportAsync_JsonContract_UsesNewFlagsAndSchemaDetails()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        try
        {
            var sut = new SqlEditorReportExportService();
            SqlEditorReportExportContext context = BuildContext();
            var request = new SqlEditorReportExportRequest(
                ReportType: SqlEditorReportType.JsonContract,
                FilePath: path,
                Title: "Users audit",
                Description: "Execution report for support",
                Profile: SqlEditorReportExportProfile.Audit,
                MetadataLevel: SqlEditorReportMetadataLevel.Complete,
                EmptyValueDisplayMode: SqlEditorReportEmptyValueDisplayMode.NullLiteral,
                IncludeSchema: true,
                IncludeSql: false,
                IncludeLineage: true);

            string exportedPath = await sut.ExportAsync(context, request);
            string payload = await File.ReadAllTextAsync(exportedPath);
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;
            JsonElement meta = root.GetProperty("meta");

            Assert.Equal("3.0", root.GetProperty("version").GetString());
            Assert.False(root.GetProperty("hasSql").GetBoolean());
            Assert.Equal(string.Empty, root.GetProperty("sql").GetString());
            Assert.Equal("warning", meta.GetProperty("summary").GetProperty("status").GetString());
            Assert.Equal("Users audit", meta.GetProperty("title").GetString());
            Assert.True(meta.GetProperty("includeMetadata").GetBoolean());
            Assert.Equal(2, root.GetProperty("schema").GetArrayLength());
            Assert.Single(root.GetProperty("nodes").EnumerateArray());
            Assert.Single(root.GetProperty("connections").EnumerateArray());
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task ExportAsync_CsvData_WritesTabularDataOnly()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        try
        {
            var sut = new SqlEditorReportExportService();
            SqlEditorReportExportContext context = BuildContext();
            var request = new SqlEditorReportExportRequest(
                ReportType: SqlEditorReportType.CsvData,
                FilePath: path,
                Title: "Users audit",
                Description: string.Empty,
                Profile: SqlEditorReportExportProfile.Technical,
                MetadataLevel: SqlEditorReportMetadataLevel.None,
                EmptyValueDisplayMode: SqlEditorReportEmptyValueDisplayMode.Blank,
                IncludeSchema: false,
                IncludeSql: false,
                IncludeLineage: false);

            string exportedPath = await sut.ExportAsync(context, request);
            string csv = await File.ReadAllTextAsync(exportedPath);

            Assert.Contains("id,name", csv, StringComparison.Ordinal);
            Assert.Contains("1,Alice", csv, StringComparison.Ordinal);
            Assert.Contains("2,Bob", csv, StringComparison.Ordinal);
            Assert.DoesNotContain("SELECT", csv, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task ExportAsync_ExcelWorkbook_WritesTabularDataOnly()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");
        try
        {
            var sut = new SqlEditorReportExportService();
            SqlEditorReportExportContext context = BuildContext();
            var request = new SqlEditorReportExportRequest(
                ReportType: SqlEditorReportType.ExcelWorkbook,
                FilePath: path,
                Title: "Users audit",
                Description: string.Empty,
                Profile: SqlEditorReportExportProfile.Technical,
                MetadataLevel: SqlEditorReportMetadataLevel.None,
                EmptyValueDisplayMode: SqlEditorReportEmptyValueDisplayMode.Blank,
                IncludeSchema: false,
                IncludeSql: false,
                IncludeLineage: false);

            string exportedPath = await sut.ExportAsync(context, request);

            using var workbook = new XLWorkbook(exportedPath);
            IXLWorksheet sheet = workbook.Worksheet("Results");

            Assert.Equal("id", sheet.Cell(1, 1).GetString());
            Assert.Equal("name", sheet.Cell(1, 2).GetString());
            Assert.Equal("1", sheet.Cell(2, 1).GetString());
            Assert.Equal("Alice", sheet.Cell(2, 2).GetString());
            Assert.Equal("2", sheet.Cell(3, 1).GetString());
            Assert.Equal("Bob", sheet.Cell(3, 2).GetString());
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static SqlEditorReportExportContext BuildContext()
    {
        var connection = new ConnectionConfig(
            Provider: DatabaseProvider.Postgres,
            Host: "localhost",
            Port: 5432,
            Database: "akkornstudio",
            Username: "app",
            Password: "secret");

        return new SqlEditorReportExportContext(
            Sql: "SELECT u.id FROM users u WHERE EXISTS (SELECT 1 FROM users_archive a WHERE a.id = u.id)",
            SchemaColumns: ["id", "name"],
            SchemaDetails:
            [
                new SqlEditorReportSchemaDetail("id", "number", 0, 2, "1", "1", "2"),
                new SqlEditorReportSchemaDetail("name", "text", 0, 2, "Alice", "Alice", "Bob"),
            ],
            ResultRows:
            [
                new Dictionary<string, object?>
                {
                    ["id"] = 1,
                    ["name"] = "Alice"
                },
                new Dictionary<string, object?>
                {
                    ["id"] = 2,
                    ["name"] = "Bob"
                }
            ],
            ExecutionResult: new SqlEditorReportExecutionResult(
                RowCount: 2,
                ExecutionTimeMs: 14,
                Status: "warning",
                ErrorMessage: null),
            Connection: connection,
            ActiveFilePath: "/tmp/report.sql",
            TabTitle: "Users query",
            NodeRows:
            [
                new SqlEditorReportLineageNode("table", "source", "users", "active"),
            ],
            ConnectionRows:
            [
                new SqlEditorReportLineageConnection("users", "id", "users_archive", "id", "integer"),
            ]);
    }
}
