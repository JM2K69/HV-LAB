using HVLab.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HVLab.Views;

public sealed partial class VirtualSwitchesPage : Page
{
    public VirtualSwitchesViewModel ViewModel { get; } = App.GetService<VirtualSwitchesViewModel>();

    public VirtualSwitchesPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.RefreshAsync();
    }
}
