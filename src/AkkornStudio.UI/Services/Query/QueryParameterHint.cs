namespace AkkornStudio.UI.Services;

internal sealed record QueryParameterHint(
    string TypeLabel,
    string ExampleValue,
    string Description,
    string? ContextLabel = null);
