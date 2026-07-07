using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Services;

namespace HVLab.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private string baseImagesFolder;
    [ObservableProperty] private string vmsFolder;
    [ObservableProperty] private string powerShellPath;
    [ObservableProperty] private string status = string.Empty;

    public SettingsViewModel()
    {
        var s = AppSettings.Current;
        baseImagesFolder = s.BaseImagesFolder;
        vmsFolder        = s.VmsFolder;
        powerShellPath   = s.PowerShellPath;
    }

    // Propagate folder changes live to the singleton so other VMs pick them up
    partial void OnBaseImagesFolderChanged(string value) => AppSettings.Current.BaseImagesFolder = value;
    partial void OnVmsFolderChanged(string value)        => AppSettings.Current.VmsFolder        = value;
    partial void OnPowerShellPathChanged(string value)   => AppSettings.Current.PowerShellPath   = value;

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
