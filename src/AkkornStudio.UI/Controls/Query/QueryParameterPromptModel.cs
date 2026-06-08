using System.Globalization;
using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.UI.Services;

namespace AkkornStudio.UI.Controls.Query;

internal enum QueryParameterPromptInputKind
{
    Text,
    Boolean,
    Integer,
    Decimal,
    DateTime,
}

internal sealed record QueryParameterPromptField(
    QueryParameterPlaceholder Placeholder,
    string StorageKey,
    QueryParameterHint Hint,
    string InitialText,
    QueryParameterPromptInputKind InputKind,
    bool StartsAsNull);

internal static class QueryParameterPromptModel
{
    public static QueryParameterPromptField BuildField(
        string sql,
        QueryParameterPlaceholder placeholder,
        IReadOnlyDictionary<string, string> initialValues,
        IReadOnlyDictionary<string, QueryParameter> suggestedParameters,
        IReadOnlyDictionary<string, QueryExecutionParameterContext> structuralContexts,
        DbMetadata? metadata,
        DatabaseProvider provider)
    {
        string storageKey = QueryParameterPlaceholderParser.GetStorageKey(placeholder);
        suggestedParameters.TryGetValue(storageKey, out QueryParameter? suggestedParameter);
        structuralContexts.TryGetValue(storageKey, out QueryExecutionParameterContext? structuralContext);

        QueryParameterHint hint = QueryParameterHintResolver.Resolve(
            sql,
            placeholder,
            suggestedParameter,
            structuralContext,
            metadata,
            provider);

        string initialText = initialValues.TryGetValue(storageKey, out string? remembered)
            ? remembered
            : FormatSuggestedValue(suggestedParameter);

        return new QueryParameterPromptField(
            placeholder,
            storageKey,
            hint,
            initialText,
            ResolveInputKind(hint),
            IsNullLiteral(initialText));
    }

    public static QueryParameterPromptInputKind ResolveInputKind(QueryParameterHint hint) =>
        hint.TypeLabel switch
        {
            "boolean" => QueryParameterPromptInputKind.Boolean,
            "integer" => QueryParameterPromptInputKind.Integer,
            "decimal" => QueryParameterPromptInputKind.Decimal,
            "date/time" => QueryParameterPromptInputKind.DateTime,
            _ => QueryParameterPromptInputKind.Text,
        };

    public static IReadOnlyList<QueryParameter>? BuildResult(
        IReadOnlyList<QueryParameterPlaceholder> placeholders,
        IReadOnlyDictionary<QueryParameterPlaceholder, string> rawValues,
        bool cancelled)
    {
        if (cancelled)
            return null;

        List<QueryParameter> parameters = [];
        foreach (QueryParameterPlaceholder placeholder in placeholders)
        {
            string raw = rawValues.TryGetValue(placeholder, out string? value)
                ? value
                : string.Empty;
            object? parsed = ParseInputValue(raw);

            parameters.Add(placeholder.Kind == QueryParameterPlaceholderKind.Named
                ? new QueryParameter(placeholder.Token, parsed)
                : new QueryParameter(null, parsed));
        }

        return parameters;
    }

    public static object? ParseInputValue(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        if (IsNullLiteral(raw))
            return null;
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
            return intValue;
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long longValue))
            return longValue;
        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal decimalValue))
            return decimalValue;
        if (bool.TryParse(raw, out bool boolValue))
            return boolValue;
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime dateTime))
            return dateTime;

        return raw;
    }

    public static string FormatSuggestedValue(QueryParameter? suggestedParameter)
    {
        if (suggestedParameter is null)
            return string.Empty;

        if (suggestedParameter.Value is null)
            return "NULL";

        return suggestedParameter.Value switch
        {
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            bool boolValue => boolValue ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => suggestedParameter.Value.ToString() ?? string.Empty,
        };
    }

    private static bool IsNullLiteral(string raw) =>
        string.Equals(raw, "null", StringComparison.OrdinalIgnoreCase);
}
