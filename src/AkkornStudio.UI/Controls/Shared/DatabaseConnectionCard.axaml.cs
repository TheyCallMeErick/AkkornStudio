using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Material.Icons;
using Material.Icons.Avalonia;
using AkkornStudio.UI.Services.ConnectionManager.Models;
using AkkornStudio.UI.Services.Search;
using AkkornStudio.UI.Services.Theming;

namespace AkkornStudio.UI.Controls.Shared;

public partial class DatabaseConnectionCard : UserControl
{
    private readonly DispatcherTimer _connectedPulseTimer;
    private readonly ScaleTransform _connectedPulseTransform = new(1, 1);
    private Ellipse? _connectedStatusDot;
    private double _pulsePhase;

    public static readonly StyledProperty<string?> ConnectionNameProperty =
        AvaloniaProperty.Register<DatabaseConnectionCard, string?>(nameof(ConnectionName));

    public static readonly StyledProperty<string?> ConnectionSubtitleProperty =
        AvaloniaProperty.Register<DatabaseConnectionCard, string?>(nameof(ConnectionSubtitle));

    public static readonly StyledProperty<ConnectionProfile?> SelectedConnectionProperty =
        AvaloniaProperty.Register<DatabaseConnectionCard, ConnectionProfile?>(nameof(SelectedConnection));

    public static readonly StyledProperty<string?> SelectedSchemaProperty =
        AvaloniaProperty.Register<DatabaseConnectionCard, string?>(nameof(SelectedSchema));

    public static readonly StyledProperty<string?> SelectedDatabaseProperty =
        AvaloniaProperty.Register<DatabaseConnectionCard, string?>(nameof(SelectedDatabase));

    public static readonly StyledProperty<IEnumerable?> AvailableConnectionsProperty =
        AvaloniaProperty.Register<DatabaseConnectionCard, IEnumerable?>(nameof(AvailableConnections));

    public static readonly StyledProperty<IEnumerable?> AvailableSchemasProperty =
        AvaloniaProperty.Register<DatabaseConnectionCard, IEnumerable?>(nameof(AvailableSchemas));

    public static readonly StyledProperty<IEnumerable?> AvailableDatabasesProperty =
        AvaloniaProperty.Register<DatabaseConnectionCard, IEnumerable?>(nameof(AvailableDatabases));

    public static readonly StyledProperty<IEnumerable?> AvailableDatabaseOptionsProperty =
        AvaloniaProperty.Register<DatabaseConnectionCard, IEnumerable?>(nameof(AvailableDatabaseOptions));

    public static readonly StyledProperty<string?> MetadataSummaryProperty =
        AvaloniaProperty.Register<DatabaseConnectionCard, string?>(nameof(MetadataSummary));

    public static readonly StyledProperty<string?> MetadataDetailsProperty =
        AvaloniaProperty.Register<DatabaseConnectionCard, string?>(nameof(MetadataDetails));

    public static readonly StyledProperty<int?> LatencyMsProperty =
        AvaloniaProperty.Register<DatabaseConnectionCard, int?>(nameof(LatencyMs));

    public static readonly StyledProperty<bool> IsConnectedProperty =
        AvaloniaProperty.Register<DatabaseConnectionCard, bool>(nameof(IsConnected));

    public static readonly StyledProperty<bool> IsReloadingProperty =
        AvaloniaProperty.Register<DatabaseConnectionCard, bool>(nameof(IsReloading));

    public static readonly StyledProperty<ICommand?> DisconnectCommandProperty =
        AvaloniaProperty.Register<DatabaseConnectionCard, ICommand?>(nameof(DisconnectCommand));

    public static readonly StyledProperty<ICommand?> SwitchConnectionCommandProperty =
        AvaloniaProperty.Register<DatabaseConnectionCard, ICommand?>(nameof(SwitchConnectionCommand));

    public static readonly StyledProperty<ICommand?> SwitchSchemaCommandProperty =
        AvaloniaProperty.Register<DatabaseConnectionCard, ICommand?>(nameof(SwitchSchemaCommand));

    public static readonly StyledProperty<ICommand?> SwitchDatabaseCommandProperty =
        AvaloniaProperty.Register<DatabaseConnectionCard, ICommand?>(nameof(SwitchDatabaseCommand));

    public static readonly StyledProperty<ICommand?> OpenConnectionManagerCommandProperty =
        AvaloniaProperty.Register<DatabaseConnectionCard, ICommand?>(nameof(OpenConnectionManagerCommand));

