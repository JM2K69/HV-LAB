using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Services;

namespace HVLab.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private string baseImagesFolder;
    [ObservableProperty] private string vmsFolder;
    [ObservableProperty] private string powerShellPath;
    [ObservableProperty] private string selectedTheme;
    [ObservableProperty] private string status = string.Empty;

    public List<string> Themes { get; } = ["System", "Light", "Dark"];

    public SettingsViewModel()
    {
        var s = AppSettings.Current;
        baseImagesFolder = s.BaseImagesFolder;
        vmsFolder        = s.VmsFolder;
        powerShellPath   = s.PowerShellPath;
        selectedTheme    = s.Theme;
    }

    // Propagate changes live to the singleton so other VMs pick them up
    partial void OnBaseImagesFolderChanged(string value) => AppSettings.Current.BaseImagesFolder = value;
    partial void OnVmsFolderChanged(string value)        => AppSettings.Current.VmsFolder        = value;
    partial void OnPowerShellPathChanged(string value)   => AppSettings.Current.PowerShellPath   = value;

    partial void OnSelectedThemeChanged(string value)
    {
        AppSettings.Current.Theme = value;
        // Apply immediately to the live window
        if (App.MainAppWindow?.Content is Microsoft.UI.Xaml.FrameworkElement root)
            ThemeService.Apply(root, value);
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        try
        {
            await AppSettings.Current.SaveAsync();
            Status = "✓ Paramètres enregistrés.";
        }
        catch (Exception ex)
        {
            Status = $"✗ Erreur : {ex.Message}";
        }
    }
}

