using HVLab.Helpers;
using HVLab.Models;
using HVLab.Services;
using HVLab.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HVLab.Views;

public sealed partial class BaseVhdxPage : Page
{
    public BaseVhdxViewModel ViewModel { get; } = new();
    public LocalizationService Loc => LocalizationService.Instance;

    public BaseVhdxPage()
    {
        InitializeComponent();
        ViewModel.ConfirmOverwriteAsync = ConfirmOverwriteAsync;
    }

    private async Task<bool> ConfirmOverwriteAsync(string vhdxPath)
    {
        var dialog = new ContentDialog
        {
            Title             = Loc["BV_OverwriteTitle"],
            Content           = string.Format(Loc["BV_OverwriteConfirm"], System.IO.Path.GetFileName(vhdxPath)),
            PrimaryButtonText = Loc["BV_BtnOverwrite"],
            CloseButtonText   = Loc["Btn_Cancel"],
            DefaultButton     = ContentDialogButton.Close,
            XamlRoot          = XamlRoot
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // Page is cached: don't refresh over a build in progress
        if (!ViewModel.IsCreating)
            _ = ViewModel.RefreshAsync();
    }

    private void BrowseWim_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
        var file = Win32FolderPicker.PickFile(hwnd, Loc["Browse_WimTitle"]);
        if (file is not null) ViewModel.WimPath = file;
    }

    private void BrowseBaseFolder_Click(object sender, RoutedEventArgs e)
    {
        var hwnd   = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
        var folder = Win32FolderPicker.Pick(hwnd, Loc["Browse_BaseFolderTitle"]);
        if (folder is not null) ViewModel.BaseFolder = folder;
    }

    private async void DeleteVhdx_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BaseVhdx vhdx }) return;
        var dialog = new ContentDialog
        {
            Title   = Loc["BV_DeleteTitle"],
            Content = $"{Loc["BV_DeleteConfirm"]} \u00ab {vhdx.Name}.vhdx \u00bb ?",
            PrimaryButtonText = Loc["Btn_Delete"],
            CloseButtonText   = Loc["Btn_Cancel"],
            DefaultButton     = ContentDialogButton.Close,
            XamlRoot          = XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.DeleteBaseVhdxAsync(vhdx);
    }
}
