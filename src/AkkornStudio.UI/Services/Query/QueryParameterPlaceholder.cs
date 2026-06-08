namespace AkkornStudio.UI.Services;

internal enum QueryParameterPlaceholderKind
{
    Named,
    Positional,
}

internal sealed record QueryParameterPlaceholder(
    string Token,
    QueryParameterPlaceholderKind Kind,
    int? Position = null);
