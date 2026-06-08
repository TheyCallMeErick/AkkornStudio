using System.Globalization;
using System.Text;

namespace AkkornStudio.UI.Extensions;

public static class StringTransformExtensions
{
    public static string ToSlugCase(this string? value)
        => value.ToSlugToken('-', string.Empty);

    public static string ToSlugToken(this string? value, char separator = '-', string emptyFallback = "")
    {
        if (string.IsNullOrWhiteSpace(value))
            return emptyFallback;

        string normalized = value.Normalize(NormalizationForm.FormD);
        StringBuilder builder = new(normalized.Length);
        bool lastWasSeparator = false;

        foreach (char ch in normalized)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                lastWasSeparator = false;
                continue;
            }

            if (lastWasSeparator)
                continue;

            builder.Append(separator);
            lastWasSeparator = true;
        }

        string result = builder.ToString().Trim(separator);
        return string.IsNullOrWhiteSpace(result) ? emptyFallback : result;
    }

    public static string ToTitleCaseInvariant(this string? value)
    {
        string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized)
            ? string.Empty
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized);
    }

    public static string ToSentenceCaseInvariant(this string? value)
    {
        string trimmed = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return string.Empty;

        string lower = trimmed.ToLowerInvariant();
        return char.ToUpperInvariant(lower[0]) + lower[1..];
    }

    public static string TransformText(this string? value, string? mode)
    {
        string text = value ?? string.Empty;
        return (mode ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "upper" => text.ToUpperInvariant(),
            "lower" => text.ToLowerInvariant(),
            "title" => text.ToTitleCaseInvariant(),
            "sentence" => text.ToSentenceCaseInvariant(),
            "slug" => text.ToSlugCase(),
            _ => text,
        };
    }
}
