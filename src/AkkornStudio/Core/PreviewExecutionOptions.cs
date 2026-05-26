using Microsoft.Extensions.Options;

namespace AkkornStudio.Core;

public sealed class PreviewExecutionOptions
{
    public const int UseConfiguredDefault = -1;
    public const int NoLimit = -2;
    public const int BuiltInDefaultMaxRows = 200;
    public const string MaxRowsEnvironmentVariable = "VSA_PREVIEW_MAX_ROWS";
    public const int BuiltInDefaultMaxCellBytes = 256 * 1024;
    public const long BuiltInDefaultMaxPayloadBytes = 8L * 1024 * 1024;

    public int DefaultMaxRows { get; set; } = BuiltInDefaultMaxRows;
    public int MaxCellBytes { get; set; } = BuiltInDefaultMaxCellBytes;
    public long MaxPayloadBytes { get; set; } = BuiltInDefaultMaxPayloadBytes;

    public static int ResolveDefaultMaxRows(IOptions<PreviewExecutionOptions>? options)
    {
        if (options is not null && options.Value.DefaultMaxRows > 0)
            return options.Value.DefaultMaxRows;

        string? fromEnv = Environment.GetEnvironmentVariable(MaxRowsEnvironmentVariable);
        if (int.TryParse(fromEnv, out int parsed) && parsed > 0)
            return parsed;

        int configuredFallback = options?.Value.DefaultMaxRows ?? BuiltInDefaultMaxRows;
        if (configuredFallback > 0)
            return configuredFallback;

        return BuiltInDefaultMaxRows;
    }

    public static bool IsUnlimitedRequested(int maxRows) => maxRows == NoLimit;
}
