namespace AkkornStudio.UI.ViewModels;

/// <summary>
/// ViewModel for the left sidebar containing tabs for nodes and connections.
/// </summary>
public sealed class SidebarViewModel : ViewModelBase
{
    private SidebarTab _activeTab = SidebarTab.Nodes;

    public RelayCommand SelectNodesCommand { get; }
    public RelayCommand SelectConnectionCommand { get; }
    public RelayCommand AddNodeCommand { get; }
    public RelayCommand AddConnectionCommand { get; }
    public RelayCommand TogglePreviewCommand { get; }

    public event Action? AddNodeRequested;
    public event Action? AddConnectionRequested;
    public event Action? TogglePreviewRequested;

    /// <summary>
    /// Gets or sets the currently active tab.
    /// </summary>
    public SidebarTab ActiveTab
    {
        get => _activeTab;
        set
        {
            if (Set(ref _activeTab, value))
            {
                RaisePropertyChanged(nameof(ShowNodes));
                RaisePropertyChanged(nameof(ShowConnection));
            }
        }
    }

    /// <summary>
    /// Returns true when Nodes tab is active.
    /// </summary>
    public bool ShowNodes => ActiveTab == SidebarTab.Nodes;

    /// <summary>
    /// Returns true when Connection tab is active.
    /// </summary>
    public bool ShowConnection => ActiveTab == SidebarTab.Connection;

    /// <summary>
    /// ViewModel for the Nodes list tab.
    /// </summary>
    public NodesListViewModel NodesList { get; }

    /// <summary>
    /// ViewModel for the Connection status tab.
    /// </summary>
    public ConnectionManagerViewModel ConnectionManager { get; }

    /// <summary>
    /// ViewModel for diagnostics tab.
    /// </summary>
    public AppDiagnosticsViewModel Diagnostics { get; }

    public SidebarViewModel(
        NodesListViewModel nodesList,
        ConnectionManagerViewModel connectionManager,
        AppDiagnosticsViewModel diagnostics)
    {
        NodesList = nodesList;
        ConnectionManager = connectionManager;
        Diagnostics = diagnostics;

        SelectNodesCommand = new RelayCommand(() => ActiveTab = SidebarTab.Nodes);
        SelectConnectionCommand = new RelayCommand(() => ActiveTab = SidebarTab.Connection);
        AddNodeCommand = new RelayCommand(RequestAddNode);
        AddConnectionCommand = new RelayCommand(RequestAddConnection);
        TogglePreviewCommand = new RelayCommand(() => TogglePreviewRequested?.Invoke());
    }

    private void RequestAddNode()
    {
        ActiveTab = SidebarTab.Nodes;
        AddNodeRequested?.Invoke();
    }

    private void RequestAddConnection()
    {
        ActiveTab = SidebarTab.Connection;
        AddConnectionRequested?.Invoke();
    }
}
