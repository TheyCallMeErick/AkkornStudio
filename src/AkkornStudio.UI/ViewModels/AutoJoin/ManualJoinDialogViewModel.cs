using System.Collections.ObjectModel;
using AkkornStudio.Metadata;
using AkkornStudio.Nodes;
using AkkornStudio.Nodes.Pins;
using AkkornStudio.UI.Services.Localization;
using AkkornStudio.UI.Services.Theming;

namespace AkkornStudio.UI.ViewModels;

public sealed record ManualJoinRequest(
    NodeViewModel LeftTable,
    NodeViewModel RightTable,
    string JoinType,
    string LeftColumn,
    string RightColumn
);

public sealed class ManualJoinColumnOption
{
    public ManualJoinColumnOption(string name, PinDataType dataType, PinDataType compatibilityType)
    {
        Name = name;
        DataType = dataType;
        CompatibilityType = compatibilityType;
    }

    public string Name { get; }
    public PinDataType DataType { get; }
    public PinDataType CompatibilityType { get; }
    public string DataTypeLabel => CompatibilityType.ToString().ToUpperInvariant();
    public string PinColorHex => CompatibilityType switch
    {
        PinDataType.Text => UiColorConstants.C_22D3EE,
        PinDataType.Integer or PinDataType.Decimal or PinDataType.Number => UiColorConstants.C_60A5FA,
        PinDataType.Boolean => UiColorConstants.C_34D399,
        PinDataType.DateTime => UiColorConstants.C_C084FC,
        PinDataType.Json => UiColorConstants.C_F59E0B,
        _ => UiColorConstants.C_9CA3AF,
    };
}

public sealed class ManualJoinDialogViewModel : ViewModelBase
{
    private bool _isVisible;
    private string _leftTableLabel = string.Empty;
    private string _rightTableLabel = string.Empty;
    private ManualJoinColumnOption? _selectedLeftColumn;
    private ManualJoinColumnOption? _selectedRightColumn;
    private string _selectedJoinType = "INNER";
    private NodeViewModel? _leftTable;
    private NodeViewModel? _rightTable;
    private List<ManualJoinColumnOption> _allRightColumns = [];

    public ObservableCollection<ManualJoinColumnOption> LeftColumns { get; } = [];
    public ObservableCollection<ManualJoinColumnOption> RightColumns { get; } = [];
    public ObservableCollection<string> JoinTypes { get; } = ["INNER", "LEFT", "RIGHT", "FULL"];
    public bool HasCompatibleRightColumns => RightColumns.Count > 0;

    public bool IsVisible
    {
        get => _isVisible;
        private set => Set(ref _isVisible, value);
    }

    public string LeftTableLabel
    {
        get => _leftTableLabel;
        private set => Set(ref _leftTableLabel, value);
    }

    public string RightTableLabel
    {
        get => _rightTableLabel;
        private set => Set(ref _rightTableLabel, value);
    }

    public ManualJoinColumnOption? SelectedLeftColumn
    {
        get => _selectedLeftColumn;
        set
        {
            if (!Set(ref _selectedLeftColumn, value))
                return;

            RebuildCompatibleRightColumns();
            ConfirmCommand.NotifyCanExecuteChanged();
        }
    }

    public ManualJoinColumnOption? SelectedRightColumn
    {
        get => _selectedRightColumn;
        set
        {
            if (!Set(ref _selectedRightColumn, value))
                return;

            ConfirmCommand.NotifyCanExecuteChanged();
        }
    }

    public string SelectedJoinType
    {
        get => _selectedJoinType;
        set => Set(ref _selectedJoinType, value);
    }

    public RelayCommand ConfirmCommand { get; }
    public RelayCommand CancelCommand { get; }

    public event EventHandler<ManualJoinRequest>? Confirmed;

    public ManualJoinDialogViewModel(ILocalizationService? localization = null)
    {
        _ = localization ?? LocalizationService.Instance;
        ConfirmCommand = new RelayCommand(Confirm, CanConfirm);
        CancelCommand = new RelayCommand(Close);
    }

