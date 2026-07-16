using HVLab.Models;
using HVLab.Services;
using HVLab.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HVLab.Views;

public sealed partial class VirtualMachinesPage : Page
{
    public VirtualMachinesViewModel ViewModel { get; } = new();
    public LocalizationService Loc => LocalizationService.Instance;

    public VirtualMachinesPage()
    {
        InitializeComponent();
        LocalizationService.Instance.PropertyChanged += (_, _) => Bindings.Update();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.RefreshAsync();
    }

    private async void StartVm_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: VirtualMachine vm }) await ViewModel.StartVmAsync(vm);
    }

    private async void StopVm_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: VirtualMachine vm }) await ViewModel.StopVmAsync(vm);
    }

    private async void DeleteVm_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: VirtualMachine vm }) return;
        var dialog = new ContentDialog
        {
            Title = "Supprimer la VM",
            Content = $"Supprimer définitivement « {vm.Name} » ?",
            PrimaryButtonText = "Supprimer",
            CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.RemoveVmAsync(vm);
    }
}
