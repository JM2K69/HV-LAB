using HVLab.Models;
using HVLab.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;

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

    private async void BrowseWim_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        var hwnd   = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".wim");
        picker.FileTypeFilter.Add(".esd");
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        var file = await picker.PickSingleFileAsync();
        if (file is not null) ViewModel.WimPath = file.Path;
    }

    private async void BrowseBaseFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        var hwnd   = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null) ViewModel.BaseFolder = folder.Path;
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
