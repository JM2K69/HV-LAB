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
    [ObservableProperty] private string selectedLanguage;
    [ObservableProperty] private string status = string.Empty;

    public List<string> Themes    { get; } = ["System", "Light", "Dark"];
    public List<string> Languages { get; } = ["fr", "en"];

    public SettingsViewModel()
    {
        var s = AppSettings.Current;
        baseImagesFolder = s.BaseImagesFolder;
        vmsFolder        = s.VmsFolder;
        powerShellPath   = s.PowerShellPath;
        selectedTheme    = s.Theme;
        selectedLanguage = s.Language;
    }

    // Propagate changes live to the singleton so other VMs pick them up
    partial void OnBaseImagesFolderChanged(string value) => AppSettings.Current.BaseImagesFolder = value;
    partial void OnVmsFolderChanged(string value)        => AppSettings.Current.VmsFolder        = value;
    partial void OnPowerShellPathChanged(string value)   => AppSettings.Current.PowerShellPath   = value;

    partial void OnSelectedThemeChanged(string value)
    {
        AppSettings.Current.Theme = value;
        if (App.MainAppWindow?.Content is Microsoft.UI.Xaml.FrameworkElement root)
            ThemeService.Apply(root, value);
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        AppSettings.Current.Language = value;
        LocalizationService.Instance.SetLanguage(value);
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        try
        {
            await AppSettings.Current.SaveAsync();
            Status = LocalizationService.Instance["SET_SavedOk"];
        }
        catch (Exception ex)
        {
            Status = $"{LocalizationService.Instance["SET_SavedError"]} : {ex.Message}";
        }
    }
}

