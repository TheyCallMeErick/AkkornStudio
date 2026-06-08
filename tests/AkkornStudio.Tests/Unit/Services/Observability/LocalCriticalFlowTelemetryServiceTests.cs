using System.Text.Json;
using AkkornStudio.UI.Services.Observability;

namespace AkkornStudio.Tests.Unit.Services.Observability;

public sealed class LocalCriticalFlowTelemetryServiceTests
{
    [Fact]
    public void Track_WritesJsonLineWithSessionCorrelation()
    {
        string root = Path.Combine(Path.GetTempPath(), "akkornstudio-tests", Guid.NewGuid().ToString("N"));
        DateTimeOffset now = new(2026, 5, 5, 10, 30, 0, TimeSpan.Zero);
        var sut = new LocalCriticalFlowTelemetryService(root, () => now);

        sut.Track(
            flowId: "CF-01-open-app-load-project",
            step: "open_from_disk_completed",
            outcome: "ok",
            properties: new Dictionary<string, object?> { ["path"] = "C:/tmp/test.vsaq" });

        string filePath = Path.Combine(root, "critical-flows-2026-05-05.jsonl");
        Assert.True(File.Exists(filePath));

        string line = Assert.Single(File.ReadAllLines(filePath));
        using JsonDocument doc = JsonDocument.Parse(line);
        JsonElement rootElement = doc.RootElement;

        Assert.Equal(sut.SessionId, rootElement.GetProperty("SessionId").GetString());
        Assert.Equal("CF-01-open-app-load-project", rootElement.GetProperty("FlowId").GetString());
        Assert.Equal("open_from_disk_completed", rootElement.GetProperty("Step").GetString());
        Assert.Equal("ok", rootElement.GetProperty("Outcome").GetString());

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Track_WithoutProperties_WritesEmptyPropertiesObject()
    {
        string root = Path.Combine(Path.GetTempPath(), "akkornstudio-tests", Guid.NewGuid().ToString("N"));
        DateTimeOffset now = new(2026, 5, 5, 11, 0, 0, TimeSpan.Zero);
        var sut = new LocalCriticalFlowTelemetryService(root, () => now);

        sut.Track(
            flowId: "CF-02-navigate-shell",
            step: "switch_to_query_mode",
            outcome: "ok");

        string filePath = Path.Combine(root, "critical-flows-2026-05-05.jsonl");
        string line = Assert.Single(File.ReadAllLines(filePath));
        using JsonDocument doc = JsonDocument.Parse(line);
        JsonElement rootElement = doc.RootElement;

        Assert.Equal(JsonValueKind.Object, rootElement.GetProperty("Properties").ValueKind);
        Assert.Empty(rootElement.GetProperty("Properties").EnumerateObject());

        Directory.Delete(root, recursive: true);
    }
}
