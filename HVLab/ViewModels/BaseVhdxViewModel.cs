using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Models;
using HVLab.Services;

namespace HVLab.ViewModels;

public partial class BaseVhdxViewModel : ObservableObject
{
    private readonly VhdxService _vhdxService = new();

    [ObservableProperty] private ObservableCollection<BaseVhdx> baseVhdxList = [];
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string status = "Prêt";
    [ObservableProperty] private string baseFolder;

    // ─── Structured form fields ──────────────────────────────────────────────

    [ObservableProperty] private string selectedOsFamily  = "WindowsServer2025";
    [ObservableProperty] private string selectedEdition   = "Standard";
    [ObservableProperty] private bool   desktopExperience = true;
    [ObservableProperty] private string osVersion         = "";
    [ObservableProperty] private int    sizeGB            = 50;
    [ObservableProperty] private int    generation        = 2;
    [ObservableProperty] private int    imageIndex        = 1;
    [ObservableProperty] private string wimPath           = "";

    // Answer file
    [ObservableProperty] private bool   useAnswerFile  = true;
    [ObservableProperty] private bool   isCreating;
    [ObservableProperty] private string buildStatus    = "";

    // Answer file fields
    [ObservableProperty] private string computerName       = "LAB-VM";
    [ObservableProperty] private string adminPassword      = "P@ssw0rd!";
    [ObservableProperty] private string productKey         = "";
    [ObservableProperty] private string uiLanguage         = "fr-FR";
    [ObservableProperty] private string timeZone           = "Romance Standard Time";
    [ObservableProperty] private string answerFilePreview  = "";

    // ─── Dynamic edition list ─────────────────────────────────────────────────

    /// <summary>Editions available for the currently selected OS family.</summary>
    public ObservableCollection<string> AvailableEditions { get; } = [];

    /// <summary>True when Desktop Experience makes sense for this OS/edition combo.</summary>
    public bool ShowDesktopExperience { get; private set; }

    // OS-family catalogue ─────────────────────────────────────────────────────

    public List<string> OsFamilies { get; } =
    [
        "Windows11",
        "Windows10",
        "WindowsServer2025",
        "WindowsServer2022",
        "WindowsServer2019",
        "WindowsServer2016",
    ];

    // All editions grouped by OS type
    private static readonly string[] ClientEditions  = ["Pro", "Enterprise", "Education", "ProEducation", "ProWorkstation"];
    private static readonly string[] ServerEditions  = ["Standard", "Datacenter", "StandardCore", "DatacenterCore"];

