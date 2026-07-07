using HVLab.Models;
using HVLab.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HVLab.Views;

public sealed partial class VirtualSwitchesPage : Page
{
    public VirtualSwitchesViewModel ViewModel { get; } = new();

    public VirtualSwitchesPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.RefreshAsync();
    }

    private async void RemoveSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: VirtualSwitch sw }) return;
        var dialog = new ContentDialog
        {
            Title   = "Supprimer le commutateur",
            Content = $"Supprimer définitivement « {sw.Name} » ?",
            PrimaryButtonText = "Supprimer",
            CloseButtonText   = "Annuler",
            DefaultButton     = ContentDialogButton.Close,
            XamlRoot          = XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.RemoveSwitchAsync(sw);
    }

    private async void RemoveNat_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: NatNetwork nat }) return;
        var dialog = new ContentDialog
        {
            Title   = "Supprimer le réseau NAT",
            Content = $"Supprimer définitivement « {nat.Name} » ?",
            PrimaryButtonText = "Supprimer",
            CloseButtonText   = "Annuler",
            DefaultButton     = ContentDialogButton.Close,
            XamlRoot          = XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.RemoveNatAsync(nat);
    }
}
