using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Services.Start.Models;

public sealed class StartTemplateItem : ViewModelBase
{
    private bool _isFavorite;

    public StartTemplateItem(string name, string category, string description, string? templateId = null)
    {
        Name = name;
        Category = category;
        Description = description;
        TemplateId = templateId;
    }

    public string Name { get; }
    public string Category { get; }
    public string Description { get; }
    public string? TemplateId { get; }

    public bool IsFavorite
    {
        get => _isFavorite;
        set => Set(ref _isFavorite, value);
    }
}
