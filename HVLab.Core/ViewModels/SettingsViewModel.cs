using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Services;

namespace HVLab.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private string baseImagesFolder;
    [ObservableProperty] private string vmsFolder;
    [ObservableProperty] private string selectedTheme;
    [ObservableProperty] private string selectedLanguage;
    [ObservableProperty] private string status = string.Empty;

    public List<string> Themes    { get; } = ["System", "Light", "Dark"];
    public List<string> Languages { get; } = ["fr", "en"];

    private readonly IThemeService _theme;
    private readonly IHyperVService? _hvService;

    // Enhanced session mode of the host: read when the page opens, written when the user toggles it
    [ObservableProperty] private bool enhancedSession;
    [ObservableProperty] private bool enhancedSessionLoaded;
    private bool _loadingEnhancedSession;

    public async Task LoadHostSettingsAsync()
    {
        if (_hvService is null) return;
        try
        {
            _loadingEnhancedSession = true;
            EnhancedSession = await _hvService.GetEnhancedSessionHostAsync();
            EnhancedSessionLoaded = true;
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { _loadingEnhancedSession = false; }
    }

    partial void OnEnhancedSessionChanged(bool value)
    {
        if (_loadingEnhancedSession || _hvService is null) return;
        _ = ApplyEnhancedSessionAsync(value);
    }

    private async Task ApplyEnhancedSessionAsync(bool value)
    {
        try
        {
            await _hvService!.SetEnhancedSessionHostAsync(value);
            Status = LocalizationService.T(value ? "SET_EnhancedOn" : "SET_EnhancedOff");
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            await LoadHostSettingsAsync();   // show the real host state again
        }
    }

    public SettingsViewModel(IThemeService theme, IHyperVService? hvService = null)
    {
        _theme = theme;
        _hvService = hvService;
        var s = AppSettings.Current;
        baseImagesFolder = s.BaseImagesFolder;
        vmsFolder        = s.VmsFolder;
        selectedTheme    = s.Theme;
        selectedLanguage = s.Language;
    }

    // Propagate changes live to the singleton so other VMs pick them up
    partial void OnBaseImagesFolderChanged(string value) => AppSettings.Current.BaseImagesFolder = value;
    partial void OnVmsFolderChanged(string value)        => AppSettings.Current.VmsFolder        = value;

    partial void OnSelectedThemeChanged(string value)
    {
        AppSettings.Current.Theme = value;
        _theme.Apply(value);
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

