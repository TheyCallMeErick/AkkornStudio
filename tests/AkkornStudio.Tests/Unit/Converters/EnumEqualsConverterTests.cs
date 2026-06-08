using AkkornStudio.UI.Converters;

namespace AkkornStudio.Tests.Unit.Converters;

public sealed class EnumEqualsConverterTests
{
    private enum Severity
    {
        Info,
        Warning,
        Critical,
    }

    private enum State
    {
        Warning,
    }

    private sealed class NullStringObject
    {
        public override string? ToString() => null;
    }

    [Fact]
    public void Convert_WhenEnumMatchesStringParameter_ReturnsTrue()
    {
        var sut = new EnumEqualsConverter();

        bool result = Assert.IsType<bool>(sut.Convert(Severity.Warning, typeof(bool), "Warning", culture: null));

        Assert.True(result);
    }

    [Fact]
    public void Convert_WhenEnumDoesNotMatchStringParameter_ReturnsFalse()
    {
        var sut = new EnumEqualsConverter();

        bool result = Assert.IsType<bool>(sut.Convert(Severity.Info, typeof(bool), "Warning", culture: null));

        Assert.False(result);
    }

    [Fact]
    public void Convert_WhenEnumParameterStringIsInvalid_ReturnsFalse()
    {
        var sut = new EnumEqualsConverter();

        bool result = Assert.IsType<bool>(sut.Convert(Severity.Info, typeof(bool), "NOT_A_MEMBER", culture: null));

        Assert.False(result);
    }

    [Fact]
    public void Convert_WhenEnumMatchesEnumSameType_ReturnsTrue()
    {
        var sut = new EnumEqualsConverter();

        bool result = Assert.IsType<bool>(sut.Convert(Severity.Critical, typeof(bool), Severity.Critical, culture: null));

        Assert.True(result);
    }

    [Fact]
    public void Convert_WhenEnumTypeDiffers_ReturnsFalse()
    {
        var sut = new EnumEqualsConverter();

        bool result = Assert.IsType<bool>(sut.Convert(Severity.Warning, typeof(bool), State.Warning, culture: null));

        Assert.False(result);
    }

    [Fact]
    public void Convert_WhenValueOrParameterIsNull_ReturnsFalse()
    {
        var sut = new EnumEqualsConverter();

        bool nullValue = Assert.IsType<bool>(sut.Convert(null, typeof(bool), "Warning", culture: null));
        bool nullParameter = Assert.IsType<bool>(sut.Convert(Severity.Warning, typeof(bool), null, culture: null));

        Assert.False(nullValue);
        Assert.False(nullParameter);
    }

    [Fact]
    public void Convert_WhenNonEnumValuesMatchIgnoringCase_ReturnsTrue()
    {
        var sut = new EnumEqualsConverter();

        bool result = Assert.IsType<bool>(sut.Convert("warning", typeof(bool), "Warning", culture: null));

        Assert.True(result);
    }

    [Fact]
    public void Convert_WhenNonEnumToStringReturnsNull_UsesEmptyFallback()
    {
        var sut = new EnumEqualsConverter();

        bool result = Assert.IsType<bool>(sut.Convert(new NullStringObject(), typeof(bool), new NullStringObject(), culture: null));

        Assert.True(result);
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        var sut = new EnumEqualsConverter();

        Assert.Throws<NotSupportedException>(() => sut.ConvertBack(true, typeof(object), null, culture: null));
    }
}
