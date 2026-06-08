using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using AkkornStudio.UI.ViewModels.UndoRedo.Commands;

namespace AkkornStudio.UI.ViewModels.Canvas;

/// <summary>
/// Manages multi-selection and node alignment operations on the canvas.
/// Handles user interactions related to selecting/deselecting nodes and aligning them.
/// </summary>
public sealed class SelectionManager : ISelectionManager
{
    private readonly ObservableCollection<NodeViewModel> _nodes;
    private readonly PropertyPanelViewModel _propertyPanel;
    private readonly UndoRedoStack _undoRedo;

    public RelayCommand SelectAllCommand { get; }
    public RelayCommand DeselectAllCommand { get; }
    public RelayCommand AlignLeftCommand { get; }
    public RelayCommand AlignRightCommand { get; }
    public RelayCommand AlignTopCommand { get; }
    public RelayCommand AlignBottomCommand { get; }
    public RelayCommand AlignCenterHCommand { get; }
    public RelayCommand AlignCenterVCommand { get; }
    public RelayCommand DistributeHCommand { get; }
    public RelayCommand DistributeVCommand { get; }

    public SelectionManager(
        ObservableCollection<NodeViewModel> nodes,
        PropertyPanelViewModel propertyPanel,
        UndoRedoStack undoRedo
    )
    {
        _nodes = nodes;
        _propertyPanel = propertyPanel;
        _undoRedo = undoRedo;

        SelectAllCommand = new RelayCommand(SelectAll);
        DeselectAllCommand = new RelayCommand(DeselectAll);

        AlignLeftCommand = new RelayCommand(
            () => AlignNodes(AlignMode.Left),
            () => HasAtLeastSelected(2)
        );
        AlignRightCommand = new RelayCommand(
            () => AlignNodes(AlignMode.Right),
            () => HasAtLeastSelected(2)
        );
        AlignTopCommand = new RelayCommand(
            () => AlignNodes(AlignMode.Top),
            () => HasAtLeastSelected(2)
        );
        AlignBottomCommand = new RelayCommand(
            () => AlignNodes(AlignMode.Bottom),
            () => HasAtLeastSelected(2)
        );
        AlignCenterHCommand = new RelayCommand(
            () => AlignNodes(AlignMode.CenterH),
            () => HasAtLeastSelected(2)
        );
        AlignCenterVCommand = new RelayCommand(
            () => AlignNodes(AlignMode.CenterV),
            () => HasAtLeastSelected(2)
        );
        DistributeHCommand = new RelayCommand(
            () => AlignNodes(AlignMode.DistributeH),
            () => HasAtLeastSelected(3)
        );
        DistributeVCommand = new RelayCommand(
            () => AlignNodes(AlignMode.DistributeV),
            () => HasAtLeastSelected(3)
        );

        _nodes.CollectionChanged += OnNodesCollectionChanged;
        foreach (NodeViewModel node in _nodes)
            node.PropertyChanged += OnNodePropertyChanged;
    }

    public void SelectAll()
    {
        foreach (NodeViewModel n in _nodes)
            n.IsSelected = true;
        NotifyAlignmentCommandsStateChanged();
    }

    public void DeselectAll()
    {
        foreach (NodeViewModel n in _nodes)
            n.IsSelected = false;
        _propertyPanel.Clear();
        NotifyAlignmentCommandsStateChanged();
    }

    public void SelectNode(NodeViewModel node, bool add = false)
    {
        if (!add)
            DeselectAll();
        node.IsSelected = true;

        List<NodeViewModel> sel = SelectedNodes();
        if (sel.Count == 1)
            _propertyPanel.ShowNode(sel[0]);
        else if (sel.Count > 1)
            _propertyPanel.ShowMultiSelection(sel);

        NotifyAlignmentCommandsStateChanged();
    }

    public List<NodeViewModel> SelectedNodes() => [.. _nodes.Where(n => n.IsSelected)];

    private bool HasAtLeastSelected(int threshold)
    {
        if (threshold <= 0)
            return true;

        int count = 0;
        foreach (NodeViewModel node in _nodes)
        {
            if (!node.IsSelected)
                continue;

            count++;
            if (count >= threshold)
                return true;
        }

        return false;
    }

    public void AlignNodes(AlignMode mode)
    {
        List<NodeViewModel> sel = SelectedNodes();
        int required = RequiredSelectionThreshold(mode);
        if (sel.Count < required)
            return;

        _undoRedo.Execute(new AlignNodesCommand(sel, mode));
    }

    /// <summary>
    /// Notifies alignment command buttons to refresh their CanExecute state.
    /// Call when selection changes.
    /// </summary>
    public void NotifyAlignmentCommandsStateChanged()
    {
        AlignLeftCommand.NotifyCanExecuteChanged();
        AlignRightCommand.NotifyCanExecuteChanged();
        AlignTopCommand.NotifyCanExecuteChanged();
        AlignBottomCommand.NotifyCanExecuteChanged();
        AlignCenterHCommand.NotifyCanExecuteChanged();
        AlignCenterVCommand.NotifyCanExecuteChanged();
        DistributeHCommand.NotifyCanExecuteChanged();
        DistributeVCommand.NotifyCanExecuteChanged();
    }

    private static int RequiredSelectionThreshold(AlignMode mode) =>
        mode is AlignMode.DistributeH or AlignMode.DistributeV ? 3 : 2;

    private void OnNodesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (object item in e.OldItems)
            {
                if (item is NodeViewModel oldNode)
                    oldNode.PropertyChanged -= OnNodePropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (object item in e.NewItems)
            {
                if (item is NodeViewModel newNode)
                    newNode.PropertyChanged += OnNodePropertyChanged;
            }
        }

        NotifyAlignmentCommandsStateChanged();
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(NodeViewModel.IsSelected), StringComparison.Ordinal))
            return;

        NotifyAlignmentCommandsStateChanged();
    }
}
