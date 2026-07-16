using HVLab.Services;
using HVLab.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HVLab.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; } = new();
    public LocalizationService Loc => LocalizationService.Instance;

    public DashboardPage()
    {
        InitializeComponent();
        LocalizationService.Instance.PropertyChanged += (_, _) => Bindings.Update();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.RefreshAsync();
    }
}
