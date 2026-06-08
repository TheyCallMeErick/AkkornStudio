using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.ViewModels;
using Material.Icons;
using Material.Icons.Avalonia;
using AkkornStudio.UI.Services.Theming;

namespace AkkornStudio.UI.Controls.SqlEditor;

public sealed class SqlEditorReportExportDialogWindow : Window
{
    private readonly SqlEditorReportExportDialogViewModel _vm;
    private readonly Button _confirmButton;
    private readonly TextBlock _typeDescription;
    private readonly TextBlock _typeBadge;

    public SqlEditorReportExportDialogWindow(SqlEditorReportExportDialogViewModel vm)
    {
        _vm = vm;

        Title = L("sqlEditor.export.dialog.windowTitle", "Exportar Dados SQL");
        Width = 700;
        Height = 650;
        MinWidth = 620;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SystemDecorations = SystemDecorations.None;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
        ExtendClientAreaTitleBarHeightHint = -1;
        Background = ResolveBrush("Bg0Brush", UiColorConstants.C_070A12);

        _confirmButton = new Button
        {
            Content = L("sqlEditor.export.dialog.confirm", "Exportar"),
            Padding = new Thickness(14, 8),
            MinWidth = 120,
            IsEnabled = _vm.CanConfirm,
            Background = ResolveBrush("BtnSuccessBgBrush", UiColorConstants.C_10291A),
            BorderBrush = ResolveBrush("StatusOkBrush", UiColorConstants.C_34D399),
            Foreground = ResolveBrush("StatusOkBrush", UiColorConstants.C_34D399),
            BorderThickness = new Thickness(1),
            CornerRadius = ResolveCornerRadius("RadiusSM", 6),
        };
        _confirmButton.Click += OnConfirmClicked;

        _typeDescription = new TextBlock
        {
            Foreground = ResolveBrush("TextMutedBrush", UiColorConstants.C_8B95A8),
            FontSize = ResolveFontSize("FontSizeCaption", 11),
            TextWrapping = TextWrapping.Wrap,
        };

        _typeBadge = new TextBlock
        {
            FontSize = ResolveFontSize("FontSizeMonoSmall", 11),
            FontFamily = ResolveFontFamily("MonoFont", "JetBrains Mono,IBM Plex Mono,Cascadia Code,Consolas,monospace"),
            Foreground = ResolveBrush("StatusOkBrush", UiColorConstants.C_34D399),
        };

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SqlEditorReportExportDialogViewModel.CanConfirm))
                _confirmButton.IsEnabled = _vm.CanConfirm;

            if (e.PropertyName == nameof(SqlEditorReportExportDialogViewModel.SelectedType))
                RefreshTypeDetails();
        };

        Content = BuildContent();
        RefreshTypeDetails();
    }

    public bool WasConfirmed { get; private set; }

    private Control BuildContent()
    {
        ComboBox MakeComboBox<T>(IEnumerable<T> items, T selectedItem, Action<object?> onChanged, Func<T, string>? display = null)
        {
            var combo = new ComboBox
            {
                ItemsSource = items.ToList(),
                SelectedItem = selectedItem,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth = 240,
            };

            if (display is not null)
                combo.ItemTemplate = new FuncDataTemplate<T>((item, _) => new TextBlock { Text = display(item) });

            combo.SelectionChanged += (_, _) => onChanged(combo.SelectedItem);
            return combo;
        }

        var typeCombo = MakeComboBox(
            _vm.ReportTypes,
            _vm.SelectedType!,
            item => _vm.SelectedType = item as SqlEditorReportTypeOption,
            item => item.Label);

        var fileNameBox = new TextBox
        {
            Text = _vm.FileName,
            Watermark = L("sqlEditor.export.dialog.fileNameWatermark", "relatorio.html"),
        };
        fileNameBox.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == nameof(TextBox.Text))
                _vm.FileName = fileNameBox.Text ?? string.Empty;
        };

        var titleBox = new TextBox
        {
            Text = _vm.Title,
            Watermark = L("sqlEditor.export.dialog.titleWatermark", "Relatorio SQL"),
        };
        titleBox.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == nameof(TextBox.Text))
                _vm.Title = titleBox.Text ?? string.Empty;
        };

        string selectedTitleTransform = "title";
        string selectedFileNameTransform = "slug";

        void SyncTitleBox() => titleBox.Text = _vm.Title;
        void SyncFileNameBox() => fileNameBox.Text = _vm.FileName;

        var descriptionBox = new TextBox
        {
            Text = _vm.Description,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 84,
            Watermark = L("sqlEditor.export.dialog.descriptionWatermark", "Contexto adicional para auditoria e compartilhamento."),
        };
        descriptionBox.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == nameof(TextBox.Text))
                _vm.Description = descriptionBox.Text ?? string.Empty;
        };

        var textTransformItems = new[]
        {
            new KeyValuePair<string, string>(L("sqlEditor.export.transform.titleCase", "Title Case"), "title"),
            new KeyValuePair<string, string>(L("sqlEditor.export.transform.upper", "UPPERCASE"), "upper"),
            new KeyValuePair<string, string>(L("sqlEditor.export.transform.lower", "lowercase"), "lower"),
            new KeyValuePair<string, string>(L("sqlEditor.export.transform.sentence", "Sentence case"), "sentence"),
            new KeyValuePair<string, string>(L("sqlEditor.export.transform.slug", "slug-case"), "slug"),
        };

        var titleTransformCombo = MakeComboBox(
            textTransformItems,
            textTransformItems[0],
            item => { if (item is KeyValuePair<string, string> pair) selectedTitleTransform = pair.Value; },
            item => item.Key);

        var fileNameTransformCombo = MakeComboBox(
            textTransformItems,
            textTransformItems.Single(item => item.Value == "slug"),
            item => { if (item is KeyValuePair<string, string> pair) selectedFileNameTransform = pair.Value; },
            item => item.Key);

        var titleActionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                titleTransformCombo,
                new Button
                {
                    Content = L("sqlEditor.export.transform.applyToTitle", "Aplicar no titulo"),
                    Padding = new Thickness(12, 8),
                    MinWidth = 140,
                }
            }
        };
        ((Button)titleActionsPanel.Children[1]!).Click += (_, _) =>
        {
            _vm.ApplyTitleTransform(selectedTitleTransform);
            SyncTitleBox();
        };

        var fileNameActionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                fileNameTransformCombo,
                new Button
                {
                    Content = L("sqlEditor.export.transform.applyToFile", "Aplicar no arquivo"),
                    Padding = new Thickness(12, 8),
                    MinWidth = 140,
                },
                new Button
                {
                    Content = L("sqlEditor.export.transform.slugFromTitle", "Slug do titulo"),
                    Padding = new Thickness(12, 8),
                    MinWidth = 132,
                }
            }
        };
        ((Button)fileNameActionsPanel.Children[1]!).Click += (_, _) =>
        {
            _vm.ApplyFileNameTransform(selectedFileNameTransform);
            SyncFileNameBox();
        };
        ((Button)fileNameActionsPanel.Children[2]!).Click += (_, _) =>
        {
            _vm.SlugifyTitleIntoFileName();
            SyncFileNameBox();
        };

        var includeSchemaCheck = new CheckBox
        {
            Content = L("sqlEditor.export.option.includeSchema", "Incluir schema de saida"),
            IsChecked = _vm.IncludeSchema,
            IsVisible = _vm.ShowIncludeSchema,
        };
        includeSchemaCheck.IsCheckedChanged += (_, _) => _vm.IncludeSchema = includeSchemaCheck.IsChecked ?? false;

        var includeSqlCheck = new CheckBox
        {
            Content = L("sqlEditor.export.option.includeSql", "Incluir SQL no artefato"),
            IsChecked = _vm.IncludeSql,
            IsVisible = _vm.ShowSqlOptions,
        };
        includeSqlCheck.IsCheckedChanged += (_, _) => _vm.IncludeSql = includeSqlCheck.IsChecked ?? false;

        var includeLineageCheck = new CheckBox
        {
            Content = L("sqlEditor.export.option.includeLineage", "Incluir linhagem quando houver dados"),
            IsChecked = _vm.IncludeLineage,
            IsVisible = _vm.ShowLineageOptions,
        };
        includeLineageCheck.IsCheckedChanged += (_, _) => _vm.IncludeLineage = includeLineageCheck.IsChecked ?? false;

        var metadataItems = new[]
        {
            new KeyValuePair<string, SqlEditorReportMetadataLevel>(L("sqlEditor.export.metadata.none", "Nenhum"), SqlEditorReportMetadataLevel.None),
            new KeyValuePair<string, SqlEditorReportMetadataLevel>(L("sqlEditor.export.metadata.essential", "Essencial"), SqlEditorReportMetadataLevel.Essential),
            new KeyValuePair<string, SqlEditorReportMetadataLevel>(L("sqlEditor.export.metadata.complete", "Completo"), SqlEditorReportMetadataLevel.Complete),
        };
        var metadataCombo = MakeComboBox(
            metadataItems,
            metadataItems.Single(item => item.Value.Equals(_vm.MetadataLevel)),
            item => { if (item is KeyValuePair<string, SqlEditorReportMetadataLevel> pair) _vm.MetadataLevel = pair.Value; },
            item => item.Key);

        var emptyValueItems = new[]
        {
            new KeyValuePair<string, SqlEditorReportEmptyValueDisplayMode>(L("sqlEditor.export.empty.blank", "Em branco"), SqlEditorReportEmptyValueDisplayMode.Blank),
            new KeyValuePair<string, SqlEditorReportEmptyValueDisplayMode>(L("sqlEditor.export.empty.dash", "Traco"), SqlEditorReportEmptyValueDisplayMode.Dash),
            new KeyValuePair<string, SqlEditorReportEmptyValueDisplayMode>(L("sqlEditor.export.empty.null", "Literal null"), SqlEditorReportEmptyValueDisplayMode.NullLiteral),
        };
        var emptyValueCombo = MakeComboBox(
            emptyValueItems,
            emptyValueItems.Single(item => item.Value.Equals(_vm.EmptyValueDisplayMode)),
            item => { if (item is KeyValuePair<string, SqlEditorReportEmptyValueDisplayMode> pair) _vm.EmptyValueDisplayMode = pair.Value; },
            item => item.Key);

        var metadataPanel = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                metadataCombo,
                new TextBlock
                {
                    Text = L("sqlEditor.export.metadata.hint", "Escolha o nivel de contexto tecnico embutido."),
                    Foreground = ResolveBrush("TextMutedBrush", UiColorConstants.C_8B95A8),
                    FontSize = ResolveFontSize("FontSizeCaption", 11),
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        };
        metadataPanel.IsVisible = _vm.ShowMetadataOptions;

        var emptyValuePanel = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                emptyValueCombo,
                new TextBlock
                {
                    Text = L("sqlEditor.export.empty.hint", "Controla como campos vazios aparecem no HTML e no JSON."),
                    Foreground = ResolveBrush("TextMutedBrush", UiColorConstants.C_8B95A8),
                    FontSize = ResolveFontSize("FontSizeCaption", 11),
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        };
        emptyValuePanel.IsVisible = _vm.ShowOptions;

        var contentOptionsPanel = new StackPanel
        {
            Spacing = 8,
            Children = { includeSchemaCheck, includeSqlCheck, includeLineageCheck },
            IsVisible = _vm.ShowOptions,
        };
        var descriptionSection = section(L("sqlEditor.export.dialog.section.description", "DESCRICAO"), descriptionBox);
        descriptionSection.IsVisible = true;

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SqlEditorReportExportDialogViewModel.ShowOptions))
            {
                contentOptionsPanel.IsVisible = _vm.ShowOptions;
                emptyValuePanel.IsVisible = _vm.ShowOptions;
            }

            if (e.PropertyName == nameof(SqlEditorReportExportDialogViewModel.ShowIncludeSchema))
                includeSchemaCheck.IsVisible = _vm.ShowIncludeSchema;

            if (e.PropertyName == nameof(SqlEditorReportExportDialogViewModel.ShowSqlOptions))
                includeSqlCheck.IsVisible = _vm.ShowSqlOptions;

            if (e.PropertyName == nameof(SqlEditorReportExportDialogViewModel.ShowLineageOptions))
                includeLineageCheck.IsVisible = _vm.ShowLineageOptions;

            if (e.PropertyName == nameof(SqlEditorReportExportDialogViewModel.ShowMetadataOptions))
                metadataPanel.IsVisible = _vm.ShowMetadataOptions;
        };

        Button cancelButton = new()
        {
            Content = L("common.cancel", "Cancelar"),
            Padding = new Thickness(14, 8),
            MinWidth = 120,
            Background = ResolveBrush("Bg2Brush", UiColorConstants.C_12172A),
            BorderBrush = ResolveBrush("BorderBrush", UiColorConstants.C_20314A),
            Foreground = ResolveBrush("TextSecondaryBrush", UiColorConstants.C_B8C3D9),
            BorderThickness = new Thickness(1),
            CornerRadius = ResolveCornerRadius("RadiusSM", 6),
        };
        cancelButton.Click += (_, _) => Close();

        Button headerCloseButton = new()
        {
            Content = "×",
            Padding = new Thickness(8, 4),
            MinWidth = 32,
            Background = ResolveBrush("Bg2Brush", UiColorConstants.C_12172A),
            BorderBrush = ResolveBrush("BorderBrush", UiColorConstants.C_20314A),
            Foreground = ResolveBrush("TextSecondaryBrush", UiColorConstants.C_B8C3D9),
            BorderThickness = new Thickness(1),
            CornerRadius = ResolveCornerRadius("RadiusSM", 6),
        };
        headerCloseButton.Click += (_, _) => Close();

        Border section(string title, Control child)
        {
            return new Border
            {
                Background = ResolveBrush("CanvasBgBrush", UiColorConstants.C_0B1220),
                BorderBrush = ResolveBrush("BorderSoftBrush", UiColorConstants.C_1E2A3F),
                BorderThickness = new Thickness(1),
                CornerRadius = ResolveCornerRadius("RadiusSM", 6),
                Padding = new Thickness(12),
                Child = new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = title,
                            Foreground = ResolveBrush("TextMutedBrush", UiColorConstants.C_8B95A8),
                            FontWeight = ResolveFontWeight("FontWeightHeading", FontWeight.Bold),
                            FontSize = ResolveFontSize("FontSizeMonoSmall", 11),
                            LetterSpacing = 1.3,
                        },
                        child,
                    },
                },
            };
        }

        var root = new Grid
        {
            Margin = new Thickness(14),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
        };

        root.Children.Add(new Border
        {
            Background = ResolveBrush("Bg0Brush", UiColorConstants.C_0A111F),
            BorderBrush = ResolveBrush("BorderSoftBrush", UiColorConstants.C_1E2A3F),
            BorderThickness = new Thickness(1),
            CornerRadius = ResolveCornerRadius("RadiusMD", 10),
            Padding = new Thickness(14, 12),
            Margin = new Thickness(0, 0, 0, 10),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                Children =
                {
                    new Border
                    {
                        Width = 30,
                        Height = 30,
                        CornerRadius = ResolveCornerRadius("RadiusSM", 6),
                        Background = ResolveBrush("BtnSuccessBgBrush", UiColorConstants.C_10291A),
                        Child = new MaterialIcon
                        {
                            Kind = MaterialIconKind.FileExportOutline,
                            Foreground = ResolveBrush("StatusOkBrush", UiColorConstants.C_34D399),
                            Width = 16,
                            Height = 16,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                    },
                    PlaceAtCol(
                        new StackPanel
                        {
                            Margin = new Thickness(10, 0, 0, 0),
                            Spacing = 1,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = L("sqlEditor.export.dialog.title", "Exportar Dados SQL"),
                                    Foreground = ResolveBrush("TextPrimaryBrush", UiColorConstants.C_E6EDF8),
                                    FontWeight = ResolveFontWeight("FontWeightTitle", FontWeight.SemiBold),
                                    FontSize = ResolveFontSize("FontSizeNodeTitle", 14),
                                },
                                new TextBlock
                                {
                                    Text = L("sqlEditor.export.dialog.subtitle", "Escolha o formato e os metadados do artefato antes de exportar."),
                                    Foreground = ResolveBrush("TextMutedBrush", UiColorConstants.C_8B95A8),
                                    FontSize = ResolveFontSize("FontSizeCaption", 11),
                                },
                            },
                        },
                        1),
                    PlaceAtCol(headerCloseButton, 2),
                },
            },
        });

        var typeInfoPanel = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                typeCombo,
                _typeDescription,
                new Border
                {
                    Background = ResolveBrush("BtnSuccessBgBrush", UiColorConstants.C_10291A),
                    BorderBrush = ResolveBrush("StatusOkBrush", UiColorConstants.C_34D399),
                    BorderThickness = new Thickness(1),
                    CornerRadius = ResolveCornerRadius("RadiusXS", 4),
                    Padding = new Thickness(6, 2),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = _typeBadge,
                },
            },
        };

        var formPanel = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                section(L("sqlEditor.export.dialog.section.reportType", "TIPO DE RELATORIO"), typeInfoPanel),
                section(L("sqlEditor.export.dialog.section.fileName", "NOME DO ARQUIVO"), new StackPanel
                {
                    Spacing = 8,
                    Children = { fileNameBox, fileNameActionsPanel },
                }),
                section(L("sqlEditor.export.dialog.section.reportTitle", "TITULO"), new StackPanel
                {
                    Spacing = 8,
                    Children = { titleBox, titleActionsPanel },
                }),
                descriptionSection,
                section(L("sqlEditor.export.dialog.section.metadataLevel", "METADADOS"), metadataPanel),
                section(L("sqlEditor.export.dialog.section.emptyValueMode", "CAMPOS VAZIOS"), emptyValuePanel),
                section(L("sqlEditor.export.dialog.section.options", "OPCOES"), contentOptionsPanel),
            },
        };
        var formScroll = new ScrollViewer
        {
            Content = formPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetRow(formScroll, 1);
        root.Children.Add(formScroll);

        var footer = new Border
        {
            BorderBrush = ResolveBrush("BorderSubtleBrush", UiColorConstants.C_1E2A3F),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Background = ResolveBrush("Bg1Brush", UiColorConstants.C_0F1220),
            CornerRadius = ResolveCornerRadius("RadiusSM", 6),
            Padding = new Thickness(0, 10, 0, 0),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Children = { cancelButton, _confirmButton },
            },
        };
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        return root;
    }

    private void RefreshTypeDetails()
    {
        _typeDescription.Text = _vm.SelectedType?.Description ?? string.Empty;

        _typeBadge.Text = _vm.SelectedType?.Type switch
        {
            SqlEditorReportType.HtmlFullFeature => L("sqlEditor.export.badge.offline", "PRONTO PARA OFFLINE"),
            SqlEditorReportType.JsonContract => L("sqlEditor.export.badge.structured", "PAYLOAD ESTRUTURADO"),
            SqlEditorReportType.CsvData => L("sqlEditor.export.badge.dataOnly", "SOMENTE DADOS"),
            SqlEditorReportType.ExcelWorkbook => L("sqlEditor.export.badge.dataOnly", "SOMENTE DADOS"),
            _ => L("sqlEditor.export.badge.offline", "PRONTO PARA OFFLINE"),
        };
    }

    private void OnConfirmClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_vm.CanConfirm)
            return;

        WasConfirmed = true;
        Close();
    }

    private static Control PlaceAtCol(Control control, int col)
    {
        Grid.SetColumn(control, col);
        return control;
    }

    private static string L(string key, string fallback)
    {
        string value = LocalizationService.Instance[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    private static IBrush ResolveBrush(string key, string fallbackHex)
    {
        if (Application.Current?.TryFindResource(key, out object? resource) == true && resource is IBrush brush)
            return brush;

        return new SolidColorBrush(Color.Parse(fallbackHex));
    }

    private static CornerRadius ResolveCornerRadius(string key, double fallbackValue)
    {
        if (Application.Current?.Resources.TryGetResource(key, null, out object? resource) == true && resource is CornerRadius radius)
            return radius;

        return new CornerRadius(fallbackValue);
    }

    private static FontFamily ResolveFontFamily(string key, string fallbackFamily)
    {
        if (Application.Current?.Resources.TryGetResource(key, null, out object? resource) == true && resource is FontFamily font)
            return font;

        return new FontFamily(fallbackFamily);
    }

    private static double ResolveFontSize(string key, double fallback)
    {
        if (Application.Current?.Resources.TryGetResource(key, null, out object? resource) == true)
        {
            if (resource is double size)
                return size;
            if (resource is int intSize)
                return intSize;
        }

        return fallback;
    }

    private static FontWeight ResolveFontWeight(string key, FontWeight fallback)
    {
        if (Application.Current?.Resources.TryGetResource(key, null, out object? resource) == true
            && resource is FontWeight fontWeight)
        {
            return fontWeight;
        }

        return fallback;
    }
}
