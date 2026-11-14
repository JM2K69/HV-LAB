using HVLab.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HVLab.Views;

public sealed partial class QuickVmPage : Page
{
    public QuickVmViewModel ViewModel { get; } = App.GetService<QuickVmViewModel>();

    public QuickVmPage() => InitializeComponent();

    private async void OpenCreatedVm_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => await VmWindow.ShowByNameAsync(ViewModel.CreatedVmName);

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.LoadDataAsync();
    }
}
