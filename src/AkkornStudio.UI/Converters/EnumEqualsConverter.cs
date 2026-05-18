using System.Globalization;
using Avalonia.Data.Converters;

namespace AkkornStudio.UI.Converters;

public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo? culture)
    {
        string left = value?.ToString() ?? string.Empty;
        string right = parameter?.ToString() ?? string.Empty;
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo? culture)
    {
        throw new NotSupportedException();
    }
}
