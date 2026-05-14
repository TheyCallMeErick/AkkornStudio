using System.Data;
using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.UI.Services.SqlEditor;

namespace AkkornStudio.Tests.Unit.Services.SqlEditor;

public sealed class SqlResultEligibilityDetectorTests
{
    [Fact]
    public void Evaluate_WithSingleTableAndPkInResult_IsEligible()
    {
        var sut = new SqlResultEligibilityDetector();
        DataTable table = BuildResultTable("id", "name", "status");
        DbMetadata metadata = BuildMetadata(TableKind.Table);
        ConnectionConfig config = BuildConfig(readOnly: false);

        SqlInlineEditEligibility result = sut.Evaluate(
            "SELECT id, name, status FROM public.users",
            table,
            metadata,
            config);

        Assert.True(result.IsEligible);
        Assert.Equal("public.users", result.TableFullName);
        Assert.Contains("id", result.PrimaryKeyColumns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("name", result.EditableColumns, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_WithJoin_IsNotEligible()
    {
        var sut = new SqlResultEligibilityDetector();
        DataTable table = BuildResultTable("id", "name");
        DbMetadata metadata = BuildMetadata(TableKind.Table);
        ConnectionConfig config = BuildConfig(readOnly: false);

        SqlInlineEditEligibility result = sut.Evaluate(
            "SELECT u.id, u.name FROM public.users u JOIN public.roles r ON r.id = u.role_id",
            table,
            metadata,
            config);

        Assert.False(result.IsEligible);
        Assert.Contains("complex", result.IneligibilityReason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_WhenConnectionReadOnly_IsNotEligible()
    {
        var sut = new SqlResultEligibilityDetector();
        DataTable table = BuildResultTable("id", "name");
        DbMetadata metadata = BuildMetadata(TableKind.Table);
        ConnectionConfig config = BuildConfig(readOnly: true);

        SqlInlineEditEligibility result = sut.Evaluate(
            "SELECT id, name FROM public.users",
            table,
            metadata,
            config);

        Assert.False(result.IsEligible);
        Assert.Contains("read-only", result.IneligibilityReason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_WithViewTableKind_IsNotEligible()
    {
        var sut = new SqlResultEligibilityDetector();
        DataTable table = BuildResultTable("id", "name");
        DbMetadata metadata = BuildMetadata(TableKind.View);
        ConnectionConfig config = BuildConfig(readOnly: false);

        SqlInlineEditEligibility result = sut.Evaluate(
            "SELECT id, name FROM public.users",
            table,
            metadata,
            config);

        Assert.False(result.IsEligible);
    }

    private static DataTable BuildResultTable(params string[] columns)
    {
        var table = new DataTable();
        foreach (string column in columns)
            table.Columns.Add(column, typeof(string));

        table.Rows.Add(columns.Select((_, i) => i == 0 ? "1" : $"v{i}").Cast<object>().ToArray());
        return table;
    }

    private static DbMetadata BuildMetadata(TableKind kind)
    {
        var users = new TableMetadata(
            Schema: "public",
            Name: "users",
            Kind: kind,
            EstimatedRowCount: 100,
            Columns:
            [
                new ColumnMetadata("id", "int", "int", false, true, false, true, true, 1),
                new ColumnMetadata("name", "text", "text", true, false, false, false, false, 2),
                new ColumnMetadata("status", "text", "text", true, false, false, false, false, 3),
            ],
            Indexes: [],
            OutboundForeignKeys: [],
            InboundForeignKeys: []);

        return new DbMetadata(
            DatabaseName: "db",
            Provider: DatabaseProvider.Postgres,
            ServerVersion: "16",
            CapturedAt: DateTimeOffset.UtcNow,
            Schemas: [new SchemaMetadata("public", [users])],
            AllForeignKeys: []);
    }

    private static ConnectionConfig BuildConfig(bool readOnly)
    {
        return new ConnectionConfig(
            Provider: DatabaseProvider.Postgres,
            Host: "localhost",
            Port: 5432,
            Database: "db",
            Username: "u",
            Password: "p",
            ExtraParameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ReadOnly"] = readOnly.ToString(),
            });
    }
}
