namespace AkkornStudio.UI.ViewModels;

public enum SqlExecutionErrorCategory
{
    None = 0,
    Validation = 1,
    Timeout = 2,
    Cancelled = 3,
    Security = 4,
    Operational = 5,
    Unexpected = 6,
}
