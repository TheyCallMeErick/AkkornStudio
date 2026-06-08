namespace AkkornStudio.UI.Services;

/// <summary>
/// Represents a SQL parameter value supplied to preview execution.
/// Use <see cref="Name"/> for named placeholders like <c>@id</c> or <c>:id</c>.
/// Leave <see cref="Name"/> empty for positional placeholders like <c>?</c> or <c>$1</c>.
/// </summary>
public sealed record QueryParameter(string? Name, object? Value);
