using HVLab.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;

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

    private async void BrowseVmFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        var hwnd   = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null) ViewModel.VmFolder = folder.Path;
    }

    private async void ReloadData_Click(object sender, RoutedEventArgs e)
        => await ViewModel.LoadDataAsync();
}
