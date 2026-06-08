using System.Text.Json;

namespace AkkornStudio.UI.Services.Observability;

public sealed class LocalCriticalFlowTelemetryService : ICriticalFlowTelemetryService
{
    private const string ProductFolderName = "AkkornStudio";
    private const string TelemetryFolderName = "telemetry";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private readonly string _logDirectory;
    private readonly string _fallbackLogDirectory;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly object _gate = new();

    public LocalCriticalFlowTelemetryService()
        : this(logDirectory: null, utcNow: null)
    {
    }

    public LocalCriticalFlowTelemetryService(string? logDirectory, Func<DateTimeOffset>? utcNow)
    {
        _logDirectory = string.IsNullOrWhiteSpace(logDirectory)
            ? ResolveDefaultDirectory()
            : logDirectory;
        _fallbackLogDirectory = ResolveFallbackDirectory();
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        SessionId = Guid.NewGuid().ToString("N");
    }

    public string SessionId { get; }

    public void Track(
        string flowId,
        string step,
        string outcome,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        var evt = new CriticalFlowEvent(
            SessionId,
            _utcNow(),
            flowId,
            step,
            outcome,
            properties ?? new Dictionary<string, object?>());

        string line = JsonSerializer.Serialize(evt, JsonOptions);
        lock (_gate)
        {
            if (TryAppend(_logDirectory, evt.TimestampUtc, line))
                return;

            // Fallback for environments where LocalAppData is read-only or ACL-restricted.
            _ = TryAppend(_fallbackLogDirectory, evt.TimestampUtc, line);
        }
    }

    private static bool TryAppend(string directory, DateTimeOffset timestampUtc, string line)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string filePath = Path.Combine(directory, $"critical-flows-{timestampUtc:yyyy-MM-dd}.jsonl");
            File.AppendAllText(filePath, line + Environment.NewLine);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static string ResolveDefaultDirectory()
    {
        string baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDirectory))
            baseDirectory = AppContext.BaseDirectory;

        return Path.Combine(baseDirectory, ProductFolderName, TelemetryFolderName);
    }

    private static string ResolveFallbackDirectory()
    {
        string baseDirectory = Path.GetTempPath();
        if (string.IsNullOrWhiteSpace(baseDirectory))
            baseDirectory = AppContext.BaseDirectory;

        return Path.Combine(baseDirectory, ProductFolderName, TelemetryFolderName);
    }
}
