using AkkornStudio.UI.Services.Workspace.Models;

namespace AkkornStudio.UI.ViewModels;

/// <summary>
/// One top-level workspace tab. A tab is bound to a single connection profile and can switch
/// freely between modes (Query/DDL/SQL/ER); each visited mode keeps its own document alive (the
/// shell owns the <c>mode → documentId</c> mapping via its document/tab map). This view model
/// carries only the tab's identity, connection and current mode for the tab strip and mode selector.
/// </summary>
public sealed class WorkspaceTabViewModel : ViewModelBase
{
    private string _title;
    private string? _profileId;
    private string _connectionTitle;
    private WorkspaceDocumentType _currentMode;
    private bool _isActive;

    public WorkspaceTabViewModel(
        Guid tabId,
        WorkspaceDocumentType initialMode,
        string? profileId,
        string connectionTitle,
        string title)
    {
        TabId = tabId;
        _currentMode = initialMode;
        _profileId = profileId;
        _connectionTitle = connectionTitle;
        _title = title;
    }

    public Guid TabId { get; }

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    public string? ProfileId
    {
        get => _profileId;
        set => Set(ref _profileId, value);
    }

    public string ConnectionTitle
    {
        get => _connectionTitle;
        set
        {
            if (Set(ref _connectionTitle, value))
                RaisePropertyChanged(nameof(DisplayTitle));
        }
    }

    public WorkspaceDocumentType CurrentMode
    {
        get => _currentMode;
        set
        {
            if (!Set(ref _currentMode, value))
                return;

            RaisePropertyChanged(nameof(CurrentModeLabel));
            RaisePropertyChanged(nameof(CurrentModeIcon));
            RaisePropertyChanged(nameof(DisplayTitle));
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }

    /// <summary>Tab strip label, e.g. "SQL · Postgres local".</summary>
    public string DisplayTitle =>
        string.IsNullOrWhiteSpace(ConnectionTitle)
            ? CurrentModeLabel
            : $"{CurrentModeLabel} · {ConnectionTitle}";

    public string CurrentModeLabel => ModeLabel(CurrentMode);

    public string CurrentModeIcon => ModeIcon(CurrentMode);

    public static string ModeLabel(WorkspaceDocumentType mode) => mode switch
    {
        WorkspaceDocumentType.QueryCanvas => "Query",
        WorkspaceDocumentType.DdlCanvas => "DDL",
        WorkspaceDocumentType.SqlEditor => "SQL",
        WorkspaceDocumentType.ErDiagram => "ER",
        WorkspaceDocumentType.DdlSchemaCompare => "Compare",
        WorkspaceDocumentType.DdlSchemaAnalysis => "Analise",
        _ => mode.ToString(),
    };

    public static string ModeIcon(WorkspaceDocumentType mode) => mode switch
    {
        WorkspaceDocumentType.QueryCanvas => "VectorPolyline",
        WorkspaceDocumentType.DdlCanvas => "TableCog",
        WorkspaceDocumentType.SqlEditor => "ConsoleLine",
        WorkspaceDocumentType.ErDiagram => "Sitemap",
        WorkspaceDocumentType.DdlSchemaCompare => "ScaleBalance",
        WorkspaceDocumentType.DdlSchemaAnalysis => "DatabaseSearch",
        _ => "FileOutline",
    };
}
