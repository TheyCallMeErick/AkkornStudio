using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using AkkornStudio.Core;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Extensions;

public static class ReportBuilderExtensions
{
    public static string ResolveReportLanguageTag(this CultureInfo culture) =>
        culture.Name.StartsWith("pt", StringComparison.OrdinalIgnoreCase) ? "pt-BR" : "en";

    public static string PickReportText(this string language, string pt, string en) =>
        language.StartsWith("pt", StringComparison.OrdinalIgnoreCase) ? pt : en;

    public static string ToEmptyValueMode(this SqlEditorReportEmptyValueDisplayMode mode) => mode switch
    {
        SqlEditorReportEmptyValueDisplayMode.Dash => "dash",
        SqlEditorReportEmptyValueDisplayMode.NullLiteral => "null",
        _ => "blank"
    };

    public static string ToReportDialect(this DatabaseProvider? provider) => provider switch
    {
        DatabaseProvider.Postgres => "postgresql",
        DatabaseProvider.SqlServer => "sqlserver",
        DatabaseProvider.MySql => "mysql",
        DatabaseProvider.SQLite => "sqlite",
        _ => "unknown"
    };

    public static string NormalizeReportStatus(this string? status) =>
        string.IsNullOrWhiteSpace(status) ? "success" : status.Trim().ToLowerInvariant() switch
        {
            "error" => "error",
            "warning" => "warning",
            _ => "success"
        };

    public static int CountRegexMatches(this string? input, string pattern) =>
        string.IsNullOrWhiteSpace(input) ? 0 : Regex.Matches(input, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count;

    public static bool ContainsSqlSubquery(this string? sql) =>
        !string.IsNullOrWhiteSpace(sql)
        && (Regex.IsMatch(sql, @"\(\s*select\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || Regex.IsMatch(sql, @"\bexists\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

    public static string ToHtmlEncoded(this string? value) =>
        System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

    public static string ToInlineScriptJson(this object value)
    {
        string json = JsonSerializer.Serialize(value, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        return Regex.Replace(json, "</script", "<\\/script", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static string DetectReportValueKind(this object value) => value switch
    {
        bool => "bool",
        byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => "number",
        DateTime or DateTimeOffset => "date",
        _ => "text"
    };
}