    public static readonly StyledProperty<bool> IsDatabaseSelectionVisibleProperty =
        AvaloniaProperty.Register<DatabaseConnectionCard, bool>(nameof(IsDatabaseSelectionVisible));

    public static readonly StyledProperty<bool> ShowConnectionManagerButtonProperty =
        AvaloniaProperty.Register<DatabaseConnectionCard, bool>(nameof(ShowConnectionManagerButton), true);

    public string? ConnectionName
    {
        get => GetValue(ConnectionNameProperty);
        set => SetValue(ConnectionNameProperty, value);
    }

    public string? ConnectionSubtitle
    {
        get => GetValue(ConnectionSubtitleProperty);
        set => SetValue(ConnectionSubtitleProperty, value);
    }

    public ConnectionProfile? SelectedConnection
    {
        get => GetValue(SelectedConnectionProperty);
        set => SetValue(SelectedConnectionProperty, value);
    }

    public string? SelectedSchema
    {
        get => GetValue(SelectedSchemaProperty);
        set => SetValue(SelectedSchemaProperty, value);
    }

    public string? SelectedDatabase
    {
        get => GetValue(SelectedDatabaseProperty);
        set => SetValue(SelectedDatabaseProperty, value);
    }

    public IEnumerable? AvailableConnections
    {
        get => GetValue(AvailableConnectionsProperty);
        set => SetValue(AvailableConnectionsProperty, value);
    }

    public IEnumerable? AvailableSchemas
    {
        get => GetValue(AvailableSchemasProperty);
        set => SetValue(AvailableSchemasProperty, value);
    }

    public IEnumerable? AvailableDatabases
    {
        get => GetValue(AvailableDatabasesProperty);
        set => SetValue(AvailableDatabasesProperty, value);
    }

    public IEnumerable? AvailableDatabaseOptions
    {
        get => GetValue(AvailableDatabaseOptionsProperty);
        set => SetValue(AvailableDatabaseOptionsProperty, value);
    }

    public string? MetadataSummary
    {
        get => GetValue(MetadataSummaryProperty);
        set => SetValue(MetadataSummaryProperty, value);
    }

    public string? MetadataDetails
    {
        get => GetValue(MetadataDetailsProperty);
        set => SetValue(MetadataDetailsProperty, value);
    }

    public int? LatencyMs
    {
        get => GetValue(LatencyMsProperty);
        set => SetValue(LatencyMsProperty, value);
    }

    public bool IsConnected
    {
        get => GetValue(IsConnectedProperty);
        set => SetValue(IsConnectedProperty, value);
    }

    public bool IsReloading
    {
        get => GetValue(IsReloadingProperty);
        set => SetValue(IsReloadingProperty, value);
    }

    public ICommand? DisconnectCommand
    {
        get => GetValue(DisconnectCommandProperty);
        set => SetValue(DisconnectCommandProperty, value);
    }

    public ICommand? SwitchConnectionCommand
    {
        get => GetValue(SwitchConnectionCommandProperty);
        set => SetValue(SwitchConnectionCommandProperty, value);
    }

    public ICommand? SwitchSchemaCommand
    {
        get => GetValue(SwitchSchemaCommandProperty);
        set => SetValue(SwitchSchemaCommandProperty, value);
    }

    public ICommand? SwitchDatabaseCommand
    {
        get => GetValue(SwitchDatabaseCommandProperty);
        set => SetValue(SwitchDatabaseCommandProperty, value);
    }

    public ICommand? OpenConnectionManagerCommand
    {
        get => GetValue(OpenConnectionManagerCommandProperty);
        set => SetValue(OpenConnectionManagerCommandProperty, value);
    }

    public bool IsDatabaseSelectionVisible
    {
        get => GetValue(IsDatabaseSelectionVisibleProperty);
        set => SetValue(IsDatabaseSelectionVisibleProperty, value);
    }

    public bool ShowConnectionManagerButton
    {
        get => GetValue(ShowConnectionManagerButtonProperty);
        set => SetValue(ShowConnectionManagerButtonProperty, value);
    }

