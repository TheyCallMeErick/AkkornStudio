using System.Globalization;
using Avalonia.Data.Converters;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Converters;

/// <summary>Maps issue sort/group modes to short human-readable labels for the analyzer toolbar.</summary>
public sealed class IssueModeLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo? culture)
    {
        return value switch
        {
            IssueSortMode sort => sort switch
            {
                IssueSortMode.Severity => "Severidade",
                IssueSortMode.Confidence => "Confiança",
                IssueSortMode.Frequency => "Frequência",
                IssueSortMode.Table => "Tabela",
                _ => sort.ToString(),
            },
            IssueGroupMode group => group switch
            {
                IssueGroupMode.Severity => "Severidade",
                IssueGroupMode.Table => "Tabela",
                IssueGroupMode.Type => "Tipo",
                IssueGroupMode.Schema => "Schema",
                _ => group.ToString(),
            },
            _ => value?.ToString() ?? string.Empty,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo? culture)
    {
        throw new NotSupportedException();
    }
}
