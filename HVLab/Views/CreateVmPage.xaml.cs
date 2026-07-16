using HVLab.Helpers;
using HVLab.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HVLab.Views;

public sealed partial class CreateVmPage : Page
{
    public CreateVmViewModel ViewModel { get; } = new();

    public CreateVmPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.LoadDataAsync();
    }

    private void BrowseVmFolder_Click(object sender, RoutedEventArgs e)
    {
        var hwnd   = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
        var folder = Win32FolderPicker.Pick(hwnd, "Sélectionner le dossier des machines virtuelles");
        if (folder is not null) ViewModel.VmFolder = folder;
    }

    private async void ReloadData_Click(object sender, RoutedEventArgs e)
        => await ViewModel.LoadDataAsync();
}
