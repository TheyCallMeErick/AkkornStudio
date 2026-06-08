using System.Text.RegularExpressions;

namespace AkkornStudio.UI.Services;

internal static partial class QueryParameterPlaceholderParser
{
    public static IReadOnlyList<QueryParameterPlaceholder> Parse(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return [];

        List<QueryParameterPlaceholder> placeholders = [];
        HashSet<string> namedSeen = new(StringComparer.OrdinalIgnoreCase);
        int questionIndex = 0;

        foreach (Match match in CombinedRegex().Matches(sql))
        {
            if (!match.Success)
                continue;

            string token = match.Value;
            if (string.IsNullOrWhiteSpace(token))
                continue;

            if (token[0] is '@' or ':')
            {
                if (namedSeen.Add(token))
                    placeholders.Add(new QueryParameterPlaceholder(token, QueryParameterPlaceholderKind.Named));
                continue;
            }

            if (token[0] == '?')
            {
                questionIndex++;
                placeholders.Add(new QueryParameterPlaceholder(token, QueryParameterPlaceholderKind.Positional, questionIndex));
                continue;
            }

            if (token[0] == '$' && int.TryParse(token[1..], out int position))
                placeholders.Add(new QueryParameterPlaceholder(token, QueryParameterPlaceholderKind.Positional, position));
        }

        return placeholders;
    }

    public static string NormalizeName(string token) =>
        token.Trim().TrimStart('@', ':', '$');

    public static string GetStorageKey(QueryParameterPlaceholder placeholder) =>
        placeholder.Kind == QueryParameterPlaceholderKind.Named
            ? $"named:{NormalizeName(placeholder.Token)}"
            : $"pos:{placeholder.Position ?? 0}:{placeholder.Token}";

    [GeneratedRegex(@"(?<!@)@[A-Za-z_][A-Za-z0-9_]*|(?<!:):[A-Za-z_][A-Za-z0-9_]*|\?|\$\d+", RegexOptions.CultureInvariant)]
    private static partial Regex CombinedRegex();
}
