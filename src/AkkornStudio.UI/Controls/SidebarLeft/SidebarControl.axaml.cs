using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Controls;

public partial class SidebarControl : UserControl
{
    private SidebarViewModel? _subscribedVm;

    public SidebarControl()
    {
        InitializeComponent();
        Loaded += (_, _) => AttachVmSubscriptions(DataContext as SidebarViewModel);
    }

    private void AttachVmSubscriptions(SidebarViewModel? vm)
    {
        if (ReferenceEquals(_subscribedVm, vm))
            return;

        if (_subscribedVm is not null)
        {
            _subscribedVm.AddNodeRequested -= OnAddNodeRequested;
        }

        _subscribedVm = vm;
        if (_subscribedVm is not null)
            _subscribedVm.AddNodeRequested += OnAddNodeRequested;
    }

    private void OnAddNodeRequested()
    {
        Dispatcher.UIThread.Post(() =>
        {
            TextBox? search = this.FindControl<TextBox>("NodesSearchBox");
            search?.Focus();
        }, DispatcherPriority.Background);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        AttachVmSubscriptions(DataContext as SidebarViewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_subscribedVm is null)
            return;

        _subscribedVm.AddNodeRequested -= OnAddNodeRequested;
        _subscribedVm = null;
    }
}
