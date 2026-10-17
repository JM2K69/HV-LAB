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
    public BaseVhdxViewModel ViewModel { get; } = App.GetService<BaseVhdxViewModel>();
    public LocalizationService Loc => LocalizationService.Instance;

    public BaseVhdxPage() => InitializeComponent();

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
}
