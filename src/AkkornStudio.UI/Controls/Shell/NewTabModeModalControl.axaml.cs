using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.UI.Controls.Shell;

public partial class NewTabModeModalControl : UserControl
{
    public NewTabModeModalControl()
    {
        InitializeComponent();
    }

    private void Overlay_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is NewTabModeModalViewModel vm)
            vm.Close();
    }

    private void Card_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Clicking inside the card must not dismiss the modal.
        e.Handled = true;
    }
}
