using AkkornStudio.Nodes;
using System.Collections.ObjectModel;

namespace AkkornStudio.UI.ViewModels;

public sealed class ParameterRowViewModel(NodeParameter param, string? currentValue) : ViewModelBase
{
    private string? _value = currentValue ?? param.DefaultValue;
    private bool _isDirty;
    private bool _isEditable = true;
    private bool _suppressDirtyTracking;
    private bool _hasSuggestedValues;

    public string Name { get; } = param.Name;
    public ParameterKind Kind { get; } = param.Kind;
    public string? Description { get; } = param.Description;
    public IReadOnlyList<string>? EnumValues { get; } = param.EnumValues;
    public string? DefaultValue { get; } = param.DefaultValue;

    public bool IsText => Kind is ParameterKind.Text or ParameterKind.JsonPath;
    public bool IsPlainText => IsText;
    public bool IsTextWithSuggestions => false;
    public bool IsNumber => Kind == ParameterKind.Number;
    public bool IsBoolean => Kind == ParameterKind.Boolean;
    public bool IsEnum => Kind is ParameterKind.Enum or ParameterKind.CastType;
    public bool IsDateTime => Kind == ParameterKind.DateTime;
    public bool IsDate => Kind == ParameterKind.Date;
    public ObservableCollection<string> SuggestedValues { get; } = [];

    public bool HasSuggestedValues
    {
        get => _hasSuggestedValues;
        private set => Set(ref _hasSuggestedValues, value);
    }

    public string? Value
    {
        get => _value;
        set
        {
            if (Set(ref _value, value) && !_suppressDirtyTracking)
                IsDirty = true;
        }
    }

    public bool IsEditable
    {
        get => _isEditable;
        private set => Set(ref _isEditable, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set => Set(ref _isDirty, value);
    }

    public void MarkClean() => IsDirty = false;

    public void SyncValueFromModel(string? value)
    {
        _suppressDirtyTracking = true;
        Value = value;
        _suppressDirtyTracking = false;
        IsDirty = false;
    }

    public void SetConnectionOverrideValue(string? value)
    {
        _suppressDirtyTracking = true;
        Value = value;
        _suppressDirtyTracking = false;
        IsEditable = false;
        IsDirty = false;
    }

    public void ClearConnectionOverride(string? fallbackValue)
    {
        _suppressDirtyTracking = true;
        Value = fallbackValue;
        _suppressDirtyTracking = false;
        IsEditable = true;
        IsDirty = false;
    }

    public void SetSuggestedValues(IEnumerable<string> values)
    {
        string? preservedValue = Value;
        // Break ComboBox selection binding before mutating ItemsSource to avoid stale index issues.
        _suppressDirtyTracking = true;
        Value = null;
        _suppressDirtyTracking = false;

        SuggestedValues.Clear();
        foreach (string value in values.Where(static v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase))
            SuggestedValues.Add(value);

        if (!string.IsNullOrWhiteSpace(preservedValue)
            && SuggestedValues.All(s => !string.Equals(s, preservedValue, StringComparison.OrdinalIgnoreCase)))
        {
            SuggestedValues.Insert(0, preservedValue);
        }

        HasSuggestedValues = SuggestedValues.Count > 0;
        RaisePropertyChanged(nameof(IsPlainText));
        RaisePropertyChanged(nameof(IsTextWithSuggestions));

        _suppressDirtyTracking = true;
        Value = preservedValue;
        _suppressDirtyTracking = false;
        IsDirty = false;
    }
}
