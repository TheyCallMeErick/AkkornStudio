namespace AkkornStudio.UI.ViewModels;

public sealed class SqlQueryParameterPromptItemViewModel : ViewModelBase
{
    private string _inputValue = string.Empty;

    public SqlQueryParameterPromptItemViewModel(string name, string? inputValue = null)
    {
        Name = name ?? string.Empty;
        _inputValue = inputValue ?? string.Empty;
    }

    public string Name { get; }

    public string InputValue
    {
        get => _inputValue;
        set => Set(ref _inputValue, value ?? string.Empty);
    }
}
