using HVLab.Helpers;
using HVLab.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HVLab.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; } = new();

    public SettingsPage() => InitializeComponent();

    private void BrowseBaseImages_Click(object sender, RoutedEventArgs e)
    {
        var hwnd   = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
        var folder = Win32FolderPicker.Pick(hwnd, "Sélectionner le dossier des images de base VHDX");
        if (folder is not null) ViewModel.BaseImagesFolder = folder;
    }

    private void BrowseVms_Click(object sender, RoutedEventArgs e)
    {
        var hwnd   = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
        var folder = Win32FolderPicker.Pick(hwnd, "Sélectionner le dossier des machines virtuelles");
        if (folder is not null) ViewModel.VmsFolder = folder;
    }

    private void BrowsePowerShell_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
        var file = Win32FolderPicker.PickFile(hwnd, "Sélectionner powershell.exe");
        if (file is not null) ViewModel.PowerShellPath = file;
    }
}
