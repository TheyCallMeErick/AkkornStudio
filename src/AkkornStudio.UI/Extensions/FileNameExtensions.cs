using System.IO;
using System.Linq;

namespace AkkornStudio.UI.Extensions;

public static class FileNameExtensions
{
    public static string ToSafeFileBase(this string? value, string fallback = "report")
    {
        string safeBase = string.Concat((value ?? string.Empty).Select(ch =>
            char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));

        return string.IsNullOrWhiteSpace(safeBase) ? fallback : safeBase;
    }

    public static string EnsureExtension(this string? fileName, string extension)
        => fileName.EnsureExtension(extension, extension);

    public static string EnsureExtension(this string? fileName, string previousExtension, string nextExtension)
    {
        string normalizedFileName = string.IsNullOrWhiteSpace(fileName) ? "report" : fileName.Trim();
        string normalizedPreviousExtension = previousExtension.TrimStart('.');
        string normalizedExtension = nextExtension.TrimStart('.');

        if (normalizedFileName.EndsWith($".{normalizedExtension}", StringComparison.OrdinalIgnoreCase))
            return normalizedFileName;

        if (normalizedFileName.EndsWith($".{normalizedPreviousExtension}", StringComparison.OrdinalIgnoreCase))
            return normalizedFileName[..^(normalizedPreviousExtension.Length)] + normalizedExtension;

        return Path.GetFileNameWithoutExtension(normalizedFileName) + "." + normalizedExtension;
    }
}
