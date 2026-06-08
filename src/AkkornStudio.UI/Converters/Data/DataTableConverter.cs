using System.Data;
using Avalonia.Data.Converters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AkkornStudio.UI.Converters;

/// <summary>
/// Converts a DataTable to its DefaultView for binding to Avalonia DataGrid.
/// Avalonia DataGrid doesn't work well with raw DataTable binding; it needs
/// a DataView which properly implements IEnumerable with change notification.
/// </summary>
public class DataTableConverter : IValueConverter
{
    private static readonly ILogger<DataTableConverter> _logger = NullLogger<DataTableConverter>.Instance;

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo? culture)
    {
        _logger.LogDebug("Convert called with value type: {ValueType}", value?.GetType().Name ?? "null");

        if (value is DataTable dt)
        {
            // Return the DefaultView which supports proper binding and change notifications
            _logger.LogDebug("Converting DataTable with {RowCount} rows to DefaultView", dt.Rows.Count);
            var view = dt.DefaultView;
            _logger.LogDebug("DefaultView created, RowFilter='{RowFilter}', Count={Count}", view.RowFilter, view.Count);
            return view;
        }

        if (value is DataView dv)
        {
            _logger.LogDebug("Value is already a DataView with {RowCount} rows", dv.Count);
            return dv;
        }

        if (value is null)
        {
            _logger.LogDebug("Value is null, returning empty DataView");
            return CreateEmptyView();
        }

        _logger.LogWarning(
            "Unsupported value type '{ValueType}' for DataTableConverter. Returning empty DataView.",
            value.GetType().FullName
        );
        return CreateEmptyView();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo? culture)
    {
        throw new NotSupportedException();
    }

    private static DataView CreateEmptyView() => new(new DataTable());
}
