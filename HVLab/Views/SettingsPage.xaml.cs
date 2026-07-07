using HVLab.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace HVLab.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; } = new();

    public SettingsPage() => InitializeComponent();

    private async void BrowseBaseImages_Click(object sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder is not null) ViewModel.BaseImagesFolder = folder;
    }

    private async void BrowseVms_Click(object sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder is not null) ViewModel.VmsFolder = folder;
    }

    private async void BrowsePowerShell_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        var hwnd   = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".exe");
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        var file = await picker.PickSingleFileAsync();
        if (file is not null) ViewModel.PowerShellPath = file.Path;
    }

    private async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        var hwnd   = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