    private static bool IsServerFamily(string family) =>
        family.StartsWith("WindowsServer", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether Desktop Experience is applicable:
    ///  - Only for Server families
    ///  - And only when the selected edition is not a Core edition
    /// </summary>
    private bool ComputeShowDesktopExperience() =>
        IsServerFamily(SelectedOsFamily) && !SelectedEdition.EndsWith("Core");

    private void RefreshEditions()
    {
        var editions = IsServerFamily(SelectedOsFamily) ? ServerEditions : ClientEditions;

        AvailableEditions.Clear();
        foreach (var e in editions) AvailableEditions.Add(e);

        // Keep the selection valid
        if (!AvailableEditions.Contains(SelectedEdition))
            SelectedEdition = AvailableEditions.FirstOrDefault() ?? "";

        ShowDesktopExperience = ComputeShowDesktopExperience();
        OnPropertyChanged(nameof(ShowDesktopExperience));
        OnPropertyChanged(nameof(PreviewFileName));
    }

    // ─── Option lists ─────────────────────────────────────────────────────────

    public List<string> Languages { get; } = ["fr-FR", "en-US", "en-GB", "de-DE", "es-ES", "it-IT"];
    public List<string> TimeZones { get; } =
    [
        "Romance Standard Time", "W. Europe Standard Time", "Central Europe Standard Time",
        "GMT Standard Time", "Eastern Standard Time", "Central Standard Time",
        "Pacific Standard Time", "Turkey Standard Time"
    ];
    public List<int> Generations { get; } = [1, 2];

    // ─── Computed filename preview ────────────────────────────────────────────

    /// <summary>
    /// BASE_WindowsServer2025Standard(DesktopExperience)_10.0.26100.4652_50
    /// </summary>
    public string PreviewFileName
    {
        get
        {
            var edition = SelectedEdition;
            if (ShowDesktopExperience && DesktopExperience)
                edition += "(DesktopExperience)";
            var ver = string.IsNullOrWhiteSpace(OsVersion) ? "0.0.0.0" : OsVersion.Trim();
            return BaseVhdx.BuildFileName($"{SelectedOsFamily}{edition}", ver, SizeGB);
        }
    }

    // ─── Property change hooks ────────────────────────────────────────────────

    partial void OnSelectedOsFamilyChanged(string value)
    {
        RefreshEditions();
    }

    partial void OnSelectedEditionChanged(string value)
    {
        ShowDesktopExperience = ComputeShowDesktopExperience();
        OnPropertyChanged(nameof(ShowDesktopExperience));
        OnPropertyChanged(nameof(PreviewFileName));
    }

    partial void OnDesktopExperienceChanged(bool value) => OnPropertyChanged(nameof(PreviewFileName));
    partial void OnOsVersionChanged(string value)       => OnPropertyChanged(nameof(PreviewFileName));
    partial void OnSizeGBChanged(int value)             => OnPropertyChanged(nameof(PreviewFileName));

    public BaseVhdxViewModel()
    {
        baseFolder = AppSettings.Current.BaseImagesFolder;
        RefreshEditions();
        UpdateAnswerPreview();
    }

    // ─── Commands ────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        Status = "Recherche des images…";
        try
        {
            var images = await _vhdxService.GetBaseVhdxListAsync(BaseFolder);
            BaseVhdxList.Clear();
            foreach (var img in images) BaseVhdxList.Add(img);
            Status = $"{images.Count} image(s) dans {BaseFolder}";
        }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public void UpdateAnswerPreview()
    {
        if (!UseAnswerFile) { AnswerFilePreview = "(fichier de réponse désactivé)"; return; }
        AnswerFilePreview = AnswerFileGenerator.Generate(BuildConfig());
    }

    [RelayCommand]
    public async Task CreateBaseVhdxAsync()
    {
        if (string.IsNullOrWhiteSpace(OsVersion))
            { Status = "Saisissez la version de l'OS (ex : 10.0.26100.4652)."; return; }
        if (string.IsNullOrWhiteSpace(WimPath) || !File.Exists(WimPath))
            { Status = "Sélectionnez un fichier WIM/ESD valide."; return; }

        IsCreating = true; IsLoading = true;
        var vhdxPath = System.IO.Path.Combine(BaseFolder, $"{PreviewFileName}.vhdx");
        try
        {
            BuildStatus = $"Création du VHDX : {System.IO.Path.GetFileName(vhdxPath)}…";
            await _vhdxService.CreateBaseVhdxAsync(vhdxPath, SizeGB);

            string? answerXml = UseAnswerFile ? AnswerFileGenerator.Generate(BuildConfig()) : null;

            BuildStatus = "Application de l'image Windows (quelques minutes)…";
            await _vhdxService.ApplyWindowsImageAsync(vhdxPath, WimPath, ImageIndex, Generation, answerXml);

            BuildStatus = "✓ Image créée avec succès !";
            Status = $"Image créée : {vhdxPath}";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            BuildStatus = $"✗ Erreur : {ex.Message}";
            Status = $"Échec : {ex.Message}";
        }
        finally { IsCreating = false; IsLoading = false; }
    }

    public async Task DeleteBaseVhdxAsync(BaseVhdx vhdx)
    {
        IsLoading = true;
        Status = $"Suppression de '{vhdx.Name}'…";
        try
        {
            await Task.Run(() => File.Delete(vhdx.FilePath));
            Status = $"Image '{vhdx.Name}' supprimée.";
            await RefreshAsync();
        }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; IsLoading = false; }
    }

    private AnswerFileConfig BuildConfig() => new()
    {
        ComputerName  = ComputerName,
        AdminPassword = AdminPassword,
        ProductKey    = ProductKey,
        UILanguage    = UiLanguage,
        InputLocale   = AnswerFileGenerator.GetInputLocale(UiLanguage),
        SystemLocale  = UiLanguage,
        UserLocale    = UiLanguage,
        TimeZone      = TimeZone,
        ImageIndex    = ImageIndex,
    };
}
