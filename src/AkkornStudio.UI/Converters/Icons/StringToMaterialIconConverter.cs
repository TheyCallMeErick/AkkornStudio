using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Material.Icons;

namespace AkkornStudio.UI.Converters;

public class StringToMaterialIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo? culture)
    {
        if (value is MaterialIconKind kindValue)
        {
            return kindValue;
        }

        if (value is null)
            return MaterialIconKind.HelpCircleOutline;

        if (value is string iconName)
        {
            string normalized = iconName.Trim();
            if (normalized.Length == 0)
                return MaterialIconKind.HelpCircleOutline;

            if (Enum.TryParse<MaterialIconKind>(normalized, ignoreCase: true, out MaterialIconKind parsed))
                return parsed;
        }

        return AvaloniaProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo? culture)
    {
        throw new NotSupportedException();
    }
}