    public DatabaseConnectionCard()
    {
        InitializeComponent();
        _connectedPulseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(110),
        };
        _connectedPulseTimer.Tick += OnConnectedPulseTick;
        EnsureConnectedStatusDot();
        UpdateConnectedPulseState();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsConnectedProperty)
            UpdateConnectedPulseState();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        EnsureConnectedStatusDot();
        UpdateConnectedPulseState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopConnectedPulse();
        base.OnDetachedFromVisualTree(e);
    }

    private async void OnOpenDatabaseSwitcherClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        if (IsReloading)
            return;

        IReadOnlyList<DatabaseOptionEntry> options = BuildDatabaseOptions();
        if (options.Count == 0)
            return;

        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;

        var dialog = new DatabaseSwitchDialogWindow(options, SelectedDatabase);
        string? selected = await dialog.ShowDialog<string?>(owner);
        if (string.IsNullOrWhiteSpace(selected))
            return;

        if (string.Equals(selected, SelectedDatabase, StringComparison.OrdinalIgnoreCase))
            return;

        if (SwitchDatabaseCommand?.CanExecute(selected) == true)
            SwitchDatabaseCommand.Execute(selected);
    }

    private void OnSchemaSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: string selectedSchema })
            return;

        if (string.IsNullOrWhiteSpace(selectedSchema))
            return;

        if (string.Equals(selectedSchema, SelectedSchema, StringComparison.OrdinalIgnoreCase))
            return;

        if (SwitchSchemaCommand?.CanExecute(selectedSchema) == true)
            SwitchSchemaCommand.Execute(selectedSchema);
    }

    private IReadOnlyList<DatabaseOptionEntry> BuildDatabaseOptions()
    {
        if (AvailableDatabaseOptions is not null)
        {
            List<DatabaseOptionEntry> mapped = [];
            foreach (object? raw in AvailableDatabaseOptions)
            {
                if (raw is DatabaseCatalogItem item && !string.IsNullOrWhiteSpace(item.Name))
                    mapped.Add(new DatabaseOptionEntry(item.Name, item.HasReadPermission, item.PermissionLabel));
            }

            if (mapped.Count > 0)
            {
                return mapped
                    .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }

        if (AvailableDatabases is null)
            return [];

        return AvailableDatabases
            .Cast<object?>()
            .Select(value => value?.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => new DatabaseOptionEntry(name!, null, "Permissao nao verificada"))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed record DatabaseOptionEntry(string Name, bool? HasReadPermission, string PermissionText);

    private void EnsureConnectedStatusDot()
    {
        if (_connectedStatusDot is not null)
            return;

        _connectedStatusDot = this.FindControl<Ellipse>("ConnectedStatusDot");
        if (_connectedStatusDot is not null)
            _connectedStatusDot.RenderTransform = _connectedPulseTransform;
    }

    private void UpdateConnectedPulseState()
    {
        EnsureConnectedStatusDot();
        if (_connectedStatusDot is null)
            return;

        if (IsConnected)
        {
            if (!_connectedPulseTimer.IsEnabled)
            {
                _pulsePhase = 0;
                _connectedPulseTimer.Start();
            }

            return;
        }

        StopConnectedPulse();
    }

    private void StopConnectedPulse()
    {
        if (_connectedPulseTimer.IsEnabled)
            _connectedPulseTimer.Stop();

        UpdateConnectedPulseVisual(opacity: 1d, scale: 1d);
    }

    private void OnConnectedPulseTick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;

        _pulsePhase += 0.24d;
        if (_pulsePhase > Math.PI * 2d)
            _pulsePhase -= Math.PI * 2d;

        double wave = (Math.Sin(_pulsePhase) + 1d) * 0.5d;
        double opacity = 0.62d + (wave * 0.38d);
        double scale = 0.92d + (wave * 0.14d);
        UpdateConnectedPulseVisual(opacity, scale);
    }

    private void UpdateConnectedPulseVisual(double opacity, double scale)
    {
        if (_connectedStatusDot is null)
            return;

        _connectedStatusDot.Opacity = opacity;
        _connectedPulseTransform.ScaleX = scale;
        _connectedPulseTransform.ScaleY = scale;
    }

    private sealed class DatabaseSwitchDialogWindow : Window
    {
        private readonly IReadOnlyList<DatabaseOptionEntry> _allOptions;
        private readonly ObservableCollection<DatabaseOptionEntry> _filteredOptions = [];
        private readonly TextSearchService _textSearch = new();
        private readonly ListBox _listBox;
        private readonly TextBox _searchBox;
        private readonly TextBlock _emptyStateText;
        private readonly Button _applyButton;
        private readonly string? _currentDatabase;
        private string? _selectedDatabaseName;

        public DatabaseSwitchDialogWindow(IReadOnlyList<DatabaseOptionEntry> options, string? currentDatabase)
        {
            _allOptions = options
                .GroupBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _currentDatabase = currentDatabase;
            _selectedDatabaseName = currentDatabase;

            Title = "Trocar banco";
            Width = 560;
            MinWidth = 520;
            Height = 520;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            CanResize = false;
            SystemDecorations = SystemDecorations.None;
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
            ExtendClientAreaTitleBarHeightHint = -1;
            Background = new SolidColorBrush(Color.Parse(UiColorConstants.C_0D0F14));
            Foreground = new SolidColorBrush(Color.Parse(UiColorConstants.C_E8EAED));
            KeyDown += (_, e) =>
            {
                if (e.Key == Avalonia.Input.Key.Escape)
                    Close(null);
            };

            _searchBox = new TextBox
            {
                Classes = { "field" },
                Watermark = "Buscar banco/catalogo...",
                FontSize = 12,
            };
            _searchBox.TextChanged += (_, _) => ApplyFilter();

            _listBox = new ListBox
            {
                ItemsSource = _filteredOptions,
            };

            _listBox.ItemTemplate = new FuncDataTemplate<DatabaseOptionEntry>((item, _) =>
            {
                var permissionColor = item.HasReadPermission switch
                {
                    true => "#5CE59D",
                    false => "#F87171",
                    _ => "#A3AAB8",
                };

                var icon = new MaterialIcon
                {
                    Kind = MaterialIconKind.Database,
                    Width = 16,
                    Height = 16,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.Parse("#80A6FF")),
                };

                var mainText = new TextBlock
                {
                    Text = item.Name,
                    FontWeight = FontWeight.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };

                var permissionText = new TextBlock
                {
                    Text = item.PermissionText,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.Parse(permissionColor)),
                };

                var textStack = new StackPanel
                {
                    Margin = new Thickness(8, 0, 0, 0),
                    Spacing = 2,
                    Children = { mainText, permissionText },
                };
                Grid.SetColumn(textStack, 1);

                var tagText = new TextBlock
                {
                    Text = item.HasReadPermission switch
                    {
                        true => "Leitura",
                        false => "Sem leitura",
                        _ => "Nao verificado",
                    },
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.Parse(permissionColor)),
                };

                var tag = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 3),
                    Background = new SolidColorBrush(Color.Parse("#1AFFFFFF")),
                    Child = tagText,
                };
                Grid.SetColumn(tag, 2);

                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                    Children = { icon, textStack, tag },
                };

                return new Border
                {
                    Padding = new Thickness(10, 8),
                    Margin = new Thickness(0, 0, 0, 6),
                    CornerRadius = new CornerRadius(8),
                    BorderBrush = new SolidColorBrush(Color.Parse("#2A3040")),
                    BorderThickness = new Thickness(1),
                    Child = row,
                };
            });

            _applyButton = new Button
            {
                Content = "Trocar banco",
                IsDefault = true,
                Classes = { "primary" },
                IsEnabled = false,
            };
            _applyButton.Click += (_, _) =>
            {
                string? selectedName = (_listBox.SelectedItem as DatabaseOptionEntry)?.Name;
                Close(selectedName);
            };
            _listBox.SelectionChanged += (_, _) =>
            {
                _applyButton.IsEnabled = _listBox.SelectedItem is DatabaseOptionEntry;
                _selectedDatabaseName = (_listBox.SelectedItem as DatabaseOptionEntry)?.Name;
            };

            var cancelButton = new Button
            {
                Content = "Cancelar",
                IsCancel = true,
                Classes = { "secondary" },
            };
            cancelButton.Click += (_, _) => Close(null);

            var headerCloseButton = new Button
            {
                Classes = { "icon-ghost" },
                Content = new MaterialIcon
                {
                    Kind = MaterialIconKind.Close,
                    Width = 14,
                    Height = 14,
                },
            };
            headerCloseButton.Click += (_, _) => Close(null);

            _emptyStateText = new TextBlock
            {
                Text = "Nenhum banco encontrado para essa busca.",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.Parse("#A3AAB8")),
                IsVisible = false,
            };
            Grid.SetRow(_emptyStateText, 1);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                Children = { cancelButton, _applyButton },
            };

            var searchPanel = new Border
            {
                Classes = { "surface-card" },
                Padding = new Thickness(10),
                Child = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                    ColumnSpacing = 8,
                    Children =
                    {
                        new MaterialIcon
                        {
                            Kind = MaterialIconKind.Magnify,
                            Width = 15,
                            Height = 15,
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = new SolidColorBrush(Color.Parse("#A3AAB8")),
                        },
                        WithColumn(_searchBox, 1),
                    },
                },
            };

            var listHost = new Grid
            {
                Children =
                {
                    new ScrollViewer
                    {
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        Content = _listBox,
                    },
                    _emptyStateText,
                },
            };

            Content = new Border
            {
                Background = new SolidColorBrush(Color.Parse(UiColorConstants.C_070A12)),
                BorderBrush = new SolidColorBrush(Color.Parse(UiColorConstants.C_1E2A3F)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
                    Children =
                    {
                        new Border
                        {
                            Background = new SolidColorBrush(Color.Parse(UiColorConstants.C_0F1220)),
                            BorderBrush = new SolidColorBrush(Color.Parse(UiColorConstants.C_1E2A3F)),
                            BorderThickness = new Thickness(0, 0, 0, 1),
                            Padding = new Thickness(16, 12),
                            Child = new Grid
                            {
                                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                                Children =
                                {
                                    new MaterialIcon
                                    {
                                        Kind = MaterialIconKind.DatabaseSearch,
                                        Width = 16,
                                        Height = 16,
                                        VerticalAlignment = VerticalAlignment.Center,
                                        Foreground = new SolidColorBrush(Color.Parse(UiColorConstants.C_60A5FA)),
                                    },
                                    WithColumn(new StackPanel
                                    {
                                        Margin = new Thickness(10, 0, 0, 0),
                                        Spacing = 2,
                                        Children =
                                        {
                                            new TextBlock
                                            {
                                                Text = "Selecionar banco (BD)",
                                                FontSize = 14,
                                                FontWeight = FontWeight.SemiBold,
                                            },
                                            new TextBlock
                                            {
                                                Text = "Escolha o catalogo e veja a permissao de leitura.",
                                                FontSize = 11,
                                                Foreground = new SolidColorBrush(Color.Parse("#A3AAB8")),
                                            },
                                        },
                                    }, 1),
                                    WithColumn(headerCloseButton, 2),
                                },
                            },
                        },
                        WithRow(new Border
                        {
                            Padding = new Thickness(16, 12, 16, 8),
                            Child = searchPanel,
                        }, 1),
                        WithRow(new Border
                        {
                            Padding = new Thickness(16, 0, 16, 12),
                            Child = listHost,
                        }, 2),
                        WithRow(new Border
                        {
                            Background = new SolidColorBrush(Color.Parse(UiColorConstants.C_0F1220)),
                            BorderBrush = new SolidColorBrush(Color.Parse(UiColorConstants.C_1E2A3F)),
                            BorderThickness = new Thickness(0, 1, 0, 0),
                            Padding = new Thickness(16, 10),
                            Child = actions,
                        }, 3),
                    },
                },
            };

            ApplyFilter();
        }

        private static Control WithRow(Control control, int row)
        {
            Grid.SetRow(control, row);
            return control;
        }

        private static Control WithColumn(Control control, int column)
        {
            Grid.SetColumn(control, column);
            return control;
        }

        private void ApplyFilter()
        {
            string query = _searchBox.Text?.Trim() ?? string.Empty;
            IEnumerable<DatabaseOptionEntry> filtered = _allOptions;
            if (!string.IsNullOrWhiteSpace(query))
            {
                filtered = _allOptions
                    .Select(option => new
                    {
                        Option = option,
                        Score = _textSearch.Score(query, option.Name, option.PermissionText),
                    })
                    .Where(x => x.Score > 0)
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.Option.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.Option);
            }

            _filteredOptions.Clear();
            foreach (DatabaseOptionEntry option in filtered)
                _filteredOptions.Add(option);

            _emptyStateText.IsVisible = _filteredOptions.Count == 0;

            if (_filteredOptions.Count == 0)
            {
                _listBox.SelectedItem = null;
                _applyButton.IsEnabled = false;
                return;
            }

            DatabaseOptionEntry? preferred =
                (!string.IsNullOrWhiteSpace(_selectedDatabaseName)
                    ? _filteredOptions.FirstOrDefault(item =>
                        string.Equals(item.Name, _selectedDatabaseName, StringComparison.OrdinalIgnoreCase))
                    : null)
                ?? _filteredOptions.FirstOrDefault(item =>
                    string.Equals(item.Name, _currentDatabase, StringComparison.OrdinalIgnoreCase))
                ?? _filteredOptions[0];

            _listBox.SelectedItem = preferred;
            _applyButton.IsEnabled = true;
        }
    }
}
