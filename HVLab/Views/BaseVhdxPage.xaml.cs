using HVLab.Helpers;
using HVLab.Models;
using HVLab.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HVLab.Views;

public sealed partial class BaseVhdxPage : Page
{
    public BaseVhdxViewModel ViewModel { get; } = new();

    public BaseVhdxPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.RefreshAsync();
    }

    private void BrowseWim_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
        var file = Win32FolderPicker.PickFile(hwnd, "Sélectionner un fichier WIM ou ESD");
        if (file is not null) ViewModel.WimPath = file;
    }

    private void BrowseBaseFolder_Click(object sender, RoutedEventArgs e)
    {
        var hwnd   = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
        var folder = Win32FolderPicker.Pick(hwnd, "Sélectionner le dossier de destination des images de base");
        if (folder is not null) ViewModel.BaseFolder = folder;
    }

    private async void DeleteVhdx_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BaseVhdx vhdx }) return;
        var dialog = new ContentDialog
        {
            Title   = "Supprimer l'image",
            Content = $"Supprimer définitivement « {vhdx.Name}.vhdx » ?",
            PrimaryButtonText = "Supprimer",
            CloseButtonText   = "Annuler",
            DefaultButton     = ContentDialogButton.Close,
            XamlRoot          = XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.DeleteBaseVhdxAsync(vhdx);
    }
}
