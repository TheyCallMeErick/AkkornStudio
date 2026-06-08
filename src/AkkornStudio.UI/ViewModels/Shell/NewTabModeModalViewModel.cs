using System.Collections.ObjectModel;
using AkkornStudio.Core;
using AkkornStudio.UI.Services.Workspace.Models;

namespace AkkornStudio.UI.ViewModels;

/// <summary>One selectable mode in the "new tab" modal.</summary>
public sealed class NewTabModeOption
{
    public NewTabModeOption(WorkspaceDocumentType mode, string title, string description, string icon)
    {
        Mode = mode;
        Title = title;
        Description = description;
        Icon = icon;
    }

    public WorkspaceDocumentType Mode { get; }
    public string Title { get; }
    public string Description { get; }
    public string Icon { get; }
}

/// <summary>
/// Modal shown when opening a new workspace tab (or switching a tab's mode): the user picks which
/// mode the tab should start in. The chosen mode is delivered through <see cref="ModeChosen"/>.
/// </summary>
public sealed class NewTabModeModalViewModel : ViewModelBase
{
    private bool _isVisible;
    private string _title = "Nova aba";

    public NewTabModeModalViewModel()
    {
        CloseCommand = new RelayCommand(Close);
        ChooseModeCommand = new RelayCommand<WorkspaceDocumentType>(Choose);
    }

    /// <summary>Raised with the mode the user picked. The shell creates/switches the tab.</summary>
    public event Action<WorkspaceDocumentType>? ModeChosen;

    public RelayCommand CloseCommand { get; }

    public RelayCommand<WorkspaceDocumentType> ChooseModeCommand { get; }

    public ObservableCollection<NewTabModeOption> Modes { get; } =
    [
        new(WorkspaceDocumentType.QueryCanvas, "Query", "Construtor visual de consultas (canvas de joins).", "VectorPolyline"),
        new(WorkspaceDocumentType.DdlCanvas, "DDL", "Modelagem de tabelas e geracao de DDL.", "TableCog"),
        new(WorkspaceDocumentType.SqlEditor, "SQL", "Editor de SQL com execucao e resultados.", "ConsoleLine"),
        new(WorkspaceDocumentType.ErDiagram, "ER", "Diagrama entidade-relacionamento do schema.", "Sitemap"),
    ];

    public bool IsVisible
    {
        get => _isVisible;
        set => Set(ref _isVisible, value);
    }

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    public void Show(string title = "Nova aba")
    {
        Title = title;
        IsVisible = true;
    }

    public void Close() => IsVisible = false;

    private void Choose(WorkspaceDocumentType mode)
    {
        IsVisible = false;
        ModeChosen?.Invoke(mode);
    }
}
