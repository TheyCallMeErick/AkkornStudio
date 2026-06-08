using System.Data;
using AkkornStudio.UI.Converters;

namespace AkkornStudio.Tests.Unit.Converters;

public sealed class DataTableConverterTests
{
    [Fact]
    public void Convert_WithDataTable_ReturnsDefaultView()
    {
        var converter = new DataTableConverter();
        var table = new DataTable();
        table.Columns.Add("id", typeof(int));
        table.Rows.Add(1);
        table.Rows.Add(2);

        object? result = converter.Convert(table, typeof(object), null, null);

        var view = Assert.IsType<DataView>(result);
        Assert.Same(table.DefaultView, view);
        Assert.Equal(2, view.Count);
    }

    [Fact]
    public void Convert_WithDataView_ReturnsSameInstance()
    {
        var converter = new DataTableConverter();
        var table = new DataTable();
        table.Columns.Add("id", typeof(int));
        table.Rows.Add(1);
        DataView dataView = table.DefaultView;

        object? result = converter.Convert(dataView, typeof(object), null, null);

        Assert.Same(dataView, result);
    }

    [Fact]
    public void Convert_WithNull_ReturnsEmptyDataView()
    {
        var converter = new DataTableConverter();

        object? result = converter.Convert(null, typeof(object), null, null);

        var view = Assert.IsType<DataView>(result);
        Assert.Empty(view);
        Assert.NotNull(view.Table);
        Assert.Empty(view.Table.Columns);
    }

    [Fact]
    public void Convert_WithUnsupportedType_ReturnsEmptyDataView()
    {
        var converter = new DataTableConverter();

        object? result = converter.Convert(123, typeof(object), null, null);

        var view = Assert.IsType<DataView>(result);
        Assert.Empty(view);
        Assert.NotNull(view.Table);
        Assert.Empty(view.Table.Columns);
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupported()
    {
        var converter = new DataTableConverter();

        Assert.Throws<NotSupportedException>(() => converter.ConvertBack(null, typeof(object), null, null));
    }
}
