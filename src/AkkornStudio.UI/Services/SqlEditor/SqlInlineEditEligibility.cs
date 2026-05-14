namespace AkkornStudio.UI.Services.SqlEditor;

public sealed record SqlInlineEditEligibility(
    bool IsEligible,
    string? TableFullName,
    IReadOnlyList<string> PrimaryKeyColumns,
    IReadOnlyList<string> EditableColumns,
    string? IneligibilityReason = null)
{
    public static SqlInlineEditEligibility NotEligible { get; } =
        new(false, null, [], [], "Safe inline edit unavailable for this result set.");

    public static SqlInlineEditEligibility Ineligible(string reason)
    {
        string normalized = string.IsNullOrWhiteSpace(reason)
            ? "Safe inline edit unavailable for this result set."
            : reason.Trim();
        return new SqlInlineEditEligibility(false, null, [], [], normalized);
    }
}
