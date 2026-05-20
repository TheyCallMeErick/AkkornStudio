using System.Globalization;
using Avalonia.Data.Converters;

namespace AkkornStudio.UI.Converters;

public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo? culture)
    {
        if (value is null || parameter is null)
            return false;

        if (value is Enum enumValue)
        {
            if (parameter.GetType() == enumValue.GetType())
                return Equals(enumValue, parameter);

            if (parameter is string enumName)
                return Enum.TryParse(enumValue.GetType(), enumName, ignoreCase: true, out object? parsed)
                    && Equals(enumValue, parsed);

            return false;
        }

        string left = value.ToString() ?? string.Empty;
        string right = parameter.ToString() ?? string.Empty;
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo? culture)
    {
        throw new NotSupportedException();
    }
}
