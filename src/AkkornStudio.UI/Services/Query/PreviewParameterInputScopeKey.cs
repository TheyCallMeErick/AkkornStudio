using AkkornStudio.Core;

namespace AkkornStudio.UI.Services;

internal static class PreviewParameterInputScopeKey
{
    public static string BuildScopedKey(ConnectionConfig? config, string parameterKey)
    {
        if (config is null)
            return parameterKey;

        return $"{BuildScopePrefix(config)}{parameterKey}";
    }

    public static string BuildScopePrefix(ConnectionConfig config)
    {
        return string.Join(
            "|",
            config.Provider,
            config.Host ?? string.Empty,
            config.Port,
            config.Database ?? string.Empty,
            string.Empty);
    }
}
