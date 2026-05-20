using AkkornStudio.UI.Controls.Query;
using AkkornStudio.UI.Services;

namespace AkkornStudio.Tests.Unit.Controls;

public sealed class QueryParameterPromptModelTests
{
    [Fact]
    public void BuildField_PrefersRememberedValueOverSuggestedParameter()
    {
        QueryParameterPlaceholder placeholder = new("@min_id", QueryParameterPlaceholderKind.Named);
        string key = QueryParameterPlaceholderParser.GetStorageKey(placeholder);

        QueryParameterPromptField field = QueryParameterPromptModel.BuildField(
            "SELECT * FROM orders WHERE id >= @min_id",
            placeholder,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [key] = "99",
            },
            new Dictionary<string, QueryParameter>(StringComparer.OrdinalIgnoreCase)
            {
                [key] = new QueryParameter("@min_id", 42),
            },
            new Dictionary<string, QueryExecutionParameterContext>(StringComparer.OrdinalIgnoreCase),
            metadata: null,
            provider: AkkornStudio.Core.DatabaseProvider.Postgres);

        Assert.Equal(placeholder, field.Placeholder);
        Assert.Equal(key, field.StorageKey);
        Assert.Equal("integer", field.Hint.TypeLabel);
        Assert.Equal("99", field.InitialText);
        Assert.Equal(QueryParameterPromptInputKind.Integer, field.InputKind);
    }

    [Fact]
    public void BuildField_UsesSuggestedNullAsInitialNullState()
    {
        QueryParameterPlaceholder placeholder = new("@status", QueryParameterPlaceholderKind.Named);
        string key = QueryParameterPlaceholderParser.GetStorageKey(placeholder);

        QueryParameterPromptField field = QueryParameterPromptModel.BuildField(
            "SELECT * FROM orders WHERE status = @status",
            placeholder,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, QueryParameter>(StringComparer.OrdinalIgnoreCase)
            {
                [key] = new QueryParameter("@status", null),
            },
            new Dictionary<string, QueryExecutionParameterContext>(StringComparer.OrdinalIgnoreCase),
            metadata: null,
            provider: AkkornStudio.Core.DatabaseProvider.Postgres);

        Assert.Equal(placeholder, field.Placeholder);
        Assert.Equal(key, field.StorageKey);
        Assert.NotNull(field.Hint);
        Assert.Equal("NULL", field.InitialText);
        Assert.True(field.StartsAsNull);
        Assert.Equal(QueryParameterPromptInputKind.Text, field.InputKind);
    }

    [Theory]
    [InlineData("boolean", "Boolean")]
    [InlineData("integer", "Integer")]
    [InlineData("decimal", "Decimal")]
    [InlineData("date/time", "DateTime")]
    [InlineData("text", "Text")]
    public void ResolveInputKind_MapsHintTypesToPromptControlKinds(
        string typeLabel,
        string expected)
    {
        QueryParameterHint hint = new(typeLabel, "sample", "description");

        Assert.Equal(expected, QueryParameterPromptModel.ResolveInputKind(hint).ToString());
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("42", 42)]
    [InlineData("1", 1)]
    [InlineData("0", 0)]
    [InlineData("NULL", null)]
    [InlineData("", "")]
    public void ParseInputValue_ConvertsPromptTextToTypedValue(string raw, object? expected)
    {
        object? parsed = QueryParameterPromptModel.ParseInputValue(raw);

        Assert.Equal(expected, parsed);
    }

    [Fact]
    public void ParseInputValue_ParsesBooleanLiteralWhenValueIsNotNumeric()
    {
        object? parsed = QueryParameterPromptModel.ParseInputValue("false");

        Assert.False(Assert.IsType<bool>(parsed));
    }

    [Fact]
    public void ParseInputValue_ParsesDecimalWithInvariantCulture()
    {
        object? parsed = QueryParameterPromptModel.ParseInputValue("19.5");

        Assert.Equal(19.5m, Assert.IsType<decimal>(parsed));
    }

    [Fact]
    public void ParseInputValue_ParsesInt64WhenNumberExceedsInt32Range()
    {
        object? parsed = QueryParameterPromptModel.ParseInputValue("2147483648");

        Assert.Equal(2147483648L, Assert.IsType<long>(parsed));
    }

    [Fact]
    public void ParseInputValue_ParsesRoundTripDateTime()
    {
        object? parsed = QueryParameterPromptModel.ParseInputValue("2026-04-22T13:45:00.0000000Z");

        DateTime value = Assert.IsType<DateTime>(parsed);
        Assert.Equal(DateTimeKind.Utc, value.Kind);
        Assert.Equal(new DateTime(2026, 4, 22, 13, 45, 0, DateTimeKind.Utc), value);
    }

    [Fact]
    public void ParseInputValue_WhenValueIsNotRecognized_ReturnsOriginalRawString()
    {
        object? parsed = QueryParameterPromptModel.ParseInputValue("not-a-supported-literal");

        Assert.Equal("not-a-supported-literal", Assert.IsType<string>(parsed));
    }

    [Fact]
    public void BuildResult_ReturnsNullWhenPromptIsCancelled()
    {
        QueryParameterPlaceholder placeholder = new("@id", QueryParameterPlaceholderKind.Named);

        IReadOnlyList<QueryParameter>? result = QueryParameterPromptModel.BuildResult(
            [placeholder],
            new Dictionary<QueryParameterPlaceholder, string>
            {
                [placeholder] = "42",
            },
            cancelled: true);

        Assert.Null(result);
    }

    [Fact]
    public void BuildResult_UsesNamedAndPositionalParameterBindings()
    {
        QueryParameterPlaceholder named = new("@id", QueryParameterPlaceholderKind.Named);
        QueryParameterPlaceholder positional = new("?", QueryParameterPlaceholderKind.Positional, 1);

        IReadOnlyList<QueryParameter>? result = QueryParameterPromptModel.BuildResult(
            [named, positional],
            new Dictionary<QueryParameterPlaceholder, string>
            {
                [named] = "42",
                [positional] = "NULL",
            },
            cancelled: false);

        Assert.NotNull(result);
        Assert.Equal("@id", result[0].Name);
        Assert.Equal(42, result[0].Value);
        Assert.Null(result[1].Name);
        Assert.Null(result[1].Value);
    }

    [Fact]
    public void BuildResult_WhenRawValueIsMissing_UsesEmptyString()
    {
        QueryParameterPlaceholder named = new("@name", QueryParameterPlaceholderKind.Named);

        IReadOnlyList<QueryParameter>? result = QueryParameterPromptModel.BuildResult(
            [named],
            new Dictionary<QueryParameterPlaceholder, string>(),
            cancelled: false);

        Assert.NotNull(result);
        Assert.Equal(string.Empty, Assert.IsType<string>(result[0].Value));
    }

    [Fact]
    public void FormatSuggestedValue_WhenSuggestionIsNull_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, QueryParameterPromptModel.FormatSuggestedValue(null));
    }

    [Fact]
    public void FormatSuggestedValue_WhenSuggestedValueIsNull_ReturnsNullLiteral()
    {
        Assert.Equal("NULL", QueryParameterPromptModel.FormatSuggestedValue(new QueryParameter("@p", null)));
    }

    [Fact]
    public void FormatSuggestedValue_WhenSuggestedValueIsDateTime_ReturnsRoundTripString()
    {
        DateTime value = new(2026, 5, 20, 10, 30, 15, DateTimeKind.Utc);

        string formatted = QueryParameterPromptModel.FormatSuggestedValue(new QueryParameter("@p", value));

        Assert.Equal(value.ToString("O", System.Globalization.CultureInfo.InvariantCulture), formatted);
    }

    [Fact]
    public void FormatSuggestedValue_WhenSuggestedValueIsDateTimeOffset_ReturnsRoundTripString()
    {
        DateTimeOffset value = new(2026, 5, 20, 10, 30, 15, TimeSpan.FromHours(-3));

        string formatted = QueryParameterPromptModel.FormatSuggestedValue(new QueryParameter("@p", value));

        Assert.Equal(value.ToString("O", System.Globalization.CultureInfo.InvariantCulture), formatted);
    }

    [Fact]
    public void FormatSuggestedValue_WhenSuggestedValueIsBoolean_ReturnsLowercaseLiteral()
    {
        Assert.Equal("true", QueryParameterPromptModel.FormatSuggestedValue(new QueryParameter("@p", true)));
        Assert.Equal("false", QueryParameterPromptModel.FormatSuggestedValue(new QueryParameter("@p", false)));
    }

    [Fact]
    public void FormatSuggestedValue_WhenSuggestedValueIsIFormattable_UsesInvariantCulture()
    {
        decimal value = 1234.50m;
        string formatted = QueryParameterPromptModel.FormatSuggestedValue(new QueryParameter("@p", value));

        Assert.Equal(value.ToString(null, System.Globalization.CultureInfo.InvariantCulture), formatted);
    }

    [Fact]
    public void FormatSuggestedValue_WhenSuggestedValueIsNonFormattable_UsesToStringOrEmpty()
    {
        string formatted = QueryParameterPromptModel.FormatSuggestedValue(new QueryParameter("@p", new object()));

        Assert.False(string.IsNullOrWhiteSpace(formatted));
    }

    [Fact]
    public void FormatSuggestedValue_WhenToStringReturnsNull_FallsBackToEmptyString()
    {
        string formatted = QueryParameterPromptModel.FormatSuggestedValue(new QueryParameter("@p", new NullToStringValue()));

        Assert.Equal(string.Empty, formatted);
    }

    private sealed class NullToStringValue
    {
        public override string ToString() => null!;
    }
}
