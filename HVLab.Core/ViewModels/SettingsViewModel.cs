using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Models;
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
    private readonly IHostService? _hvService;

    /// <summary>DPAPI-protect the password written into the notes of new VMs (off by default).</summary>
    [ObservableProperty] private bool protectNotesPassword = AppSettings.Current.ProtectNotesPassword;
    partial void OnProtectNotesPasswordChanged(bool value) => AppSettings.Current.ProtectNotesPassword = value;

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

    public SettingsViewModel(IThemeService theme, IHostService? hvService = null, IDialogService? dialogs = null)
    {
        _theme = theme;
        _hvService = hvService;
        _dialogs = dialogs;
        var s = AppSettings.Current;
        baseImagesFolder = s.BaseImagesFolder;
        vmsFolder        = s.VmsFolder;
        selectedTheme    = s.Theme;
        selectedLanguage = s.Language;
        ProductKeys      = new(s.ProductKeys);
    }

    // ─── Product keys of the user (Windows Insider builds…), saved as soon as they change ──

    private readonly IDialogService? _dialogs;

    public ObservableCollection<CustomProductKey> ProductKeys { get; }
    public bool HasProductKeys => ProductKeys.Count > 0;

    [ObservableProperty] private string newKeyName    = "";
    [ObservableProperty] private string newKeyPattern = "";
    [ObservableProperty] private string newKeyValue   = "";
    [ObservableProperty] private string productKeyStatus = "";

    /// <summary>Adds the key of the form, or replaces the key of the same pattern.</summary>
    [RelayCommand]
    public async Task AddProductKeyAsync()
    {
        if (InputValidator.CheckKeyPattern(NewKeyPattern) is { } p) { ProductKeyStatus = LocalizationService.T(p); return; }
        if (string.IsNullOrWhiteSpace(NewKeyValue) || InputValidator.CheckProductKey(NewKeyValue) is not null)
        { ProductKeyStatus = LocalizationService.T("Val_ProductKey"); return; }

        var key  = new CustomProductKey { Name = NewKeyName.Trim(), Pattern = NewKeyPattern.Trim(), Key = NewKeyValue.Trim().ToUpperInvariant() };
        var same = ProductKeys.FirstOrDefault(k => k.Pattern.Trim().Equals(key.Pattern, StringComparison.OrdinalIgnoreCase));
        if (same is null) ProductKeys.Add(key);
        else ProductKeys[ProductKeys.IndexOf(same)] = key;

        if (await SaveProductKeysAsync(LocalizationService.T(same is null ? "SET_KeyAdded" : "SET_KeyReplaced", key.DisplayName)))
            NewKeyName = NewKeyPattern = NewKeyValue = "";
    }

    [RelayCommand]
    public async Task RemoveProductKeyAsync(CustomProductKey? key)
    {
        if (key is null || !ProductKeys.Contains(key)) return;
        if (_dialogs is not null
            && !await _dialogs.ConfirmAsync(LocalizationService.T("SET_KeyRemoveTitle"),
                                            LocalizationService.T("SET_KeyRemoveText", key.DisplayName), LocalizationService.T("Btn_Delete")))
            return;
        ProductKeys.Remove(key);
        await SaveProductKeysAsync(LocalizationService.T("SET_KeyRemoved", key.DisplayName));
    }

    private async Task<bool> SaveProductKeysAsync(string done)
    {
        AppSettings.Current.ProductKeys = [.. ProductKeys];
        OnPropertyChanged(nameof(HasProductKeys));
        try
        {
            await AppSettings.Current.SaveAsync();
            ProductKeyStatus = done;
            return true;
        }
        catch (Exception ex)
        {
            ProductKeyStatus = $"{LocalizationService.Instance["SET_SavedError"]} : {ex.Message}";
            return false;
        }
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