    public void Open(NodeViewModel leftTable, NodeViewModel rightTable)
    {
        _leftTable = leftTable;
        _rightTable = rightTable;

        LeftTableLabel = ResolveTableLabel(leftTable);
        RightTableLabel = ResolveTableLabel(rightTable);

        LeftColumns.Clear();
        foreach (PinViewModel pin in leftTable.OutputPins)
            LeftColumns.Add(new ManualJoinColumnOption(pin.Name, pin.DataType, ResolveCompatibilityType(pin)));

        _allRightColumns = rightTable.OutputPins
            .Select(pin => new ManualJoinColumnOption(pin.Name, pin.DataType, ResolveCompatibilityType(pin)))
            .ToList();

        SelectedLeftColumn = SelectDefaultLeftColumn(leftTable, rightTable);
        SelectedJoinType = "INNER";

        IsVisible = true;
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    public void Close()
    {
        IsVisible = false;
    }

    private bool CanConfirm()
    {
        return _leftTable is not null
            && _rightTable is not null
            && SelectedLeftColumn is not null
            && SelectedRightColumn is not null
            && AreTypesCompatible(SelectedLeftColumn.CompatibilityType, SelectedRightColumn.CompatibilityType);
    }

    private void Confirm()
    {
        if (!CanConfirm() || _leftTable is null || _rightTable is null)
            return;

        Confirmed?.Invoke(
            this,
            new ManualJoinRequest(
                LeftTable: _leftTable,
                RightTable: _rightTable,
                JoinType: SelectedJoinType,
                LeftColumn: SelectedLeftColumn!.Name,
                RightColumn: SelectedRightColumn!.Name
            )
        );

        Close();
    }

    private void RebuildCompatibleRightColumns()
    {
        RightColumns.Clear();
        if (SelectedLeftColumn is null)
        {
            RaisePropertyChanged(nameof(HasCompatibleRightColumns));
            SelectedRightColumn = null;
            return;
        }

        foreach (ManualJoinColumnOption option in _allRightColumns.Where(o =>
                     AreTypesCompatible(SelectedLeftColumn.CompatibilityType, o.CompatibilityType)))
        {
            RightColumns.Add(option);
        }

        RaisePropertyChanged(nameof(HasCompatibleRightColumns));
        SelectedRightColumn = RightColumns.FirstOrDefault();
    }

    private static bool AreTypesCompatible(PinDataType left, PinDataType right)
    {
        PinModel source = CreateCompatibilityProbePin(
            ownerId: "manual-join-left",
            pinName: "left",
            direction: PinDirection.Output,
            dataType: left);
        PinModel destination = CreateCompatibilityProbePin(
            ownerId: "manual-join-right",
            pinName: "right",
            direction: PinDirection.Input,
            dataType: right);

        PinConnectionDecision decision = destination.CanConnect(source, PinConnectionContext.ValidationOnly());
        return decision.IsAllowed;
    }

    private static PinModel CreateCompatibilityProbePin(
        string ownerId,
        string pinName,
        PinDirection direction,
        PinDataType dataType)
    {
        var descriptor = new PinDescriptor(pinName, direction, dataType);
        var owner = new PinModelOwner(ownerId, NodeType.TableSource);
        var pinId = new PinId($"{ownerId}:{pinName}:{direction}");
        return PinModelFactory.Create(pinId, descriptor, owner, dataType, expectedColumnScalarType: null);
    }

    private static string ResolveTableLabel(NodeViewModel node)
    {
        if (!string.IsNullOrWhiteSpace(node.Subtitle))
            return node.Subtitle;

        return node.Title;
    }

    private static ManualJoinColumnOption? SelectDefaultLeftColumn(NodeViewModel leftTable, NodeViewModel rightTable)
    {
        string rightShort = (rightTable.Subtitle ?? rightTable.Title).Split('.').Last();
        string expectedFk = $"{AutoJoinDetector.Singularize(rightShort)}_id";

        PinViewModel? match = leftTable.OutputPins.FirstOrDefault(p =>
            p.Name.Equals(expectedFk, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            return new ManualJoinColumnOption(match.Name, match.DataType, ResolveCompatibilityType(match));

        PinViewModel? genericFk = leftTable.OutputPins.FirstOrDefault(p =>
            p.Name.EndsWith("_id", StringComparison.OrdinalIgnoreCase));
        if (genericFk is not null)
            return new ManualJoinColumnOption(genericFk.Name, genericFk.DataType, ResolveCompatibilityType(genericFk));

        PinViewModel? first = leftTable.OutputPins.FirstOrDefault();
        return first is null ? null : new ManualJoinColumnOption(first.Name, first.DataType, ResolveCompatibilityType(first));
    }

    private static PinDataType ResolveCompatibilityType(PinViewModel pin)
    {
        if (pin.ColumnRefMeta is not null)
            return pin.ColumnRefMeta.ScalarType;

        if (pin.ExpectedColumnScalarType is not null)
            return pin.ExpectedColumnScalarType.Value;

        return pin.DataType;
    }
}
