using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.UI.Services;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.Services.Theming;

namespace AkkornStudio.UI.Controls.Query;

internal sealed class QueryParameterPromptWindow : Window
{
    private readonly IReadOnlyList<QueryParameterPlaceholder> _placeholders;
    private readonly Dictionary<QueryParameterPlaceholder, ParameterInputAdapter> _inputs = [];
    private readonly IReadOnlyDictionary<string, string> _initialValues;
    private readonly IReadOnlyDictionary<string, QueryParameter> _suggestedParameters;
    private readonly IReadOnlyDictionary<string, QueryExecutionParameterContext> _structuralContexts;
    private readonly DbMetadata? _metadata;
    private readonly DatabaseProvider _provider;
    private readonly string _sql;

    public QueryParameterPromptWindow(
        string sql,
        IReadOnlyList<QueryParameterPlaceholder> placeholders,
        IReadOnlyDictionary<string, string>? initialValues = null,
        IReadOnlyDictionary<string, QueryParameter>? suggestedParameters = null,
        IReadOnlyDictionary<string, QueryExecutionParameterContext>? structuralContexts = null,
        DbMetadata? metadata = null,
        DatabaseProvider provider = DatabaseProvider.Postgres)
    {
        _sql = sql;
        _placeholders = placeholders;
        _initialValues = initialValues ?? new Dictionary<string, string>();
        _suggestedParameters = suggestedParameters ?? new Dictionary<string, QueryParameter>();
        _structuralContexts = structuralContexts ?? new Dictionary<string, QueryExecutionParameterContext>(StringComparer.OrdinalIgnoreCase);
        _metadata = metadata;
        _provider = provider;

        Title = L("queryParameters.dialog.title", "Preview parameters");
        Width = 640;
        Height = 520;
        MinWidth = 520;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse(UiColorConstants.C_0D0F14));
        KeyDown += OnKeyDown;
        Content = BuildContent();
    }

    public IReadOnlyList<QueryParameter>? Result { get; private set; }

    public IReadOnlyDictionary<string, string> EnteredValues { get; private set; } =
        new Dictionary<string, string>();

    private Control BuildContent()
    {
        StackPanel rows = new()
        {
            Spacing = 10,
        };

        foreach (QueryParameterPlaceholder placeholder in _placeholders)
        {
            QueryParameterPromptField field = QueryParameterPromptModel.BuildField(
                _sql,
                placeholder,
                _initialValues,
                _suggestedParameters,
                _structuralContexts,
                _metadata,
                _provider);
            _structuralContexts.TryGetValue(field.StorageKey, out QueryExecutionParameterContext? structuralContext);
            string label = placeholder.Kind == QueryParameterPlaceholderKind.Named
                ? placeholder.Token
                : placeholder.Token == "?"
                    ? $"? #{placeholder.Position}"
                    : placeholder.Token;

            ParameterInputAdapter input = CreateInputAdapter(field.Hint, field.InitialText, field.InputKind);
            _inputs[placeholder] = input;
            StackPanel headerPanel = new()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 8,
            };
            headerPanel.Children.Add(new TextBlock
            {
                Text = label,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse(UiColorConstants.C_E8EAED)),
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (BuildExpressionKindBadge(structuralContext) is Control expressionBadge)
                headerPanel.Children.Add(expressionBadge);
            if (BuildExpressionDetailBadge(structuralContext) is Control detailBadge)
                headerPanel.Children.Add(detailBadge);

            StackPanel fieldPanel = new()
            {
                Spacing = 4,
                Children =
                {
                    headerPanel,
                    new TextBlock
                    {
                        Text = $"{field.Hint.TypeLabel} · {field.Hint.Description}",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.Parse(UiColorConstants.C_9CA3AF)),
                    },
                },
            };

            if (!string.IsNullOrWhiteSpace(field.Hint.ContextLabel))
            {
                fieldPanel.Children.Add(new TextBlock
                {
                    Text = field.Hint.ContextLabel,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.Parse(UiColorConstants.C_60A5FA)),
                });
            }

            fieldPanel.Children.Add(input.Control);
            rows.Children.Add(fieldPanel);
        }

        Button cancelButton = new()
        {
            Content = L("queryParameters.dialog.cancel", "Cancel"),
            MinWidth = 96,
            Padding = new Thickness(12, 6),
        };
        cancelButton.Click += (_, _) => Close();

        Button runButton = new()
        {
            Content = L("queryParameters.dialog.run", "Run preview"),
            MinWidth = 120,
            Padding = new Thickness(12, 6),
        };
        runButton.Click += (_, _) =>
        {
            EnteredValues = BuildEnteredValues();
            Result = BuildResult();
            Close();
        };

        return new Border
        {
            Padding = new Thickness(16),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                Children =
                {
                    new StackPanel
                    {
                        Spacing = 4,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = L("queryParameters.dialog.heading", "Provide parameter values"),
                                FontSize = 18,
                                FontWeight = FontWeight.SemiBold,
                                Foreground = new SolidColorBrush(Color.Parse(UiColorConstants.C_E8EAED)),
                            },
                            new TextBlock
                            {
                                Text = L("queryParameters.dialog.description", "These values will be used only for the current preview execution."),
                                TextWrapping = TextWrapping.Wrap,
                                Foreground = new SolidColorBrush(Color.Parse(UiColorConstants.C_9CA3AF)),
                            },
                        },
                    },
                    PlaceAtRow(new ScrollViewer
                    {
                        Margin = new Thickness(0, 12, 0, 12),
                        Content = rows,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    }, 1),
                    PlaceAtRow(new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            cancelButton,
                            runButton,
                        },
                    }, 2),
                },
            },
        };
    }

    private IReadOnlyList<QueryParameter> BuildResult()
    {
        return QueryParameterPromptModel.BuildResult(
            _placeholders,
            BuildRawValuesByPlaceholder(),
            cancelled: false)!;
    }

    private IReadOnlyDictionary<string, string> BuildEnteredValues()
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<QueryParameterPlaceholder, string> item in BuildRawValuesByPlaceholder())
            values[QueryParameterPlaceholderParser.GetStorageKey(item.Key)] = item.Value;

        return values;
    }

    private IReadOnlyDictionary<QueryParameterPlaceholder, string> BuildRawValuesByPlaceholder()
    {
        Dictionary<QueryParameterPlaceholder, string> values = [];

        foreach (QueryParameterPlaceholder placeholder in _placeholders)
        {
            values[placeholder] = _inputs.TryGetValue(placeholder, out ParameterInputAdapter? input)
                ? input.GetRawValue()
                : string.Empty;
        }

        return values;
    }

    private static Control PlaceAtRow(Control control, int row)
    {
        Grid.SetRow(control, row);
        return control;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        Close();
        e.Handled = true;
    }

    private static string L(string key, string fallback)
    {
        string value = LocalizationService.Instance[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    private static ParameterInputAdapter CreateInputAdapter(
        QueryParameterHint hint,
        string initialText,
        QueryParameterPromptInputKind inputKind)
    {
        ParameterInputAdapter inner = inputKind switch
        {
            QueryParameterPromptInputKind.Boolean => new BooleanInputAdapter(initialText),
            QueryParameterPromptInputKind.Integer => new NumericInputAdapter(initialText, allowDecimal: false, hint.ExampleValue),
            QueryParameterPromptInputKind.Decimal => new NumericInputAdapter(initialText, allowDecimal: true, hint.ExampleValue),
            QueryParameterPromptInputKind.DateTime => new DateTimeInputAdapter(initialText, hint.ExampleValue),
            _ => new TextInputAdapter(initialText, hint.ExampleValue),
        };

        return new NullableInputAdapter(inner, initialText);
    }

    private static Control? BuildExpressionKindBadge(QueryExecutionParameterContext? structuralContext)
    {
        if (structuralContext is null || string.IsNullOrWhiteSpace(structuralContext.ExpressionKind))
            return null;

        return structuralContext.ExpressionKind switch
        {
            "aggregate" => BuildBadge("Aggregate", UiColorConstants.C_30_FBBF24, UiColorConstants.C_FCD34D),
            "aggregate-string" => BuildBadge("String agg", UiColorConstants.C_30_FBBF24, UiColorConstants.C_FCD34D),
            "window" => BuildBadge("Window", UiColorConstants.C_60_3B82F6, UiColorConstants.C_60A5FA),
            "concat" => BuildBadge("Concat", UiColorConstants.C_60_14B8A6, UiColorConstants.C_14B8A6),
            "arithmetic" => BuildBadge("Arithmetic", UiColorConstants.C_60_14B8A6, UiColorConstants.C_14B8A6),
            "conditional" => BuildBadge("Fallback", UiColorConstants.C_60_14B8A6, UiColorConstants.C_14B8A6),
            "string-transform" => BuildBadge("Text", UiColorConstants.C_60_14B8A6, UiColorConstants.C_14B8A6),
            "date-transform" => BuildBadge("Date", UiColorConstants.C_60_14B8A6, UiColorConstants.C_14B8A6),
            "cast" => BuildBadge("Cast", UiColorConstants.C_20_6B7280, UiColorConstants.C_D1D5DB),
            "json" => BuildBadge("Json", UiColorConstants.C_55_6B8CFF, UiColorConstants.C_A78BFA),
            _ when structuralContext.SourceCount > 1 => BuildBadge("Composite", UiColorConstants.C_60_14B8A6, UiColorConstants.C_14B8A6),
            _ => null,
        };
    }

    private static Control? BuildExpressionDetailBadge(QueryExecutionParameterContext? structuralContext)
    {
        if (structuralContext is null || structuralContext.SourceCount <= 1)
            return null;

        string label = structuralContext.ExpressionKind switch
        {
            "window" => "Partitioned",
            "aggregate-string" => "Ordered",
            "aggregate" => structuralContext.SourceCount == 2 ? "2 inputs" : $"{structuralContext.SourceCount} inputs",
            _ => structuralContext.SourceCount == 2 ? "2 sources" : $"{structuralContext.SourceCount} sources",
        };
        return BuildBadge(label, UiColorConstants.C_20_6B7280, UiColorConstants.C_D1D5DB);
    }

    private static Border BuildBadge(string label, string backgroundColor, string foregroundColor)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse(backgroundColor)),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = label,
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse(foregroundColor)),
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    private abstract class ParameterInputAdapter
    {
        public abstract Control Control { get; }
        public abstract string GetRawValue();
    }

    private sealed class NullableInputAdapter : ParameterInputAdapter
    {
        private readonly ParameterInputAdapter _inner;
        private readonly CheckBox _nullCheckBox;
        private readonly StackPanel _panel;

        public NullableInputAdapter(ParameterInputAdapter inner, string initialText)
        {
            _inner = inner;
            _nullCheckBox = new CheckBox
            {
                Content = L("queryParameters.dialog.useNull", "Use NULL"),
                IsChecked = string.Equals(initialText, "NULL", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(initialText, "null", StringComparison.OrdinalIgnoreCase),
                Foreground = new SolidColorBrush(Color.Parse(UiColorConstants.C_D1D5DB)),
            };
            _panel = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    _inner.Control,
                    _nullCheckBox,
                },
            };
        }

        public override Control Control => _panel;

        public override string GetRawValue() => _nullCheckBox.IsChecked == true
            ? "NULL"
            : _inner.GetRawValue();
    }

    private sealed class TextInputAdapter : ParameterInputAdapter
    {
        private readonly TextBox _textBox;

        public TextInputAdapter(string initialText, string watermark)
        {
            _textBox = new TextBox
            {
                Text = initialText,
                Watermark = watermark,
            };
        }

        public override Control Control => _textBox;

        public override string GetRawValue() => _textBox.Text?.Trim() ?? string.Empty;
    }

    private sealed class NumericInputAdapter : ParameterInputAdapter
    {
        private readonly TextBox _textBox;
        private readonly bool _allowDecimal;

        public NumericInputAdapter(string initialText, bool allowDecimal, string watermark)
        {
            _allowDecimal = allowDecimal;
            _textBox = new TextBox
            {
                Text = initialText,
                Watermark = watermark,
            };
            _textBox.AddHandler(InputElement.TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
        }

        public override Control Control => _textBox;

        public override string GetRawValue() => _textBox.Text?.Trim() ?? string.Empty;

        private void OnTextInput(object? sender, TextInputEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text))
                return;

            foreach (char ch in e.Text)
            {
                bool valid = char.IsDigit(ch)
                    || ch == '-'
                    || (_allowDecimal && (ch == '.' || ch == ','));
                if (valid)
                    continue;

                e.Handled = true;
                return;
            }
        }
    }

    private sealed class BooleanInputAdapter : ParameterInputAdapter
    {
        private readonly ComboBox _comboBox;

        public BooleanInputAdapter(string initialText)
        {
            _comboBox = new ComboBox
            {
                ItemsSource = new[] { "true", "false" },
                SelectedItem = string.Equals(initialText, "false", StringComparison.OrdinalIgnoreCase)
                    ? "false"
                    : "true",
            };
        }

        public override Control Control => _comboBox;

        public override string GetRawValue() => _comboBox.SelectedItem?.ToString()?.Trim() ?? "true";
    }

    private sealed class DateTimeInputAdapter : ParameterInputAdapter
    {
        private readonly DatePicker _datePicker;
        private readonly TextBox _timeBox;
        private readonly StackPanel _panel;

        public DateTimeInputAdapter(string initialText, string watermark)
        {
            DateTimeOffset? parsed = TryParseDate(initialText) ?? TryParseDate(watermark);
            _datePicker = new DatePicker
            {
                SelectedDate = parsed,
            };
            _timeBox = new TextBox
            {
                Text = parsed.HasValue && parsed.Value.TimeOfDay != TimeSpan.Zero
                    ? parsed.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                    : string.Empty,
                Watermark = "HH:mm:ss",
                Width = 120,
            };
            _panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    _datePicker,
                    _timeBox,
                },
            };
        }

        public override Control Control => _panel;

        public override string GetRawValue()
        {
            if (_datePicker.SelectedDate is null)
                return _timeBox.Text?.Trim() ?? string.Empty;

            DateTimeOffset selectedDate = _datePicker.SelectedDate.Value;
            string timeRaw = _timeBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(timeRaw))
                return selectedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            if (TimeSpan.TryParse(timeRaw, CultureInfo.InvariantCulture, out TimeSpan time))
            {
                DateTimeOffset combined = new(
                    selectedDate.Year,
                    selectedDate.Month,
                    selectedDate.Day,
                    time.Hours,
                    time.Minutes,
                    time.Seconds,
                    selectedDate.Offset);
                return combined.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            }

            return $"{selectedDate:yyyy-MM-dd}T{timeRaw}";
        }

        private static DateTimeOffset? TryParseDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset dto))
                return dto;

            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime dateTime))
                return new DateTimeOffset(dateTime);

            return null;
        }
    }
}
