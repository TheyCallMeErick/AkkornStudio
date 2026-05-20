using Avalonia;
using AkkornStudio.UI.Converters;
using Material.Icons;

namespace AkkornStudio.Tests.Unit.Converters;

public sealed class StringToMaterialIconConverterTests
{
    [Fact]
    public void Convert_WithMaterialIconKind_ReturnsSameEnum()
    {
        var sut = new StringToMaterialIconConverter();

        object? result = sut.Convert(MaterialIconKind.Database, typeof(object), null, null);

        Assert.Equal(MaterialIconKind.Database, result);
    }

    [Fact]
    public void Convert_WithValidIconName_IsCaseInsensitiveAndTrimmed()
    {
        var sut = new StringToMaterialIconConverter();

        object? result = sut.Convert("  database  ", typeof(object), null, null);

        Assert.Equal(MaterialIconKind.Database, result);
    }

    [Fact]
    public void Convert_WithNullOrWhitespace_ReturnsFallbackIcon()
    {
        var sut = new StringToMaterialIconConverter();

        object? fromNull = sut.Convert(null, typeof(object), null, null);
        object? fromWhitespace = sut.Convert("   ", typeof(object), null, null);

        Assert.Equal(MaterialIconKind.HelpCircleOutline, fromNull);
        Assert.Equal(MaterialIconKind.HelpCircleOutline, fromWhitespace);
    }

    [Fact]
    public void Convert_WithInvalidName_ReturnsUnsetValue()
    {
        var sut = new StringToMaterialIconConverter();

        object? result = sut.Convert("not_an_icon", typeof(object), null, null);

        Assert.Same(AvaloniaProperty.UnsetValue, result);
    }

    [Fact]
    public void Convert_WithUnsupportedType_ReturnsUnsetValue()
    {
        var sut = new StringToMaterialIconConverter();

        object? result = sut.Convert(123, typeof(object), null, null);

        Assert.Same(AvaloniaProperty.UnsetValue, result);
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupported()
    {
        var sut = new StringToMaterialIconConverter();

        Assert.Throws<NotSupportedException>(() => sut.ConvertBack(null, typeof(object), null, null));
    }
}
